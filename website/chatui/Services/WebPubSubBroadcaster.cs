using System.Text.Json;
using Azure.Core;
using Azure.Messaging.WebPubSub;
using chatui.Configuration;
using Microsoft.Extensions.Options;

namespace chatui.Services;

/// <summary>
/// Server-side broadcaster for meeting events over Web PubSub. Encapsulates
/// group naming (m:{meetingId}:presence | :lang:{lang} | :u:{userId}) so
/// controllers never spell them out. See docs/translator-design.md §4.
/// </summary>
public sealed class WebPubSubBroadcaster
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly WebPubSubServiceClient _client;
    private readonly WebPubSubOptions _options;

    public WebPubSubBroadcaster(TokenCredential credential, IOptions<WebPubSubOptions> options)
    {
        _options = options.Value;
        _client = new WebPubSubServiceClient(new Uri(_options.Endpoint), _options.Hub, credential);
    }

    public Uri GetClientAccessUri(Guid userId, Guid meetingId, string language)
    {
        var groups = new[]
        {
            PresenceGroup(meetingId),
            LanguageGroup(meetingId, language),
            UserGroup(meetingId, userId),
        };

        return _client.GetClientAccessUri(
            expiresAfter: TimeSpan.FromMinutes(_options.ClientTokenLifetimeMinutes),
            userId: userId.ToString(),
            groups: groups);
    }

    /// <summary>
    /// Client access URL pre-joined to the caller's personal inbox group.
    /// Used by the personal-chat surface to push new messages to the recipient
    /// without polling.
    /// </summary>
    public Uri GetInboxAccessUri(Guid userId)
    {
        return _client.GetClientAccessUri(
            expiresAfter: TimeSpan.FromMinutes(_options.ClientTokenLifetimeMinutes),
            userId: userId.ToString(),
            groups: [InboxGroup(userId)]);
    }

    public Task PublishInboxAsync(Guid userId, object envelope, CancellationToken cancellationToken)
        => SendJsonAsync(InboxGroup(userId), envelope, cancellationToken);

    public Task PublishTurnAsync(
        Guid meetingId,
        string targetLanguage,
        object envelope,
        CancellationToken cancellationToken)
        => SendJsonAsync(LanguageGroup(meetingId, targetLanguage), envelope, cancellationToken);

    public Task PublishPresenceAsync(Guid meetingId, object envelope, CancellationToken cancellationToken)
        => SendJsonAsync(PresenceGroup(meetingId), envelope, cancellationToken);

    public Task PublishToUserAsync(Guid meetingId, Guid userId, object envelope, CancellationToken cancellationToken)
        => SendJsonAsync(UserGroup(meetingId, userId), envelope, cancellationToken);

    private async Task SendJsonAsync(string group, object envelope, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        await _client.SendToGroupAsync(group, json, ContentType.ApplicationJson).ConfigureAwait(false);
    }

    private static string PresenceGroup(Guid meetingId) => $"m:{meetingId}:presence";
    private static string LanguageGroup(Guid meetingId, string language) => $"m:{meetingId}:lang:{language}";
    private static string UserGroup(Guid meetingId, Guid userId) => $"m:{meetingId}:u:{userId}";
    private static string InboxGroup(Guid userId) => $"inbox:{userId}";
}
