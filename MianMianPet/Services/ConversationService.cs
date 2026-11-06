using MianMianPet.Models;

namespace MianMianPet.Services;

public sealed class ConversationService
{
    private const string ContextStartIdKey = "conversation.context.start_id";
    private readonly DatabaseService _database;

    public ConversationService(DatabaseService database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<ChatMessage>> BuildContextAsync(ChatMessage currentUserMessage)
    {
        var contextStartId = await GetContextStartIdAsync();
        var history = await _database.GetRecentMessagesAsync(
            InternalAiConfig.ContextMessageCount,
            contextStartId);
        var selected = new LinkedList<ChatMessage>();
        var totalCharacters = currentUserMessage.Content.Length;

        for (var i = history.Count - 1; i >= 0; i--)
        {
            var message = history[i];
            if (totalCharacters + message.Content.Length > InternalAiConfig.MaxContextCharacters)
            {
                break;
            }

            selected.AddFirst(message);
            totalCharacters += message.Content.Length;
        }

        // Keep the context boundary on a complete exchange, including older stored conversations.
        while (selected.First is { Value.Role: "assistant" })
        {
            selected.RemoveFirst();
        }

        selected.AddLast(currentUserMessage);
        return selected.ToList();
    }

    public async Task SaveAsync(ChatMessage message)
    {
        await _database.AddMessageAsync(message);
    }

    public async Task SaveExchangeAsync(ChatMessage userMessage, ChatMessage assistantMessage)
    {
        await _database.AddExchangeAsync(userMessage, assistantMessage);
    }

    public async Task<string?> GetLastAssistantReplyAsync()
    {
        var contextStartId = await GetContextStartIdAsync();
        var history = await _database.GetRecentMessagesAsync(
            InternalAiConfig.ContextMessageCount,
            contextStartId);
        return history.LastOrDefault(message => message.Role == "assistant")?.Content;
    }

    public async Task ResetContextAsync()
    {
        var latestId = await _database.GetLatestConversationIdAsync();
        await _database.SetStateAsync(ContextStartIdKey, latestId.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private async Task<long> GetContextStartIdAsync()
    {
        var value = await _database.GetStateAsync(ContextStartIdKey);
        return long.TryParse(value, out var id) ? Math.Max(0, id) : 0;
    }
}
