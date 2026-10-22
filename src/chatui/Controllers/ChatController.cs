using Microsoft.AspNetCore.Mvc;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
using chatui.Configuration;
using System.Text;
using System.Text.Json;

namespace chatui.Controllers;

#pragma warning disable OPENAI001 // FoundryAgent is experimental

[ApiController]
[Route("[controller]/[action]")]
public class ChatController(
    FoundryAgentResolver agentResolver,
    SharedHostedSessionProvider sessionProvider,
    ILogger<ChatController> logger) : ControllerBase
{
    public class Request
    {
        public string Message { get; set; } = default!;

        // Serialized AgentSession from the previous turn (null on the first turn).
        public JsonElement? Session { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> Responses([FromBody] Request request)
    {
        if (string.IsNullOrWhiteSpace(request.Message))
            throw new ArgumentException("Message cannot be null, empty, or whitespace.", nameof(request.Message));

        // Sanitize untrusted input before logging to prevent log forging via newline/control characters.
        logger.LogDebug("Prompt received: {Prompt}", SanitizeForLog(request.Message));

        FoundryAgent agent = agentResolver.GetAgent();

        // Stateless server: the client round-trips the serialized session. Turn 1 has none,
        // so create a fresh one pinned to the shared hosted sandbox; later turns restore
        // the prior conversation state.
        AgentSession session = request.Session is { } saved
            ? await agent.DeserializeSessionAsync(saved)
            : await CreatePinnedSessionAsync(agent);

        var response = await agent.RunAsync(request.Message, session);

        // Marshal the (now updated) session back to the client so the next turn can
        // restore it. Serialized sessions carry conversation state and are sensitive —
        // treat them like a credential on the wire.
        JsonElement serialized = await agent.SerializeSessionAsync(session);

        return Ok(new { data = response.ToString(), session = serialized });
    }

    /// <summary>
    /// Creates a MAF session pinned to the shared hosted sandbox. The hosted session id is
    /// provisioned through the agent administration API (pinned to a specific agent version);
    /// attaching it here binds this MAF session's conversation to that sandbox.
    /// </summary>
    private async Task<AgentSession> CreatePinnedSessionAsync(FoundryAgent agent)
    {
        string hostedSessionId = await sessionProvider.GetHostedSessionIdAsync();
        return await agent.CreateFoundryHostedAgentSessionAsync(hostedSessionId);
    }

    private static string SanitizeForLog(string input)
    {
        var builder = new StringBuilder(input.Length);

        foreach (char character in input)
        {
            if (character == '\r')
            {
                builder.Append("\\r");
            }
            else if (character == '\n')
            {
                builder.Append("\\n");
            }
            else if (char.IsControl(character))
            {
                builder.Append('?');
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }
}
