using Microsoft.Extensions.Options;
using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.AspNetCore;
using chatui.Auth;
using chatui.Configuration;
using chatui.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<ChatApiOptions>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<PostgresOptions>()
    .Bind(builder.Configuration.GetSection(PostgresOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<DevAuthOptions>()
    .Bind(builder.Configuration.GetSection(DevAuthOptions.SectionName));

builder.Services.AddOptions<TranslatorOptions>()
    .Bind(builder.Configuration.GetSection(TranslatorOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<KeyVaultOptions>()
    .Bind(builder.Configuration.GetSection(KeyVaultOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<JoinTokenOptions>()
    .Bind(builder.Configuration.GetSection(JoinTokenOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<WebPubSubOptions>()
    .Bind(builder.Configuration.GetSection(WebPubSubOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddOptions<SpeechOptions>()
    .Bind(builder.Configuration.GetSection(SpeechOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton<TokenCredential>(sp =>
{
    // Explicitly pin to the user-assigned managed identity attached to this
    // App Service. On App Service Linux with a single UAMI, DefaultAzureCredential
    // still works but is much slower on cold-start and occasionally 400s on
    // certain scopes (Translator's cognitiveservices.azure.com/.default has been
    // reported to fail via the IMDS probe path). Using ManagedIdentityCredential
    // with an explicit clientId is faster and skips the probe chain.
    var clientId = builder.Configuration["AZURE_CLIENT_ID"];
    if (!string.IsNullOrEmpty(clientId))
    {
        return new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(clientId));
    }
    // Local dev fallback: full chain (VS, VS Code, Azure CLI, etc.).
    return new DefaultAzureCredential();
});

builder.Services.AddSingleton((provider) =>
{
    var config = provider.GetRequiredService<IOptions<ChatApiOptions>>().Value;
    var credential = provider.GetRequiredService<TokenCredential>();
    return new AIProjectClient(new Uri(config.AIProjectEndpoint), credential);
});

builder.Services.AddSingleton<FoundryAgentResolver>();

// Npgsql data source whose password is a periodically-refreshed Entra ID access token
// scoped to the Azure Database for PostgreSQL AAD audience. The Postgres server has
// passwordAuth disabled and only accepts these tokens (see postgres.bicep).
builder.Services.AddSingleton<NpgsqlDataSource>((provider) =>
{
    var opts = provider.GetRequiredService<IOptions<PostgresOptions>>().Value;
    var credential = provider.GetRequiredService<TokenCredential>();

    var connectionString = new NpgsqlConnectionStringBuilder
    {
        Host = opts.Host,
        Port = opts.Port,
        Database = opts.Database,
        Username = opts.Username,
        SslMode = SslMode.Require,
        IncludeErrorDetail = true,
    }.ToString();

    var dataSourceBuilder = new NpgsqlDataSourceBuilder(connectionString);
    dataSourceBuilder.UsePeriodicPasswordProvider(
        async (settings, ct) =>
        {
            var context = new TokenRequestContext(["https://ossrdbms-aad.database.windows.net/.default"]);
            var accessToken = await credential.GetTokenAsync(context, ct).ConfigureAwait(false);
            return accessToken.Token;
        },
        successRefreshInterval: TimeSpan.FromMinutes(50),
        failureRefreshInterval: TimeSpan.FromSeconds(5));
    dataSourceBuilder.UseVector();
    return dataSourceBuilder.Build();
});

builder.Services.AddDbContext<TranslatorDbContext>((provider, options) =>
{
    var dataSource = provider.GetRequiredService<NpgsqlDataSource>();
    options.UseNpgsql(dataSource, npg => npg.UseVector());
    options.UseSnakeCaseNamingConvention();
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserContext, EasyAuthUserContext>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<chatui.Services.TranslatorService>();
builder.Services.AddSingleton<chatui.Services.JoinTokenService>();
builder.Services.AddSingleton<chatui.Services.QrCodeService>();
builder.Services.AddSingleton<chatui.Services.WebPubSubBroadcaster>();
builder.Services.AddSingleton<chatui.Services.FoundryAgentService>();
builder.Services.AddSingleton<chatui.Services.TranslatorTelemetry>();
builder.Services.AddSingleton<chatui.Services.TransientSessionStore>();

// Azure Monitor OpenTelemetry Distro — the current Microsoft-recommended
// path for new .NET apps. It wires up:
//  * Auto-instrumentation for ASP.NET Core, HttpClient, SqlClient, and
//    anything else with an OTel instrumentation package installed.
//  * An Azure Monitor exporter that reads APPLICATIONINSIGHTS_CONNECTION_STRING
//    from configuration and ships traces/metrics/logs to the workspace-based
//    App Insights component.
//  * Our custom ActivitySource + Meter (see TranslatorTelemetry) — spans
//    show up as dependencies, metrics as Azure Monitor Metrics + customMetrics.
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(chatui.Services.TranslatorTelemetry.SourceName))
    .WithMetrics(m => m.AddMeter(chatui.Services.TranslatorTelemetry.SourceName));
builder.Services.AddOpenTelemetry().UseAzureMonitor();

builder.Services.AddControllers();

var app = builder.Build();

app.Use(async (context, next) =>
{
    try
    {
        await next(context);
    }
    catch (DevPersonaRequiredException)
    {
        await Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Demo persona required",
            detail: "Specify a valid demo user in the URL.",
            extensions: new Dictionary<string, object?> { ["code"] = "demo_persona_required" })
            .ExecuteAsync(context);
    }
    catch (UserNotProvisionedException)
    {
        await Results.Problem(
            statusCode: StatusCodes.Status403Forbidden,
            title: "User is not provisioned",
            detail: "The selected user does not exist in this demo.",
            extensions: new Dictionary<string, object?> { ["code"] = "user_not_provisioned" })
            .ExecuteAsync(context);
    }
    catch (UnauthorizedAccessException)
    {
        await Results.Problem(
            statusCode: StatusCodes.Status401Unauthorized,
            title: "Authentication required",
            extensions: new Dictionary<string, object?> { ["code"] = "authentication_required" })
            .ExecuteAsync(context);
    }
});

app.UseDefaultFiles();

// SPA-friendly caching: HTML must revalidate every request so users pick up
// UI updates without hard refresh; everything else can be cached briefly.
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.File.Name;
        if (path.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        }
    }
});

app.UseRouting();

// Enable WebSocket support for the audio streaming endpoint
// (/api/speech/transcribe → server-side ConversationTranscriber).
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30),
});

app.MapControllers();

// Default MapFallbackToFile uses the `{*path:nonfile}` pattern which rejects
// any path containing a dot (treats it as a file extension). This breaks
// deep links like /join/{jwt} because JWTs contain dots. Use an explicit
// route that matches any non-/api/* path and serves index.html.
app.MapFallback("{**path}", async context =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    var indexPath = Path.Combine(app.Environment.WebRootPath, "index.html");
    if (!File.Exists(indexPath))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
    context.Response.ContentType = "text/html; charset=utf-8";
    await context.Response.SendFileAsync(indexPath);
});

app.Run();