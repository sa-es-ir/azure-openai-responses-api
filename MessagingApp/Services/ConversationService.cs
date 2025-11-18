using Azure.AI.OpenAI;
using MessagingApp.Models;
using Microsoft.Extensions.Options;
using OpenAI.Assistants;

namespace MessagingApp.Services;
#pragma warning disable OPENAI001

public class ConversationService(AzureOpenAIClient azureOpenAIClient,
    IOptions<AssistantOptions> assistantOptions,
    ILogger<ConversationService> logger)
{
    private readonly List<Conversation> _conversations = new();
    private readonly object _lock = new();

    private readonly AssistantClient _assistantClient = azureOpenAIClient.GetAssistantClient();

    // Event raised whenever conversations list or a conversation's metadata changes
    public event Action? ConversationsChanged;
    private void RaiseChanged() => ConversationsChanged?.Invoke();

    public List<Conversation> GetAllConversations()
    {
        lock (_lock)
        {
            return _conversations.OrderByDescending(c => c.LastMessageAt).ToList();
        }
    }

    public Conversation? GetConversation(string conversationId)
    {
        lock (_lock)
        {
            return _conversations.FirstOrDefault(c => c.Id == conversationId);
        }
    }

    public async Task<Conversation> CreateOrGetConversationAsync(string userName)
    {
        bool created = false;
        Conversation? conversation;
        var existingConversation = _conversations.FirstOrDefault(c => c.UserName == userName);
        if (existingConversation != null)
        {
            conversation = existingConversation;
        }
        else
        {
            var assistantThread = await _assistantClient.CreateThreadAsync();
            conversation = new Conversation
            {
                UserName = userName,
                Title = "New Conversation",
                ThreadId = assistantThread.Value.Id
            };
            _conversations.Add(conversation);
            created = true;
        }
        if (created) RaiseChanged();
        return conversation!;
    }

    public async Task<Message?> AddMessageAsync(string conversationId, string text, bool isFromUser)
    {
        bool changed = false;
        var conversation = _conversations.FirstOrDefault(c => c.Id == conversationId);
        if (conversation == null)
            return null;

        if (isFromUser)
        {
            var message = new Message
            {
                Text = text,
                IsFromUser = isFromUser,
                ConversationId = conversationId,
            };

            conversation.Messages.Add(message);
            conversation.LastMessageAt = DateTime.Now;

            if (conversation.Messages.Count(m => m.IsFromUser) == 1)
            {
                conversation.Title = text.Length > 50 ? text.Substring(0, 50) + "..." : text;
            }

            await _assistantClient.CreateMessageAsync(conversation.ThreadId, MessageRole.User,
               [MessageContent.FromText(text)]);
        }
        else
        {
            var run = await _assistantClient.CreateRunAsync(conversation.ThreadId, assistantOptions.Value.AssistantId);

            do
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500));
                run = await _assistantClient.GetRunAsync(conversation.ThreadId, run.Value.Id);
            }
            while (run.Value.Status == RunStatus.Queued || run.Value.Status == RunStatus.InProgress);

            if (run.Value.Status == RunStatus.Completed)
            {
                var collection = _assistantClient.GetMessagesAsync(conversation.ThreadId, new MessageCollectionOptions { PageSizeLimit = 1 });
                await foreach (var assistantMessage in collection)
                {
                    if (assistantMessage.Role == MessageRole.Assistant)
                    {
                        var assistantText = assistantMessage.Content[0].Text;
                        if (!string.IsNullOrEmpty(assistantText))
                        {
                            var assistantResponseMessage = new Message
                            {
                                Text = assistantText,
                                IsFromUser = false,
                                ConversationId = conversationId,
                            };
                            conversation.Messages.Add(assistantResponseMessage);
                            conversation.LastMessageAt = DateTime.Now;
                            changed = true;
                        }
                        break;
                    }
                }
            }

            logger.LogError("AI run failed with status: {Status} for threadId: {threadId}", run.Value.Status, conversation.ThreadId);

        }

        changed = true;

        if (changed) RaiseChanged();

        return conversation.Messages.OrderByDescending(x => x.Timestamp).First();
    }

    public void DeleteConversation(string conversationId)
    {
        bool deleted = false;
        lock (_lock)
        {
            var conversation = _conversations.FirstOrDefault(c => c.Id == conversationId);
            if (conversation != null)
            {
                _conversations.Remove(conversation);
                deleted = true;
            }
        }
        if (deleted) RaiseChanged();
    }
}
