# Bring Your Existing Web App to Fulora

Fulora is designed to help teams ship desktop applications with the web stack they already know.

If you already have a React, Vue, or similar web application, you do not need to rewrite it to adopt Fulora. The recommended path is to keep your existing frontend, host it inside a Fulora-powered WebView, and add native desktop capabilities only where they provide clear product value.

This guide explains the recommended architecture, development flow, and best practices for that adoption path.

The canonical brownfield path is:

```text
attach web -> dev -> package
```

Start by wiring the existing frontend into Fulora explicitly:

```bash
fulora attach web \
  --web ./app/web \
  --desktop ./app/desktop \
  --bridge ./app/bridge \
  --framework react \
  --web-command "npm run dev" \
  --dev-server-url http://localhost:5173
```

This scaffolds the Fulora-owned desktop + bridge wiring, creates `fulora.json`, and keeps your existing frontend business code intact.

## When to use this approach

This guide is for teams that:

- already have a working web application
- want to keep their current frontend framework and development workflow
- want to run that app inside a native desktop shell
- want to add native capabilities gradually rather than redesign the whole product

Typical fits include:

- React + Vite
- Vue + Vite
- Angular
- Next.js
- internal web platforms that expose a development server and/or static build output

If you are starting from scratch, use the standard Fulora template path first. If you already have a product, this “bring your own web app” path is usually the better starting point.

## Brownfield workflow at a glance

Use the commands in this order:

```bash
fulora attach web --web ./app/web --framework react
fulora dev --preflight-only
fulora package --profile desktop-public --preflight-only
```

If bridge artifacts drift, regenerate them explicitly:

```bash
fulora generate types --project ./app/bridge/MyProduct.Bridge.csproj
```

Fulora reports drift in preflight output instead of silently auto-fixing generated files during `dev` or `package`.

## Fulora’s role in an existing web app architecture

When adopting Fulora in an existing product, the best mental model is:

- your web app remains your web app
- Fulora provides the native host
- Fulora bridge services expose desktop-only capabilities in a typed way

In other words, Fulora should not become the place where ordinary frontend business logic lives. It should become the boundary where your web application crosses into native desktop functionality.

That boundary is where Fulora adds the most value:

- native window hosting
- development-time dev-server proxying
- production asset hosting
- typed host/web contracts
- desktop capabilities such as file access, dialogs, notifications, and shell behavior

## Recommended project structure

A strong structure for existing-product adoption looks like this:

```text
MyProduct/
  app/
    desktop/
      MyProduct.Desktop.csproj
      Program.cs
      MainWindow.axaml
      MainWindow.axaml.cs

    bridge/
      MyProduct.Bridge.csproj
      Contracts/
        IUserProfileService.cs
      Models/
        UserProfileDto.cs

    web/
      package.json
      vite.config.ts
      src/
        main.tsx
        App.tsx
        bridge/
          client.ts
          services.ts
          generated/
            bridge.d.ts
            bridge.client.ts
            bridge.mock.ts
            bridge.manifest.json
```

### Why this structure works

- `desktop/` clearly owns the native host
- `bridge/` keeps contracts and DTOs separate from application UI code
- `web/` stays recognizable to frontend developers
- `generated/` keeps bridge artifacts contained and predictable

This structure helps preserve the existing web project mental model while still giving the native host and typed bridge a clear place in the system.

## Development mode: keep your existing dev server

For most teams, the best first step is to connect Fulora to the same frontend development server you already use.

If your frontend already runs locally, for example:

```bash
cd app/web
npm run dev
```

configure the desktop host like this:

```csharp
WebView.EnableSpaHosting(new SpaHostingOptions
{
    DevServerUrl = "http://localhost:5173"
});

await WebView.NavigateAsync(new Uri("app://localhost/index.html"));
```

This means:

- your frontend still runs through its normal dev server
- HMR still works
- your team keeps the same frontend development experience
- the desktop shell uses a stable `app://localhost/...` navigation surface

### Recommendation

Always start here unless you have a strong reason not to.

It keeps adoption lightweight and lets your team get to a working desktop shell before learning deeper Fulora concepts.

## Production mode: switch to embedded or local assets

Once development is working, production should move to embedded or packaged static assets:

```csharp
WebView.EnableSpaHosting(new SpaHostingOptions
{
    EmbeddedResourcePrefix = "wwwroot",
    ResourceAssembly = typeof(MainWindow).Assembly
});

await WebView.NavigateAsync(new Uri("app://localhost/index.html"));
```

The important design goal is that the runtime URL model stays consistent:

- development: `app://localhost/*` is proxied to the dev server
- production: `app://localhost/*` is served from packaged assets

That consistency reduces environment-specific logic in the web app and keeps routing behavior easier to reason about.

## Add native capabilities gradually through typed services

Do not begin by pushing all logic into the native host.

Instead, expose only the capabilities that genuinely require desktop integration.

Example contract:

```csharp
[JsExport]
public interface IUserProfileService
{
    Task<UserProfileDto> Get();
}
```

Example implementation:

```csharp
public sealed class UserProfileService : IUserProfileService
{
    public Task<UserProfileDto> Get()
    {
        return Task.FromResult(new UserProfileDto
        {
            Name = "Alice",
            Email = "alice@example.com"
        });
    }
}
```

Expose it from the host:

```csharp
WebView.Bridge.Expose<IUserProfileService>(new UserProfileService());
```

Use it from the frontend:

