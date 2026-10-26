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
var bingConnectionName = Environment.GetEnvironmentVariable("BING_CONNECTION_NAME")
    ?? "bingaiagent";

AIProjectClient projectClient = new (projectEndpoint, new ManagedIdentityCredential());

AIProjectConnection bingConnection = projectClient.Connections.GetConnection(bingConnectionName);
#pragma warning disable AAIP001 // Bing grounding APIs are experimental
BingGroundingSearchToolOptions bingGroundingOptions = new(
    searchConfigurations: [new BingGroundingSearchOptions(projectConnectionId: bingConnection.Id)]);

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
