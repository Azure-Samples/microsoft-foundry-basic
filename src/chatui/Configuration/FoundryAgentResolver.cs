using Azure.AI.Projects;
using Azure.AI.Extensions.OpenAI;
using Microsoft.Agents.AI.Foundry;
using Microsoft.Extensions.Options;

namespace chatui.Configuration;

#pragma warning disable OPENAI001 // FoundryAgent is experimental

public class FoundryAgentResolver : IDisposable
{
    private readonly AIProjectClient _projectClient;
    private readonly IOptionsMonitor<ChatApiOptions> _options;
    private readonly IDisposable? _changeToken;
    private volatile FoundryAgent? _agent;

    public FoundryAgentResolver(AIProjectClient projectClient, IOptionsMonitor<ChatApiOptions> options)
    {
        _projectClient = projectClient;
        _options = options;
        _changeToken = options.OnChange(_ => Interlocked.Exchange(ref _agent, null));
    }

    public FoundryAgent GetAgent()
    {
        var current = _agent;
        if (current is not null)
            return current;

        // Hosted agents are reached through an agent-specific endpoint. The agent name is
        // parsed from the URI and the active version is resolved server side from the
        // endpoint's administrator-controlled version selector.
        var agentEndpoint = new Uri(_options.CurrentValue.AIAgentEndpoint);
        var agent = _projectClient.AsAIAgent(agentEndpoint);

        _agent = agent;
        return agent;
    }

    public void Dispose()
    {
        _changeToken?.Dispose();
    }
}
