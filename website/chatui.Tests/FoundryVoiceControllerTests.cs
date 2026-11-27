using System.Text.Json;
using chatui.Controllers;
using Xunit;

namespace chatui.Tests;

public sealed class FoundryVoiceControllerTests
{
    [Fact]
    public void VoiceEndpointPreservesProjectPathAndAddsSession()
    {
        var sessionId = Guid.Parse("59c8d63f-a993-4107-bbb0-5741ef9a6ca6");

        var endpoint = FoundryVoiceController.BuildWebSocketUri(
            "https://foundry.example/api/projects/projchat/",
            "agent-aitr-prod-swe-001",
            sessionId);

        Assert.Equal("wss", endpoint.Scheme);
        Assert.Equal(
            "/api/projects/projchat/agents/agent-aitr-prod-swe-001/endpoint/protocols/voice",
            endpoint.AbsolutePath);
        Assert.Equal("?api-version=v1&agent_session_id=59c8d63f-a993-4107-bbb0-5741ef9a6ca6", endpoint.Query);
    }

    [Fact]
    public void InputTranscriptionDeltaMapsToBrowserEnvelope()
    {
        var mapped = Map("""
            {"type":"conversation.item.input_audio_transcription.delta","delta":"Hel","speaker":"Guest-1"}
            """);

        Assert.NotNull(mapped);
        Assert.Equal("transcribing", mapped.Type);
        Assert.Equal("Hel", mapped.Text);
        Assert.Equal("Guest-1", mapped.SpeakerId);
    }

    [Fact]
    public void CompletedInputTranscriptionUsesReportedLanguage()
    {
        var mapped = Map("""
            {"type":"conversation.item.input_audio_transcription.completed","transcript":"Hej världen","language":"sv"}
            """, "en");

        Assert.NotNull(mapped);
        Assert.Equal("transcribed", mapped.Type);
        Assert.Equal("Hej världen", mapped.Text);
        Assert.Equal("sv", mapped.Language);
    }

    [Fact]
    public void CompletedInputTranscriptionFallsBackToRequestedLanguage()
    {
        var mapped = Map("""
            {"type":"item.input_audio_transcription.completed","text":"Hello"}
            """, "en");

        Assert.NotNull(mapped);
        Assert.Equal("en", mapped.Language);
    }

    [Theory]
    [InlineData("response.output_audio_transcript.delta")]
    [InlineData("response.output_audio.delta")]
    [InlineData("session.updated")]
    public void AgentOutputEventsAreNotForwardedAsUserTranscripts(string eventType)
    {
        var mapped = Map($$"""{"type":"{{eventType}}","delta":"ignored"}""");

        Assert.Null(mapped);
    }

    private static BrowserVoiceEvent? Map(string json, string defaultLanguage = "en")
    {
        using var document = JsonDocument.Parse(json);
        return FoundryVoiceController.MapUpstreamEvent(document.RootElement, defaultLanguage);
    }
}
