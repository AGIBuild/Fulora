import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { BridgeError } from "../dist/index.js";
import { createBridgeProfile } from "../dist/profile.js";

describe("createBridgeProfile (dist)", () => {
  it("uses profile subpath API with resolver-backed bridge", async () => {
    const rpc = {
      async invoke(method) {
        if (method === "Svc.ping") {
          return "pong";
        }
        throw { code: -32601, message: "Method not found" };
      },
    };

    const profile = createBridgeProfile({
      resolveRpc: () => rpc,
    });

    await profile.ready();
    const result = await profile.bridge.invoke("Svc.ping");
    assert.equal(result, "pong");
  });

  it("registers error normalization in profile defaults", async () => {
    const rpc = {
      async invoke() {
        throw { code: -32000, message: "boom", data: { scope: "profile" } };
      },
    };

    const profile = createBridgeProfile({
      resolveRpc: () => rpc,
    });

    await profile.ready();
    await assert.rejects(
      () => profile.bridge.invoke("Svc.fail"),
      (err) => {
        assert.ok(err instanceof BridgeError);
        assert.equal(err.code, -32000);
        assert.equal(err.message, "boom");
        assert.deepEqual(err.data, { scope: "profile" });
        return true;
      },
    );
  });

  it("passes structured error context to normalization hook before rethrow", async () => {
    const rpc = {
      async invoke() {
        throw { code: -32001, message: "hook-me", data: { reason: "profile-default" } };
      },
    };

    const hookPayloads = [];
    const profile = createBridgeProfile({
      resolveRpc: () => rpc,
      errorNormalization: {
        onError: (ctx) => {
          hookPayloads.push({
            code: ctx.error.code,
            message: ctx.error.message,
            serviceName: ctx.serviceName,
            methodName: ctx.methodName,
            elapsedMs: ctx.elapsedMs,
            rawCode: ctx.rawError?.code,
          });
        },
      },
    });

    await profile.ready();
    await assert.rejects(() => profile.bridge.invoke("Svc.failingCall"), BridgeError);

    assert.equal(hookPayloads.length, 1);
    assert.equal(hookPayloads[0].code, -32001);
    assert.equal(hookPayloads[0].message, "hook-me");
    assert.equal(hookPayloads[0].serviceName, "Svc");
    assert.equal(hookPayloads[0].methodName, "failingCall");
    assert.ok(hookPayloads[0].elapsedMs >= 0);
    assert.equal(hookPayloads[0].rawCode, -32001);
  });

  it("installs mock when forceMock is enabled", async () => {
    const rpc = { invoke: async () => "ok" };
    const root = {};
    let installCalls = 0;

    const profile = createBridgeProfile({
      forceMock: true,
      installMock: () => {
        installCalls++;
        root.agWebView = { rpc };
      },
      resolveRpc: () => root.agWebView?.rpc ?? null,
    });

    await profile.ready();
    assert.equal(installCalls, 1);
    assert.equal(profile.isMockMode, true);
  });

  it("falls back to mock install after handshake timeout", async () => {
    const rpc = { invoke: async () => "ok" };
    const root = {};
    let installCalls = 0;

    const profile = createBridgeProfile({
      timeoutMs: 5,
      pollIntervalMs: 0,
      installMock: () => {
        installCalls++;
        root.agWebView = { rpc };
      },
      resolveRpc: () => root.agWebView?.rpc ?? null,
    });

    await profile.ready();
    assert.equal(installCalls, 1);
    assert.equal(profile.isMockMode, true);
    const result = await profile.bridge.invoke("Svc.any");
    assert.equal(result, "ok");
  });

  it("allows per-call ready timeout overrides", async () => {
    const rpc = { invoke: async () => "ok" };
    const root = {};
    let installCalls = 0;

    const profile = createBridgeProfile({
      timeoutMs: 5000,
      pollIntervalMs: 0,
      installMock: () => {
        installCalls++;
        root.agWebView = { rpc };
      },
      resolveRpc: () => root.agWebView?.rpc ?? null,
    });

    await profile.ready({ timeoutMs: 5 });
    assert.equal(installCalls, 1);
    assert.equal(profile.isMockMode, true);
  });
});
