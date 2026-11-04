using System;

/// <summary>Two exact 26-bit integers in a double: primary metric first, secondary metric second.</summary>
public static class DemoLeaderboardScoreCodec
{
    public const int MetricLimit = (1 << 26) - 1; // >2 years of survival, 67 million kills.
    public const string Format = "scrapwaves-pair-v1";
    private const long Radix = 1L << 26;
    private const long MaximumScore = Radix * Radix - 1; // 2^52 - 1, exactly representable.

    public static bool CanEncode(DemoLeaderboardEntry entry) => entry != null &&
        entry.Kills >= 0 && entry.Kills <= MetricLimit &&
        entry.SurvivalSeconds >= 0 && entry.SurvivalSeconds <= MetricLimit;

    public static double Encode(DemoLeaderboardEntry entry, bool survival)
    {
        if (!CanEncode(entry)) throw new ArgumentOutOfRangeException(nameof(entry), "Result exceeds the online metric range.");
        return survival ? entry.SurvivalSeconds * Radix + entry.Kills : entry.Kills * Radix + entry.SurvivalSeconds;
    }

    public static void Decode(double score, bool survival, out int kills, out int seconds)
    {
        if (double.IsNaN(score) || double.IsInfinity(score) || score < 0 || score > MaximumScore || score != Math.Floor(score))
            throw new ArgumentOutOfRangeException(nameof(score), "Global board contains an incompatible score.");
        long value = (long)score;
        int primary = (int)(value / Radix);
        int secondary = (int)(value % Radix);
        kills = survival ? secondary : primary;
        seconds = survival ? primary : secondary;
    }
}
