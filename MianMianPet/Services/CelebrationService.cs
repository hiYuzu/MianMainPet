using System.Globalization;
using System.Text.Json;
using MianMianPet.Infrastructure;
using MianMianPet.Models;

namespace MianMianPet.Services;

public sealed class CelebrationService
{
    private readonly DatabaseService _database;
    private readonly AppPaths _paths;
    private readonly LogService? _log;
    private readonly HashSet<string> _opened = new();
    private List<CelebrationRule>? _lastValidRules;
    internal string? LastConfigurationError { get; private set; }
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CelebrationService(DatabaseService database, AppPaths paths, LogService? log = null)
    {
        _database = database;
        _paths = paths;
        _log = log;
    }

    public static Celebration? ForDate(CelebrationRule rule, DateTime localDate)
    {
        if (rule.Calendar == "solar")
        {
            return localDate.Month == rule.Month && localDate.Day == rule.Day
                ? new Celebration(rule.Id, localDate.Year, rule.Message)
                : null;
        }

        var calendar = new ChineseLunisolarCalendar();
        if (localDate < calendar.MinSupportedDateTime || localDate > calendar.MaxSupportedDateTime)
        {
            return null;
        }

        var year = calendar.GetYear(localDate);
        var month = calendar.GetMonth(localDate);
        var leapMonth = calendar.GetLeapMonth(year);
        // GetMonth includes the leap month in its numbering. Rules refer to regular lunar months.
        if (month == leapMonth)
        {
            return null;
        }

        if (leapMonth > 0 && month > leapMonth)
        {
            month--;
        }

        return month == rule.Month && calendar.GetDayOfMonth(localDate) == rule.Day
            ? new Celebration(rule.Id, year, rule.Message)
            : null;
    }

    public async Task<Celebration?> GetPendingAsync(DateTime localDate)
    {
        var rules = await LoadRulesAsync();
        foreach (var rule in rules)
        {
            var celebration = ForDate(rule, localDate);
            if (celebration is null) continue;
            var key = $"celebration.{celebration.Key}.{celebration.Year}";
            var saved = await _database.GetStateAsync(key);
            if (saved != "opened" && !_opened.Contains(key)) return celebration;
        }

        return null;
    }

    public async Task MarkOpenedAsync(Celebration celebration)
    {
        var key = $"celebration.{celebration.Key}.{celebration.Year}";
        _opened.Add(key);
        await _database.SetStateAsync(key, "opened");
    }

    private async Task<List<CelebrationRule>> LoadRulesAsync()
    {
        try
        {
            if (!File.Exists(_paths.CelebrationsFile))
                await File.WriteAllTextAsync(_paths.CelebrationsFile, "[]");

            var json = await File.ReadAllTextAsync(_paths.CelebrationsFile);
            var rules = JsonSerializer.Deserialize<List<CelebrationRule>>(json, JsonOptions)
                ?? throw new InvalidDataException("纪念日配置应为 JSON 数组。");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < rules.Count; i++)
            {
                var rule = rules[i];
                string? field = null;
                if (rule is null) field = "项目（不能为 null）";
                else if (string.IsNullOrWhiteSpace(rule.Id)) field = "id（不能为空）";
                else if (!ids.Add(rule.Id)) field = "id（不能重复）";
                else if (rule.Calendar is not ("solar" or "lunar")) field = "calendar（应为 solar 或 lunar）";
                else if (rule.Month is < 1 or > 12) field = "month（应为 1～12）";
                else if (rule.Day < 1 || rule.Day > (rule.Calendar == "lunar" ? 30 : DateTime.DaysInMonth(2000, rule.Month)))
                    field = "day（超出当前历法和月份范围）";
                else if (string.IsNullOrWhiteSpace(rule.Message) || rule.Message.Length > 2000)
                    field = "message（应为 1～2000 个字符）";
                if (field is not null)
                    throw new InvalidDataException($"纪念日配置第 {i + 1} 项的 {field} 无效。");
            }

            LastConfigurationError = null;
            _lastValidRules = rules;
            return rules;
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            LastConfigurationError = ex is JsonException json
                ? $"纪念日 JSON 格式或字段类型无效（行 {json.LineNumber + 1}，字节位置 {json.BytePositionInLine + 1}）。"
                : ex is InvalidDataException ? ex.Message : "无法读取纪念日配置，请检查文件权限及占用情况。";
            var error = new InvalidDataException(LastConfigurationError);
            if (_lastValidRules is null) throw error;
            if (_log is not null) await _log.ErrorAsync("celebration_config_invalid_using_previous", error);
            return _lastValidRules;
        }
    }
}
