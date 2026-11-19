using System.Globalization;
using BatTray.Core.Models;

namespace BatTray.Core.Providers;

/// <summary>
/// Battery levels of a pair of earbuds and their case, or of a single-battery headset
/// (<see cref="SingleBattery"/>, level in <see cref="Left"/>). Levels are 0–100 or null when unknown.
/// </summary>
public sealed record EarbudBattery(
    int? Left,
    int? Right,
    int? Case,
    bool LeftCharging = false,
    bool RightCharging = false,
    bool CaseCharging = false,
    bool SingleBattery = false)
{
    /// <summary>A single-battery headset (headphones, neckband).</summary>
    public static EarbudBattery Single(int percent, bool charging) =>
        new(percent, null, null, LeftCharging: charging, SingleBattery: true);

    /// <summary>True when at least one part reports a level.</summary>
    public bool HasAny => Left is not null || Right is not null || Case is not null;

    /// <summary>The level shown in the tray: the lower earbud (the case is only listed in the details).</summary>
    public int? Headline
    {
        get
        {
            if (SingleBattery)
                return Left;
            if (Left is { } l && Right is { } r)
                return Math.Min(l, r);
            return Left ?? Right;
        }
    }

    /// <summary>True when every reported earbud is charging, false when one is not, null when no earbud reports.</summary>
    public bool? Charging
    {
        get
        {
            if (SingleBattery)
                return Left is null ? null : LeftCharging;
            var buds = new List<bool>(2);
            if (Left is not null)
                buds.Add(LeftCharging);
            if (Right is not null)
                buds.Add(RightCharging);
            return buds.Count == 0 ? null : buds.All(c => c);
        }
    }

    /// <summary>Per-part summary such as <c>L 80% · R 90% · case 50%</c> (empty for a single battery).</summary>
    public string Describe()
    {
        if (SingleBattery)
            return string.Empty;
        var parts = new List<string>(3);
        Add(parts, "L", Left, LeftCharging);
        Add(parts, "R", Right, RightCharging);
        Add(parts, "case", Case, CaseCharging);
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Fills the parts this reading lacks from <paramref name="other"/> (e.g. the second earbud's own broadcast).
    /// </summary>
    public EarbudBattery FillFrom(EarbudBattery other)
    {
        if (SingleBattery || other.SingleBattery)
            return this;
        return this with
        {
            Left = Left ?? other.Left,
            LeftCharging = Left is null ? other.LeftCharging : LeftCharging,
            Right = Right ?? other.Right,
            RightCharging = Right is null ? other.RightCharging : RightCharging,
            Case = Case ?? other.Case,
            CaseCharging = Case is null ? other.CaseCharging : CaseCharging,
        };
    }

    /// <summary>
    /// One tray entry for the product, or null when no earbud level is known (a case alone is not shown).
    /// <paramref name="fallbackSource"/> is used when there is no per-part detail.
    /// </summary>
    public Device? ToDevice(string id, string name, DateTimeOffset time, string fallbackSource)
    {
        if (Headline is not { } percent)
            return null;
        var detail = Describe();
        return new Device(id, name, DeviceKind.Headset, percent, time,
            string.IsNullOrEmpty(detail) ? fallbackSource : detail, Charging);
    }

    private static void Add(List<string> parts, string label, int? level, bool charging)
    {
        if (level is not { } value)
            return;
        var text = string.Create(CultureInfo.InvariantCulture, $"{label} {value}%");
        parts.Add(charging ? text + " charging" : text);
    }
}
