using System.Security.Cryptography;
using System.Text.Json;
using Azure;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.AI.Extensions.OpenAI;
using Azure.Core;
using Azure.Data.Tables;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
using chatui.Configuration;
using chatui.Data;
using Microsoft.Extensions.Options;
using Microsoft.CognitiveServices.Speech;
using System.Net.WebSockets;

namespace chatui.Services;

#pragma warning disable OPENAI001
internal static class ServiceInitialization
{
    internal static async Task VerifyAsync(IServiceProvider services)
    {
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;
        var tableOptions = provider.GetRequiredService<IOptions<TableStorageOptions>>().Value;
        var usersTable = provider.GetRequiredService<TableServiceClient>().GetTableClient(tableOptions.UsersTableName);
        await foreach (var _ in usersTable.QueryAsync<TableEntity>(maxPerPage: 1, cancellationToken: lifetime.Token))
        {
            break;
        }
        Console.WriteLine("VERIFY table storage passed");
        var translation = await provider.GetRequiredService<TranslatorService>()
            .TranslateAsync("Hello", "ru", "en", lifetime.Token);
        if (string.IsNullOrWhiteSpace(translation.TranslatedText)) throw new InvalidOperationException("Empty translation.");
        Console.WriteLine("VERIFY translator passed");
        await provider.GetRequiredService<JoinTokenService>().IssueAsync(Guid.NewGuid(), "deployment-check", lifetime.Token);
        Console.WriteLine("VERIFY vault passed");
        var credential = provider.GetRequiredService<TokenCredential>();
        var speech = provider.GetRequiredService<IOptions<SpeechOptions>>().Value;
        var speechToken = await credential.GetTokenAsync(new TokenRequestContext(["https://cognitiveservices.azure.com/.default"]), lifetime.Token);
        using (var speechSocket = new ClientWebSocket())
        {
            speechSocket.Options.SetRequestHeader("Authorization", $"Bearer aad#{speech.ResourceId}#{speechToken.Token}");
            var speechUri = new UriBuilder(speech.Endpoint) { Scheme = "wss", Port = -1, Path = "/tts/cognitiveservices/websocket/v1" }.Uri;
            await speechSocket.ConnectAsync(speechUri, lifetime.Token);
            speechSocket.Abort();
            Console.WriteLine("VERIFY speech websocket passed");
        }
        var speechConfig = SpeechConfig.FromEndpoint(new UriBuilder(speech.Endpoint) { Scheme = "wss", Port = -1 }.Uri);
        speechConfig.AuthorizationToken = $"aad#{speech.ResourceId}#{speechToken.Token}";
        speechConfig.SpeechSynthesisVoiceName = "en-US-JennyNeural";
        using var synthesizer = new SpeechSynthesizer(speechConfig, null);
        using var audio = await synthesizer.SpeakTextAsync("Connection check.").WaitAsync(lifetime.Token);
        if (audio.Reason != ResultReason.SynthesizingAudioCompleted)
        {
            var details = SpeechSynthesisCancellationDetails.FromResult(audio);
            throw new InvalidOperationException($"Speech failed: {details.ErrorCode}: {details.ErrorDetails.Replace(speechToken.Token, "[redacted]")}");
        }
        Console.WriteLine($"VERIFY speech audioBytes={audio.AudioData.Length}");
        var pubsub = provider.GetRequiredService<IOptions<WebPubSubOptions>>().Value;
        var pubsubClient = new Azure.Messaging.WebPubSub.WebPubSubServiceClient(new Uri(pubsub.Endpoint), pubsub.Hub, credential);
        var accessUri = await pubsubClient.GetClientAccessUriAsync(TimeSpan.FromMinutes(1), "deployment-check", cancellationToken: lifetime.Token);
        using var socket = new ClientWebSocket();
        socket.Options.AddSubProtocol("json.webpubsub.azure.v1");
        await socket.ConnectAsync(accessUri, lifetime.Token);
        socket.Abort();
        Console.WriteLine("VERIFY pubsub passed");
        var summary = await provider.GetRequiredService<FoundryAgentService>().SummariseAsync(
            new("en", [new(Guid.NewGuid(), "We agreed to meet Friday.", "en", DateTimeOffset.UtcNow)]), lifetime.Token);
        if (string.IsNullOrWhiteSpace(summary)) throw new InvalidOperationException("Empty agent summary.");
        Console.WriteLine("VERIFY foundry passed");
        var chat = provider.GetRequiredService<IOptions<ChatApiOptions>>().Value;
        var realtimeToken = await credential.GetTokenAsync(new TokenRequestContext(["https://ai.azure.com/.default"]), lifetime.Token);
        using var realtime = new ClientWebSocket();
        realtime.Options.SetRequestHeader("Authorization", $"Bearer {realtimeToken.Token}");
        var realtimeUri = new UriBuilder(chat.AIProjectEndpoint) { Scheme = "wss", Port = -1, Path = "/openai/v1/realtime", Query = "model=gpt-realtime-2.1" }.Uri;
        await realtime.ConnectAsync(realtimeUri, lifetime.Token);
        realtime.Abort();
        Console.WriteLine("VERIFY training realtime passed");
    }

