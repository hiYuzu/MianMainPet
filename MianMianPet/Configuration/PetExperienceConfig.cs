namespace MianMianPet.Configuration;

/// <summary>
/// 绵绵的默认体验参数，可通过本机 config.json 的 experience 节点覆盖。
/// </summary>
internal sealed class PetExperienceConfig
{
    // 闲置场景：先出现草地、云朵和星光，继续闲置后进入睡眠。
    public TimeSpan AmbientIdleDelay { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan SleepIdleDelay { get; set; } = TimeSpan.FromMinutes(1.5);

    // 主动玩法：首次空闲时较早出现，之后留出陪伴间隔；被交互占用时稍后重试。
    public TimeSpan PlayMinimumIdle { get; set; } = TimeSpan.FromSeconds(45);
    public TimeSpan FirstPlayMaximumDelay { get; set; } = TimeSpan.FromSeconds(75);
    public TimeSpan PlayMinimumDelay { get; set; } = TimeSpan.FromMinutes(8);
    public TimeSpan PlayMaximumDelay { get; set; } = TimeSpan.FromMinutes(12);
    public TimeSpan PlayBlockedRetryDelay { get; set; } = TimeSpan.FromSeconds(15);

    // 主动陪伴：启动后的第一次会稍早出现，之后保持克制且带随机性。
    public TimeSpan FirstCompanionMinimumDelay { get; set; } = TimeSpan.FromMinutes(8);
    public TimeSpan FirstCompanionMaximumDelay { get; set; } = TimeSpan.FromMinutes(12);
    public TimeSpan CompanionMinimumDelay { get; set; } = TimeSpan.FromMinutes(50);
    public TimeSpan CompanionMaximumDelay { get; set; } = TimeSpan.FromMinutes(60);
    public TimeSpan QuietHoursMinimumDelay { get; set; } = TimeSpan.FromMinutes(90);
    public TimeSpan QuietHoursMaximumDelay { get; set; } = TimeSpan.FromMinutes(120);
    public TimeSpan CompanionBlockedRetryDelay { get; set; } = TimeSpan.FromSeconds(45);
    public TimeSpan MaximumSystemIdleForCompanion { get; set; } = TimeSpan.FromMinutes(2);
    public int QuietHoursStartHour { get; set; } = 0;
    public int QuietHoursEndHour { get; set; } = 7;

    // 失败和主动陪伴气泡的停留时间。
    public int FailureBubbleAutoHideSeconds { get; set; } = 6;
    public int CompanionBubbleAutoHideSeconds { get; set; } = 7;
    public TimeSpan AiRequestTimeout { get; set; } = TimeSpan.FromMinutes(2);

    // 昼夜分段使用电脑本地时间。
    public int DawnStartHour { get; set; } = 6;
    public int DayStartHour { get; set; } = 10;
    public int DuskStartHour { get; set; } = 17;
    public int NightStartHour { get; set; } = 20;
    public TimeSpan DayPhaseRefreshInterval { get; set; } = TimeSpan.FromMinutes(1);

    public string[] NetworkFailureReplies { get; set; } =
    [
        "外面的风好像把声音吹散了……等一下再和我说一次吧。",
        "云朵把{PetName}的信号挡住啦，等等它飘过去吧。",
        "{PetName}刚刚没能听清，再等一小会儿好不好？"
    ];

    public string[] TimeoutFailureReplies { get; set; } =
    [
        "唔……{PetName}刚刚走神了，再和我说一次好不好？",
        "这句话在云朵里绕得有点久，我们一会儿再试试吧。",
        "咩……刚才没有接住这句话。"
    ];

    public string[] ServiceFailureReplies { get; set; } =
    [
        "{PetName}现在有一点点迷糊，休息一下就回来啦。",
        "今天的云有点厚，{PetName}暂时没办法自由说话了。",
        "{PetName}的声音暂时躲起来了，过一会儿再来找我吧。"
    ];

    public string[] GeneralFailureReplies { get; set; } =
    [
        "咩……刚才那句话不小心从羊毛里滑走了。",
        "{PetName}刚刚打了个小喷嚏，没有把话说出来。",
        "这次没有接住，不过{PetName}还在这里呀。"
    ];

    public string[] MorningCompanionReplies { get; set; } =
    [
        "早呀，{UserTitle}。今天也要慢慢来，不用着急。",
        "晨光软软的，{PetName}把最好的一小团留给{UserTitle}。",
        "新的一天开始啦，先送给{UserTitle}一个羊毛抱抱。"
    ];

    public string[] DayCompanionReplies { get; set; } =
    [
        "{UserTitle}认真做事情的样子，{PetName}有偷偷看到哦。",
        "路过一下，给{UserTitle}放下一小团软绵绵。",
        "要是有一点累，就看{PetName}一眼吧。",
        "{PetName}没有打扰，只是在旁边陪着{UserTitle}。"
    ];

    public string[] DuskCompanionReplies { get; set; } =
    [
        "天色变得软软的啦，{UserTitle}今天辛苦了。",
        "晚霞来了，{PetName}想把暖暖的颜色分给{UserTitle}。",
        "忙了一天，也记得给自己留一点轻松呀。"
    ];

    public string[] NightCompanionReplies { get; set; } =
    [
        "夜里安静下来了，{PetName}还在这里陪着{UserTitle}。",
        "星星亮起来啦，今天也辛苦了。",
        "困了就早点休息吧，没做完的事情明天也会等你。"
    ];

    public string[] LateNightCompanionReplies { get; set; } =
    [
        "夜已经很深啦，{PetName}陪{UserTitle}把这一点做完就休息。",
        "小声提醒一下：现在很晚了，要照顾好自己呀。",
        "{PetName}开始犯困啦，{UserTitle}也不要熬得太久哦。"
    ];

    internal void Validate()
    {
        var durations = new[]
        {
            AmbientIdleDelay, SleepIdleDelay, PlayMinimumIdle, FirstPlayMaximumDelay,
            PlayMinimumDelay, PlayMaximumDelay, PlayBlockedRetryDelay,
            FirstCompanionMinimumDelay, FirstCompanionMaximumDelay, CompanionMinimumDelay,
            CompanionMaximumDelay, QuietHoursMinimumDelay, QuietHoursMaximumDelay,
            CompanionBlockedRetryDelay, MaximumSystemIdleForCompanion, AiRequestTimeout, DayPhaseRefreshInterval
        };
        if (durations.Any(value => value <= TimeSpan.Zero || value > TimeSpan.FromDays(1)))
            throw new InvalidDataException("experience 中的时间间隔应大于零且不超过 24 小时，格式为 hh:mm:ss。");
        if (SleepIdleDelay < AmbientIdleDelay || FirstPlayMaximumDelay < PlayMinimumIdle ||
            PlayMaximumDelay < PlayMinimumDelay || FirstCompanionMaximumDelay < FirstCompanionMinimumDelay ||
            CompanionMaximumDelay < CompanionMinimumDelay || QuietHoursMaximumDelay < QuietHoursMinimumDelay)
            throw new InvalidDataException("experience 中的最大间隔不能小于最小间隔，睡眠时间不能早于环境待机时间。");
        if (QuietHoursStartHour is < 0 or > 23 || QuietHoursEndHour > 24 || QuietHoursEndHour <= QuietHoursStartHour ||
            DawnStartHour < 0 || DayStartHour <= DawnStartHour || DuskStartHour <= DayStartHour ||
            NightStartHour <= DuskStartHour || NightStartHour > 23 ||
            FailureBubbleAutoHideSeconds is < 1 or > 86400 || CompanionBubbleAutoHideSeconds is < 1 or > 86400)
            throw new InvalidDataException("experience 中的小时分段或气泡时长无效；分段需递增，气泡秒数为 1～86400。");
        var replyGroups = new[]
        {
            NetworkFailureReplies, TimeoutFailureReplies, ServiceFailureReplies, GeneralFailureReplies,
            MorningCompanionReplies, DayCompanionReplies, DuskCompanionReplies, NightCompanionReplies, LateNightCompanionReplies
        };
        if (replyGroups.Any(group => group is null || group.Length == 0 || group.Any(string.IsNullOrWhiteSpace)))
            throw new InvalidDataException("experience 中的每组文案至少需要一条非空字符串。");

        // Upgrade only the exact old built-in replies; preserve user-written text.
        var defaults = new PetExperienceConfig();
        var legacyReplies = new[]
        {
            defaults.NetworkFailureReplies, defaults.TimeoutFailureReplies, defaults.ServiceFailureReplies,
            defaults.GeneralFailureReplies, defaults.MorningCompanionReplies, defaults.DayCompanionReplies,
            defaults.DuskCompanionReplies, defaults.NightCompanionReplies, defaults.LateNightCompanionReplies
        }.SelectMany(group => group).Where(reply => reply.Contains("{PetName}", StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToDictionary(reply => reply.Replace("{PetName}", "绵绵", StringComparison.Ordinal), reply => reply, StringComparer.Ordinal);
        foreach (var group in replyGroups)
            for (var i = 0; i < group.Length; i++)
                if (legacyReplies.TryGetValue(group[i], out var replacement)) group[i] = replacement;
    }

    internal PetDayPhase GetDayPhase(DateTime localTime)
    {
        var hour = localTime.Hour;
        if (hour >= DawnStartHour && hour < DayStartHour)
        {
            return PetDayPhase.Dawn;
        }

        if (hour >= DayStartHour && hour < DuskStartHour)
        {
            return PetDayPhase.Day;
        }

        return hour >= DuskStartHour && hour < NightStartHour
            ? PetDayPhase.Dusk
            : PetDayPhase.Night;
    }

    internal bool IsQuietHours(DateTime localTime) =>
        localTime.Hour >= QuietHoursStartHour && localTime.Hour < QuietHoursEndHour;

    internal TimeSpan GetNextCompanionDelay(Random random, DateTime localTime, bool isFirst)
    {
        if (IsQuietHours(localTime))
        {
            return RandomBetween(random, QuietHoursMinimumDelay, QuietHoursMaximumDelay);
        }

        return isFirst
            ? RandomBetween(random, FirstCompanionMinimumDelay, FirstCompanionMaximumDelay)
            : RandomBetween(random, CompanionMinimumDelay, CompanionMaximumDelay);
    }

    internal string GetCompanionReply(
        Random random,
        DateTime localTime,
        string userTitle,
        string petName,
        string? previousReply)
    {
        var candidates = IsQuietHours(localTime)
            ? LateNightCompanionReplies
            : GetDayPhase(localTime) switch
            {
                PetDayPhase.Dawn => MorningCompanionReplies,
                PetDayPhase.Day => DayCompanionReplies,
                PetDayPhase.Dusk => DuskCompanionReplies,
                _ => NightCompanionReplies
            };

        var formatted = candidates
            .Select(item => item
                .Replace("{UserTitle}", userTitle, StringComparison.Ordinal)
                .Replace("{PetName}", petName, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var available = formatted.Length > 1
            ? formatted.Where(item => !string.Equals(item, previousReply, StringComparison.Ordinal)).ToArray()
            : formatted;
        return available[random.Next(available.Length)];
    }

    internal static string PickReply(Random random, IReadOnlyList<string> replies) =>
        replies[random.Next(replies.Count)];

    private static TimeSpan RandomBetween(Random random, TimeSpan minimum, TimeSpan maximum)
    {
        var range = maximum - minimum;
        return minimum + TimeSpan.FromMilliseconds(random.NextDouble() * range.TotalMilliseconds);
    }
}

internal enum PetDayPhase
{
    Dawn,
    Day,
    Dusk,
    Night
}
