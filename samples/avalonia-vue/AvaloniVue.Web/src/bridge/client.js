import { createBridgeProfile } from '@agibuild/bridge/profile';
import { installBridgeMock } from './generated/bridge.mock';
export const bridgeProfile = createBridgeProfile({
    enableLogging: import.meta.env.DEV,
    logging: { maxParamLength: 200 },
    installMock: installBridgeMock,
});
export const bridge = bridgeProfile.bridge;
