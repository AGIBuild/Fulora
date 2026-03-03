import { describe, it } from "node:test";
import assert from "node:assert/strict";
import { createBridgeClient, withLogging, withErrorNormalization } from "../dist/index.js";
import type { BridgeRpc } from "../dist/index.js";

function createMockRpc(responses: Record<string, unknown>): BridgeRpc {
  return {
    async invoke(method: string, _params?: Record<string, unknown>) {
      if (method in responses) {
        return responses[method];
      }
      throw { code: -32601, message: `Method not found: ${method}` };
    },
  };
}

describe("bridge client integration with middleware", () => {
  it("logging middleware executes on service call", async () => {
    const logs: string[] = [];
    const rpc = createMockRpc({ "TestService.getUser": { name: "Alice" } });
    const client = createBridgeClient(() => rpc);

    client.use(withLogging({ logger: (msg) => logs.push(msg) }));

    const svc = client.getService<{ getUser: () => Promise<{ name: string }> }>("TestService");
    const result = await svc.getUser();

    assert.deepEqual(result, { name: "Alice" });
    assert.equal(logs.length, 1);
    assert.ok(logs[0].includes("TestService.getUser"));
    assert.ok(logs[0].includes("ms"));
  });

  it("error normalization wraps RPC errors from service calls", async () => {
    const rpc = createMockRpc({});
    const client = createBridgeClient(() => rpc);
    client.use(withErrorNormalization());

    const svc = client.getService<{ missing: () => Promise<void> }>("TestService");

    try {
      await svc.missing();
      assert.fail("Should have thrown");
    } catch (err: unknown) {
      assert.ok(err instanceof Error);
      assert.equal(err.constructor.name, "BridgeError");
    }
  });

  it("multiple middleware compose correctly on real service call", async () => {
    const logs: string[] = [];
    const rpc = createMockRpc({ "Svc.getData": [1, 2, 3] });
    const client = createBridgeClient(() => rpc);

    client.use(withLogging({ logger: (msg) => logs.push(msg) }));
    client.use(withErrorNormalization());

    const result = await client.invoke<number[]>("Svc.getData");

    assert.deepEqual(result, [1, 2, 3]);
    assert.equal(logs.length, 1);
  });

  it("client without middleware works identically", async () => {
    const rpc = createMockRpc({ "Svc.ping": "pong" });
    const client = createBridgeClient(() => rpc);

    const result = await client.invoke<string>("Svc.ping");
    assert.equal(result, "pong");
  });
});
