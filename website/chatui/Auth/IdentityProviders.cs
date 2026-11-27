namespace chatui.Auth;

public static class IdentityProviders
{
    public const string Microsoft = "microsoft";
    public const string Google = "google";

    public static string? Normalize(string? authenticationType) => authenticationType?.Trim().ToLowerInvariant() switch
    {
        "aad" or "azureactivedirectory" or "microsoft" => Microsoft,
        "google" => Google,
        _ => null,
    };
}
