using System.ComponentModel.DataAnnotations;
using chatui.Auth;
using chatui.Data;
using chatui.Data.Entities;
using chatui.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace chatui.Controllers;

/// <summary>
/// On-site meeting lifecycle: create (with QR + join URL), list mine,
/// get details, end. Join/leave/turns/summary/addressee-hint live in
/// sibling controllers or extend this one in later commits.
/// </summary>
[ApiController]
[Route("api/meetings")]
public class MeetingsController(
    IUserContext userContext,
    TranslatorDbContext dbContext,
    JoinTokenService joinTokens,
    QrCodeService qrCodes) : ControllerBase
{
    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<MeetingSummary>>> ListMine(CancellationToken ct)
    {
        var me = await userContext.GetOrCreateCurrentAsync(ct).ConfigureAwait(false);

        var meetings = await dbContext.Meetings
            .AsNoTracking()
            .Where(m => m.InitiatorId == me.Id
                     || m.Participants.Any(p => p.UserId == me.Id))
            .OrderByDescending(m => m.CreatedAt)
            .Select(m => new MeetingSummary(
                m.Id,
                m.Title,
                m.Status,
                m.InitiatorId == me.Id ? "initiator" : "listener",
                m.CreatedAt,
                m.EndedAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Ok(meetings);
    }

    [HttpPost]
    public async Task<ActionResult<CreateMeetingResponse>> Create(
        [FromBody] CreateMeetingRequest request,
        CancellationToken ct)
    {
        var me = await userContext.GetOrCreateCurrentAsync(ct).ConfigureAwait(false);
        var title = string.IsNullOrWhiteSpace(request.Title)
            ? $"On-site meeting — {DateTime.UtcNow:yyyy-MM-dd}"
            : request.Title!.Trim();

        var meeting = new Meeting
        {
            InitiatorId = me.Id,
            Title = title,
            Status = "active",
        };
        dbContext.Meetings.Add(meeting);

        dbContext.Participants.Add(new Participant
        {
            Meeting = meeting,
            UserId = me.Id,
            Language = me.PreferredLanguage,
            Role = "initiator",
        });

        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);

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
        var me = await userContext.GetOrCreateCurrentAsync(ct).ConfigureAwait(false);

        var meeting = await dbContext.Meetings
            .AsNoTracking()
            .Include(m => m.Initiator)
            .Include(m => m.Participants).ThenInclude(p => p.User)
            .FirstOrDefaultAsync(m => m.Id == id, ct)
            .ConfigureAwait(false);
        if (meeting is null)
        {
            return NotFound();
        }
        if (meeting.InitiatorId != me.Id && meeting.Participants.All(p => p.UserId != me.Id))
        {
            return NotFound();
        }

        return Ok(new MeetingDetails(
            meeting.Id,
            meeting.Title,
            meeting.Status,
            new UserRef(meeting.Initiator.Id, meeting.Initiator.DisplayName, meeting.Initiator.PreferredLanguage),
            meeting.Participants
                .OrderBy(p => p.JoinedAt)
                .Select(p => new ParticipantRef(p.User.Id, p.User.DisplayName, p.Language, p.Role, p.JoinedAt, p.LeftAt))
                .ToList(),
            meeting.CreatedAt,
            meeting.EndedAt));
    }

    [HttpPost("{id:guid}/end")]
    public async Task<IActionResult> End(Guid id, CancellationToken ct)
    {
        var me = await userContext.GetOrCreateCurrentAsync(ct).ConfigureAwait(false);
        var meeting = await dbContext.Meetings
            .FirstOrDefaultAsync(m => m.Id == id, ct)
            .ConfigureAwait(false);
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
        await dbContext.SaveChangesAsync(ct).ConfigureAwait(false);
        return NoContent();
    }

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
}
