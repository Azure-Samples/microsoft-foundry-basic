using System.Text.Json;
using chatui.Configuration;
using chatui.Data.Entities;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;

namespace chatui.Services;

/// <summary>
/// Calls the versioned Foundry agent (see agents/translator-agent.json) for
/// the two workload-specific reasoning tasks: addressee-hint and summarise.
/// Translation of individual utterances does NOT go through this service —
/// personal chat uses Azure AI Translator directly, meetings use Azure
/// Speech Translation on the client.
/// </summary>
#pragma warning disable OPENAI001 // FoundryAgent is experimental
public sealed class FoundryAgentService(
    FoundryAgentResolver agentResolver,
    ILogger<FoundryAgentService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<AddresseeHint?> InferAddresseeAsync(
        AddresseeContext context,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            task = "addressee_hint",
            participants = context.Participants,
            recent_turns = context.RecentTurns,
        };
        var raw = await RunAsync(payload, cancellationToken).ConfigureAwait(false);
        if (raw is null)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<AddresseeHint>(raw, JsonOptions);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Foundry addressee-hint returned non-JSON: {Response}", raw);
            return null;
        }
    }

    public async Task<string?> SummariseAsync(
        SummariseContext context,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            task = "summarise",
            target_language = context.TargetLanguage,
            turns = context.Turns,
        };
        var raw = await RunAsync(payload, cancellationToken).ConfigureAwait(false);
        if (raw is null)
        {
            return null;
        }

        try
        {
            var doc = JsonSerializer.Deserialize<SummariseResponse>(raw, JsonOptions);
            return doc?.Summary;
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "Foundry summarise returned non-JSON: {Response}", raw);
            return null;
        }
    }

    private async Task<string?> RunAsync(object payload, CancellationToken cancellationToken)
    {
        var agent = agentResolver.GetAgent();
        var innerAgent = agent.GetService<ChatClientAgent>()!;
        var session = await innerAgent.CreateSessionAsync().ConfigureAwait(false);
        var input = JsonSerializer.Serialize(payload, JsonOptions);
        var response = await agent.RunAsync(input, session).ConfigureAwait(false);
        return response?.ToString();
    }

    public sealed record ParticipantView(Guid Id, string Name, string Language);

    public sealed record TurnView(Guid Speaker, string Text, string Lang, DateTimeOffset Ts);

    public sealed record AddresseeContext(
        IReadOnlyList<ParticipantView> Participants,
        IReadOnlyList<TurnView> RecentTurns);

    public sealed record AddresseeHint(Guid? AddresseeId, double Confidence, string? Rationale);

    public sealed record SummariseContext(
        string TargetLanguage,
        IReadOnlyList<TurnView> Turns);

    private sealed record SummariseResponse(string Summary);
}
