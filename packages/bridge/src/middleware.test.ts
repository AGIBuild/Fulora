import { describe, it } from "node:test";
import assert from "node:assert/strict";
import {
  type BridgeCallContext,
  type BridgeMiddleware,
  BridgeError,
  BridgeTimeoutError,
  createContext,
  executeMiddlewareChain,
  parseMethodString,
  withLogging,
  withTimeout,
  withRetry,
  withErrorNormalization,
} from "./middleware.ts";

// ─── parseMethodString ──────────────────────────────────────────────────────

describe("parseMethodString", () => {
  it("parses Service.method", () => {
    const { serviceName, methodName } = parseMethodString("AppService.getCurrentUser");
    assert.equal(serviceName, "AppService");
    assert.equal(methodName, "getCurrentUser");
  });

  it("handles no dot (method only)", () => {
    const { serviceName, methodName } = parseMethodString("ping");
    assert.equal(serviceName, "");
    assert.equal(methodName, "ping");
  });

  it("handles multiple dots", () => {
    const { serviceName, methodName } = parseMethodString("Namespace.Service.method");
    assert.equal(serviceName, "Namespace");
    assert.equal(methodName, "Service.method");
  });
});

// ─── createContext ───────────────────────────────────────────────────────────

describe("createContext", () => {
  it("populates serviceName and methodName", () => {
    const ctx = createContext("ThemeService.getCurrentTheme", undefined);
    assert.equal(ctx.serviceName, "ThemeService");
    assert.equal(ctx.methodName, "getCurrentTheme");
  });

  it("captures startedAt timestamp", () => {
    const before = Date.now();
    const ctx = createContext("Svc.m", undefined);
    assert.ok(ctx.startedAt >= before);
    assert.ok(ctx.startedAt <= Date.now());
  });

  it("has empty properties map", () => {
    const ctx = createContext("Svc.m", undefined);
    assert.equal(ctx.properties.size, 0);
  });

  it("passes params through", () => {
    const params = { key: "value" };
    const ctx = createContext("Svc.m", params);
    assert.deepEqual(ctx.params, { key: "value" });
  });
});

// ─── Middleware Pipeline ────────────────────────────────────────────────────

describe("executeMiddlewareChain", () => {
  it("no middleware — calls RPC directly", async () => {
    let called = false;
    const result = await executeMiddlewareChain(
      [],
      createContext("Svc.m", undefined),
      async () => { called = true; return 42; }
    );
    assert.equal(called, true);
    assert.equal(result, 42);
  });

  it("single middleware wraps RPC call", async () => {
    const log: string[] = [];
    const mw: BridgeMiddleware = async (ctx, next) => {
      log.push("before");
      const r = await next();
      log.push("after");
      return r;
    };
    await executeMiddlewareChain([mw], createContext("S.m", undefined), async () => {
      log.push("rpc");
      return "ok";
    });
    assert.deepEqual(log, ["before", "rpc", "after"]);
  });

  it("multiple middleware — onion order (A → B → RPC → B → A)", async () => {
    const log: string[] = [];
    const mwA: BridgeMiddleware = async (ctx, next) => {
      log.push("A-before");
      const r = await next();
      log.push("A-after");
      return r;
    };
    const mwB: BridgeMiddleware = async (ctx, next) => {
      log.push("B-before");
      const r = await next();
      log.push("B-after");
      return r;
    };
    await executeMiddlewareChain([mwA, mwB], createContext("S.m", undefined), async () => {
      log.push("rpc");
      return "ok";
    });
    assert.deepEqual(log, ["A-before", "B-before", "rpc", "B-after", "A-after"]);
  });

  it("context properties shared across middleware", async () => {
    const mwA: BridgeMiddleware = async (ctx, next) => {
      ctx.properties.set("correlationId", "abc-123");
      return next();
    };
    let seen: unknown;
    const mwB: BridgeMiddleware = async (ctx, next) => {
      seen = ctx.properties.get("correlationId");
      return next();
    };
    await executeMiddlewareChain([mwA, mwB], createContext("S.m", undefined), async () => "ok");
    assert.equal(seen, "abc-123");
  });
});

// ─── Short-Circuit ──────────────────────────────────────────────────────────

