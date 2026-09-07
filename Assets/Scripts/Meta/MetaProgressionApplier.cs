using UnityEngine;

/// <summary>
/// Aplica meta-upgrades de stats base (Upgrade 0–10) al iniciar la run / escena de gameplay.
/// </summary>
[DefaultExecutionOrder(-190)]
[DisallowMultipleComponent]
public class MetaProgressionApplier : MonoBehaviour
{
    private static readonly StatType[] UpgradeableStats =
    {
        StatType.DamageMultiplier,
        StatType.AttackSpeedMultiplier,
        StatType.ProjectileAreaSize,
        StatType.CriticalChance,
        StatType.CriticalDamage,
        StatType.MaxHealth,
        StatType.HealthRegeneration,
        StatType.PickupRange
    };

    private readonly object _metaSource = new();

    private void Start()
    {
        Apply();
    }

    public void Apply()
    {
        if (!TryGetComponent(out PlayerStats stats) || SaveManager.Instance == null)
            return;

        stats.RemoveModifiersFromSource(_metaSource);

        int previousMax = 0;
        if (TryGetComponent(out PlayerHealth health))
            previousMax = health.MaxHealth;

        for (int i = 0; i < UpgradeableStats.Length; i++)
        {
            StatType type = UpgradeableStats[i];
            if (stats.GetDefinition(type) == null)
                continue;

            float mult = SaveManager.Instance.GetMetaStatBaseMultiplier(type);
            if (Mathf.Approximately(mult, 1f))
                continue;

            // Represent +5% per level as multiplicative on base: value 1.05 at level 1, etc.
            stats.AddModifier(new StatModifier(
                type,
                mult,
                StatUpgradeSource.Base,
                _metaSource,
                StatModifierType.Multiplicative));
        }

        if (health != null)
        {
            int newMax = stats.GetDefinition(StatType.MaxHealth) != null
                ? stats.GetMaxHealthTotal()
                : previousMax;
            health.ApplyMaxHealthDelta(newMax - previousMax);
        }
    }
}
