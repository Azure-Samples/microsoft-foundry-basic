using System.ComponentModel.DataAnnotations;

namespace chatui.Configuration;

public class ChatApiOptions
{
    [Url]
    public string AIProjectEndpoint { get; init; } = default!;

    [Url]
    public string AIAgentEndpoint { get; init; } = default!;
}