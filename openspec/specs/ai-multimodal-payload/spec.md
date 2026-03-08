## Purpose

Specifies binary upload/download scheme handlers (`app://ai/upload`, `app://ai/blob`) and MIME type support for AI multimodal content such as images and audio.

## Requirements

### Requirement: Chunked binary upload via scheme handler
The system SHALL provide an `app://ai/upload/{id}` scheme endpoint that accepts binary data from JS via `fetch()` POST and delivers it as a `Stream` to C#.

#### Scenario: JS uploads image for AI vision
- **WHEN** JS performs `fetch("app://ai/upload/img-001", { method: "POST", body: imageBlob })`
- **THEN** C# receives a `Stream` with the image data and Content-Type header from the request

#### Scenario: Upload with MIME type
- **WHEN** JS includes `Content-Type: image/png` header in the upload request
- **THEN** C# receives the MIME type metadata alongside the stream

### Requirement: Binary download via scheme handler
The system SHALL provide an `app://ai/blob/{id}` scheme endpoint that serves binary data from C# to JS via `fetch()` GET.

#### Scenario: JS fetches AI-generated audio
- **WHEN** C# registers a blob with `IAiPayloadStore.RegisterBlob("audio-001", stream, "audio/wav")`
- **THEN** JS can `fetch("app://ai/blob/audio-001")` and receive the audio data with correct Content-Type

#### Scenario: Blob auto-expiry
- **WHEN** a registered blob is not accessed within the configured TTL (default 5 minutes)
- **THEN** the blob is automatically removed from the store

### Requirement: MIME type annotation on bridge payload
The system SHALL support MIME type metadata on binary bridge payloads for AI multimodal content.

#### Scenario: Bridge call with typed binary payload
- **WHEN** a `[JsExport]` method accepts `AiMediaPayload` parameter (containing `byte[] Data` and `string MimeType`)
- **THEN** JS can pass `{ data: Uint8Array, mimeType: "image/png" }` and C# receives the typed payload

### Requirement: Large payload chunking
The system SHALL automatically chunk binary payloads exceeding a configurable threshold (default 1MB) to avoid WebMessage size limits.

#### Scenario: Payload below threshold
- **WHEN** a binary payload is 500KB
- **THEN** the system transmits it in a single WebMessage (Base64 encoded)

#### Scenario: Payload above threshold uses scheme handler
- **WHEN** a binary payload is 5MB
- **THEN** the system automatically routes through the `app://ai/upload` scheme handler instead of WebMessage
