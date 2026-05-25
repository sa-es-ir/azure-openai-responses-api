using Azure.AI.OpenAI;
using MessagingApp.Models;
using Microsoft.Extensions.Options;
using OpenAI.Responses;

namespace MessagingApp.Services;
#pragma warning disable OPENAI001
public class ConversationWithResponsesAPIService(AzureOpenAIClient azureOpenAIClient,
        IOptions<AssistantOptions> assistantOptions,
        ILogger<ConversationWithResponsesAPIService> logger) : IConversationService
{
    private readonly List<Conversation> _conversations = new();
    private readonly object _lock = new();

    private readonly ResponsesClient _responsesClient = azureOpenAIClient.GetResponsesClient();

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

    public Task<Conversation> CreateOrGetConversationAsync(string userName)
    {
        bool created = false;
        Conversation? conversation;
        lock (_lock)
        {
            conversation = _conversations.FirstOrDefault(c => c.UserName == userName);
            if (conversation == null)
            {
                conversation = new Conversation
                {
                    UserName = userName,
                    Title = "New Conversation"
                };
                _conversations.Add(conversation);
                created = true;
            }
        }

        if (created) RaiseChanged();

        return Task.FromResult(conversation!);
    }

    public async Task<Message?> AddMessageAsync(string conversationId, string text, bool isFromUser)
    {
        bool changed = false;
        Conversation? conversation;
        lock (_lock)
        {
            conversation = _conversations.FirstOrDefault(c => c.Id == conversationId);
        }

        if (conversation == null)
        {
            return null;
        }

        if (isFromUser)
        {
            var message = new Message
            {
                Text = text,
                IsFromUser = true,
                ConversationId = conversationId,
            };

            conversation.Messages.Add(message);
            conversation.LastMessageAt = DateTime.Now;

            if (conversation.Messages.Count(m => m.IsFromUser) == 1)
            {
                conversation.Title = text.Length > 50 ? text[..50] + "..." : text;
            }
        }
        else
        {
            try
            {
                var options = new CreateResponseOptions
                {
                    Model = assistantOptions.Value.ModelName,
                    Instructions = "You are an AI assistant that only talks about food based on the user's mood. remember what the user mood before and offer him again if they ask. remember personal user info like name if they put",
                };

                options.InputItems.Add(ResponseItem.CreateUserMessageItem(conversation.Messages.Last().Text));

                // To add external tools
                //options.Tools.Add(ResponseTool.CreateWebSearchTool());

                if (!string.IsNullOrEmpty(conversation.PreviousResponseId))
                {
                    options.PreviousResponseId = conversation.PreviousResponseId;
                }

                ResponseResult response = await _responsesClient.CreateResponseAsync(options);

                string assistantText = response.GetOutputText();
                string responseId = response.Id;

                var assistantResponseMessage = new Message
                {
                    Text = assistantText,
                    IsFromUser = false,
                    ConversationId = conversationId,
                };

                conversation.Messages.Add(assistantResponseMessage);
                conversation.LastMessageAt = DateTime.Now;

                // Persist last response id for stateful chaining via previous_response_id
                conversation.PreviousResponseId = responseId;
                changed = true;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error while calling Azure OpenAI Responses API.");
            }
        }

        changed = true;

        if (changed) RaiseChanged();

        return conversation.Messages.OrderByDescending(x => x.Timestamp).FirstOrDefault();
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
