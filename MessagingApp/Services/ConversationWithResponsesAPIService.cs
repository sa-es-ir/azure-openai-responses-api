using Azure.AI.OpenAI;
using MessagingApp.Models;
using Microsoft.Extensions.Options;
using OpenAI.Responses;
using System.ClientModel;

namespace MessagingApp.Services;
#pragma warning disable OPENAI001
public class ConversationWithResponsesAPIService(AzureOpenAIClient azureOpenAIClient,
        IOptions<AssistantOptions> assistantOptions,
        ILogger<ConversationWithResponsesAPIService> logger) : IConversationService
{
    private readonly List<Conversation> _conversations = new();
    private readonly object _lock = new();

    private readonly OpenAIResponseClient _responsesClient
        = azureOpenAIClient.GetOpenAIResponseClient(assistantOptions.Value.ModelName);
    private readonly ILogger<ConversationWithResponsesAPIService> _logger;

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
                var items = new List<ResponseItem>
                {
                    ResponseItem.CreateUserMessageItem(
                        new List<ResponseContentPart>
                        {
                            ResponseContentPart.CreateInputTextPart(conversation.Messages.Last().Text)
                        })
                };

                var options = new ResponseCreationOptions
                {
                    Instructions = "You are an AI assistant that only talks about food based on the user's mood. remember what the user mood before and offer him again if they ask. remember personal user info like name if they put"
                };

                // To add external tools
                //options.Tools.Add(ResponseTool.CreateWebSearchTool());

                if (!string.IsNullOrEmpty(conversation.PreviousResponseId))
                {
                    options.PreviousResponseId = conversation.PreviousResponseId;
                }

                var response = await _responsesClient.CreateResponseAsync(items, options);

                if (response.Value.Status is ResponseStatus.InProgress or ResponseStatus.Queued)
                {
                    response = await GetCompletedResponseAsync(response);
                }

                if (response.Value.Status == ResponseStatus.Completed)
                {
                    string? assistantText = response.Value.GetOutputText();
                    string? responseId = response.Value.Id;

                    var assistantResponseMessage = new Message
                    {
                        Text = assistantText!,
                        IsFromUser = false,
                        ConversationId = conversationId,
                    };

                    conversation.Messages.Add(assistantResponseMessage);
                    conversation.LastMessageAt = DateTime.Now;

                    // Persist last response id for stateful chaining via previous_response_id
                    conversation.PreviousResponseId = responseId;
                    changed = true;
                }
                else
                {
                    // set proper message in case of failure
                    logger.LogWarning("Azure OpenAI Responses API returned incomplete response or empty text.");
                }
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

    private async Task<ClientResult<OpenAIResponse>> GetCompletedResponseAsync(ClientResult<OpenAIResponse> response)
    {
        do
        {
            await Task.Delay(500);
            response = await _responsesClient.GetResponseAsync(response.Value.Id);
        }
        while (response.Value.Status is ResponseStatus.InProgress or ResponseStatus.Queued);

        return response;
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
