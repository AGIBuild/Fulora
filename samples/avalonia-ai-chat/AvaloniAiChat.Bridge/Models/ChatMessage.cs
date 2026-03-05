namespace AvaloniAiChat.Bridge.Models;

/// <summary>A single chat message.</summary>
public record ChatMessage(
    string Id,
    string Role,
    string Content,
    DateTime Timestamp);
