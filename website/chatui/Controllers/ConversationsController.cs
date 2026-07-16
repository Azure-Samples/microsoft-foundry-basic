using chatui.Auth;
using chatui.Data;
using chatui.Data.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace chatui.Controllers;

/// <summary>
/// Personal-chat conversations. A conversation is a canonicalised pair
/// (UserAId &lt; UserBId), so /conversations {peerId} is idempotent.
/// </summary>
[ApiController]
[Route("api/conversations")]
public class ConversationsController(
    IUserContext userContext,
    TranslatorDbContext dbContext) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ConversationSummary>>> List(
        CancellationToken cancellationToken)
    {
        var me = await userContext.GetOrCreateCurrentAsync(cancellationToken).ConfigureAwait(false);

        var conversations = await dbContext.Conversations
            .AsNoTracking()
            .Include(c => c.UserA)
            .Include(c => c.UserB)
            .Where(c => c.UserAId == me.Id || c.UserBId == me.Id)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var lastMessages = await dbContext.Messages
            .AsNoTracking()
            .Where(m => conversations.Select(c => c.Id).Contains(m.ConversationId))
            .GroupBy(m => m.ConversationId)
            .Select(g => new { ConversationId = g.Key, LastAt = g.Max(m => m.CreatedAt) })
            .ToDictionaryAsync(x => x.ConversationId, x => x.LastAt, cancellationToken)
            .ConfigureAwait(false);

        var response = conversations.Select(c =>
        {
            var peer = c.UserAId == me.Id ? c.UserB : c.UserA;
            lastMessages.TryGetValue(c.Id, out var lastAt);
            return new ConversationSummary(
                c.Id,
                new PeerRef(peer.Id, peer.DisplayName, peer.PreferredLanguage),
                lastAt == default ? c.CreatedAt : lastAt);
        }).ToList();

        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<ConversationSummary>> CreateOrGet(
        [FromBody] CreateConversationRequest request,
        CancellationToken cancellationToken)
    {
        var me = await userContext.GetOrCreateCurrentAsync(cancellationToken).ConfigureAwait(false);

        if (request.PeerId == me.Id)
        {
            return BadRequest(new { error = "Cannot start a conversation with yourself." });
        }

        var peer = await dbContext.Users
            .FirstOrDefaultAsync(u => u.Id == request.PeerId, cancellationToken)
            .ConfigureAwait(false);
        if (peer is null)
        {
            return NotFound(new { error = "Peer user not found." });
        }

        var (userAId, userBId) = Canonicalise(me.Id, peer.Id);

        var existing = await dbContext.Conversations
            .Include(c => c.UserA)
            .Include(c => c.UserB)
            .FirstOrDefaultAsync(c => c.UserAId == userAId && c.UserBId == userBId, cancellationToken)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            var peerRef = existing.UserAId == me.Id ? existing.UserB : existing.UserA;
            return Ok(new ConversationSummary(
                existing.Id,
                new PeerRef(peerRef.Id, peerRef.DisplayName, peerRef.PreferredLanguage),
                existing.CreatedAt));
        }

        var created = new Conversation { UserAId = userAId, UserBId = userBId };
        dbContext.Conversations.Add(created);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Ok(new ConversationSummary(
            created.Id,
            new PeerRef(peer.Id, peer.DisplayName, peer.PreferredLanguage),
            created.CreatedAt));
    }

    private static (Guid A, Guid B) Canonicalise(Guid x, Guid y) =>
        x.CompareTo(y) < 0 ? (x, y) : (y, x);

    public sealed record CreateConversationRequest(Guid PeerId);

    public sealed record PeerRef(Guid Id, string DisplayName, string PreferredLanguage);

    public sealed record ConversationSummary(Guid Id, PeerRef Peer, DateTimeOffset LastActivityAt);
}
