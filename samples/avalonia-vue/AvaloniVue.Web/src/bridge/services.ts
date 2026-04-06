/**
 * Re-export generated bridge contracts as the only app-layer consumption surface.
 */
export {
  createFuloraClient,
  services,
  appShellService,
  chatService,
  fileService,
  settingsService,
  systemInfoService,
} from './client';

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
} from './client';
