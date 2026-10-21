using Microsoft.AspNetCore.Mvc;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
using chatui.Configuration;
using System.Text;

namespace chatui.Controllers;

#pragma warning disable OPENAI001 // FoundryAgent is experimental

[ApiController]
[Route("[controller]/[action]")]
public class ChatController(
    FoundryAgentResolver agentResolver,
    ILogger<ChatController> logger) : ControllerBase
{
    // TODO: [security] Do not trust client to provide conversationId. Instead map current user to their active conversationId in your application's own state store.
    // Without this security control in place, a user can inject messages into another user's conversation.
    [HttpPost("{conversationId?}")]
    public async Task<IActionResult> Responses([FromRoute] string? conversationId, [FromBody] string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message cannot be null, empty, or whitespace.", nameof(message));

        // Sanitize untrusted input before logging to prevent log forging via newline/control characters.
        logger.LogDebug("Prompt received {Prompt}", SanitizeForLog(message));

        FoundryAgent agent = agentResolver.GetAgent();

        AgentSession session;
        string? conversationIdResult;
        if (string.IsNullOrEmpty(conversationId))
        {
            // First turn: create a server-side conversation through the platform's
            // Conversations API. The platform stores message history under the
            // returned id and binds a stable hosted-agent session to it.
            var conversationSession = (ChatClientAgentSession)await agent.CreateConversationSessionAsync();
            session = conversationSession;
            conversationIdResult = conversationSession.ConversationId;
        }
        else
        {
            // Subsequent turns: resume the existing server-side conversation.
            var innerAgent = agent.GetService<ChatClientAgent>()!;
            session = await innerAgent.CreateSessionAsync(conversationId);
            conversationIdResult = conversationId;
        }

        var response = await agent.RunAsync(message, session);

        // Return the conversation id so the client carries it on subsequent turns.
        return Ok(new { data = response.ToString(), id = conversationIdResult });
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