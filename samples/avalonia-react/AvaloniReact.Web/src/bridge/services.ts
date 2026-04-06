/**
 * Re-exports from generated typed client and DTO declarations.
 * All handwritten types and service proxies have been replaced by the source generator output.
 */

export {
  createFuloraClient,
  services,
  appShellService,
  systemInfoService,
  chatService,
  fileService,
  settingsService,
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
