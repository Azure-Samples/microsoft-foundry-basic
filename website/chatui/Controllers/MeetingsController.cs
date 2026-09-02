using System.ComponentModel.DataAnnotations;
using chatui.Auth;
using chatui.Data.Entities;
using chatui.Services;
using Microsoft.AspNetCore.Mvc;

namespace chatui.Controllers;

/// <summary>
/// On-site meeting lifecycle: create (with QR + join URL), list mine,
/// get details, end, join by token, leave, list turns, send a turn.
/// Summary + addressee-hint live in a sibling controller.
/// </summary>
[ApiController]
[Route("api/meetings")]
public class MeetingsController(
    IUserContext userContext,
    TransientSessionStore sessions,
    JoinTokenService joinTokens,
    QrCodeService qrCodes,
    WebPubSubBroadcaster broadcaster,
    ILogger<MeetingsController> logger) : ControllerBase
{
    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<MeetingSummary>>> ListMine(CancellationToken ct)
    {
        var me = await userContext.GetCurrentAsync(ct).ConfigureAwait(false);

        var meetings = sessions.ListMeetings(me.Id)
            .Select(m => new MeetingSummary(
                m.Id,
                m.Title,
                m.Status,
                m.InitiatorId == me.Id ? "initiator" : "listener",
                m.CreatedAt,
                m.EndedAt))
            .ToList();

        return Ok(meetings);
    }

    [HttpPost]
    public async Task<ActionResult<CreateMeetingResponse>> Create(
        [FromBody] CreateMeetingRequest request,
        CancellationToken ct)
    {
        var me = await userContext.GetCurrentAsync(ct).ConfigureAwait(false);
        var title = string.IsNullOrWhiteSpace(request.Title)
            ? $"On-site meeting — {DateTime.UtcNow:yyyy-MM-dd}"
            : request.Title!.Trim();

        var meeting = sessions.CreateMeeting(me, title);

        var baseUri = $"{Request.Scheme}://{Request.Host}";
        var token = await joinTokens.IssueAsync(meeting.Id, baseUri, ct).ConfigureAwait(false);
        var joinUrl = $"{baseUri}/join/{token}";
        var qr = qrCodes.GenerateDataUrl(joinUrl);

        return Ok(new CreateMeetingResponse(
            meeting.Id,
            meeting.Title,
            meeting.Status,
            token,
            joinUrl,
            qr,
            meeting.CreatedAt));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MeetingDetails>> Get(Guid id, CancellationToken ct)
    {
        var me = await userContext.GetCurrentAsync(ct).ConfigureAwait(false);
        var meeting = await LoadMyMeetingAsync(id, me.Id, ct).ConfigureAwait(false);
        if (meeting is null)
        {
            return NotFound();
        }
        return Ok(ToDetails(meeting));
    }

    [HttpPost("{id:guid}/end")]
    public async Task<IActionResult> End(Guid id, CancellationToken ct)
    {
        var me = await userContext.GetCurrentAsync(ct).ConfigureAwait(false);
        var meeting = sessions.FindMeeting(id);
        if (meeting is null)
        {
            return NotFound();
        }
        if (meeting.InitiatorId != me.Id)
        {
            return Forbid();
        }
        if (meeting.Status == "ended")
        {
            return NoContent();
        }

        meeting.Status = "ended";
        meeting.EndedAt = DateTimeOffset.UtcNow;

        await broadcaster.PublishPresenceAsync(id, new
        {
            type = "meeting.ended",
            meetingId = id,
            ts = DateTimeOffset.UtcNow,
        }, ct).ConfigureAwait(false);

        return NoContent();
    }

    [HttpPost("join")]
    public async Task<ActionResult<JoinResponse>> Join(
        [FromQuery(Name = "token")] string token,
        [FromBody] JoinRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return BadRequest(new { error = "Missing token." });
        }

        JoinTokenService.JoinTokenClaims claims;
        try
        {
            claims = await joinTokens.ValidateAsync(token, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Rejected meeting join with invalid token.");
            return Unauthorized(new { error = "Invalid or expired join token." });
        }

        var me = await userContext.GetCurrentAsync(ct).ConfigureAwait(false);
        var meeting = sessions.FindMeeting(claims.MeetingId);
        if (meeting is null)
        {
            return NotFound(new { error = "Meeting not found." });
        }
        if (meeting.Status == "ended")
        {
            return StatusCode(StatusCodes.Status410Gone, new { error = "Meeting has ended." });
        }

        sessions.JoinMeeting(meeting, me, request.Language);

        var loaded = await LoadMyMeetingAsync(claims.MeetingId, me.Id, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Meeting disappeared mid-join.");

        var wpsUri = broadcaster.GetClientAccessUri(me.Id, claims.MeetingId, request.Language);
        await broadcaster.PublishPresenceAsync(claims.MeetingId, new
        {
            type = "presence.joined",
            meetingId = claims.MeetingId,
            participant = new
            {
                id = me.Id,
                displayName = me.DisplayName,
                language = request.Language,
            },
            ts = DateTimeOffset.UtcNow,
        }, ct).ConfigureAwait(false);

        return Ok(new JoinResponse(
            claims.MeetingId,
            ToDetails(loaded),
            new WebPubSubAccess(wpsUri.ToString(), DateTimeOffset.UtcNow.AddHours(1))));
    }

    [HttpPost("{id:guid}/leave")]
    public async Task<IActionResult> Leave(Guid id, CancellationToken ct)
    {
        var me = await userContext.GetCurrentAsync(ct).ConfigureAwait(false);
        var meeting = sessions.GetMeeting(id, me.Id);
        if (meeting is null)
        {
            return NoContent();
        }

        sessions.LeaveMeeting(id, me.Id);

        await broadcaster.PublishPresenceAsync(id, new
        {
            type = "presence.left",
            meetingId = id,
            participantId = me.Id,
            ts = DateTimeOffset.UtcNow,
        }, ct).ConfigureAwait(false);

        return NoContent();
    }

    [HttpGet("{id:guid}/turns")]
    public async Task<ActionResult<IReadOnlyList<TurnResponse>>> ListTurns(
        Guid id,
        [FromQuery] DateTimeOffset? before,
        [FromQuery] int limit = 100,
        [FromQuery] string? lang = null,
        CancellationToken ct = default)
    {
        var me = await userContext.GetCurrentAsync(ct).ConfigureAwait(false);
        var meeting = await LoadMyMeetingAsync(id, me.Id, ct).ConfigureAwait(false);
        if (meeting is null)
        {
            return NotFound();
        }

        var callerLanguage = lang ?? meeting.Participants.First(p => p.UserId == me.Id).Language;
        limit = Math.Clamp(limit, 1, 500);

        var turns = sessions.ListTurns(id, before, limit);

        var response = turns
            .OrderBy(t => t.CreatedAt)
            .Select(t => new TurnResponse(
                t.Id,
                t.SpeakerId,
                t.AddresseeId,
                t.SourceLang,
                callerLanguage,
                t.Original,
                t.Translations.FirstOrDefault(tt => tt.TargetLang == callerLanguage)?.Translated,
                t.CreatedAt))
            .ToList();

        return Ok(response);
    }

    [HttpPost("{id:guid}/turns")]
    public async Task<ActionResult<PostTurnResponse>> PostTurn(
        Guid id,
        [FromBody] PostTurnRequest request,
        CancellationToken ct)
    {
        var me = await userContext.GetCurrentAsync(ct).ConfigureAwait(false);
        var meeting = sessions.FindMeeting(id);
        if (meeting is null)
        {
            return NotFound();
        }
        if (meeting.Participants.All(p => p.UserId != me.Id))
        {
            return NotFound();
        }
        if (meeting.Status == "ended")
        {
            return StatusCode(StatusCodes.Status410Gone, new { error = "Meeting has ended." });
        }

        var turn = new Turn
        {
            MeetingId = id,
            Meeting = meeting,
            SpeakerId = me.Id,
            Speaker = me,
            AddresseeId = request.AddresseeId,
            SourceLang = request.SourceLang,
            Original = request.Original,
        };
        sessions.AddTurn(turn, request.Translations);

        var speakerName = me.DisplayName;
        foreach (var (targetLang, translated) in request.Translations)
        {
            try
            {
                await broadcaster.PublishTurnAsync(id, targetLang, new
                {
                    type = "turn.translated",
                    meetingId = id,
                    turnId = turn.Id,
                    speaker = new { id = me.Id, displayName = speakerName },
                    sourceLang = request.SourceLang,
                    targetLang,
                    original = request.Original,
                    translated,
                    addresseeId = request.AddresseeId,
                    ts = turn.CreatedAt,
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // Persistence already succeeded — broadcast is best-effort; clients can pull.
                logger.LogWarning(ex, "Failed to publish turn {TurnId} in {Language}", turn.Id, targetLang);
            }
        }

        if (request.AddresseeId is Guid addressee)
        {
            try
            {
                await broadcaster.PublishToUserAsync(id, addressee, new
                {
                    type = "u.addressed",
                    meetingId = id,
                    turnId = turn.Id,
                    ts = turn.CreatedAt,
                }, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to publish addressee notification for turn {TurnId}", turn.Id);
            }
        }

        return Ok(new PostTurnResponse(turn.Id, turn.CreatedAt));
    }

    private Task<Meeting?> LoadMyMeetingAsync(Guid meetingId, Guid meId, CancellationToken ct) =>
        Task.FromResult(sessions.GetMeeting(meetingId, meId));

    private static MeetingDetails ToDetails(Meeting meeting) => new(
        meeting.Id,
        meeting.Title,
        meeting.Status,
        new UserRef(meeting.Initiator.Id, meeting.Initiator.DisplayName, meeting.Initiator.PreferredLanguage),
        meeting.Participants
            .OrderBy(p => p.JoinedAt)
            .Select(p => new ParticipantRef(p.User.Id, p.User.DisplayName, p.Language, p.Role, p.JoinedAt, p.LeftAt))
            .ToList(),
        meeting.CreatedAt,
        meeting.EndedAt);

    public sealed record CreateMeetingRequest([StringLength(256)] string? Title);

    public sealed record CreateMeetingResponse(
        Guid MeetingId,
        string? Title,
        string Status,
        string JoinToken,
        string JoinUrl,
        string QrPngDataUrl,
        DateTimeOffset CreatedAt);

    public sealed record MeetingSummary(
        Guid Id,
        string? Title,
        string Status,
        string Role,
        DateTimeOffset CreatedAt,
        DateTimeOffset? EndedAt);

    public sealed record UserRef(Guid Id, string DisplayName, string PreferredLanguage);

    public sealed record ParticipantRef(
        Guid Id,
        string DisplayName,
        string Language,
        string Role,
        DateTimeOffset JoinedAt,
        DateTimeOffset? LeftAt);

    public sealed record MeetingDetails(
        Guid Id,
        string? Title,
        string Status,
        UserRef Initiator,
        IReadOnlyList<ParticipantRef> Participants,
        DateTimeOffset CreatedAt,
        DateTimeOffset? EndedAt);

    public sealed record JoinRequest(
        [Required][StringLength(16, MinimumLength = 2)] string Language);

    public sealed record WebPubSubAccess(string Url, DateTimeOffset ExpiresAt);

    public sealed record JoinResponse(Guid MeetingId, MeetingDetails Meeting, WebPubSubAccess Wps);

    public sealed record PostTurnRequest(
        [Required] string Original,
        [Required][StringLength(16, MinimumLength = 2)] string SourceLang,
        [Required] Dictionary<string, string> Translations,
        Guid? AddresseeId);

    public sealed record PostTurnResponse(Guid TurnId, DateTimeOffset CreatedAt);

    public sealed record TurnResponse(
        Guid Id,
        Guid SpeakerId,
        Guid? AddresseeId,
        string SourceLang,
        string CallerLang,
        string Original,
        string? TranslatedForCaller,
        DateTimeOffset CreatedAt);
}
