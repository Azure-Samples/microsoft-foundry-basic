using chatui.Auth;
using chatui.Services;
using Microsoft.AspNetCore.Mvc;

namespace chatui.Controllers;

/// <summary>
/// Issues a Web PubSub client access URL scoped to the caller's personal
/// inbox group (used for real-time delivery of personal-chat messages,
/// summaries and any other user-targeted notifications). Separate from the
/// per-meeting join flow so clients can subscribe on app boot without
/// picking a meeting first.
/// </summary>
[ApiController]
[Route("api/wps")]
public class WpsController(IUserContext userContext, WebPubSubBroadcaster broadcaster) : ControllerBase
{
    [HttpGet("inbox")]
    public async Task<ActionResult<InboxTokenResponse>> Inbox(CancellationToken ct)
    {
        var me = await userContext.GetCurrentAsync(ct).ConfigureAwait(false);
        var uri = broadcaster.GetInboxAccessUri(me.Id);
        return Ok(new InboxTokenResponse(uri.ToString(), DateTimeOffset.UtcNow.AddHours(1)));
    }

    public sealed record InboxTokenResponse(string Url, DateTimeOffset ExpiresAt);
}
