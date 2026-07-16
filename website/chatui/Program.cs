using Microsoft.Extensions.Options;
using Azure.AI.Projects;
using Azure.Identity;
using chatui.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<ChatApiOptions>()
    .Bind(builder.Configuration)
    .ValidateDataAnnotations()
    .ValidateOnStart();

builder.Services.AddSingleton((provider) =>
{
    var config = provider.GetRequiredService<IOptions<ChatApiOptions>>().Value;
    AIProjectClient projectClient = new(new Uri(config.AIProjectEndpoint), new DefaultAzureCredential());

    return projectClient;
});

builder.Services.AddSingleton<FoundryAgentResolver>();

builder.Services.AddControllers();

var app = builder.Build();

app.UseStaticFiles();

app.UseRouting();

app.MapControllers();

app.Run();