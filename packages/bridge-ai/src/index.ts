import type { BridgeClient, BridgeServiceContract } from "@agibuild/bridge";

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
