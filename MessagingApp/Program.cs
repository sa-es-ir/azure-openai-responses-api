using Azure.AI.OpenAI;
using MessagingApp.Components;
using MessagingApp.Models;
using System.ClientModel;

var builder = WebApplication.CreateBuilder(args);


// Add services to the container.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var aiEndPoint = builder.Configuration["AzureOpenAI:EndPoint"];
var aiApiKey = builder.Configuration["AzureOpenAI:ApiKey"];

builder.Services.AddSingleton(_ => new AzureOpenAIClient(new Uri(aiEndPoint!), new ApiKeyCredential(aiApiKey!)));
builder.Services.AddSingleton<MessagingApp.Services.ConversationService>();
builder.Services.Configure<AssistantOptions>(builder.Configuration.GetSection("AzureOpenAI"));

//string endpoint = builder.Configuration["AZURE_OPENAI_ENDPOINT"];
//string deployment = builder.Configuration["AZURE_OPENAI_GPT_NAME"];

//IChatClient chatClient =
//    new AzureOpenAIClient(new Uri(endpoint), new DefaultAzureCredential())
//    .GetChatClient(deployment)
//    .AsIChatClient();

//List<ChatMessage> chatHistory = [];
//while (true)
//{
//    Console.WriteLine("Your prompt:");
//    string? userPrompt = Console.ReadLine();
//    chatHistory.Add(new ChatMessage(ChatRole.User, userPrompt));

//    Console.WriteLine("AI Response:");
//    string response = "";
//    await foreach (ChatResponseUpdate item in
//        chatClient.GetStreamingResponseAsync(chatHistory))
//    {
//        Console.Write(item.Text);
//        response += item.Text;
//    }
//    chatHistory.Add(new ChatMessage(ChatRole.Assistant, response));
//}

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();
