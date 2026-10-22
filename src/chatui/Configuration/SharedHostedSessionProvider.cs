using Azure.AI.Projects.Agents;
using Azure.Identity;
using Microsoft.Extensions.Options;

namespace chatui.Configuration;

/// <summary>
/// Provisions a single shared hosted-agent session through the agent administration API and
/// caches its id. The session is pinned to a specific agent version, which is the only way to
/// pin a hosted session to a version (the MAF factory can only attach an already-existing id).
/// </summary>
public class SharedHostedSessionProvider(IOptionsMonitor<ChatApiOptions> options)
{
    private string? _hostedSessionId;

    /// <summary>
    /// Returns the shared hosted session id, provisioning it on first use.
    /// </summary>
    public async Task<string> GetHostedSessionIdAsync(CancellationToken cancellationToken = default)
    {
        if (_hostedSessionId is not null)
            return _hostedSessionId;

        var config = options.CurrentValue;
        var adminClient = new AgentAdministrationClient(
            new Uri(config.AIProjectEndpoint),
            new DefaultAzureCredential());

        // Resolve the agent's latest version so the session is pinned to a concrete version.
        ProjectsAgentRecord record = await adminClient.GetAgentAsync(config.AIAgentName, cancellationToken);
        string version = record.GetLatestVersion().Version;

        ProjectAgentSession hostedSession = await adminClient.CreateSessionAsync(
            config.AIAgentName,
            new VersionRefIndicator(version),
            cancellationToken: cancellationToken);

        // Wait until the platform finishes provisioning the sandbox.
        while (hostedSession.Status != AgentSessionStatus.Active)
        {
            if (hostedSession.Status == AgentSessionStatus.Failed
                || hostedSession.Status == AgentSessionStatus.Deleted
                || hostedSession.Status == AgentSessionStatus.Expired)
            {
                throw new InvalidOperationException(
                    $"Hosted agent session {hostedSession.AgentSessionId} ended in status {hostedSession.Status}.");
            }

            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            hostedSession = await adminClient.GetSessionAsync(config.AIAgentName, hostedSession.AgentSessionId, cancellationToken);
        }

        _hostedSessionId = hostedSession.AgentSessionId;
        return _hostedSessionId;
    }
}
