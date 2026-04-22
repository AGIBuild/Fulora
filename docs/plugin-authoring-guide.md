# Bridge Plugin Authoring Guide

This guide covers how to create, publish, and consume Fulora bridge plugins — reusable NuGet + npm package pairs that extend the C#↔JS bridge with typed services.

## Overview

A Fulora bridge plugin consists of:

1. **NuGet package** — Contains `[JsExport]` interfaces, implementations, and an `IBridgePlugin` manifest
2. **npm package** — Contains TypeScript declarations and a typed helper function

Consumers install both packages and register the plugin with a single call:

```csharp
// C# — one line to register all plugin services
webView.Bridge.UsePlugin<LocalStoragePlugin>();
```

```typescript
// TypeScript — fully typed service proxy
import { getLocalStorageService } from '@agibuild/fulora-plugin-local-storage';
const storage = getLocalStorageService(bridgeClient);
await storage.set({ key: 'theme', value: 'dark' });
```

## Creating a Plugin (NuGet Side)

### 1. Define the Service Interface

Create a `[JsExport]` interface in your plugin project:

```csharp
[JsExport]
public interface IMyService
{
    Task<string> GetValue(string key);
    Task SetValue(string key, string value);
}
```

### 2. Implement the Service

```csharp
public sealed class MyService : IMyService
{
    public Task<string> GetValue(string key) => /* ... */;
    public Task SetValue(string key, string value) => /* ... */;
}
```

### 3. Create the Plugin Manifest

Implement `IBridgePlugin` with both a static `GetMetadata()` method and a static `GetServices()` method:

```csharp
public sealed class MyPlugin : IBridgePlugin
{
    public static BridgePluginMetadata GetMetadata()
        => new(
            "Contoso.Fulora.Plugin.MyFeature",
            ["plugin.my-feature.read"],
            ["plugin.my-feature.write"],
            ["Document the trust boundary and least-privilege expectations for this plugin."],
            ["desktop-hosts"]);

    public static IEnumerable<BridgePluginServiceDescriptor> GetServices()
    {
        yield return BridgePluginServiceDescriptor.Create<IMyService>(
            sp => new MyService());
    }
}
```

