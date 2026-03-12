import { useState, useRef, useEffect, useCallback, type CSSProperties } from 'react';
import { useBridgeReady } from './hooks/useBridge';
import { aiChatService, windowShellBridgeService } from './bridge/services';
import type { AiModelState, WindowShellState, WindowShellSettings } from './bridge/services';

interface Message {
  id: string;
  role: 'user' | 'assistant';
  content: string;
}

type ThemeMode = 'liquid' | 'classic';
type ThemePreference = 'system' | 'liquid' | 'classic';
type SettingsTab = 'appearance' | 'model' | 'diagnostics';

type AppearanceSettings = WindowShellSettings;
type AppearanceState = WindowShellState;

const defaultAppearanceSettings: AppearanceSettings = {
  themePreference: 'system',
  enableTransparency: true,
  glassOpacityPercent: 78,
};

// ── Icon components ───────────────────────────────────────────────────────────

function IconChat() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
      <path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2z" />
    </svg>
  );
}

function IconSettings() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="12" cy="12" r="3" />
      <path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15a1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1Z" />
    </svg>
  );
}

function IconSend() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
      <line x1="22" y1="2" x2="11" y2="13" />
      <polygon points="22 2 15 22 11 13 2 9 22 2" />
    </svg>
  );
}

function IconStop() {
  return (
    <svg viewBox="0 0 24 24" fill="currentColor">
      <rect x="6" y="6" width="12" height="12" rx="2" />
    </svg>
  );
}

function IconRefresh() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <path d="M21 12a9 9 0 1 1-2.64-6.36" />
      <path d="M21 3v6h-6" />
    </svg>
  );
}

function IconExternalLink() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <path d="M14 3h7v7" />
      <path d="M10 14 21 3" />
      <path d="M21 14v7h-7" />
      <path d="M3 10V3h7" />
      <path d="M3 21h7" />
    </svg>
  );
}

function IconDownload() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <path d="M12 3v12" />
      <path d="m7 10 5 5 5-5" />
      <path d="M3 21h18" />
    </svg>
  );
}

function IconClose() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <path d="M18 6 6 18" />
      <path d="m6 6 12 12" />
    </svg>
  );
}

function IconCheck() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <path d="m20 6-11 11-5-5" />
    </svg>
  );
}

function IconPalette() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <path d="M12 22a10 10 0 1 1 0-20 8 8 0 0 1 0 16h-1a2 2 0 0 0 0 4h1Z" />
      <circle cx="7.5" cy="10.5" r="1" />
      <circle cx="12" cy="7.5" r="1" />
      <circle cx="16.5" cy="10.5" r="1" />
    </svg>
  );
}

function IconServer() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <rect x="3" y="4" width="18" height="6" rx="2" />
      <rect x="3" y="14" width="18" height="6" rx="2" />
      <path d="M7 7h.01" />
      <path d="M7 17h.01" />
    </svg>
  );
}

function IconInfo() {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
      <circle cx="12" cy="12" r="10" />
      <path d="M12 16v-4" />
      <path d="M12 8h.01" />
    </svg>
  );
}

// ── Main App ──────────────────────────────────────────────────────────────────

