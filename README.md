# MoodBites — AI Food Suggestions with Azure OpenAI (Responses & Assistants) in .NET

![conversation_image](MessagingApp_conversations.png)

**MoodBites** is a Blazor Server chat app that suggests food based on your mood. It demonstrates **three** ways to talk to Azure OpenAI, all behind the same `IConversationService` interface so you can switch between them by changing one line of DI in `Program.cs`:

1. **Responses API via the Azure SDK** – stateful, recommended.
2. **Responses API via the official OpenAI SDK, pointed at Azure** – same Responses API, using the vendor-neutral `OpenAI` package against Azure's OpenAI-compatible endpoint.
3. **Assistants API via the Azure SDK** – legacy (being deprecated), kept for comparison.

## Thank you for giving a ⭐ to this repo!

The best is to watch these videos before:

[Azure OpenAI Assistant in .NET](https://youtu.be/Tr6oA4MU580)

[Azure OpenAI Responses in .NET](https://youtu.be/JoG5Gp1Yvfo)

## Tech stack

- **.NET 10** / Blazor Server (interactive server rendering)
- **Azure.AI.OpenAI** `2.9.0-beta.1`
- **OpenAI** `2.10.0` (used directly by implementation #2)
- **Markdig** `1.2.0` – renders the assistant's markdown (e.g. **bold**, lists, recipes) in the chat

---

Before running, set the configuration in `appsettings.json`:

```json
"AzureOpenAI": {
  "Endpoint": "https://your-azure-openai-endpoint/",
  "ApiKey": "your-azure-openai-api-key",
  "ModelName": "your-deployment-name",
  "AssistantId": "your-assistant-id" // only needed for the Assistants API
}
```

> All three implementations read from this same `AzureOpenAI` section. Implementation #2 reuses the same endpoint and key — it just targets Azure's OpenAI-compatible `/openai/v1/` path.

## 1. Responses API via the Azure SDK (recommended)

`ConversationWithResponsesAPIService` uses the Azure OpenAI **Responses API**. It keeps the conversation stateful by passing `PreviousResponseId` on each turn.

The Responses client comes from the `AzureOpenAIClient`:

```csharp
private readonly ResponsesClient _responsesClient = azureOpenAIClient.GetResponsesClient();
```

### Calling the Responses API (core part)

```csharp
// ConversationWithResponsesAPIService.cs / ConversationWithOpenAIResponsesService.cs
var options = new CreateResponseOptions
{
    Model = assistantOptions.Value.ModelName,
    Instructions = "You are an AI assistant that only talks about food based on the user's mood."
};

options.InputItems.Add(ResponseItem.CreateUserMessageItem(conversation.Messages.Last().Text));

// To add external tools:
//options.Tools.Add(ResponseTool.CreateWebSearchTool());

if (!string.IsNullOrEmpty(conversation.PreviousResponseId))
{
    options.PreviousResponseId = conversation.PreviousResponseId; // stateful chaining
}

ResponseResult response = await _responsesClient.CreateResponseAsync(options);

string assistantText = response.GetOutputText();
string responseId = response.Id;

conversation.Messages.Add(new Message
{
    Text = assistantText,
    IsFromUser = false,
    ConversationId = conversationId,
});

conversation.LastMessageAt = DateTime.Now;
conversation.PreviousResponseId = responseId; // save for the next turn
```

> This uses the OpenAI 2.10 Responses surface (`ResponsesClient`, `CreateResponseOptions`, `ResponseResult`). The model is set on the options (`Model`) and the convenience `CreateResponseAsync(options)` returns a completed `ResponseResult` directly.

---

## 2. Responses API via the OpenAI SDK, pointed at Azure

`ConversationWithOpenAIResponsesService` uses the **exact same Responses code as #1**, but obtains its `ResponsesClient` from the official `OpenAI` package's `OpenAIClient` instead of the Azure SDK. The `OpenAIClient` is pointed at Azure's OpenAI-compatible `/openai/v1/` endpoint, authenticated with your Azure key — so no openai.com account is required.

```csharp
private readonly ResponsesClient _responsesClient = openAIClient.GetResponsesClient();
```

The client is registered in `Program.cs` using the same Azure endpoint and key:

```csharp
var openAIv1Endpoint = new Uri(new Uri(aiEndPoint!), "openai/v1/");
builder.Services.AddSingleton(_ => new OpenAI.OpenAIClient(
    new ApiKeyCredential(aiApiKey!),
    new OpenAI.OpenAIClientOptions { Endpoint = openAIv1Endpoint }));
```

Use this option if you prefer the vendor-neutral OpenAI SDK (e.g. to keep the same code whether you point at Azure or openai.com).

---

## 3. Assistants API via the Azure SDK (legacy – will be deprecated)

`ConversationWithAssistantService` uses the older **Assistants API** via threads, messages, and runs. This API is expected to be deprecated in favor of Responses and is kept only for learning and migration.

### Calling the Assistants API (core part)

```csharp
// ConversationWithAssistantService.cs (assistants)
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

> This Assistants-based implementation is **legacy** and will be deprecated. Prefer a Responses-based implementation (#1 or #2) for new projects.

---

## Switching implementations

All three services implement `IConversationService`. Pick one in `Program.cs` — uncomment a single registration:

```csharp
// 1) Responses API via the Azure SDK (recommended)
builder.Services.AddSingleton<IConversationService, ConversationWithResponsesAPIService>();

// 2) Responses API via the OpenAI SDK pointed at Azure
//var openAIv1Endpoint = new Uri(new Uri(aiEndPoint!), "openai/v1/");
//builder.Services.AddSingleton(_ => new OpenAI.OpenAIClient(
//    new ApiKeyCredential(aiApiKey!),
//    new OpenAI.OpenAIClientOptions { Endpoint = openAIv1Endpoint }));
//builder.Services.AddSingleton<IConversationService, ConversationWithOpenAIResponsesService>();

// 3) Assistants API via the Azure SDK (legacy)
//builder.Services.AddSingleton<IConversationService, ConversationWithAssistantService>();
```

No UI changes are required; the Blazor components continue to use `IConversationService`.

## Markdown in the chat

Assistant replies are rendered as markdown with [Markdig](https://github.com/xoofx/markdig), so formatting like `**bold**`, bullet lists, and recipes display properly. Raw HTML is disabled in the pipeline (`.DisableHtml()`) so model output is rendered safely. User messages are shown as plain text.

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
