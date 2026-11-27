using chatui.Auth;
using chatui.Data;
using chatui.Data.Entities;
using chatui.Services;
using Microsoft.AspNetCore.Mvc;

namespace chatui.Controllers;

/// <summary>
/// Personal-chat conversations. A conversation is a canonicalised pair
/// (UserAId &lt; UserBId), so /conversations {peerId} is idempotent.
/// </summary>
[ApiController]
[Route("api/conversations")]
public class ConversationsController(
    IUserContext userContext,
    IUserProfileStore profiles,
    TransientSessionStore sessions) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<ConversationSummary>>> List(
        CancellationToken cancellationToken)
    {
        var me = await userContext.GetCurrentAsync(cancellationToken).ConfigureAwait(false);

        var conversations = sessions.ListConversations(me.Id);

        var response = conversations.Select(c =>
        {
            var peer = c.UserAId == me.Id ? c.UserB : c.UserA;
            return new ConversationSummary(
                c.Id,
                new PeerRef(peer.Id, peer.DisplayName, peer.PreferredLanguage),
                sessions.LastActivity(c.Id));
        }).ToList();

        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<ConversationSummary>> CreateOrGet(
        [FromBody] CreateConversationRequest request,
        CancellationToken cancellationToken)
    {
        var me = await userContext.GetCurrentAsync(cancellationToken).ConfigureAwait(false);

        if (request.PeerId == me.Id)
        {
            return BadRequest(new { error = "Cannot start a conversation with yourself." });
        }

        var peer = await profiles.GetByIdAsync(request.PeerId, cancellationToken).ConfigureAwait(false);
        if (peer is null)
        {
            return NotFound(new { error = "Peer user not found." });
        }

        var created = sessions.GetOrCreateConversation(me, peer);

        return Ok(new ConversationSummary(
            created.Id,
            new PeerRef(peer.Id, peer.DisplayName, peer.PreferredLanguage),
            created.CreatedAt));
    }

    public sealed record CreateConversationRequest(Guid PeerId);

    public sealed record PeerRef(Guid Id, string DisplayName, string PreferredLanguage);

    public sealed record ConversationSummary(Guid Id, PeerRef Peer, DateTimeOffset LastActivityAt);
}
