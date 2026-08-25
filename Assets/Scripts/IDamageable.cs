public interface IDamageable
{
    bool ApplyDamage(int amount);
}

/// <summary>
/// Implemented by production targets that can report their exact health delta.
/// The legacy <see cref="IDamageable.ApplyDamage(int)"/> member remains available
/// while older callers and lightweight test doubles migrate.
/// </summary>
public interface IAuthoritativeDamageable : IDamageable
{
    DamageApplicationResult ApplyDamage(in DamageRequest request);
}

public static class DamageableResultExtensions
{
    /// <summary>
    /// Dispatches to the authoritative result contract when available. Legacy
    /// implementations retain their former accepted/requested-damage semantics.
    /// </summary>
    public static DamageApplicationResult ApplyDamage(this IDamageable damageable, in DamageRequest request)
    {
        if (damageable == null)
            return default;

        if (damageable is IAuthoritativeDamageable authoritative)
            return authoritative.ApplyDamage(in request);

        bool applied = damageable.ApplyDamage(request.ModifiedDamage);
        return DamageApplicationResult.FromLegacy(in request, applied);
    }
}
