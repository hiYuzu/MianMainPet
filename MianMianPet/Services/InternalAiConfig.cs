namespace MianMianPet.Services;

/// <summary>
/// 从本机 config.json 读取的 AI 参数，默认不包含密钥。
/// </summary>
internal sealed class InternalAiConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string Model { get; set; } = "deepseek-flash";
    public int MaxTokens { get; set; } = 480;
    public double Temperature { get; set; } = 0.8;
    public int ContextMessageCount { get; set; } = 16;
    public int MaxContextCharacters { get; set; } = 12_000;
    // Minimum reading time; longer replies may remain visible for longer.
    public int BubbleAutoHideSeconds { get; set; } = 2;

    internal bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ApiKey) &&
        !ApiKey.StartsWith("PASTE_", StringComparison.Ordinal);

    internal void Validate()
    {
        ApiKey = ApiKey?.Trim() ?? string.Empty;
        BaseUrl = BaseUrl?.Trim().TrimEnd('/') ?? string.Empty;
        Model = Model?.Trim() ?? string.Empty;
        if (ApiKey.Any(char.IsWhiteSpace))
            throw new InvalidDataException("ai.apiKey 不能包含空白字符。");
        if (!Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http") ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidDataException("ai.baseUrl 应为不含账号、查询参数或片段的 HTTP/HTTPS 地址。");
        if (Model.Length == 0 || MaxTokens <= 0 || !double.IsFinite(Temperature) || Temperature is < 0 or > 2 ||
            ContextMessageCount is < 1 or > 50 || MaxContextCharacters <= 0 || BubbleAutoHideSeconds is < 1 or > 86400)
            throw new InvalidDataException("ai 中的模型或生成参数无效；温度应为 0～2，历史条数为 1～50，其余数值应为正数，气泡秒数最多 86400。");
    }
}