describe("short-circuit", () => {
  it("returning without next() bypasses RPC", async () => {
    let rpcCalled = false;
    const cache: BridgeMiddleware = async () => "cached-value";
    const result = await executeMiddlewareChain(
      [cache],
      createContext("S.m", undefined),
      async () => { rpcCalled = true; return "real"; }
    );
    assert.equal(rpcCalled, false);
    assert.equal(result, "cached-value");
  });

  it("throwing without next() prevents RPC and propagates", async () => {
    let rpcCalled = false;
    const blocker: BridgeMiddleware = async () => { throw new Error("blocked"); };
    await assert.rejects(
      () => executeMiddlewareChain(
        [blocker],
        createContext("S.m", undefined),
        async () => { rpcCalled = true; return "x"; }
      ),
      { message: "blocked" }
    );
    assert.equal(rpcCalled, false);
  });

  it("error from inner middleware propagates outward", async () => {
    const outer: BridgeMiddleware = async (ctx, next) => {
      try {
        return await next();
      } catch (err) {
        throw new Error("outer-caught: " + (err instanceof Error ? err.message : err));
      }
    };
    const inner: BridgeMiddleware = async () => { throw new Error("inner-fail"); };
    await assert.rejects(
      () => executeMiddlewareChain(
        [outer, inner],
        createContext("S.m", undefined),
        async () => "ok"
      ),
      { message: "outer-caught: inner-fail" }
    );
  });
});

// ─── withLogging ────────────────────────────────────────────────────────────

describe("withLogging", () => {
  it("logs success with service.method and latency", async () => {
    const logs: string[] = [];
    const mw = withLogging({ logger: (msg) => logs.push(msg) });
    await executeMiddlewareChain([mw], createContext("Svc.getItem", undefined), async () => "ok");
    assert.equal(logs.length, 1);
    assert.ok(logs[0].includes("Svc.getItem"));
    assert.ok(logs[0].includes("ms"));
  });

  it("logs error with message", async () => {
    const logs: string[] = [];
    const mw = withLogging({ logger: (msg) => logs.push(msg) });
    await assert.rejects(() =>
      executeMiddlewareChain([mw], createContext("Svc.bad", undefined), async () => {
        throw new Error("fail!");
      })
    );
    assert.equal(logs.length, 1);
    assert.ok(logs[0].includes("FAILED"));
    assert.ok(logs[0].includes("fail!"));
  });

  it("truncates params to maxParamLength", async () => {
    const logs: string[] = [];
    const mw = withLogging({ logger: (msg) => logs.push(msg), maxParamLength: 10 });
    await executeMiddlewareChain(
      [mw],
      createContext("Svc.m", { longKey: "x".repeat(500) }),
      async () => "ok"
    );
    assert.ok(logs[0].length < 200);
  });
});

// ─── withTimeout ────────────────────────────────────────────────────────────

describe("withTimeout", () => {
  it("succeeds within timeout", async () => {
    const mw = withTimeout(1000);
    const result = await executeMiddlewareChain(
      [mw],
      createContext("S.m", undefined),
      async () => "fast"
    );
    assert.equal(result, "fast");
  });

  it("rejects with BridgeTimeoutError when exceeded", async () => {
    const mw = withTimeout(10);
    await assert.rejects(
      () => executeMiddlewareChain(
        [mw],
        createContext("S.m", undefined),
        () => new Promise((resolve) => setTimeout(() => resolve("slow"), 200))
      ),
      (err) => {
        assert.ok(err instanceof BridgeTimeoutError);
        assert.equal(err.timeoutMs, 10);
        return true;
      }
    );
  });
});

// ─── withRetry ──────────────────────────────────────────────────────────────

describe("withRetry", () => {
  it("returns on first success without retry", async () => {
    let callCount = 0;
    const mw = withRetry({ maxRetries: 3, delay: 1 });
    const result = await executeMiddlewareChain(
      [mw],
      createContext("S.m", undefined),
      async () => { callCount++; return "ok"; }
    );
    assert.equal(result, "ok");
    assert.equal(callCount, 1);
  });

  it("retries and succeeds on second attempt", async () => {
    let callCount = 0;
    const mw = withRetry({ maxRetries: 3, delay: 1 });
    const result = await executeMiddlewareChain(
      [mw],
      createContext("S.m", undefined),
      async () => {
        callCount++;
        if (callCount < 2) throw new Error("transient");
        return "recovered";
      }
    );
    assert.equal(result, "recovered");
    assert.equal(callCount, 2);
  });

  it("exhausts retries and throws last error", async () => {
    let callCount = 0;
    const mw = withRetry({ maxRetries: 2, delay: 1 });
    await assert.rejects(
      () => executeMiddlewareChain(
        [mw],
        createContext("S.m", undefined),
        async () => { callCount++; throw new Error(`fail-${callCount}`); }
      ),
      { message: "fail-3" }
    );
    assert.equal(callCount, 3); // 1 initial + 2 retries
  });

  it("respects retryOn filter", async () => {
    let callCount = 0;
    const mw = withRetry({
      maxRetries: 3,
      delay: 1,
      retryOn: (err) => err instanceof Error && err.message === "retryable",
    });
    await assert.rejects(
      () => executeMiddlewareChain(
        [mw],
        createContext("S.m", undefined),
        async () => { callCount++; throw new Error("not-retryable"); }
      ),
      { message: "not-retryable" }
    );
    assert.equal(callCount, 1); // no retries
  });
});

