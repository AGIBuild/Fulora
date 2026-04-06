/**
 * Middleware-enabled bridge client setup.
 * Configures cross-cutting concerns (logging, error normalization) before any service calls.
 */

import {
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
  bridgeClient.use(withLogging({ maxParamLength: 100 }));
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