```ts
import { services } from "./bridge/client";

const profile = await services.userProfile.get();
```

### Best practice

Frontend business code should prefer a stable application-service surface such as:

```ts
services.someService.someMethod(...)
```

and avoid direct use of low-level transport calls such as:

```ts
window.agWebView.rpc.invoke(...)
```

The bridge should feel like application services, not RPC plumbing.

## What belongs in Fulora bridge services?

Good candidates include:

- file system access
- notifications
- clipboard
- system dialogs
- printing / PDF
- screenshot capture
- authentication flows
- tray / menu / window-shell capabilities
- local database entry points
- desktop-specific settings or shell state

Poor candidates include:

- routine page state
- form validation
- frontend-only business logic
- standard routing
- UI composition logic

### Rule of thumb

Use the bridge for host capabilities, not as a general place to move frontend code into C#.

## Recommended frontend bridge entrypoint pattern

Keep one thin, hand-written bridge entrypoint in the frontend:

```ts
import {
  createBridgeClient,
  type BridgeReadyOptions,
  withErrorNormalization,
  withLogging,
} from "@agibuild/fulora-client";

import { userProfileService } from "./generated/bridge.client";

const bridgeClient = createBridgeClient();

if (import.meta.env.DEV) {
  bridgeClient.use(withLogging({ maxParamLength: 200 }));
}

bridgeClient.use(withErrorNormalization());

export const bridge = bridgeClient;

export const bridgeProfile = {
  bridge,
  ready(options?: BridgeReadyOptions) {
    return bridge.ready(options);
  },
};

export function createFuloraClient() {
  return {
    userProfile: userProfileService,
  } as const;
}

export const services = createFuloraClient();
```

### Why this is a good pattern

- it gives the frontend one stable import surface
- middleware stays centralized
- generated files remain internal implementation detail
- future bridge evolution is easier to absorb in one place

## Example development workflow

### Frontend

```bash
cd app/web
npm run dev
```

### Desktop host

```bash
cd app/desktop
dotnet run
```

Or, once your workspace is wired for the CLI:

```bash
fulora dev --web ./app/web --desktop ./app/desktop/MyProduct.Desktop.csproj
```

### Generate bridge artifacts when needed

```bash
fulora generate types \
  --project ./app/bridge/MyProduct.Bridge.csproj \
  --output ./app/web/src/bridge/generated
```

This writes:

- `bridge.d.ts`
- `bridge.client.ts`
- `bridge.mock.ts`
- `bridge.manifest.json`

### Run preflight checks

Before development:

```bash
fulora dev \
  --web ./app/web \
  --desktop ./app/desktop/MyProduct.Desktop.csproj \
  --preflight-only
```

Before packaging:

```bash
fulora package \
  --project ./app/desktop/MyProduct.Desktop.csproj \
  --profile desktop-public \
  --preflight-only
```

These checks help surface missing or stale bridge artifacts before they become harder-to-diagnose runtime problems.

## Best practices

### 1. Keep the web app recognizable to the frontend team

Do not over-desktop-ify your frontend project structure too early. Frontend developers should still feel like they are working in a standard web application.

### 2. Keep the runtime URL surface stable

Prefer `app://localhost/...` in both development and production. This reduces routing and asset-hosting surprises.

### 3. Keep generated files contained

Use a dedicated `src/bridge/generated/` directory. Do not mix generated artifacts with everyday business code.

### 4. Keep the bridge layer narrow

Add native services where they create product value. Do not treat the bridge as a dumping ground for all application logic.

### 5. Keep business code on `services.*`

Application code should call typed services, not raw bridge internals. This improves readability, maintainability, and migration safety.

### 6. Use preflight checks regularly

Preflight is especially useful before:

- beginning a day of development
- switching branches
- packaging a release
- debugging “works on one machine but not another” bridge issues

## Common mistakes

### Mistake: rewriting the frontend around the desktop host too early

You usually get faster adoption by keeping the web app intact and only adding desktop capabilities where needed.

### Mistake: exposing too much through bridge contracts

If ordinary UI/business logic starts migrating into host services, the architecture becomes harder to reason about.

### Mistake: letting frontend code call raw RPC methods directly

That leaks transport concerns into business code and makes the app harder to evolve.

### Mistake: treating generated artifacts as user-managed source files

Generated bridge files should feel like tooling output, not like files every developer has to constantly hand-manage.

## Troubleshooting

### The desktop host launches, but the app does not load

Check:

- your dev server is actually running
- `DevServerUrl` points to the right port
- the host navigates to `app://localhost/index.html`

### Bridge artifacts are missing or stale

Run:

```bash
fulora generate types --project ./app/bridge/MyProduct.Bridge.csproj
```

Then re-run:

```bash
fulora dev --preflight-only
```

### The project is part of a larger monorepo

Start by connecting only the specific frontend app you want to host. Do not try to formalize the entire monorepo into Fulora on day one.

## Summary

The best way to adopt Fulora for an existing web product is:

- keep your current frontend framework
- keep your existing dev server
- use Fulora as the desktop host
- add typed native services only where needed
- keep the bridge layer narrow and predictable

That approach preserves the speed of your current web workflow while giving you a path to native desktop capabilities and packaging.

## Related documents

- [Getting Started](./getting-started.md)
- [Bridge Guide](./bridge-guide.md)
- [SPA Hosting](./spa-hosting.md)
- [CLI Reference](../cli.md)
- [Shipping Your App](../shipping-your-app.md)
