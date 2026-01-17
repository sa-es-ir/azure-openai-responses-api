# MessagingApp with Azure OpenAI (Responses API + Assistants)

![conversation_image](MessagingApp_conversations.png)

This repo is a Blazor Server chat app that shows two ways to talk to Azure OpenAI:

1. **Responses API** – stateful, recommended (new)
2. **Assistants API** – legacy (will be deprecated), kept for comparison

Both implementations plug into the same `IConversationService` interface, so you can switch between them just by changing DI in `Program.cs`.

---

## Responses API (recommended)

`ConversationWithResponsesAPIService` uses the Azure OpenAI **Responses API** with the official .NET SDK. It keeps the conversation stateful by passing `PreviousResponseId` on each turn.

### Calling the Responses API (core part)

```csharp
// ConversationWithResponsesAPIService.cs (Responses API)
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
    Instructions = "You are an AI assistant that only talks about food based on the user's mood."
};

if (!string.IsNullOrEmpty(conversation.PreviousResponseId))
{
    options.PreviousResponseId = conversation.PreviousResponseId; // stateful chaining
}

var response = await _responsesClient.CreateResponseAsync(items, options);

var assistantText = response.Value.GetOutputText();
var responseId = response.Value.Id;

if (!string.IsNullOrEmpty(assistantText))
{
    conversation.Messages.Add(new Message
    {
        Text = assistantText,
        IsFromUser = false,
        ConversationId = conversationId,
    });

    conversation.LastMessageAt = DateTime.Now;
    conversation.PreviousResponseId = responseId; // save for the next turn
}
```

This uses the latest Responses API surface (`OpenAI.Responses` types) instead of constructing raw JSON.

---

## Assistants API (legacy – will be deprecated)

`ConversationService` uses the older **Assistants API** via threads, messages, and runs. This API is expected to be deprecated in favor of Responses and is kept only for learning and migration.

### Calling the Assistants API (core part)

```csharp
// ConversationService.cs (assistants)
var run = await _assistantClient.CreateRunAsync(
    conversation.ThreadId,
    assistantOptions.Value.AssistantId);

do
{
    await Task.Delay(TimeSpan.FromMilliseconds(500));
    run = await _assistantClient.GetRunAsync(conversation.ThreadId, run.Value.Id);
}
while (run.Value.Status is RunStatus.Queued or RunStatus.InProgress);

if (run.Value.Status == RunStatus.Completed)
{
    var collection = _assistantClient.GetMessagesAsync(
        conversation.ThreadId,
        new MessageCollectionOptions { PageSizeLimit = 1 });

    await foreach (var assistantMessage in collection)
    {
        if (assistantMessage.Role != MessageRole.Assistant) continue;

        var assistantText = assistantMessage.Content[0].Text;
        if (!string.IsNullOrEmpty(assistantText))
        {
            conversation.Messages.Add(new Message
            {
                Text = assistantText,
                IsFromUser = false,
                ConversationId = conversationId,
            });

            conversation.LastMessageAt = DateTime.Now;

            await _assistantClient.DeleteMessageAsync(
                conversation.ThreadId,
                assistantMessage.Id);
        }

        break;
    }
}
else
{
    logger.LogError("AI run failed with status {Status} for threadId {ThreadId}",
        run.Value.Status, conversation.ThreadId);
}
```

> Note: this Assistants-based implementation is **legacy** and will be deprecated. Prefer the Responses-based `ConversationWithResponsesAPIService` for new projects.

---

## Switching implementations

Both services implement `IConversationService`. To switch:

- Use **Responses API**:

  ```csharp
  builder.Services.AddSingleton<IConversationService, ConversationWithResponsesAPIService>();
  ```

- Use **Assistants API** (legacy):

  ```csharp
  builder.Services.AddSingleton<IConversationService, ConversationWithAssistantService>();
  ```

No UI changes are required; the Blazor components continue to use `IConversationService`.

# Azure OpenAI Service connection

> Update this section with details specific to your Azure OpenAI Service setup.

To use the Azure OpenAI services, you need to set up the following:

- **Azure subscription**: An active Azure subscription is required to access the Azure OpenAI Service.
- **Resource group**: A resource group in your Azure subscription to contain the OpenAI resources.
- **Azure OpenAI Service resource**: The actual OpenAI resource created in your Azure portal.

Once you have these set up, configure your application with the necessary connection details, usually in the `appsettings.json` or through environment variables.

### Example configuration

```json
{
  "AzureOpenAI": {
    "Endpoint": "https://<your-resource-name>.openai.azure.com/",
    "ApiKey": "<your-api-key>", // better to put in a secure place or at least environment vars
    "ModelName": "gpt-4o",
    "AssistantId": "<your-assistant-id>" // only for Assistants API
  }
}
```

[Responses API reference](https://learn.microsoft.com/en-us/azure/cognitive-services/openai/)
[Assistants API reference](https://learn.microsoft.com/en-us/azure/ai-foundry/openai/concepts/assistants?view=foundry-classic)
