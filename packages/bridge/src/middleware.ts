// ─── Types ──────────────────────────────────────────────────────────────────

export interface BridgeCallContext {
  readonly serviceName: string;
  readonly methodName: string;
  readonly params: Record<string, unknown> | undefined;
  readonly startedAt: number;
  properties: Map<string, unknown>;
}

export type BridgeMiddleware = (
  context: BridgeCallContext,
  next: () => Promise<unknown>
) => Promise<unknown>;

// ─── Error Classes ──────────────────────────────────────────────────────────

export class BridgeError extends Error {
  readonly code: number;
  readonly data?: unknown;

  constructor(message: string, code: number, data?: unknown) {
    super(message);
    this.name = "BridgeError";
    this.code = code;
    this.data = data;
  }
}

export class BridgeTimeoutError extends Error {
  readonly timeoutMs: number;

  constructor(timeoutMs: number) {
    super(`Bridge call timed out after ${timeoutMs}ms`);
    this.name = "BridgeTimeoutError";
    this.timeoutMs = timeoutMs;
  }
}

// ─── Pipeline Executor ──────────────────────────────────────────────────────

export function parseMethodString(method: string): {
  serviceName: string;
  methodName: string;
} {
  const dotIndex = method.indexOf(".");
  if (dotIndex === -1) {
    return { serviceName: "", methodName: method };
  }
  return {
    serviceName: method.substring(0, dotIndex),
    methodName: method.substring(dotIndex + 1),
  };
}

export function createContext(
  method: string,
  params: Record<string, unknown> | undefined
): BridgeCallContext {
  const { serviceName, methodName } = parseMethodString(method);
  return {
    serviceName,
    methodName,
    params,
    startedAt: Date.now(),
    properties: new Map(),
  };
}

export function executeMiddlewareChain(
  middlewares: BridgeMiddleware[],
  context: BridgeCallContext,
  invokeRpc: () => Promise<unknown>
): Promise<unknown> {
  let index = 0;

  function next(): Promise<unknown> {
    if (index >= middlewares.length) {
      return invokeRpc();
    }
    const mw = middlewares[index++];
    return mw(context, next);
  }

  return next();
}

// ─── Built-in Middlewares ────────────────────────────────────────────────────

export interface LoggingOptions {
  logger?: (message: string) => void;
  maxParamLength?: number;
}

export function withLogging(options?: LoggingOptions): BridgeMiddleware {
  const log = options?.logger ?? console.log;
  const maxLen = options?.maxParamLength ?? 200;

  return async (context, next) => {
    const label = `${context.serviceName}.${context.methodName}`;
    try {
      const result = await next();
      const elapsed = Date.now() - context.startedAt;
      const paramStr = context.params
        ? JSON.stringify(context.params).substring(0, maxLen)
        : "void";
      log(`[bridge] ${label}(${paramStr}) → ${elapsed}ms`);
      return result;
    } catch (err) {
      const elapsed = Date.now() - context.startedAt;
      const msg = err instanceof Error ? err.message : String(err);
      log(`[bridge] ${label} FAILED (${elapsed}ms): ${msg}`);
      throw err;
    }
  };
}

export function withTimeout(ms: number): BridgeMiddleware {
  return (context, next) => {
    return new Promise<unknown>((resolve, reject) => {
      const timer = setTimeout(
        () => reject(new BridgeTimeoutError(ms)),
        ms
      );
      next().then(
        (result) => {
          clearTimeout(timer);
          resolve(result);
        },
        (err) => {
          clearTimeout(timer);
          reject(err);
        }
      );
    });
  };
}

export interface RetryOptions {
  maxRetries: number;
  delay: number;
  retryOn?: (error: unknown) => boolean;
}

export function withRetry(options: RetryOptions): BridgeMiddleware {
  return async (context, next) => {
    let lastError: unknown;

    for (let attempt = 0; attempt <= options.maxRetries; attempt++) {
      try {
        return await next();
      } catch (err) {
        lastError = err;
        if (options.retryOn && !options.retryOn(err)) {
          throw err;
        }
        if (attempt < options.maxRetries) {
          await new Promise((r) => setTimeout(r, options.delay));
        }
      }
    }

    throw lastError;
  };
}

export function withErrorNormalization(): BridgeMiddleware {
  return async (context, next) => {
    try {
      return await next();
    } catch (err) {
      if (isRpcError(err)) {
        throw new BridgeError(
          err.message ?? "RPC error",
          err.code ?? -1,
          err.data
        );
      }
      throw err;
    }
  };
}

function isRpcError(
  err: unknown
): err is { code?: number; message?: string; data?: unknown } {
  return (
    typeof err === "object" &&
    err !== null &&
    "code" in err &&
    typeof (err as Record<string, unknown>).code === "number"
  );
}
