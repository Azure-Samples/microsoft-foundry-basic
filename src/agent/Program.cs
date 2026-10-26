using Azure.AI.AgentServer.Core;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Extensions.AI;

var projectEndpoint = new Uri(Environment.GetEnvironmentVariable("FOUNDRY_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("FOUNDRY_PROJECT_ENDPOINT is not set."));
var deployment = Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME")
    ?? "agent-model";
// The agent identity can't resolve project connections by name at runtime (no RBAC on the
// connection read API), so the full connection resource ID is injected via environment variable.
var bingConnectionId = Environment.GetEnvironmentVariable("BING_CONNECTION_ID")
    ?? throw new InvalidOperationException("BING_CONNECTION_ID is not set.");

AIProjectClient projectClient = new (projectEndpoint, new ManagedIdentityCredential());

#pragma warning disable AAIP001 // Bing grounding APIs are experimental
BingGroundingSearchToolOptions bingGroundingOptions = new(
    searchConfigurations: [new BingGroundingSearchOptions(projectConnectionId: bingConnectionId)]);

AIAgent agent = projectClient
    .AsAIAgent(
        model: deployment,
        instructions: "You are a helpful Chatbot agent. You'll consult the Bing Search tool to answer questions. Always search the web for information before responding.",
        name: "baseline-chatbot-agent",
        tools: [FoundryAITool.CreateBingGroundingTool(bingGroundingOptions)]);

var builder = AgentHost.CreateBuilder(args);
builder.Services.AddFoundryResponses(agent);
builder.RegisterProtocol("responses", endpoints => endpoints.MapFoundryResponses());

var app = builder.Build();
app.Run();
