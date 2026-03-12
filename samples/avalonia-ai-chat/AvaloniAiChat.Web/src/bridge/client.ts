import { createBridgeProfile } from '@agibuild/bridge/profile';

export const bridgeProfile = createBridgeProfile({
  enableLogging: import.meta.env.DEV,
  logging: { maxParamLength: 200 },
});

export const bridge = bridgeProfile.bridge;
