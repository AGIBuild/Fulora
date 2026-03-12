/**
 * Middleware-enabled bridge client setup.
 * Configures cross-cutting concerns (logging, error normalization) before any service calls.
 */

import { createBridgeProfile } from '@agibuild/bridge/profile';

export const bridgeProfile = createBridgeProfile({
  enableLogging: import.meta.env.DEV,
  logging: { maxParamLength: 100 },
});

export const bridge = bridgeProfile.bridge;
