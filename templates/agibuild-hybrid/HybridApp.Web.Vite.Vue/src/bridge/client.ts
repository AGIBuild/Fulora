import {
  createBridgeClient,
  type BridgeReadyOptions,
  withErrorNormalization,
  withLogging,
} from "@agibuild/bridge";
import { greeterService } from "./generated/bridge.client";

export type { GreeterService } from "./generated/bridge";

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
