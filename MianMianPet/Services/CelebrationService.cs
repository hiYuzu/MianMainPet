using System.Globalization;
using System.Text.Json;
using MianMianPet.Infrastructure;
using MianMianPet.Models;

namespace MianMianPet.Services;

public sealed class CelebrationService
{
    private readonly DatabaseService _database;
    private readonly AppPaths _paths;
    private readonly HashSet<string> _opened = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public CelebrationService(DatabaseService database, AppPaths paths)
    {
        _database = database;
        _paths = paths;
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
        if (!File.Exists(_paths.CelebrationsFile))
        {
            await File.WriteAllTextAsync(_paths.CelebrationsFile, "[]");
        }

        var json = await File.ReadAllTextAsync(_paths.CelebrationsFile);
        var rules = JsonSerializer.Deserialize<List<CelebrationRule>>(json, JsonOptions)
            ?? throw new InvalidDataException("纪念日配置应为 JSON 数组。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in rules)
        {
            if (rule is null || string.IsNullOrWhiteSpace(rule.Id) || !ids.Add(rule.Id) ||
                rule.Calendar is not ("solar" or "lunar") || rule.Month is < 1 or > 12 ||
                rule.Day < 1 || rule.Day > (rule.Calendar == "lunar" ? 30 : DateTime.DaysInMonth(2000, rule.Month)) ||
                string.IsNullOrWhiteSpace(rule.Message) || rule.Message.Length > 2000)
            {
                throw new InvalidDataException("纪念日配置的标识、历法、日期或文案无效。");
            }
        }

        return rules;
    }
}
