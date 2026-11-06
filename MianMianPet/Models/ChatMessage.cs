namespace MianMianPet.Models;

public sealed record ChatMessage(
    long Id,
    string Role,
    string Content,
    DateTimeOffset CreatedAt,
    MessageStatus Status)
{
    public static ChatMessage User(string content, MessageStatus status = MessageStatus.Complete) =>
        new(0, "user", content, DateTimeOffset.UtcNow, status);

    public static ChatMessage Assistant(string content) =>
        new(0, "assistant", content, DateTimeOffset.UtcNow, MessageStatus.Complete);
}

public enum MessageStatus
{
    Complete,
    Cancelled,
    Failed
}
