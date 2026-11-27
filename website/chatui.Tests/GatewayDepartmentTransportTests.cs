using System.ClientModel.Primitives;
using System.Net;
using System.Text.Json;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Core;
using chatui.Auth;
using chatui.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Xunit;

namespace chatui.Tests;

#pragma warning disable OPENAI001

public sealed class GatewayDepartmentTransportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CachedAgentSelectsSubscriptionPerRequestIncludingStreaming(bool streaming)
    {
        var accessor = new HttpContextAccessor();
        var options = Options.Create(new GatewayDepartmentOptions
        {
            Enabled = true,
            TenantId = "acaa000d-7d61-417a-99c0-c5d791278c97",
            FinanceSubscriptionKey = "synthetic-finance-key",
            UnallocatedSubscriptionKey = "synthetic-unallocated-key",
        });
        var endpoint = new Uri("https://gateway.example/api/projects/projchat");
        var department = new GatewayDepartmentContext(accessor, options, new ProductionEnvironment(), Options.Create(new DevAuthOptions()));
        var capture = new CaptureHandler();
        using var httpClient = new HttpClient(new GatewaySubscriptionHandler(department, options, endpoint) { InnerHandler = capture });
        var project = new AIProjectClient(endpoint, new SyntheticCredential(), new AIProjectClientOptions
        {
            Transport = new HttpClientPipelineTransport(httpClient),
        });
        var agent = project.AsAIAgent(new AgentReference("synthetic-agent", "1"));

        foreach (var finance in new[] { true, false, true })
        {
            accessor.HttpContext = Principal(options.Value.TenantId, finance);
            capture.Keys.Clear();
            await Assert.ThrowsAnyAsync<Exception>(async () =>
            {
                if (streaming)
                {
                    await foreach (var update in agent.RunStreamingAsync("synthetic")) { }
                }
                else
                {
                    await agent.RunAsync("synthetic");
                }
            });
            Assert.NotEmpty(capture.Keys);
            Assert.All(capture.Keys, key => Assert.Equal(finance ? "synthetic-finance-key" : "synthetic-unallocated-key", key));
            Assert.Contains(capture.Paths, path => path.EndsWith("/responses", StringComparison.Ordinal));
        }
    }

    [Theory]
    [InlineData("https://elsewhere.example/api/projects/projchat/responses")]
    [InlineData("http://gateway.example/api/projects/projchat/responses")]
    [InlineData("https://gateway.example/api/projects/other/responses")]
    public async Task SubscriptionNeverLeavesConfiguredGateway(string target)
    {
        var options = Options.Create(new GatewayDepartmentOptions
        {
            Enabled = true, TenantId = "acaa000d-7d61-417a-99c0-c5d791278c97", FinanceSubscriptionKey = "synthetic",
        });
        var accessor = new HttpContextAccessor { HttpContext = Principal(options.Value.TenantId, true) };
        var department = new GatewayDepartmentContext(accessor, options, new ProductionEnvironment(), Options.Create(new DevAuthOptions()));
        var capture = new CaptureHandler();
        using var client = new HttpClient(new GatewaySubscriptionHandler(department, options,
            new Uri("https://gateway.example/api/projects/projchat")) { InnerHandler = capture });
        using var request = new HttpRequestMessage(HttpMethod.Post, target);
        request.Headers.Add("Ocp-Apim-Subscription-Key", "untrusted");
        using var response = await client.SendAsync(request);
        Assert.Null(Assert.Single(capture.Keys));
    }

    private static HttpContext Principal(string tenantId, bool finance)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-MS-CLIENT-PRINCIPAL"] = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new
        {
            auth_typ = "aad",
            claims = new[]
            {
                new { typ = "tid", val = tenantId },
                new { typ = "oid", val = "d17c1309-f166-46a3-84f6-9ca76b8d2c29" },
                new { typ = "roles", val = finance ? "Department.Finance" : "Other.Role" },
            },
        }));
        return context;
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<string?> Keys { get; } = [];
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Keys.Add(request.Headers.TryGetValues("Ocp-Apim-Subscription-Key", out var values) ? values.Single() : null);
            Paths.Add(request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent("{\"error\":{\"message\":\"Synthetic test response\",\"type\":\"invalid_request_error\"}}", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class SyntheticCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("synthetic-token", DateTimeOffset.UtcNow.AddHours(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class ProductionEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}