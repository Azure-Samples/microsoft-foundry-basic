using System.ComponentModel.DataAnnotations;

namespace chatui.Configuration;

public class ChatApiOptions
{
    public bool TrainingGuidedEnabled { get; init; }
    public string[] TrainingMemberObjectIds { get; init; } = [];

    [Url]
    public string AIProjectEndpoint { get; init; } = default!;

    [Required]
    public string AIAgentId { get; init; } = default!;

    [Required]
    public string AIAgentVersion { get; init; } = default!;
}