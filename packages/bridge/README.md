# @agibuild/fulora-client

Typed bridge client runtime for [Agibuild.Fulora](https://github.com/AGIBuild/Fulora) — call C# services from JavaScript with full type safety.

## Install

```bash
npm install @agibuild/fulora-client
```

## Quick Start

```typescript
import { createBridgeClient } from '@agibuild/fulora-client';

const bridge = createBridgeClient();

// Wait for the native bridge to be ready
await bridge.ready();

// Call a C# service method directly
const result = await bridge.invoke<string>('GreeterService.SayHello', { name: 'World' });
```

## Typed Service Proxies

Define TypeScript interfaces that mirror your C# `[JsExport]` services, then use `getService()` for type-safe calls:

```typescript
import { createBridgeClient, type BridgeServiceMethod } from '@agibuild/fulora-client';

// Define interface matching your C# service
interface ISystemInfoService {
  getSystemInfo: BridgeServiceMethod<void, SystemInfo>;
  getRuntimeMetrics: BridgeServiceMethod<void, RuntimeMetrics>;
}

interface SystemInfo {
  osName: string;
  dotnetVersion: string;
  machineName: string;
}

interface RuntimeMetrics {
  workingSetMb: number;
  uptimeSeconds: number;
}

// Create typed proxy
const bridge = createBridgeClient();
const systemInfo = bridge.getService<ISystemInfoService>('SystemInfoService');

// Fully typed calls
const info = await systemInfo.getSystemInfo();       // → SystemInfo
const metrics = await systemInfo.getRuntimeMetrics(); // → RuntimeMetrics
```

## Middleware

Add cross-cutting concerns (logging, timeout, retry, error normalization) via the middleware pipeline:

```typescript
import {
  createBridgeClient,
  withLogging,
  withTimeout,
  withRetry,
  withErrorNormalization,
} from '@agibuild/fulora-client';

const bridge = createBridgeClient();

// Log all bridge calls in development
bridge.use(withLogging({ maxParamLength: 100 }));

// 5-second timeout for all calls
bridge.use(withTimeout(5000));

// Retry transient failures up to 3 times
bridge.use(withRetry({ maxRetries: 3, delay: 500 }));

// Convert raw RPC errors to typed BridgeError instances
bridge.use(withErrorNormalization());
```

### Custom Middleware

```typescript
import type { BridgeMiddleware } from '@agibuild/fulora-client';

const analytics: BridgeMiddleware = async (context, next) => {
  const start = Date.now();
  try {
    const result = await next();
    trackCall(context.serviceName, context.methodName, Date.now() - start);
    return result;
  } catch (err) {
    trackError(context.serviceName, context.methodName, err);
    throw err;
  }
};

bridge.use(analytics);
```

## Error Handling

```typescript
import { BridgeError, BridgeTimeoutError } from '@agibuild/fulora-client';

try {
  await bridge.invoke('SomeService.DoWork');
} catch (err) {
  if (err instanceof BridgeTimeoutError) {
    console.error(`Timed out after ${err.timeoutMs}ms`);
  } else if (err instanceof BridgeError) {
    console.error(`RPC error [${err.code}]: ${err.message}`, err.data);
  }
}
```

## API Reference

### `createBridgeClient(resolveRpc?)`

Creates a new bridge client instance. The optional `resolveRpc` parameter allows custom RPC resolution (defaults to `window.agWebView.rpc`).

### `BridgeClient`

| Method | Description |
|---|---|
| `ready(options?)` | Wait for the native bridge to be injected. Options: `timeoutMs` (default 3000), `pollIntervalMs` (default 50) |
| `invoke<T>(method, params?)` | Call a C# method by fully-qualified name (e.g. `ServiceName.MethodName`) |
| `getService<T>(name)` | Create a typed proxy for a C# service |
| `use(middleware)` | Add a middleware to the pipeline |

### Built-in Middlewares

| Middleware | Description |
|---|---|
| `withLogging(options?)` | Log bridge calls with timing. Options: `logger`, `maxParamLength` |
| `withTimeout(ms)` | Reject calls that exceed the timeout |
| `withRetry(options)` | Retry failed calls. Options: `maxRetries`, `delay`, `retryOn` |
| `withErrorNormalization()` | Convert raw RPC error objects to `BridgeError` instances |

## How It Works

This package is the JavaScript side of the Agibuild.Fulora bridge. On the C# side, services decorated with `[JsExport]` are exposed via a JSON-RPC transport injected into the WebView as `window.agWebView.rpc`. This package provides a typed client that calls those services and supports middleware for cross-cutting concerns.

```
┌─────────────────────┐          ┌─────────────────────┐
│   Web App (JS/TS)   │          │   .NET Host (C#)    │
│                     │          │                     │
│  bridge.getService  │──JSON──▶│  [JsExport] Service │
│  bridge.invoke      │   RPC   │  Bridge Runtime      │
│  bridge.use(mw)     │◀──────── │  Source Generator    │
└─────────────────────┘          └─────────────────────┘
```

## License

MIT
