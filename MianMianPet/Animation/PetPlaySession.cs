using MianMianPet.Models;

namespace MianMianPet.Animation;

internal enum PetPlayKind { Walk, HideAndSeek }
internal enum PetPlayPhase { Idle, WalkGreeting, Walking, Returning, ShowingTreasure, HideGreeting, Hiding, Found, TimedOut }

// Only the last completed activity is persisted. Deadlines and treasures belong to this session.
internal sealed class PetPlaySession
{
    internal const long GreetingMilliseconds = 1450;
    internal const long WalkMilliseconds = 4000;
    internal const long ReturnMilliseconds = 820;
    internal const long TreasureMilliseconds = 7000;
    internal const long HidingMilliseconds = 10000;
    internal const long RevealMilliseconds = 1800;

    internal PetPlayPhase Phase { get; private set; }
    internal bool IsActive => Phase != PetPlayPhase.Idle;
    internal WalkTreasure? Treasure { get; private set; }
    internal int HiddenCloud { get; private set; } = -1;
    internal long Deadline { get; private set; }

    internal void StartWalk(WalkTreasure treasure, long now)
    {
        if (IsActive) return;
        Treasure = treasure;
        Phase = PetPlayPhase.WalkGreeting;
        Deadline = now + GreetingMilliseconds;
    }

    internal void StartHiding(int cloud, long now)
    {
        if (IsActive) return;
        if (cloud is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(cloud));
        HiddenCloud = cloud;
        Phase = PetPlayPhase.HideGreeting;
        Deadline = now + GreetingMilliseconds;
    }

    internal void Advance(long now)
    {
        if (!IsActive || now < Deadline) return;
        switch (Phase)
        {
            case PetPlayPhase.WalkGreeting:
                Phase = PetPlayPhase.Walking;
                Deadline = now + WalkMilliseconds;
                break;
            case PetPlayPhase.Walking:
                Phase = PetPlayPhase.Returning;
                Deadline = now + ReturnMilliseconds;
                break;
            case PetPlayPhase.Returning:
                Phase = PetPlayPhase.ShowingTreasure;
                Deadline = now + TreasureMilliseconds;
                break;
            case PetPlayPhase.HideGreeting:
                Phase = PetPlayPhase.Hiding;
                Deadline = now + HidingMilliseconds;
                break;
            case PetPlayPhase.Hiding:
                Phase = PetPlayPhase.TimedOut;
                Deadline = now + RevealMilliseconds;
                break;
            default:
                Reset();
                break;
        }
    }

    internal bool ChooseCloud(int cloud, long now)
    {
        // Check the actual deadline even if the UI timer has not had its next tick.
        Advance(now);
        if (Phase != PetPlayPhase.Hiding || cloud != HiddenCloud) return false;
        Phase = PetPlayPhase.Found;
        Deadline = now + RevealMilliseconds;
        return true;
    }

    internal bool IsPeeking(long now)
    {
        if (Phase != PetPlayPhase.Hiding) return false;
        var elapsed = HidingMilliseconds - (Deadline - now);
        return elapsed is >= 2200 and < 2750 or >= 6200 and < 6800;
    }

    internal void Reset()
    {
        Phase = PetPlayPhase.Idle;
        Treasure = null;
        HiddenCloud = -1;
        Deadline = 0;
    }
}