Key points:
- Uses `static abstract` members (C# 11+) — NativeAOT-safe, no reflection
- `GetMetadata()` declares the plugin id, required capabilities, optional capabilities, security notes, and platform constraints
- The factory `Func<IServiceProvider?, T>` receives an optional DI container
- Multiple services can be yielded from a single plugin

### 4. Declare Capability Metadata

Capability metadata is part of the plugin contract, not an optional comment.

Populate these fields deliberately:

| Field | Purpose |
|---|---|
| `PluginId` | Stable identifier used by tooling, diagnostics, and release governance |
| `RequiredCapabilities` | Capabilities needed for the plugin's baseline behavior |
| `OptionalCapabilities` | Capabilities the host may enable explicitly for extra behavior |
| `SecurityNotes` | Short operational notes that explain the plugin's trust boundary |
| `PlatformConstraints` | Short notes describing supported platforms or environment assumptions |

Guidance:
- Prefer stable dot-separated ids such as `plugin.http.outbound` or `plugin.database.write`
- Keep required capabilities minimal; move host-optional powers into `OptionalCapabilities`
- `SecurityNotes` should describe real operational expectations, not marketing text
- `PlatformConstraints` should match the actual provider/runtime limitations of the plugin
- If a plugin persists data or touches the network, say what policy boundary should govern it

### 5. Add BridgeOptions (Optional)

```csharp
yield return BridgePluginServiceDescriptor.Create<IMyService>(
    sp => new MyService(),
    new BridgeOptions
    {
        RateLimit = new RateLimit(100, TimeSpan.FromMinutes(1))
    });
```

### 6. Project Setup

Your `.csproj` needs:

```xml
<ItemGroup>
  <PackageReference Include="Agibuild.Fulora.Core" Version="*" />
  <PackageReference Include="Agibuild.Fulora.Bridge.Generator"
                    Version="*"
                    OutputItemType="Analyzer"
                    ReferenceOutputAssembly="true" />
</ItemGroup>
```

The source generator will automatically produce:
- `{ServiceName}BridgeRegistration.g.cs` — RPC handler registration
- `BridgeTypeScriptDeclarations.g.cs` — TypeScript `.d.ts` content

### 7. Extract TypeScript Declarations

Build with `EmitCompilerGeneratedFiles` to extract the generated TS:

```bash
dotnet build -p:EmitCompilerGeneratedFiles=true
```

The declarations are in:
`obj/Debug/net10.0/generated/.../BridgeTypeScriptDeclarations.g.cs`

## Creating the npm Companion Package

### 1. Package Structure

```
packages/bridge-plugin-my-feature/
├── src/
│   └── index.ts        # Types + helper
├── dist/               # Built output
├── package.json
└── tsconfig.json
```

### 2. Define TypeScript Interface

Copy the interface from the generated `.d.ts` and add the helper:

```typescript
import type { BridgeClient, BridgeServiceContract } from '@agibuild/fulora-client';

export interface MyService {
  getValue(params: { key: string }): Promise<string>;
  setValue(params: { key: string; value: string }): Promise<void>;
}

export function getMyService(
  client: BridgeClient
): BridgeServiceContract<MyService> {
  return client.getService<MyService>('MyService');
}
```

### 3. package.json

```json
{
  "name": "@agibuild/fulora-plugin-my-feature",
  "peerDependencies": {
    "@agibuild/fulora-client": "^1.0.0"
  }
}
```

## Consuming a Plugin

### C# (Host Application)

```csharp
// In your MainWindow or startup:
webView.Bridge.UsePlugin<LocalStoragePlugin>();

// With DI:
webView.Bridge.UsePlugin<LocalStoragePlugin>(serviceProvider);
```

### TypeScript (Frontend)

```typescript
import { bridgeClient } from '@agibuild/fulora-client';
import { getLocalStorageService } from '@agibuild/fulora-plugin-local-storage';

const storage = getLocalStorageService(bridgeClient);
const keys = await storage.getKeys();
```

## Conventions

| Convention | Value |
|---|---|
| NuGet package name | `Agibuild.Fulora.Plugin.{Name}` |
| npm package name | `@agibuild/fulora-plugin-{name}` |
| Plugin class name | `{Name}Plugin : IBridgePlugin` |
| Service name (RPC) | Derived from interface name minus `I` prefix |

## Official Plugins

Fulora ships with the following official plugins, each available as a NuGet + npm package pair:

| Plugin | NuGet Package | npm Package | Description |
|---|---|---|---|
| **LocalStorage** | `Agibuild.Fulora.Plugin.LocalStorage` | `@agibuild/fulora-plugin-local-storage` | Key-value local persistence |
| **Database** | `Agibuild.Fulora.Plugin.Database` | `@agibuild/fulora-plugin-database` | SQLite embedded database (query, execute, transactions) |
| **HTTP Client** | `Agibuild.Fulora.Plugin.HttpClient` | `@agibuild/fulora-plugin-http-client` | Host-routed HTTP with base URL, headers, interceptors |
| **File System** | `Agibuild.Fulora.Plugin.FileSystem` | `@agibuild/fulora-plugin-file-system` | Sandboxed file read/write/list/delete operations |
| **Notifications** | `Agibuild.Fulora.Plugin.Notifications` | `@agibuild/fulora-plugin-notifications` | Cross-platform system notifications (toast/banner) |
| **Auth Token** | `Agibuild.Fulora.Plugin.AuthToken` | `@agibuild/fulora-plugin-auth-token` | Platform-secure token storage (Keychain/CredMgr/Keystore) |
| **Biometric** | `Agibuild.Fulora.Plugin.Biometric` | `@agibuild/fulora-plugin-biometric` | Biometric authentication (Touch ID, Face ID, Windows Hello) |

Use the CLI to discover and install plugins:

```bash
fulora search database
fulora add plugin Agibuild.Fulora.Plugin.Database
```

## Testing

- **Unit tests**: Test the service implementation independently
- **Manifest tests**: Verify `GetMetadata()` declares the expected plugin id and capabilities
- **Contract tests**: Verify `UsePlugin` registers all declared services
- **Integration tests**: Full round-trip: register plugin → call from JS → verify

## Example: Biometric Authentication Plugin

The `Agibuild.Fulora.Plugin.Biometric` package provides biometric authentication (Touch ID, Face ID, Windows Hello) as a bridge plugin.

### Platform Support

| Platform | Provider | API |
|----------|----------|-----|
| macOS | Touch ID | `LocalAuthentication` (LAContext) |
| Windows | Windows Hello | `UserConsentVerifier` |
| iOS | Face ID / Touch ID | `LocalAuthentication` |
| Android | Fingerprint / Face | `BiometricPrompt` |
| Linux | ❌ Not supported | Returns `platform_not_supported` |

### Usage

```csharp
webView.Bridge.UsePlugin<BiometricPlugin>();
```

```typescript
import type { IBiometricService } from '@agibuild/fulora-plugin-biometric';

const availability = await BiometricService.checkAvailabilityAsync();
if (availability.isAvailable) {
  const result = await BiometricService.authenticateAsync('Confirm payment');
  if (result.success) {
    // authenticated
  }
}
```
