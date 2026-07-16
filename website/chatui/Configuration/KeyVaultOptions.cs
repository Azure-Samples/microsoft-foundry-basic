using System.ComponentModel.DataAnnotations;

namespace chatui.Configuration;

public class KeyVaultOptions
{
    public const string SectionName = "KeyVault";

    [Required]
    [Url]
    public string Uri { get; init; } = default!;

    /// <summary>
    /// Name of the secret that holds the HMAC signing key for meeting join
    /// tokens. Seeded post-deploy — see main.bicep `postDeployHintKeyVault`.
    /// </summary>
    [Required]
    public string JoinTokenSigningKeySecretName { get; init; } = "join-token-signing-key";
}
