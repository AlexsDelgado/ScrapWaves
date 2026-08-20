/// <summary>
/// Semantic origin of one authoritative damage result. The combat-text layer uses
/// this identity for aggregation; it never attempts to infer it from weapon type.
/// </summary>
public enum DamageFeedbackKind
{
    Direct,
    Explosion,
    Fragment,
    Piercing,
    SustainedContact,
    ManualMultiHit,
    Burn,
    JellifiedBurn,
    PersistentArea,
    Ability
}

public static class DamageFeedbackKindExtensions
{
    public static bool IsBurnFamily(this DamageFeedbackKind kind)
    {
        return kind == DamageFeedbackKind.Burn || kind == DamageFeedbackKind.JellifiedBurn;
    }
}