// ─── withErrorNormalization ─────────────────────────────────────────────────

describe("withErrorNormalization", () => {
  it("wraps RPC error (with code) in BridgeError", async () => {
    const mw = withErrorNormalization();
    await assert.rejects(
      () => executeMiddlewareChain(
        [mw],
        createContext("S.m", undefined),
        async () => { throw { code: -32601, message: "Method not found", data: "extra" }; }
      ),
      (err) => {
        assert.ok(err instanceof BridgeError);
        assert.equal(err.code, -32601);
        assert.equal(err.message, "Method not found");
        assert.equal(err.data, "extra");
        return true;
      }
    );
  });

  it("passes through non-RPC errors unchanged", async () => {
    const mw = withErrorNormalization();
    const original = new TypeError("network failure");
    await assert.rejects(
      () => executeMiddlewareChain(
        [mw],
        createContext("S.m", undefined),
        async () => { throw original; }
      ),
      (err) => {
        assert.ok(err instanceof TypeError);
        assert.equal(err, original);
        return true;
      }
    );
  });

  it("preserves structured code/message/data fields", async () => {
    const mw = withErrorNormalization();
    await assert.rejects(
      () => executeMiddlewareChain(
        [mw],
        createContext("S.m", undefined),
        async () => { throw { code: -32000, message: "Custom error", data: { detail: "info" } }; }
      ),
      (err) => {
        assert.ok(err instanceof BridgeError);
        assert.equal(err.code, -32000);
        assert.equal(err.message, "Custom error");
        assert.deepEqual(err.data, { detail: "info" });
        return true;
      }
    );
  });

  it("does not re-wrap existing BridgeError instances", async () => {
    const mw = withErrorNormalization();
    const original = new BridgeError("already wrapped", -32001, { key: "val" });
    await assert.rejects(
      () => executeMiddlewareChain(
        [mw],
        createContext("S.m", undefined),
        async () => { throw original; }
      ),
      (err) => {
        assert.ok(err instanceof BridgeError);
        assert.equal(err, original);
        assert.equal(err.code, -32001);
        assert.deepEqual(err.data, { key: "val" });
        return true;
      }
    );
  });

  it("calls global error hook before rethrowing", async () => {
    const hookContexts: Array<{
      error: BridgeError;
      serviceName: string;
      methodName: string;
      elapsedMs: number;
      rawError: unknown;
    }> = [];
    const mw = withErrorNormalization({
      onError: (ctx) =>
        hookContexts.push({
          error: ctx.error,
          serviceName: ctx.serviceName,
          methodName: ctx.methodName,
          elapsedMs: ctx.elapsedMs,
          rawError: ctx.rawError,
        }),
    });
    await assert.rejects(
      () => executeMiddlewareChain(
        [mw],
        createContext("S.m", undefined),
        async () => { throw { code: -32603, message: "Internal" }; }
      ),
      (err) => {
        assert.ok(err instanceof BridgeError);
        return true;
      }
    );
    assert.equal(hookContexts.length, 1);
    assert.equal(hookContexts[0].error.code, -32603);
    assert.equal(hookContexts[0].error.message, "Internal");
    assert.equal(hookContexts[0].serviceName, "S");
    assert.equal(hookContexts[0].methodName, "m");
    assert.ok(hookContexts[0].elapsedMs >= 0);
    assert.deepEqual(hookContexts[0].rawError, { code: -32603, message: "Internal" });
  });

  it("global error hook fires for already-wrapped BridgeError", async () => {
    const hookContexts: Array<{ error: BridgeError; rawError: unknown }> = [];
    const mw = withErrorNormalization({
      onError: (ctx) => hookContexts.push({ error: ctx.error, rawError: ctx.rawError }),
    });
    const original = new BridgeError("wrapped", -1);
    await assert.rejects(
      () => executeMiddlewareChain(
        [mw],
        createContext("S.m", undefined),
        async () => { throw original; }
      ),
    );
    assert.equal(hookContexts.length, 1);
    assert.equal(hookContexts[0].error, original);
    assert.equal(hookContexts[0].rawError, original);
  });
});
