using System.Text.Encodings.Web;
using System.Text.Json;
using MianMianPet.Infrastructure;
using MianMianPet.Services;

namespace MianMianPet.Configuration;

/// <summary>启动时读取本机配置；缺失字段使用默认值，已有文件不会被重写。</summary>
internal sealed class LocalConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    internal static LocalConfig Current { get; private set; } = new();

    public InternalAiConfig Ai { get; set; } = new();
    public PetExperienceConfig Experience { get; set; } = new();
    public string PersonaPrompt { get; set; } = ReadDefaultPrompt();

    internal static async Task LoadAsync(AppPaths paths, LogService log)
    {
        Current = new LocalConfig();
        try
        {
            if (!File.Exists(paths.ConfigFile))
            {
                await using var output = new FileStream(paths.ConfigFile, FileMode.CreateNew, FileAccess.Write);
                await JsonSerializer.SerializeAsync(output, Current, JsonOptions);
                return;
            }

            await using var input = File.OpenRead(paths.ConfigFile);
            var loaded = await JsonSerializer.DeserializeAsync<LocalConfig>(input, JsonOptions)
                ?? throw new InvalidDataException("配置根节点应为 JSON 对象。");
            if (loaded.Ai is null || loaded.Experience is null || string.IsNullOrWhiteSpace(loaded.PersonaPrompt))
                throw new InvalidDataException("ai、experience 不能为 null，personaPrompt 不能为空。");
            loaded.Ai.Validate();
            loaded.Experience.Validate();
            Current = loaded;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            // Do not log parser messages or raw JSON: either may contain an API key.
            var reason = ex switch
            {
                JsonException json => $"JSON 格式或字段类型无效（行 {json.LineNumber + 1}，字节位置 {json.BytePositionInLine}）。",
                InvalidDataException invalid => invalid.Message,
                _ => "无法读取或创建配置文件，请检查文件权限及占用情况。"
            };
            await log.ErrorAsync("config_load_failed", new InvalidDataException(
                $"config.json：{reason} 已使用默认体验并暂停 AI 请求；请修正后重启。"));
        }
    }

    private static string ReadDefaultPrompt()
    {
        using var stream = typeof(LocalConfig).Assembly.GetManifestResourceStream("MianMianPet.Resources.persona-prompt.txt")
            ?? throw new InvalidOperationException("内置人设提示词资源缺失。");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
