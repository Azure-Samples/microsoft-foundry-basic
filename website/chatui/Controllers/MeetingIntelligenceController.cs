using System.ComponentModel.DataAnnotations;
using chatui.Auth;
using chatui.Data.Entities;
using chatui.Services;
using Microsoft.AspNetCore.Mvc;

namespace chatui.Controllers;

/// <summary>
/// Foundry-agent-backed endpoints on top of a meeting:
///   POST /api/meetings/{id}/addressee-hint
///   POST /api/meetings/{id}/summary
///   GET  /api/meetings/{id}/summary?lang=
/// Kept separate from MeetingsController so agent latency and error handling
/// don't muddy the CRUD paths.
/// </summary>
[ApiController]
[Route("api/meetings/{id:guid}")]
public class MeetingIntelligenceController(
    IUserContext userContext,
    TransientSessionStore sessions,
    FoundryAgentService agent,
    WebPubSubBroadcaster broadcaster) : ControllerBase
{
    [HttpPost("addressee-hint")]
    public async Task<ActionResult<FoundryAgentService.AddresseeHint>> AddresseeHint(
        Guid id,
        [FromBody] AddresseeHintRequest request,
        CancellationToken ct)
    {
        var me = await userContext.GetCurrentAsync(ct).ConfigureAwait(false);
        var meeting = await LoadMyMeetingAsync(id, me.Id, ct).ConfigureAwait(false);
        if (meeting is null)
        {
            return NotFound();
        }

        var participants = meeting.Participants
            .Select(p => new FoundryAgentService.ParticipantView(p.User.Id, p.User.DisplayName, p.Language))
            .ToList();

        var turns = sessions.ListTurns(id, before: null, limit: 500)
            .Where(t => request.RecentTurnIds.Contains(t.Id))
            .OrderBy(t => t.CreatedAt)
            .Select(t => new FoundryAgentService.TurnView(t.SpeakerId, t.Original, t.SourceLang, t.CreatedAt))
            .ToList();

        var hint = await agent.InferAddresseeAsync(
            new FoundryAgentService.AddresseeContext(participants, turns), ct).ConfigureAwait(false);

        if (hint is null)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Agent did not return a usable hint." });
        }
        return Ok(hint);
    }

    [HttpPost("summary")]
    public async Task<ActionResult<MeetingSummaryResponse>> GenerateSummary(
        Guid id,
        [FromBody] SummaryRequest request,
        CancellationToken ct)
    {
        var me = await userContext.GetCurrentAsync(ct).ConfigureAwait(false);
        var meeting = await LoadMyMeetingAsync(id, me.Id, ct).ConfigureAwait(false);
        if (meeting is null)
        {
            return NotFound();
        }

        var language = string.IsNullOrWhiteSpace(request.Language) ? me.PreferredLanguage : request.Language!;

        var turns = sessions.ListTurns(id, before: null, limit: 500)
            .OrderBy(t => t.CreatedAt)
            .Select(t => new FoundryAgentService.TurnView(t.SpeakerId, t.Original, t.SourceLang, t.CreatedAt))
            .ToList();

        var summary = await agent.SummariseAsync(
            new FoundryAgentService.SummariseContext(language, turns), ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(summary))
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { error = "Agent did not return a summary." });
        }

        var storedSummary = sessions.UpsertSummary(id, language, summary!);

        try
        {
            await broadcaster.PublishPresenceAsync(id, new
            {
                type = "summary.ready",
                meetingId = id,
                language,
                ts = DateTimeOffset.UtcNow,
            }, ct).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort; summary is persisted.
        }

        return Ok(new MeetingSummaryResponse(summary!, language, storedSummary.GeneratedAt));
    }

    [HttpGet("summary")]
    public async Task<ActionResult<MeetingSummaryResponse>> GetSummary(
        Guid id,
        [FromQuery] string? lang,
        CancellationToken ct)
    {
        var me = await userContext.GetCurrentAsync(ct).ConfigureAwait(false);
        var meeting = await LoadMyMeetingAsync(id, me.Id, ct).ConfigureAwait(false);
        if (meeting is null)
        {
            return NotFound();
        }

        var language = string.IsNullOrWhiteSpace(lang) ? me.PreferredLanguage : lang!;
        var cached = sessions.GetSummary(id, language);
        if (cached is null)
        {
            return NotFound(new { error = "No cached summary for that language." });
        }
        return Ok(new MeetingSummaryResponse(cached.Summary, cached.Language, cached.GeneratedAt));
    }

    private Task<Meeting?> LoadMyMeetingAsync(Guid meetingId, Guid meId, CancellationToken ct) =>
        Task.FromResult(sessions.GetMeeting(meetingId, meId));

    public sealed record AddresseeHintRequest([Required] List<Guid> RecentTurnIds);

    public sealed record SummaryRequest([StringLength(16, MinimumLength = 2)] string? Language);

    public sealed record MeetingSummaryResponse(string Summary, string Language, DateTimeOffset GeneratedAt);
}
