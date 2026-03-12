export type {
  BridgeCallContext,
  BridgeErrorNormalizationContext,
  BridgeMiddleware,
  LoggingOptions,
  RetryOptions,
  ErrorNormalizationOptions,
} from "./middleware.js";

export {
  BridgeError,
  BridgeTimeoutError,
  withLogging,
  withTimeout,
  withRetry,
  withErrorNormalization,
} from "./middleware.js";

export { enableHmrPreservation } from "./hmr.js";
export type { BridgeState } from "./hmr.js";

export { WorkerBridgeClient, createBridgeRelay } from "./worker.js";
export type { WorkerBridgeMessage, BridgeWithGetService } from "./worker.js";

import {
  type BridgeMiddleware,
  createContext,
  executeMiddlewareChain,
} from "./middleware.js";

export interface BridgeReadyOptions {
  timeoutMs?: number;
  pollIntervalMs?: number;
}

export interface BridgeRpc {
  invoke(method: string, params?: Record<string, unknown>): Promise<unknown>;
  handle?(method: string, handler: (params: unknown) => unknown | Promise<unknown>): void;
}

export class BridgeReadyTimeoutError extends Error {
  readonly timeoutMs: number;
  readonly phase: "handshake" | "polling";

  constructor(timeoutMs: number, phase: "handshake" | "polling") {
    super(`Bridge not available within timeout (${timeoutMs}ms) during ${phase}.`);
    this.name = "BridgeReadyTimeoutError";
    this.timeoutMs = timeoutMs;
    this.phase = phase;
  }
}

export type BridgeServiceMethod<TParams = void, TResult = unknown> =
  [TParams] extends [void]
    ? () => Promise<TResult>
    : (params: TParams) => Promise<TResult>;

export type BridgeServiceContract<TService extends object> = {
  [K in keyof TService]: TService[K] extends (...args: any[]) => Promise<any>
    ? TService[K]
    : never;
};

export interface BridgeClient {
  ready(options?: BridgeReadyOptions): Promise<void>;
  invoke<T>(method: string, params?: Record<string, unknown>): Promise<T>;
  getService<TService extends object>(serviceName: string): BridgeServiceContract<TService>;
  use(middleware: BridgeMiddleware): void;
}

type BridgeRoot = {
  agWebView?: {
    rpc?: BridgeRpc;
  };
  __agWebViewReady?: boolean;
};

function getRpcFromWindow(win: Window & typeof globalThis): BridgeRpc | null {
  const root = win as unknown as BridgeRoot;
  return root.agWebView?.rpc ?? null;
}

function hasStickyReadyFlag(): boolean {
  if (typeof window === "undefined") {
    return false;
  }
  const root = window as unknown as BridgeRoot;
  return root.__agWebViewReady === true;
}

function validateServiceParams(params: unknown): Record<string, unknown> | undefined {
  if (params === undefined) {
    return undefined;
  }

  if (params === null || Array.isArray(params) || typeof params !== "object") {
    throw new Error("Bridge service methods accept only object params.");
  }

  return params as Record<string, unknown>;
}

export function createBridgeClient(
  resolveRpc: () => BridgeRpc | null = () => getRpcFromWindow(window)
): BridgeClient {
  const middlewares: BridgeMiddleware[] = [];

  function use(middleware: BridgeMiddleware): void {
    middlewares.push(middleware);
  }

  async function ready(options: BridgeReadyOptions = {}): Promise<void> {
    const timeoutMs = options.timeoutMs ?? 3000;
    const pollIntervalMs = options.pollIntervalMs ?? 50;

    if (resolveRpc()) {
      return;
    }

    if (hasStickyReadyFlag() && resolveRpc()) {
      return;
    }

    const startedAt = Date.now();

    try {
      await waitForHandshakeReady(timeoutMs);
      return;
    } catch (error) {
      if (!(error instanceof BridgeReadyTimeoutError) || pollIntervalMs <= 0) {
        throw error;
      }
    }

    const remainingMs = timeoutMs - (Date.now() - startedAt);
    if (remainingMs <= 0) {
      throw new BridgeReadyTimeoutError(timeoutMs, "polling");
    }

    await waitForPollingFallback(remainingMs, timeoutMs, pollIntervalMs);
  }

  function waitForHandshakeReady(timeoutMs: number): Promise<void> {
    return new Promise<void>((resolve, reject) => {
      let settled = false;

      const cleanup = () => {
        settled = true;
        clearTimeout(timeoutHandle);
        if (typeof window !== "undefined") {
          window.removeEventListener("agWebViewReady", onEvent);
        }
      };

      const tryResolve = () => {
        if (settled) {
          return;
        }

        if (resolveRpc()) {
          cleanup();
          resolve();
        }
      };

      const onEvent = () => {
        tryResolve();
      };

      const timeoutHandle = setTimeout(() => {
        if (settled) {
          return;
        }

        cleanup();
        reject(new BridgeReadyTimeoutError(timeoutMs, "handshake"));
      }, timeoutMs);

      if (typeof window !== "undefined") {
        window.addEventListener("agWebViewReady", onEvent, { once: true });
      }

      // Handle race where ready event fired before listener registration.
      if (hasStickyReadyFlag() && resolveRpc()) {
        cleanup();
        resolve();
        return;
      }

      tryResolve();
    });
  }

  function waitForPollingFallback(
    timeoutMsForPolling: number,
    configuredTimeoutMs: number,
    pollIntervalMs: number,
  ): Promise<void> {
    return new Promise<void>((resolve, reject) => {
      let settled = false;

      const cleanup = () => {
        settled = true;
        clearTimeout(timeoutHandle);
        clearInterval(pollHandle);
      };

      const tryResolve = () => {
        if (settled) {
          return;
        }

        if (resolveRpc()) {
          cleanup();
          resolve();
        }
      };

      const timeoutHandle = setTimeout(() => {
        if (settled) {
          return;
        }

        cleanup();
        reject(new BridgeReadyTimeoutError(configuredTimeoutMs, "polling"));
      }, timeoutMsForPolling);

      const pollHandle = setInterval(tryResolve, pollIntervalMs);
      tryResolve();
    });
  }

  async function invoke<T>(method: string, params?: Record<string, unknown>): Promise<T> {
    const rpc = resolveRpc();
    if (!rpc) {
      throw new Error("Bridge not available.");
    }

    if (middlewares.length === 0) {
      return (await rpc.invoke(method, params)) as T;
    }

    const context = createContext(method, params);
    const result = await executeMiddlewareChain(
      middlewares,
      context,
      () => rpc.invoke(method, params)
    );
    return result as T;
  }

  function getService<TService extends object>(serviceName: string): BridgeServiceContract<TService> {
    return new Proxy(
      {},
      {
        get(_target, prop) {
          if (typeof prop !== "string") {
            return undefined;
          }

          return (params?: unknown) => invoke(`${serviceName}.${prop}`, validateServiceParams(params));
        },
      }
    ) as BridgeServiceContract<TService>;
  }

  return {
    ready,
    invoke,
    getService,
    use,
  };
}

export const bridgeClient = createBridgeClient();

export function ready(options?: BridgeReadyOptions): Promise<void> {
  return bridgeClient.ready(options);
}
