namespace EcoPort.Cloud.Core.Read;

/// <summary>Connectivity from last contact: online ≤ 3 min, delayed ≤ 15 min, else offline.</summary>
public static class Connectivity
{
    public const string Online = "online";
    public const string Delayed = "delayed";
    public const string Offline = "offline";

    public static readonly TimeSpan OnlineWithin = TimeSpan.FromMinutes(3);
    public static readonly TimeSpan DelayedWithin = TimeSpan.FromMinutes(15);

    public static string From(DateTimeOffset? lastSeen, DateTimeOffset now)
    {
        if (lastSeen is not { } seen)
        {
            return Offline;
        }

        var age = now - seen;
        return age <= OnlineWithin ? Online : age <= DelayedWithin ? Delayed : Offline;
    }
}
