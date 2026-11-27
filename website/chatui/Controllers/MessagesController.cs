using System.ComponentModel.DataAnnotations;
using chatui.Auth;
using chatui.Data;
using chatui.Data.Entities;
using chatui.Services;
using Microsoft.AspNetCore.Mvc;

namespace chatui.Controllers;

/// <summary>
/// Personal-chat messages. Read scoped to the caller's conversations.
/// Send performs a synchronous Translator round-trip and persists both the
/// original and the translated text.
/// </summary>
[ApiController]
[Route("api/conversations/{conversationId:guid}/messages")]
public class MessagesController(
    IUserContext userContext,
    TransientSessionStore sessions,
    TranslatorService translator,
    WebPubSubBroadcaster broadcaster,
    ILogger<MessagesController> logger) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<MessageResponse>>> List(
        Guid conversationId,
        [FromQuery] DateTimeOffset? before,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var me = await userContext.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var conversation = await LoadMyConversationAsync(conversationId, me.Id, cancellationToken).ConfigureAwait(false);
        if (conversation is null)
        {
            return NotFound();
        }

        limit = Math.Clamp(limit, 1, 200);

        var response = sessions.ListMessages(conversationId, before, limit)
            .Select(ToResponse)
            .ToList();

        return Ok(response);
    }

    [HttpPost]
    public async Task<ActionResult<MessageResponse>> Send(
        Guid conversationId,
        [FromBody] SendMessageRequest request,
        CancellationToken cancellationToken)
    {
        var me = await userContext.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        var conversation = await LoadMyConversationAsync(conversationId, me.Id, cancellationToken).ConfigureAwait(false);
        if (conversation is null)
        {
            return NotFound();
        }

        var peer = conversation.UserAId == me.Id ? conversation.UserB : conversation.UserA;
        var targetLanguage = string.IsNullOrWhiteSpace(request.TargetLanguage)
            ? peer.PreferredLanguage
            : request.TargetLanguage!;

        var result = await translator.TranslateAsync(
            request.Text,
            targetLanguage,
            sourceLanguage: null,
            cancellationToken).ConfigureAwait(false);

        var message = new Message
        {
            ConversationId = conversationId,
            FromUserId = me.Id,
            SourceLang = result.SourceLanguage,
            TargetLang = result.TargetLanguage,
            Original = request.Text,
            Translated = result.TranslatedText,
        };
        sessions.AddMessage(message);

        var response = ToResponse(message);

        // Best-effort real-time push to the peer's inbox so they see the
        // message without polling. Persistence already succeeded — we swallow
        // WPS failures and the client can still pick it up on next fetch.
        try
        {
            await broadcaster.PublishInboxAsync(peer.Id, new
            {
                type = "message.new",
                conversationId,
                message = response,
                fromUser = new { id = me.Id, displayName = me.DisplayName },
                ts = message.CreatedAt,
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push message {MessageId} to inbox {UserId}", message.Id, peer.Id);
        }

        return Ok(response);
    }

    private Task<Conversation?> LoadMyConversationAsync(Guid conversationId, Guid meId, CancellationToken ct) =>
        Task.FromResult(sessions.GetConversation(conversationId, meId));

    private static MessageResponse ToResponse(Message m) => new(
        m.Id,
        m.ConversationId,
        m.FromUserId,
        m.SourceLang,
        m.TargetLang,
        m.Original,
        m.Translated,
        m.CreatedAt);

    public sealed record SendMessageRequest(
        [Required][StringLength(4000, MinimumLength = 1)] string Text,
        [StringLength(16, MinimumLength = 2)] string? TargetLanguage);

    public sealed record MessageResponse(
        Guid Id,
        Guid ConversationId,
        Guid FromUserId,
        string SourceLang,
        string TargetLang,
        string Original,
        string Translated,
        DateTimeOffset CreatedAt);
}
