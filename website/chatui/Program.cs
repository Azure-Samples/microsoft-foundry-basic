using Microsoft.Extensions.Options;
using Azure.AI.Projects;
using Azure.Core;
using Azure.Identity;
using chatui.Auth;
using chatui.Configuration;
using chatui.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

builder.Services.AddSingleton<TokenCredential>(_ => new DefaultAzureCredential());

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
builder.Services.AddSingleton<chatui.Services.TranslatorService>();
builder.Services.AddSingleton<chatui.Services.JoinTokenService>();
builder.Services.AddSingleton<chatui.Services.QrCodeService>();
builder.Services.AddSingleton<chatui.Services.WebPubSubBroadcaster>();

builder.Services.AddControllers();

var app = builder.Build();

app.UseStaticFiles();

app.UseRouting();

app.MapControllers();

app.Run();