    internal static async Task RunAsync(string inputPath)
    {
        ServiceInput input;
        try
        {
            input = JsonSerializer.Deserialize<ServiceInput>(await File.ReadAllTextAsync(inputPath))
                ?? throw new InvalidOperationException("Missing initialization input.");
        }
        finally
        {
            File.Delete(inputPath);
        }
        using var lifetime = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        var vault = new SecretClient(new Uri(input.VaultUri), new BootstrapCredential(input.VaultToken));
        if (!string.IsNullOrEmpty(input.MicrosoftLoginSecret))
            await vault.SetSecretAsync("microsoft-login-client-secret", input.MicrosoftLoginSecret, lifetime.Token);
        if (!string.IsNullOrEmpty(input.GoogleLoginSecret))
            await vault.SetSecretAsync("google-login-client-secret", input.GoogleLoginSecret, lifetime.Token);
        try
        {
            await vault.GetSecretAsync("join-token-signing-key", cancellationToken: lifetime.Token);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            await vault.SetSecretAsync("join-token-signing-key", Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)), lifetime.Token);
        }
        Console.WriteLine("Key Vault secrets ready.");

        var project = new AIProjectClient(new Uri(input.ProjectEndpoint), new BootstrapCredential(input.FoundryToken));
        var definition = new DeclarativeAgentDefinition(input.Agent.Model) { Instructions = input.Agent.Instructions };
        ProjectsAgentVersion version = await project.AgentAdministrationClient.CreateAgentVersionAsync(
            input.Agent.Name, new ProjectsAgentVersionCreationOptions(definition), cancellationToken: lifetime.Token);
        Console.WriteLine(JsonSerializer.Serialize(new { agentName = version.Name, agentVersion = version.Version }));
        var agent = project.AsAIAgent(new AgentReference(version.Name, version.Version));
        var session = await agent.GetService<ChatClientAgent>()!.CreateSessionAsync(lifetime.Token);
        var result = await agent.RunAsync("""{"task":"summarise","target_language":"en","turns":[{"speaker":"Alice","text":"We agreed to meet Friday.","lang":"en"}]}""", session, cancellationToken: lifetime.Token);
        using var document = JsonDocument.Parse(result.ToString());
        if (string.IsNullOrWhiteSpace(document.RootElement.GetProperty("summary").GetString()))
            throw new InvalidOperationException("Agent smoke check returned no summary.");
        Console.WriteLine("Agent private invocation passed.");
    }

    private sealed record ServiceInput(
        string VaultUri,
        string VaultToken,
        string? MicrosoftLoginSecret,
        string? GoogleLoginSecret,
        string ProjectEndpoint,
        string FoundryToken,
        AgentInput Agent);
    private sealed record AgentInput(string Name, string Model, string Instructions);

    private sealed class BootstrapCredential(string token) : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new(token, DateTimeOffset.UtcNow.AddMinutes(5));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
}