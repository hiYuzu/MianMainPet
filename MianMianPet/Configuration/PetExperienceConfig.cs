namespace MianMianPet.Configuration;

/// <summary>
/// 绵绵的内置体验参数。这里的内容只在代码中调整，不会出现在设置窗口。
/// </summary>
internal static class PetExperienceConfig
{
    // 闲置场景：先出现草地、云朵和星光，继续闲置后进入睡眠。
    internal static readonly TimeSpan AmbientIdleDelay = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan SleepIdleDelay = TimeSpan.FromMinutes(1.5);

    // 主动陪伴：启动后的第一次会稍早出现，之后保持克制且带随机性。
    internal static readonly TimeSpan FirstCompanionMinimumDelay = TimeSpan.FromMinutes(8);
    internal static readonly TimeSpan FirstCompanionMaximumDelay = TimeSpan.FromMinutes(12);
    internal static readonly TimeSpan CompanionMinimumDelay = TimeSpan.FromMinutes(50);
    internal static readonly TimeSpan CompanionMaximumDelay = TimeSpan.FromMinutes(60);
    internal static readonly TimeSpan QuietHoursMinimumDelay = TimeSpan.FromMinutes(90);
    internal static readonly TimeSpan QuietHoursMaximumDelay = TimeSpan.FromMinutes(120);
    internal static readonly TimeSpan CompanionBlockedRetryDelay = TimeSpan.FromSeconds(45);
    internal static readonly TimeSpan MaximumSystemIdleForCompanion = TimeSpan.FromMinutes(2);
    internal const int QuietHoursStartHour = 0;
    internal const int QuietHoursEndHour = 7;

    // 气泡停留时间。正常 AI 回复仍由 InternalAiConfig.BubbleAutoHideSeconds 控制。
    internal const int FailureBubbleAutoHideSeconds = 6;
    internal const int CompanionBubbleAutoHideSeconds = 7;
    internal static readonly TimeSpan AiRequestTimeout = TimeSpan.FromMinutes(2);

    // 昼夜分段使用电脑本地时间。
    internal const int DawnStartHour = 6;
    internal const int DayStartHour = 10;
    internal const int DuskStartHour = 17;
    internal const int NightStartHour = 20;
    internal static readonly TimeSpan DayPhaseRefreshInterval = TimeSpan.FromMinutes(1);

    internal static readonly string[] NetworkFailureReplies =
    [
        "外面的风好像把声音吹散了……等一下再和我说一次吧。",
        "云朵把绵绵的信号挡住啦，等等它飘过去吧。",
        "绵绵刚刚没能听清，再等一小会儿好不好？"
    ];

    internal static readonly string[] TimeoutFailureReplies =
    [
        "唔……绵绵刚刚走神了，再和我说一次好不好？",
        "这句话在云朵里绕得有点久，我们一会儿再试试吧。",
        "咩……刚才没有接住这句话。"
    ];

    internal static readonly string[] ServiceFailureReplies =
    [
        "绵绵现在有一点点迷糊，休息一下就回来啦。",
        "今天的云有点厚，绵绵暂时没办法自由说话了。",
        "绵绵的声音暂时躲起来了，过一会儿再来找我吧。"
    ];

    internal static readonly string[] GeneralFailureReplies =
    [
        "咩……刚才那句话不小心从羊毛里滑走了。",
        "绵绵刚刚打了个小喷嚏，没有把话说出来。",
        "这次没有接住，不过绵绵还在这里呀。"
    ];

    internal static readonly string[] MorningCompanionReplies =
    [
        "早呀，{UserTitle}。今天也要慢慢来，不用着急。",
        "晨光软软的，绵绵把最好的一小团留给{UserTitle}。",
        "新的一天开始啦，先送给{UserTitle}一个羊毛抱抱。"
    ];

    internal static readonly string[] DayCompanionReplies =
    [
        "{UserTitle}认真做事情的样子，绵绵有偷偷看到哦。",
        "路过一下，给{UserTitle}放下一小团软绵绵。",
        "要是有一点累，就看绵绵一眼吧。",
        "绵绵没有打扰，只是在旁边陪着{UserTitle}。"
    ];

    internal static readonly string[] DuskCompanionReplies =
    [
        "天色变得软软的啦，{UserTitle}今天辛苦了。",
        "晚霞来了，绵绵想把暖暖的颜色分给{UserTitle}。",
        "忙了一天，也记得给自己留一点轻松呀。"
    ];

    internal static readonly string[] NightCompanionReplies =
    [
        "夜里安静下来了，绵绵还在这里陪着{UserTitle}。",
        "星星亮起来啦，今天也辛苦了。",
        "困了就早点休息吧，没做完的事情明天也会等你。"
    ];

    internal static readonly string[] LateNightCompanionReplies =
    [
        "夜已经很深啦，绵绵陪{UserTitle}把这一点做完就休息。",
        "小声提醒一下：现在很晚了，要照顾好自己呀。",
        "绵绵开始犯困啦，{UserTitle}也不要熬得太久哦。"
    ];

    internal static PetDayPhase GetDayPhase(DateTime localTime)
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

    internal static bool IsQuietHours(DateTime localTime) =>
        localTime.Hour >= QuietHoursStartHour && localTime.Hour < QuietHoursEndHour;

    internal static TimeSpan GetNextCompanionDelay(Random random, DateTime localTime, bool isFirst)
    {
        if (IsQuietHours(localTime))
        {
            return RandomBetween(random, QuietHoursMinimumDelay, QuietHoursMaximumDelay);
        }

        return isFirst
            ? RandomBetween(random, FirstCompanionMinimumDelay, FirstCompanionMaximumDelay)
            : RandomBetween(random, CompanionMinimumDelay, CompanionMaximumDelay);
    }

    internal static string GetCompanionReply(
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
