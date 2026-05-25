using Azure.AI.OpenAI;
using MessagingApp.Components;
using MessagingApp.Models;
using MessagingApp.Services;
using System.ClientModel;

var builder = WebApplication.CreateBuilder(args);


// Add services to the container.
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var aiEndPoint = builder.Configuration["AzureOpenAI:EndPoint"];
var aiApiKey = builder.Configuration["AzureOpenAI:ApiKey"];

builder.Services.AddSingleton(_ => new AzureOpenAIClient(new Uri(aiEndPoint!), new ApiKeyCredential(aiApiKey!)));
//builder.Services.AddSingleton<IConversationService, ConversationWithAssistantService>();
//builder.Services.AddSingleton<IConversationService, ConversationWithResponsesAPIService>();

builder.Services.Configure<AssistantOptions>(builder.Configuration.GetSection("AzureOpenAI"));

// Option: use the official OpenAI package pointed at Azure OpenAI (same Azure endpoint + key).
// The OpenAI SDK targets Azure's OpenAI-compatible "/openai/v1/" endpoint; ModelName comes
// from the AzureOpenAI section configured above. Swap the registration above for the lines below.
var openAIv1Endpoint = new Uri(new Uri(aiEndPoint!), "openai/v1/");
builder.Services.AddSingleton(_ => new OpenAI.OpenAIClient(
    new ApiKeyCredential(aiApiKey!),
    new OpenAI.OpenAIClientOptions { Endpoint = openAIv1Endpoint }));
builder.Services.AddSingleton<IConversationService, ConversationWithOpenAIResponsesService>();


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
