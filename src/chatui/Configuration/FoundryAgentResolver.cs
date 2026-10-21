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

        // Hosted agents are reached through their per-agent endpoint, derived from the
        // project endpoint and the registered agent name. The active version is resolved
        // server side from the endpoint's administrator-controlled version selector.
        var projectEndpoint = new Uri(_options.CurrentValue.AIProjectEndpoint);
        var agentEndpoint = new UriBuilder(projectEndpoint)
        {
            Path = $"{projectEndpoint.PathAndQuery.TrimEnd('/')}/agents/{_options.CurrentValue.AIAgentName}/endpoint/protocols/openai"
        }.Uri;

        var agent = _projectClient.AsAIAgent(agentEndpoint);

        _agent = agent;
        return agent;
    }

    public void Dispose()
    {
        _changeToken?.Dispose();
    }
}
