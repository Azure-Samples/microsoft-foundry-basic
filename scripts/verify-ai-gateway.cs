#:property PublishAot=false
#:property PublishTrimmed=false
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

if (args.Contains("--self-test"))
{
    using var completed = JsonDocument.Parse("{\"status\":\"completed\",\"id\":\"test\",\"usage\":{\"input_tokens\":2,\"output_tokens\":3,\"total_tokens\":5}}");
    Validate(completed.RootElement);
    using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
        "event: response.created\n\ndata: {\"type\":\"response.completed\",\"response\":" + completed.RootElement.GetRawText() + "}\n\ndata: [DONE]\n"));
    Validate(await ReadStream(stream, CancellationToken.None));
    using var incomplete = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("data: [DONE]\n"));
    try { await ReadStream(incomplete, CancellationToken.None); throw new Exception("Missing completion accepted"); }
    catch (InvalidDataException) { }
    Console.WriteLine("VERIFY self-test passed");
    return 0;
}

using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
try
{
    var cancellation = deadline.Token;
    var endpoint = new Uri(Required("GATEWAY_VERIFY_ENDPOINT").TrimEnd('/') + "/");
    if (endpoint.Scheme != Uri.UriSchemeHttps) throw new InvalidOperationException("HTTPS required");
    var addresses = await Dns.GetHostAddressesAsync(endpoint.Host, cancellation);
    Console.WriteLine($"VERIFY gateway addresses={string.Join(",", addresses.Select(address => address.ToString()))}");
    using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
    var identityUri = Required("IDENTITY_ENDPOINT") + "?api-version=2019-08-01&resource="
        + Uri.EscapeDataString("https://ai.azure.com") + "&client_id=" + Uri.EscapeDataString(Required("AZURE_CLIENT_ID"));
    using var identityRequest = new HttpRequestMessage(HttpMethod.Get, identityUri);
    identityRequest.Headers.Add("X-IDENTITY-HEADER", Required("IDENTITY_HEADER"));
    using var identityResponse = await client.SendAsync(identityRequest, cancellation);
    identityResponse.EnsureSuccessStatusCode();
    using var identity = await JsonDocument.ParseAsync(await identityResponse.Content.ReadAsStreamAsync(cancellation), cancellationToken: cancellation);
    var token = identity.RootElement.GetProperty("access_token").GetString();
    Console.WriteLine("VERIFY managed identity token acquired");
    var departments = args.Contains("--departments") ? new[] { "Finance", "Unallocated" } : new[] { "Legacy" };
    if (args.Contains("--departments"))
    {
        foreach (var identityOnly in new[] { true, false })
        {
            using var deniedRequest = new HttpRequestMessage(HttpMethod.Get, new Uri(endpoint, "openai/responses/missing?api-version=2025-11-15-preview"));
            if (identityOnly)
                deniedRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            else
                deniedRequest.Headers.Add("Ocp-Apim-Subscription-Key", Required("GatewayDepartment__FinanceSubscriptionKey"));
            using var denied = await client.SendAsync(deniedRequest, cancellation);
            Console.WriteLine(JsonSerializer.Serialize(new { check = identityOnly ? "missing-key" : "missing-identity", status = (int)denied.StatusCode }));
            if (denied.StatusCode != HttpStatusCode.Unauthorized)
                throw new InvalidDataException("Gateway accepted incomplete credentials");
        }
    }
    foreach (var department in departments)
    foreach (var streaming in new[] { false, true })
    {
        var correlation = Guid.NewGuid().ToString();
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint, "openai/responses?api-version=2025-11-15-preview"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (department != "Legacy")
            request.Headers.Add("Ocp-Apim-Subscription-Key", Required($"GatewayDepartment__{department}SubscriptionKey"));
        request.Headers.Add("x-ms-client-request-id", correlation);
        request.Content = JsonContent.Create(new
        {
            agent = new { type = "agent_reference", name = Required("AIAgentId"), version = Environment.GetEnvironmentVariable("AIAgentVersion") ?? "1" },
            input = "Translate into English: Bonjour. Do not search the web.",
            max_output_tokens = 128,
            store = false,
            stream = streaming
        });
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        Console.WriteLine(JsonSerializer.Serialize(new { department, streaming, correlation, status = (int)response.StatusCode }));
        response.EnsureSuccessStatusCode();
        using var content = await response.Content.ReadAsStreamAsync(cancellation);
        JsonElement completed;
        if (streaming)
            completed = await ReadStream(content, cancellation);
        else
        {
            using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellation);
            completed = document.RootElement.Clone();
        }
        Validate(completed);
        Console.WriteLine(JsonSerializer.Serialize(new { department, streaming, response_id = completed.GetProperty("id").GetString(), usage = completed.GetProperty("usage") }));
    }
    Console.WriteLine("VERIFY gateway passed");
    return 0;
}
catch (Exception exception)
{
    Console.WriteLine($"VERIFY gateway failed {exception.GetType().Name}");
    return 1;
}

static string Required(string name) => Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"Missing {name}");

static void Validate(JsonElement response)
{
    if (response.GetProperty("status").GetString() != "completed"
        || !response.TryGetProperty("usage", out var usage)
        || usage.ValueKind != JsonValueKind.Object)
        throw new InvalidDataException("Response did not complete with usage");
}

static async Task<JsonElement> ReadStream(Stream stream, CancellationToken cancellation)
{
    using var reader = new StreamReader(stream, leaveOpen: true);
    while (await reader.ReadLineAsync(cancellation) is { } line)
    {
        if (!line.StartsWith("data: ") || line == "data: [DONE]") continue;
        using var document = JsonDocument.Parse(line[6..]);
        if (document.RootElement.TryGetProperty("type", out var type) && type.GetString() == "response.completed")
            return document.RootElement.GetProperty("response").Clone();
    }
    throw new InvalidDataException("Stream did not complete");
}