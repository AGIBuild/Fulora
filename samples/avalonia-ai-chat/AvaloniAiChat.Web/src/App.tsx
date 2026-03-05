import { useState, useRef, useEffect, useCallback } from 'react';

declare global {
  interface Window {
    agWebView?: {
      rpc?: {
        invoke: (method: string, params?: unknown, signal?: AbortSignal) => Promise<unknown>;
        _createAsyncIterable: (method: string, params?: unknown) => AsyncIterable<unknown>;
      };
    };
  }
}

interface Message {
  id: string;
  role: 'user' | 'assistant';
  content: string;
}

function useBridgeReady(): boolean {
  const [ready, setReady] = useState(false);
  useEffect(() => {
    const check = () => {
      if (window.agWebView?.rpc) { setReady(true); return; }
      requestAnimationFrame(check);
    };
    check();
  }, []);
  return ready;
}

export function App() {
  const bridgeReady = useBridgeReady();
  const [messages, setMessages] = useState<Message[]>([]);
  const [input, setInput] = useState('');
  const [streaming, setStreaming] = useState(false);
  const [backendInfo, setBackendInfo] = useState('');
  const abortRef = useRef<AbortController | null>(null);
  const messagesEndRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!bridgeReady) return;
    window.agWebView!.rpc!.invoke('AiChatService.getBackendInfo', {})
      .then((info) => setBackendInfo(info as string))
      .catch(() => {});
  }, [bridgeReady]);

  useEffect(() => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages]);

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
      const rpc = window.agWebView!.rpc!;
      const iterable = rpc._createAsyncIterable(
        'AiChatService.streamCompletion',
        { prompt: userMsg.content }
      ) as AsyncIterable<string>;

      for await (const token of iterable) {
        if (controller.signal.aborted) break;
        setMessages(prev => {
          const updated = [...prev];
          const last = updated[updated.length - 1];
          if (last && last.id === assistantMsg.id) {
            updated[updated.length - 1] = { ...last, content: last.content + token };
          }
          return updated;
        });
      }
    } catch (err: unknown) {
      if ((err as Error)?.name !== 'AbortError') {
        setMessages(prev => {
          const updated = [...prev];
          const last = updated[updated.length - 1];
          if (last && last.id === assistantMsg.id) {
            updated[updated.length - 1] = {
              ...last,
              content: last.content || `Error: ${(err as Error)?.message ?? 'Unknown error'}`,
            };
          }
          return updated;
        });
      }
    } finally {
      setStreaming(false);
      abortRef.current = null;
    }
  }, [input, streaming, bridgeReady]);

  const handleStop = useCallback(() => {
    abortRef.current?.abort();
  }, []);

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSend();
    }
  };

  if (!bridgeReady) {
    return (
      <div className="flex items-center justify-center h-screen bg-gray-950 text-gray-400">
        <div className="text-center space-y-3">
          <div className="w-8 h-8 border-2 border-blue-500 border-t-transparent rounded-full animate-spin mx-auto" />
          <p className="text-sm">Connecting to bridge...</p>
        </div>
      </div>
    );
  }

  const isDemo = backendInfo.toLowerCase().includes('echo');

  return (
    <div className="flex flex-col h-screen bg-gray-950 text-gray-100">
      {/* Header */}
      <header className="flex items-center justify-between px-6 py-3 border-b border-gray-800 bg-gray-900/80 backdrop-blur">
        <div className="flex items-center gap-3">
          <div className="w-8 h-8 rounded-lg bg-gradient-to-br from-blue-500 to-purple-600 flex items-center justify-center text-white font-bold text-sm">
            AI
          </div>
          <div>
            <h1 className="text-sm font-semibold">Fulora AI Chat</h1>
            <p className="text-xs text-gray-500">{backendInfo || 'Loading...'}</p>
          </div>
        </div>
      </header>

      {/* Demo mode banner */}
      {isDemo && (
        <div className="px-6 py-2 bg-amber-900/30 border-b border-amber-800/50 text-amber-300 text-xs text-center">
          Demo mode — responses are echoed. Set <code className="bg-amber-900/50 px-1 rounded">AI__PROVIDER=ollama</code> for real AI.
        </div>
      )}

      {/* Messages */}
      <div className="flex-1 overflow-y-auto px-6 py-4 space-y-4">
        {messages.length === 0 && (
          <div className="flex items-center justify-center h-full text-gray-600">
            <div className="text-center space-y-2">
              <p className="text-lg">Start a conversation</p>
              <p className="text-sm">Type a message below to begin streaming AI responses.</p>
            </div>
          </div>
        )}
        {messages.map((msg) => (
          <div key={msg.id} className={`flex ${msg.role === 'user' ? 'justify-end' : 'justify-start'}`}>
            <div className={`max-w-[75%] rounded-2xl px-4 py-2.5 text-sm leading-relaxed whitespace-pre-wrap ${
              msg.role === 'user'
                ? 'bg-blue-600 text-white'
                : 'bg-gray-800 text-gray-100'
            }`}>
              {msg.content}
              {msg.role === 'assistant' && msg.content === '' && streaming && (
                <span className="inline-block w-2 h-4 bg-gray-400 animate-pulse rounded-sm ml-0.5" />
              )}
            </div>
          </div>
        ))}
        <div ref={messagesEndRef} />
      </div>

      {/* Input area */}
      <div className="px-6 py-4 border-t border-gray-800 bg-gray-900/80 backdrop-blur">
        <div className="flex gap-3 items-end max-w-3xl mx-auto">
          <textarea
            value={input}
            onChange={e => setInput(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Type a message..."
            rows={1}
            className="flex-1 resize-none rounded-xl bg-gray-800 border border-gray-700 px-4 py-2.5 text-sm text-gray-100 placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-blue-500 focus:border-transparent"
            disabled={streaming}
          />
          {streaming ? (
            <button
              onClick={handleStop}
              className="px-4 py-2.5 rounded-xl bg-red-600 hover:bg-red-700 text-white text-sm font-medium transition-colors"
            >
              Stop
            </button>
          ) : (
            <button
              onClick={handleSend}
              disabled={!input.trim()}
              className="px-4 py-2.5 rounded-xl bg-blue-600 hover:bg-blue-700 disabled:bg-gray-700 disabled:text-gray-500 text-white text-sm font-medium transition-colors"
            >
              Send
            </button>
          )}
        </div>
      </div>
    </div>
  );
}
