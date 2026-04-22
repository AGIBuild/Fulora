import {
  createBridgeClient,
  type BridgeReadyOptions,
  withErrorNormalization,
  withLogging,
} from "@agibuild/fulora-client";
import { greeterService } from "./generated/bridge.client";
import { installBridgeMock } from "./generated/bridge.mock";

export type { GreeterService } from "./generated/bridge";

export const isMockMode =
  import.meta.env.MODE === "mock" || import.meta.env.VITE_FULORA_MOCK === "true";

if (isMockMode) {
  installBridgeMock();
}

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
    greeter: greeterService,
  } as const;
}

export const services = createFuloraClient();

export { greeterService };