export function App() {
  const bridgeReady = useBridgeReady();
  const [appearance, setAppearance] = useState<AppearanceState | null>(null);
  const [draftAppearance, setDraftAppearance] = useState<AppearanceSettings | null>(null);
  const [settingsOpen, setSettingsOpen] = useState(false);
  const [activeSettingsTab, setActiveSettingsTab] = useState<SettingsTab>('appearance');
  const [savingAppearance, setSavingAppearance] = useState(false);
  const appearanceEpochRef = useRef(0);
  const [appearanceError, setAppearanceError] = useState<string | null>(null);
  const [modelState, setModelState] = useState<AiModelState | null>(null);
  const [modelError, setModelError] = useState<string | null>(null);
  const [downloadingModel, setDownloadingModel] = useState(false);
  const [needsModelSetup, setNeedsModelSetup] = useState(false);
  const [messages, setMessages] = useState<Message[]>([]);
  const [input, setInput] = useState('');
  const [streaming, setStreaming] = useState(false);
  const [backendInfo, setBackendInfo] = useState('');
  const [dragOver, setDragOver] = useState(false);
  const abortRef = useRef<AbortController | null>(null);
  const messagesEndRef = useRef<HTMLDivElement>(null);

  const activeThemeMode: ThemeMode = (appearance?.effectiveThemeMode as ThemeMode | undefined) ?? 'liquid';
  const isLiquid = activeThemeMode === 'liquid';

  const appliedAppearance = appearance?.settings ?? defaultAppearanceSettings;
  const transparencyEnabled = appliedAppearance.enableTransparency;
  const opacityPercent = appliedAppearance.glassOpacityPercent;
  const shellTopInset = appearance?.chromeMetrics?.safeInsets?.top ?? 0;
  const titleBarHeight = appearance?.chromeMetrics?.titleBarHeight ?? 44;

  const shellStyle = {
    '--ag-shell-top-inset': `${shellTopInset}px`,
    '--titlebar-h': `${titleBarHeight}px`,
    '--ag-glass-top': (0.05 + (opacityPercent / 100) * 0.45).toFixed(3),
    '--ag-glass-bottom': (0.02 + (opacityPercent / 100) * 0.28).toFixed(3),
    '--ag-glass-border': (0.08 + (opacityPercent / 100) * 0.42).toFixed(3),
    '--ag-orb-opacity': (0.15 + (opacityPercent / 100) * 0.7).toFixed(3),
    '--ag-surface-alpha-strong': (0.35 + (opacityPercent / 100) * 0.55).toFixed(3),
    '--ag-surface-alpha-soft': (0.22 + (opacityPercent / 100) * 0.42).toFixed(3),
    '--ag-user-alpha-main': (0.55 + (opacityPercent / 100) * 0.4).toFixed(3),
    '--ag-user-alpha-soft': (0.42 + (opacityPercent / 100) * 0.35).toFixed(3),
  } as CSSProperties;

  const isDemo = backendInfo.toLowerCase().includes('echo');
  const modelReady = modelState?.isModelAvailable ?? false;
  const modelDownloading = modelState?.isDownloading || downloadingModel;
  const modelProgress = modelState?.downloadProgressPercent ?? 0;
  const modelRequiredName = modelState?.requiredModel ?? 'qwen2.5:3b';
  const ollamaInstallUrl = modelState?.installUrl ?? 'https://ollama.com/download';
  const ollamaStartCommand = modelState?.startCommand ?? 'ollama serve';
  const stepInstallState = (modelState?.isOllamaInstalled ?? false) ? 'done' : (modelState?.nextStep === 'install-ollama' ? 'active' : 'pending');
  const stepStartState = (modelState?.isOllamaRunning ?? false)
    ? 'done'
    : ((modelState?.isOllamaInstalled ?? false) ? (modelState?.nextStep === 'start-ollama' ? 'active' : 'pending') : 'blocked');
  const stepDownloadState = (modelState?.isModelAvailable ?? false)
    ? 'done'
    : ((modelState?.isOllamaInstalled ?? false) && (modelState?.isOllamaRunning ?? false)
      ? (modelDownloading || modelState?.nextStep === 'download-model' ? 'active' : 'pending')
      : 'blocked');

  const statusDotClass = !bridgeReady ? 'status-dot--connecting'
    : modelReady || isDemo ? 'status-dot--ready'
    : modelDownloading ? 'status-dot--downloading'
    : 'status-dot--warning';

  // ── Data loading ──────────────────────────────────────────────────────────

  const loadAppearance = useCallback(async () => {
    if (!bridgeReady) return;
    const epoch = appearanceEpochRef.current;
    try {
      const state = await windowShellBridgeService.getWindowShellState();
      if (appearanceEpochRef.current === epoch) {
        setAppearance(state);
        setAppearanceError(null);
      }
    } catch (err) {
      if (appearanceEpochRef.current === epoch) {
        setAppearanceError((err as Error)?.message ?? 'Failed to load appearance.');
      }
    }
  }, [bridgeReady]);

  const loadModelState = useCallback(async () => {
    if (!bridgeReady) return;
    try {
      const state = await aiChatService.getModelState();
      setModelState(state);
      setNeedsModelSetup(!state.isModelAvailable);
      setModelError(null);
    } catch (err) {
      setModelError((err as Error)?.message ?? 'Failed to load model status.');
    }
  }, [bridgeReady]);

  useEffect(() => { void loadAppearance(); }, [loadAppearance]);
  useEffect(() => { void loadModelState(); }, [loadModelState]);

  useEffect(() => {
    if (!bridgeReady) return;
    let disposed = false;
    const stream = aiChatService.streamModelState();

    void (async () => {
      try {
        for await (const state of stream) {
          if (disposed) break;
          setModelState(state);
          setNeedsModelSetup(!state.isModelAvailable);
          setModelError(null);
        }
      } catch (err) {
        if (!disposed) setModelError((err as Error)?.message ?? 'Stream disconnected.');
      }
    })();

    return () => { disposed = true; };
  }, [bridgeReady]);

  useEffect(() => {
    if (!bridgeReady) return;
    let disposed = false;
    const stream = windowShellBridgeService.streamWindowShellState();

    void (async () => {
      try {
        for await (const state of stream) {
          if (disposed) break;
          setAppearance(state);
          setAppearanceError(null);
        }
      } catch (err) {
        if (!disposed) setAppearanceError((err as Error)?.message ?? 'Appearance stream disconnected.');
      }
    })();

    return () => { disposed = true; };
  }, [bridgeReady]);

  useEffect(() => {
    if (!bridgeReady) return;
    aiChatService.getBackendInfo()
      .then((info: string) => setBackendInfo(info))
      .catch(() => {});
  }, [bridgeReady]);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: streaming ? 'auto' : 'smooth', block: 'end' });
  }, [messages, streaming]);

  useEffect(() => {
    if (!bridgeReady) return;
    let disposed = false;
    const stream = aiChatService.streamDroppedFiles();

    void (async () => {
      try {
        for await (const result of stream) {
          if (disposed) break;
          if (!result?.content) continue;
          const prefix = `[File: ${result.fileName}]\n`;
          setInput(prev => prev ? `${prev}\n${prefix}${result.content}` : `${prefix}${result.content}`);
        }
      } catch {
        // Ignore stream disconnect during shutdown/reload.
      }
    })();

    return () => { disposed = true; };
  }, [bridgeReady]);

  const openSettings = useCallback((tab?: SettingsTab) => {
    setDraftAppearance({ ...(appearance?.settings ?? defaultAppearanceSettings) });
    setActiveSettingsTab(tab ?? (needsModelSetup ? 'model' : 'appearance'));
    setSettingsOpen(true);
  }, [appearance?.settings, needsModelSetup]);

  // ── Chat ──────────────────────────────────────────────────────────────────

  const handleSend = useCallback(async () => {
    if (!input.trim() || streaming || !bridgeReady) return;

    const userMsg: Message = { id: crypto.randomUUID(), role: 'user', content: input.trim() };
    const assistantMsg: Message = { id: crypto.randomUUID(), role: 'assistant', content: '' };
    setMessages(prev => [...prev, userMsg, assistantMsg]);
    setInput('');
    setStreaming(true);

    const controller = new AbortController();
    abortRef.current = controller;

    try {
      const iterable = aiChatService.streamCompletion(userMsg.content, { signal: controller.signal });

      for await (const token of iterable) {
        if (controller.signal.aborted) break;
        setMessages(prev => {
          const updated = [...prev];
          const last = updated[updated.length - 1];
          if (last?.id === assistantMsg.id)
            updated[updated.length - 1] = { ...last, content: last.content + token };
          return updated;
        });
      }
    } catch (err: unknown) {
      if ((err as Error)?.name !== 'AbortError') {
        const errText = (err as Error)?.message ?? 'Unknown error';
        const modelMissing = /model.*missing|not found|ollama|cannot connect|connection refused/i.test(errText);
        if (modelMissing) {
          setNeedsModelSetup(true);
          openSettings('model');
          setModelError(errText);
          void loadModelState();
        }
        setMessages(prev => {
          const updated = [...prev];
          const last = updated[updated.length - 1];
          if (last?.id === assistantMsg.id) {
            updated[updated.length - 1] = {
              ...last,
              content: last.content + `\n\n[Error: ${errText}]${modelMissing ? '\n\nOpen settings to download the required model.' : ''}`,
            };
          }
          return updated;
        });
      }
    } finally {
      setStreaming(false);
      abortRef.current = null;
    }
  }, [input, streaming, bridgeReady, loadModelState, openSettings]);

  const handleStop = useCallback(() => { abortRef.current?.abort(); }, []);

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) { e.preventDefault(); handleSend(); }
  };

  // ── Settings ──────────────────────────────────────────────────────────────

  const saveAppearanceSettings = async () => {
    if (!bridgeReady || !draftAppearance) return;
    setSavingAppearance(true);
    appearanceEpochRef.current++;
    try {
      const updated = await windowShellBridgeService.updateWindowShellSettings(draftAppearance);
      setAppearance(updated);
      setAppearanceError(null);
      setSettingsOpen(false);
    } catch (err) {
      setAppearanceError((err as Error)?.message ?? 'Failed to save.');
    } finally {
      setSavingAppearance(false);
    }
  };

  const downloadRequiredModel = async () => {
    if (!bridgeReady) return;
    setDownloadingModel(true);
    try {
      const state = await aiChatService.downloadRequiredModel();
      setModelState(state);
      setNeedsModelSetup(!state.isModelAvailable);
      setModelError(null);
    } catch (err) {
      setModelError((err as Error)?.message ?? 'Failed to download model.');
    } finally {
      setDownloadingModel(false);
      void loadModelState();
    }
  };

  // ── Drag & Drop ───────────────────────────────────────────────────────────

  const handleDragOver = (e: React.DragEvent) => { e.preventDefault(); setDragOver(true); };
  const handleDragLeave = () => setDragOver(false);
  const handleDrop = (e: React.DragEvent) => { e.preventDefault(); setDragOver(false); };

  // ── Render ────────────────────────────────────────────────────────────────

  return (
    <div
      className={`app-shell ${isLiquid ? 'liquid-shell' : 'classic-shell'} ${transparencyEnabled ? '' : 'no-transparency'}`}
      style={shellStyle}
      onDragOver={handleDragOver}
      onDragLeave={handleDragLeave}
      onDrop={handleDrop}
    >
      {isLiquid && transparencyEnabled && (
        <div className="liquid-bg" aria-hidden>
          <div className="liquid-orb liquid-orb--1" />
          <div className="liquid-orb liquid-orb--2" />
          <div className="liquid-orb liquid-orb--3" />
        </div>
      )}

      {dragOver && (
        <div className="drop-overlay">
          <p className="drop-overlay__title">Drop file here</p>
          <p className="drop-overlay__hint">Text files will be sent to AI</p>
        </div>
      )}

      {/* ── Rail ─────────────────────────────────────────────────────────── */}
      <nav className="rail">
        <div className="rail__top">
          <div className="rail__logo">AI</div>
          <button
            onClick={() => setSettingsOpen(false)}
            className={`rail__btn ${!settingsOpen ? 'rail__btn--active' : ''}`}
            data-tooltip="Chat"
            aria-label="Chat"
          >
            <IconChat />
          </button>
        </div>
        <div className="rail__bottom">
          <button
            onClick={() => openSettings()}
            className={`rail__btn ${settingsOpen ? 'rail__btn--active' : ''}`}
            data-tooltip="Settings"
            aria-label="Settings"
          >
            <IconSettings />
          </button>
        </div>
      </nav>

      {/* ── Main area ────────────────────────────────────────────────────── */}
      <main className="chat-main">
        {/* Header */}
        <header className="chat-header">
          <div className="chat-header__left">
            <h1 className="chat-header__title">Fulora AI Chat</h1>
            <span className={`status-dot ${statusDotClass}`} />
            <span className="chat-header__status">
              {!bridgeReady ? 'Connecting...'
                : isDemo ? 'Echo mode'
                : modelReady ? modelState?.requiredModel ?? ''
                : modelDownloading ? `Downloading ${modelProgress}%`
                : 'Setup required'}
            </span>
          </div>
        </header>

        {/* Inline alert banner — only one at a time */}
        {bridgeReady && isDemo && (
          <div className="alert-bar alert-bar--warn">
            Demo mode — responses are echoed. Set <code>AI__PROVIDER=ollama</code> for real AI.
          </div>
        )}
        {bridgeReady && !isDemo && needsModelSetup && modelState && !modelState.isModelAvailable && !modelDownloading && (
          <div className="alert-bar alert-bar--error">
            <span>
              {modelState.nextStep === 'install-ollama' ? 'Ollama is not installed.'
                : modelState.nextStep === 'start-ollama' ? 'Ollama is not running.'
                : `Model "${modelState.requiredModel}" not found.`}
            </span>
            <button onClick={() => openSettings('model')} className="alert-bar__action">Fix in Settings</button>
          </div>
        )}
        {bridgeReady && !isDemo && modelDownloading && (
          <div className="alert-bar alert-bar--info">
            <span>Downloading model... {modelProgress > 0 ? `${modelProgress}%` : 'initializing'}</span>
            <div className="progress-track"><div className="progress-fill" style={{ width: `${modelProgress}%` }} /></div>
          </div>
        )}

        {/* Messages */}
        <div className="chat-messages">
          {!bridgeReady && (
            <div className="chat-empty">
              <div className="spinner" />
              <p>Connecting to bridge...</p>
            </div>
          )}
          {bridgeReady && messages.length === 0 && (
            <div className="chat-empty">
              <p className="chat-empty__title">Start a conversation</p>
              <p className="chat-empty__hint">Type a message below to begin.</p>
            </div>
          )}
          {messages.map((msg) => (
            <div key={msg.id} className={`msg-row ${msg.role === 'user' ? 'msg-row--user' : 'msg-row--assistant'}`}>
              <div className={`msg-bubble ${msg.role === 'user' ? 'msg-bubble--user' : 'msg-bubble--assistant'}`}>
                {msg.content}
                {msg.role === 'assistant' && msg.content === '' && streaming && (
                  <span className="typing-cursor" />
                )}
              </div>
            </div>
          ))}
          <div ref={messagesEndRef} />
        </div>

        {/* Input */}
        <div className="chat-input-bar">
          <div className="chat-input-bar__inner">
            <textarea
              value={input}
              onChange={e => setInput(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="Type a message..."
              rows={1}
              className="chat-textarea"
              disabled={streaming || !bridgeReady}
            />
            {streaming ? (
              <button onClick={handleStop} className="send-btn send-btn--stop" aria-label="Stop">
                <IconStop />
              </button>
            ) : (
              <button
                onClick={handleSend}
                disabled={!input.trim() || !bridgeReady}
                className="send-btn send-btn--send"
                aria-label="Send"
              >
                <IconSend />
              </button>
            )}
          </div>
        </div>
      </main>

      {/* ── Settings Panel ───────────────────────────────────────────────── */}
      {settingsOpen && draftAppearance && (
        <div className="settings-overlay" onClick={() => setSettingsOpen(false)}>
          <div className="settings-panel" onClick={e => e.stopPropagation()}>
            <div className="settings-panel__header">
              <h2>Settings</h2>
              <button
                type="button"
                onClick={() => setSettingsOpen(false)}
                className="icon-btn icon-btn--ghost"
                data-tooltip="Close"
                aria-label="Close"
              >
                <IconClose />
              </button>
            </div>

            <div className="settings-tabs" role="tablist" aria-label="Settings tabs">
              <button
                type="button"
                role="tab"
                aria-selected={activeSettingsTab === 'appearance'}
                onClick={() => setActiveSettingsTab('appearance')}
                className={`icon-btn icon-btn--tab ${activeSettingsTab === 'appearance' ? 'icon-btn--tab-active' : ''}`}
                aria-label="Appearance"
              >
                <span className="tab-btn__content">
                  <IconPalette />
                  <span className="tab-btn__label">Appearance</span>
                </span>
              </button>
              <button
                type="button"
                role="tab"
                aria-selected={activeSettingsTab === 'model'}
                onClick={() => setActiveSettingsTab('model')}
                className={`icon-btn icon-btn--tab ${activeSettingsTab === 'model' ? 'icon-btn--tab-active' : ''}`}
                aria-label="AI Model"
              >
                <span className="tab-btn__content">
                  <IconServer />
                  <span className="tab-btn__label">AI Model</span>
                </span>
              </button>
              <button
                type="button"
                role="tab"
                aria-selected={activeSettingsTab === 'diagnostics'}
                onClick={() => setActiveSettingsTab('diagnostics')}
                className={`icon-btn icon-btn--tab ${activeSettingsTab === 'diagnostics' ? 'icon-btn--tab-active' : ''}`}
                aria-label="Diagnostics"
              >
                <span className="tab-btn__content">
                  <IconInfo />
                  <span className="tab-btn__label">Diagnostics</span>
                </span>
              </button>
            </div>

            <div className="settings-panel__body">
              {activeSettingsTab === 'appearance' && (
                <section className="settings-section settings-tab-content">
                  <h3 className="settings-section__title">Appearance</h3>
                  <div className="settings-row">
                    <label className="settings-label">Theme</label>
                    <select
                      value={draftAppearance.themePreference}
                      onChange={e => setDraftAppearance({ ...draftAppearance, themePreference: e.target.value as ThemePreference })}
                      className="settings-select"
                    >
                      <option value="system">System</option>
                      <option value="liquid">Liquid Glass</option>
                      <option value="classic">Classic</option>
                    </select>
                  </div>
                  <div className="settings-row">
                    <label className="settings-label">Window transparency</label>
                    <button
                      type="button"
                      onClick={() => setDraftAppearance({ ...draftAppearance, enableTransparency: !draftAppearance.enableTransparency })}
                      className={`toggle ${draftAppearance.enableTransparency ? 'toggle--on' : ''}`}
                    >
                      <span className="toggle__thumb" />
                    </button>
                  </div>
                  <div className="settings-row settings-row--note">
                    <p className="settings-note">
                      Controls native window composition. Glass opacity only adjusts intensity when transparency is enabled.
                    </p>
                  </div>
                  <div className="settings-row">
                    <label className="settings-label">
                      Glass opacity
                      <span className="settings-label__value">
                        {draftAppearance.enableTransparency ? `${draftAppearance.glassOpacityPercent}%` : 'Disabled'}
                      </span>
                    </label>
                    <input
                      type="range" min={20} max={95}
                      value={draftAppearance.glassOpacityPercent}
                      onChange={e => setDraftAppearance({ ...draftAppearance, glassOpacityPercent: Number(e.target.value) })}
                      className="settings-slider"
                      disabled={!draftAppearance.enableTransparency}
                    />
                  </div>
                </section>
              )}

              {activeSettingsTab === 'model' && (
                <section className="settings-section settings-tab-content">
                  <h3 className="settings-section__title">AI Model</h3>
                  {isDemo ? (
                    <div className="hint-box hint-box--warn">
                      Echo mode is active. To use Ollama:
                      <br />
                      1) set <code>AI__PROVIDER=ollama</code> (or unset <code>AI__PROVIDER</code>);
                      <br />
                      2) ensure Ollama is installed and running (<code>ollama serve</code>);
                      <br />
                      3) reopen this app and download the required model.
                    </div>
                  ) : (
                    <>
                      <div className="model-card">
                        <div className="model-card__row">
                          <span className="model-card__label">Model</span>
                          <span className="model-card__value">{modelRequiredName}</span>
                        </div>
                        <div className="model-card__row">
                          <span className="model-card__label">Status</span>
                          <span className="model-card__value">{modelState?.statusMessage ?? 'Loading...'}</span>
                        </div>
                        {modelDownloading && (
                          <div className="progress-track"><div className="progress-fill" style={{ width: `${modelProgress}%` }} /></div>
                        )}
                        <div className="model-card__actions">
                          <button
                            type="button"
                            onClick={() => { void loadModelState(); }}
                            className="icon-btn icon-btn--ghost"
                            data-tooltip="Refresh Status"
                            aria-label="Refresh Status"
                          >
                            <IconRefresh />
                          </button>
                        </div>
                      </div>

                      <ol className="setup-steps">
                        <li className={`setup-step setup-step--${stepInstallState}`}>
                          <div className="setup-step__head">
                            <span className="setup-step__index">1</span>
                            <div className="setup-step__meta">
                              <p className="setup-step__title">Install Ollama</p>
                              <p className="setup-step__desc">Download and install Ollama runtime.</p>
                            </div>
                            <span className="setup-step__badge">
                              {stepInstallState === 'done' ? 'Done' : stepInstallState === 'active' ? 'Now' : 'Pending'}
                            </span>
                          </div>
                          <div className="setup-step__actions">
                            <a
                              href={ollamaInstallUrl}
                              target="_blank"
                              rel="noreferrer"
                              className="icon-btn icon-btn--ghost"
                              data-tooltip="Open Download Page"
                              aria-label="Open Download Page"
                            >
                              <IconExternalLink />
                            </a>
                          </div>
                        </li>

                        <li className={`setup-step setup-step--${stepStartState}`}>
                          <div className="setup-step__head">
                            <span className="setup-step__index">2</span>
                            <div className="setup-step__meta">
                              <p className="setup-step__title">Start Ollama Service</p>
                              <p className="setup-step__desc">Run <code>{ollamaStartCommand}</code> in terminal, then refresh status.</p>
                            </div>
                            <span className="setup-step__badge">
                              {stepStartState === 'done' ? 'Done' : stepStartState === 'active' ? 'Now' : stepStartState === 'blocked' ? 'Blocked' : 'Pending'}
                            </span>
                          </div>
                          <div className="setup-step__actions">
                            <button
                              type="button"
                              onClick={() => { void loadModelState(); }}
                              className="icon-btn icon-btn--ghost"
                              data-tooltip="Refresh"
                              aria-label="Refresh"
                            >
                              <IconRefresh />
                            </button>
                          </div>
                        </li>

                        <li className={`setup-step setup-step--${stepDownloadState}`}>
                          <div className="setup-step__head">
                            <span className="setup-step__index">3</span>
                            <div className="setup-step__meta">
                              <p className="setup-step__title">Download Required Model</p>
                              <p className="setup-step__desc">Fetch <code>{modelRequiredName}</code> to local Ollama.</p>
                            </div>
                            <span className="setup-step__badge">
                              {stepDownloadState === 'done' ? 'Done' : stepDownloadState === 'active' ? 'Now' : stepDownloadState === 'blocked' ? 'Blocked' : 'Pending'}
                            </span>
                          </div>
                          <div className="setup-step__actions">
                            <button
                              type="button"
                              onClick={() => { void downloadRequiredModel(); }}
                              disabled={downloadingModel || modelState?.isDownloading || modelState?.isModelAvailable || !modelState?.isOllamaInstalled || !modelState?.isOllamaRunning}
                              className="icon-btn icon-btn--primary"
                              data-tooltip={downloadingModel || modelState?.isDownloading ? 'Downloading...' : modelState?.isModelAvailable ? 'Ready' : 'Download'}
                              aria-label="Download"
                            >
                              <IconDownload />
                            </button>
                          </div>
                        </li>
                      </ol>
                    </>
                  )}
                </section>
              )}

              {activeSettingsTab === 'diagnostics' && (
                <section className="settings-section settings-tab-content">
                  <h3 className="settings-section__title">Diagnostics</h3>
                  <div className="model-card">
                    <div className="model-card__row">
                      <span className="model-card__label">Endpoint</span><span className="model-card__value">{modelState?.endpoint ?? '...'}</span>
                    </div>
                    <div className="model-card__row">
                      <span className="model-card__label">Ollama installed</span><span className="model-card__value">{modelState?.isOllamaInstalled ? 'Yes' : 'No'}</span>
                    </div>
                    <div className="model-card__row">
                      <span className="model-card__label">Ollama running</span><span className="model-card__value">{modelState?.isOllamaRunning ? 'Yes' : 'No'}</span>
                    </div>
                    <div className="model-card__row">
                      <span className="model-card__label">Stage</span><span className="model-card__value">{modelState?.downloadStage ?? 'idle'}</span>
                    </div>
                  </div>

                  {appearance && (
                    <div className="model-card">
                      <div className="model-card__row">
                        <span className="model-card__label">Platform</span><span className="model-card__value">{appearance.capabilities.platform}</span>
                      </div>
                      <div className="model-card__row">
                        <span className="model-card__label">Effective level</span><span className="model-card__value">{appearance.capabilities.effectiveTransparencyLevel}</span>
                      </div>
                      <div className="model-card__row">
                        <span className="model-card__label">Supported</span><span className="model-card__value">{appearance.capabilities.supportsTransparency ? 'Yes' : 'Unknown'}</span>
                      </div>
                      <div className="model-card__row">
                        <span className="model-card__label">Applied opacity</span><span className="model-card__value">{appearance.capabilities.appliedOpacityPercent}%</span>
                      </div>
                      <div className="model-card__row">
                        <span className="model-card__label">Title bar height</span><span className="model-card__value">{Math.round(appearance.chromeMetrics.titleBarHeight)}px</span>
                      </div>
                      <div className="model-card__row">
                        <span className="model-card__label">Safe top inset</span><span className="model-card__value">{Math.round(appearance.chromeMetrics.safeInsets.top)}px</span>
                      </div>
                      <p className="diagnostics-note">{appearance.capabilities.validationMessage}</p>
                    </div>
                  )}

                  {appearanceError && <div className="error-tip">{appearanceError}</div>}
                  {modelError && <div className="error-tip">{modelError}</div>}
                </section>
              )}
            </div>

            <div className="settings-panel__footer">
              <button
                type="button"
                onClick={() => setSettingsOpen(false)}
                className="icon-btn icon-btn--ghost"
                data-tooltip="Cancel"
                aria-label="Cancel"
              >
                <IconClose />
              </button>
              <button
                type="button"
                onClick={() => { void saveAppearanceSettings(); }}
                disabled={savingAppearance}
                className="icon-btn icon-btn--primary"
                data-tooltip={savingAppearance ? 'Applying...' : 'Apply'}
                aria-label={savingAppearance ? 'Applying' : 'Apply'}
              >
                <IconCheck />
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
