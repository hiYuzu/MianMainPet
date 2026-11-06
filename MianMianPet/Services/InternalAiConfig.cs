namespace MianMianPet.Services;

/// <summary>
/// Private-build AI configuration. Desktop binaries cannot truly conceal an embedded API key.
/// Keep builds containing the real key within a trusted audience; use a backend proxy before
/// any public distribution.
/// </summary>
internal static class InternalAiConfig
{
    // Replace this one value locally with the DeepSeek key verified in v0.1 before publishing.
    // Never commit or publicly distribute the resulting source/binary.
    internal const string ApiKey = "PASTE_YOUR_VERIFIED_DEEPSEEK_API_KEY_HERE";

    internal const string BaseUrl = "https://api.deepseek.com";
    internal const string Model = "deepseek-flash";
    internal const int MaxTokens = 180;
    internal const double Temperature = 0.8;
    internal const int ContextMessageCount = 8;
    internal const int MaxContextCharacters = 12_000;
    internal const int BubbleAutoHideSeconds = 2;

    internal static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !ApiKey.StartsWith("PASTE_", StringComparison.Ordinal);
}
