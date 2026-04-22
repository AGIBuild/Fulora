import type { BridgeClient, BridgeServiceContract } from "@agibuild/fulora-client";

// ── Types matching C# DTOs ──

export interface AiChatRequest {
  message: string;
  systemPrompt?: string;
  provider?: string;
  modelId?: string;
}

export interface AiChatResult {
  text: string;
  modelId?: string;
  promptTokens?: number;
  completionTokens?: number;
}

export interface AiTypedChatRequest {
  message: string;
  jsonSchema: string;
  systemPrompt?: string;
  provider?: string;
  maxRetries?: number;
}

export interface AiConversationCreateRequest {
  systemPrompt?: string;
  provider?: string;
  modelId?: string;
}

export interface AiConversationMessageRequest {
  conversationId: string;
  message: string;
  provider?: string;
  modelId?: string;
  useTools?: boolean;
}

export interface AiConversationHistory {
  conversationId: string;
  messages: AiHistoryMessage[];
}

export interface AiHistoryMessage {
  role: string;
  text: string;
}

// ── Service contract matching IAiBridgeService ──

export interface IAiBridgeService {
  Complete(params: AiChatRequest): Promise<AiChatResult>;
  CompleteTyped(params: AiTypedChatRequest): Promise<string>;
  ListProviders(): Promise<string[]>;
  UploadBlob(params: {
    base64Data: string;
    mimeType: string;
    name?: string;
  }): Promise<string>;
  FetchBlob(params: { blobId: string }): Promise<string | null>;
  StreamCompletion(params: AiChatRequest): AsyncIterable<string>;
  RunWithTools(params: AiChatRequest): Promise<AiChatResult>;
  StreamWithTools(params: AiChatRequest): AsyncIterable<string>;
  CreateConversation(params: AiConversationCreateRequest): Promise<string>;
  SendMessage(params: AiConversationMessageRequest): Promise<AiChatResult>;
  StreamMessage(params: AiConversationMessageRequest): AsyncIterable<string>;
  GetHistory(params: { conversationId: string }): Promise<AiConversationHistory>;
  DeleteConversation(params: { conversationId: string }): Promise<void>;
}

// ── Helper class ──

export class AiBridgeClient {
  private readonly service: BridgeServiceContract<IAiBridgeService>;

  constructor(bridge: BridgeClient) {
    this.service = bridge.getService<IAiBridgeService>("AiBridgeService");
  }

  /** Send a chat completion request. */
  async complete(
    message: string,
    options?: {
      systemPrompt?: string;
      provider?: string;
      modelId?: string;
    }
  ): Promise<AiChatResult> {
    return this.service.Complete({
      message,
      systemPrompt: options?.systemPrompt,
      provider: options?.provider,
      modelId: options?.modelId,
    });
  }

  /** Send a structured (typed) chat completion request. */
  async completeTyped<T>(
    message: string,
    schema: object,
    options?: {
      systemPrompt?: string;
      provider?: string;
      maxRetries?: number;
    }
  ): Promise<T> {
    const raw = await this.service.CompleteTyped({
      message,
      jsonSchema: JSON.stringify(schema),
      systemPrompt: options?.systemPrompt,
      provider: options?.provider,
      maxRetries: options?.maxRetries ?? 3,
    });
    return JSON.parse(raw) as T;
  }

  /** List available AI provider names. */
  async listProviders(): Promise<string[]> {
    return this.service.ListProviders();
  }

  /** Upload a binary blob and get a blob ID. */
  async uploadBlob(
    data: ArrayBuffer | Uint8Array,
    mimeType: string,
    name?: string
  ): Promise<string> {
    const bytes =
      data instanceof Uint8Array ? data : new Uint8Array(data);
    const base64 = btoa(
      String.fromCharCode(...bytes)
    );
    return this.service.UploadBlob({
      base64Data: base64,
      mimeType,
      name,
    });
  }

  /** Run a chat completion with registered tools (tool-calling loop). */
  async runWithTools(
    message: string,
    options?: {
      systemPrompt?: string;
      provider?: string;
      modelId?: string;
    }
  ): Promise<AiChatResult> {
    return this.service.RunWithTools({
      message,
      systemPrompt: options?.systemPrompt,
      provider: options?.provider,
      modelId: options?.modelId,
    });
  }

  /** Stream a chat completion with registered tools. */
  async *streamWithTools(
    message: string,
    options?: {
      systemPrompt?: string;
      provider?: string;
      modelId?: string;
    }
  ): AsyncIterable<string> {
    yield* this.service.StreamWithTools({
      message,
      systemPrompt: options?.systemPrompt,
      provider: options?.provider,
      modelId: options?.modelId,
    });
  }

  /** Create a new conversation session. */
  async createConversation(options?: {
    systemPrompt?: string;
    provider?: string;
    modelId?: string;
  }): Promise<string> {
    return this.service.CreateConversation({
      systemPrompt: options?.systemPrompt,
      provider: options?.provider,
      modelId: options?.modelId,
    });
  }

  /** Send a message in a conversation. */
  async sendMessage(
    conversationId: string,
    message: string,
    options?: { provider?: string; modelId?: string; useTools?: boolean }
  ): Promise<AiChatResult> {
    return this.service.SendMessage({
      conversationId,
      message,
      provider: options?.provider,
      modelId: options?.modelId,
      useTools: options?.useTools,
    });
  }

  /** Stream a message in a conversation. */
  async *streamMessage(
    conversationId: string,
    message: string,
    options?: { provider?: string; modelId?: string; useTools?: boolean }
  ): AsyncIterable<string> {
    yield* this.service.StreamMessage({
      conversationId,
      message,
      provider: options?.provider,
      modelId: options?.modelId,
      useTools: options?.useTools,
    });
  }

  /** Get conversation history. */
  async getHistory(conversationId: string): Promise<AiConversationHistory> {
    return this.service.GetHistory({ conversationId });
  }

  /** Delete a conversation. */
  async deleteConversation(conversationId: string): Promise<void> {
    return this.service.DeleteConversation({ conversationId });
  }

  /** Stream chat completion tokens as they are generated. */
  async *streamCompletion(
    message: string,
    options?: {
      systemPrompt?: string;
      provider?: string;
      modelId?: string;
    }
  ): AsyncIterable<string> {
    const iterable = this.service.StreamCompletion({
      message,
      systemPrompt: options?.systemPrompt,
      provider: options?.provider,
      modelId: options?.modelId,
    });
    yield* iterable;
  }

  /** Fetch a blob by ID as a Uint8Array. */
  async fetchBlob(blobId: string): Promise<Uint8Array | null> {
    const base64 = await this.service.FetchBlob({ blobId });
    if (!base64) return null;
    const binaryString = atob(base64);
    const bytes = new Uint8Array(binaryString.length);
    for (let i = 0; i < binaryString.length; i++) {
      bytes[i] = binaryString.charCodeAt(i);
    }
    return bytes;
  }
}
