import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { ready, BridgeReadyTimeoutError } from "../dist/index.js";

function createFakeWindow() {
  const listeners = new Map();
  const fakeWindow = {
    __agWebViewReady: false,
    agWebView: undefined,
    addEventListener(name, handler) {
      const handlers = listeners.get(name) ?? [];
      handlers.push(handler);
      listeners.set(name, handlers);
    },
    removeEventListener(name, handler) {
      const handlers = listeners.get(name) ?? [];
      listeners.set(name, handlers.filter((h) => h !== handler));
    },
    emit(name) {
      const handlers = listeners.get(name) ?? [];
      for (const handler of handlers.slice()) {
        handler();
      }
    },
  };

  return fakeWindow;
}

describe("ready handshake and fallback semantics (dist)", () => {
  it("resolves from sticky state for late subscribers", async () => {
    const originalWindow = globalThis.window;
    const fakeWindow = createFakeWindow();
    fakeWindow.__agWebViewReady = true;
    fakeWindow.agWebView = { rpc: { invoke: async () => "ok" } };
    globalThis.window = fakeWindow;
    try {
      await ready({ timeoutMs: 30, pollIntervalMs: 0 });
    } finally {
      globalThis.window = originalWindow;
    }
  });

  it("resolves from ready event for early subscribers without polling", async () => {
    const originalWindow = globalThis.window;
    const fakeWindow = createFakeWindow();
    globalThis.window = fakeWindow;
    try {
      const wait = ready({ timeoutMs: 100, pollIntervalMs: 0 });
      setTimeout(() => {
        fakeWindow.agWebView = { rpc: { invoke: async () => "ok" } };
        fakeWindow.__agWebViewReady = true;
        fakeWindow.emit("agWebViewReady");
      }, 10);
      await wait;
    } finally {
      globalThis.window = originalWindow;
    }
  });

  it("times out deterministically when fallback polling cannot resolve", async () => {
    const originalWindow = globalThis.window;
    const fakeWindow = createFakeWindow();
    globalThis.window = fakeWindow;
    try {
      await assert.rejects(
        () => ready({ timeoutMs: 20, pollIntervalMs: 5 }),
        (err) => {
          assert.ok(err instanceof BridgeReadyTimeoutError);
          assert.equal(err.timeoutMs, 20);
          assert.equal(err.phase, "polling");
          return true;
        },
      );
    } finally {
      globalThis.window = originalWindow;
    }
  });

  it("reports handshake timeout when polling fallback is disabled", async () => {
    const originalWindow = globalThis.window;
    const fakeWindow = createFakeWindow();
    globalThis.window = fakeWindow;
    try {
      await assert.rejects(
        () => ready({ timeoutMs: 15, pollIntervalMs: 0 }),
        (err) => {
          assert.ok(err instanceof BridgeReadyTimeoutError);
          assert.equal(err.timeoutMs, 15);
          assert.equal(err.phase, "handshake");
          return true;
        },
      );
    } finally {
      globalThis.window = originalWindow;
    }
  });
});
