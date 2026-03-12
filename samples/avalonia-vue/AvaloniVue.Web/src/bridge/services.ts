/**
 * Re-export generated bridge contracts as the only app-layer consumption surface.
 */
export {
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
