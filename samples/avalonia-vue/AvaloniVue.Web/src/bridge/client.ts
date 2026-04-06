import {
  BridgeReadyTimeoutError,
  createBridgeClient,
  type BridgeReadyOptions,
  withErrorNormalization,
  withLogging,
} from '@agibuild/bridge';
import {
  appShellService,
  chatService,
  fileService,
  settingsService,
  systemInfoService,
} from './generated/bridge.client';
import { installBridgeMock } from './generated/bridge.mock';

export type {
  AppInfo,
  AppSettings,
  ChatMessage,
  ChatRequest,
  ChatResponse,
  FileEntry,
  PageDefinition,
  RuntimeMetrics,
  SystemInfo,
} from './generated/bridge.d';

const bridgeClient = createBridgeClient();

if (import.meta.env.DEV) {
  bridgeClient.use(withLogging({ maxParamLength: 200 }));
}

bridgeClient.use(withErrorNormalization());

let mockInstalled = false;

function installMockOnce() {
  if (mockInstalled) {
    return;
  }

  installBridgeMock();
  mockInstalled = true;
}

export const bridge = bridgeClient;

export const bridgeProfile = {
  bridge,
  get isMockMode() {
    return mockInstalled;
  },
  async ready(options?: BridgeReadyOptions) {
    try {
      await bridge.ready(options);
    } catch (err) {
      if (!mockInstalled && err instanceof BridgeReadyTimeoutError) {
        installMockOnce();
        await bridge.ready(options);
        return;
      }

      throw err;
    }
  },
};

export function createFuloraClient() {
  return {
    appShell: appShellService,
    chat: chatService,
    file: fileService,
    settings: settingsService,
    systemInfo: systemInfoService,
  } as const;
}

export const services = createFuloraClient();

export {
  appShellService,
  chatService,
  fileService,
  settingsService,
  systemInfoService,
};
