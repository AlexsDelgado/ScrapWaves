using System.Text;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WeaponTestMetrics : MonoBehaviour
{
    private float _totalDamage;
    private float _damageWindowStart;
    private int _hitCount;
    private int _critCount;
    private int _damageRollCount;
    private int _killCount;
    private int _ammoConsumed;
    private int _activeAbilityUses;
    private int _statusEffectsApplied;
    private float _knockbackDistanceTotal;
    private int _knockbackSamples;
    private float _timeToKillTotal;
    private int _timeToKillSamples;
    private float _lastDamage;
    private WeaponTestingSandboxManager _sandbox;
    private float _smoothedFrameTimeMs;

    public float TotalDamage => _totalDamage;
    public float DamagePerSecond
    {
        get
        {
            float elapsed = Mathf.Max(0.001f, Time.time - _damageWindowStart);
            return _totalDamage / elapsed;
        }
    }

    public float DamagePerShot => _hitCount > 0 ? _totalDamage / _hitCount : 0f;
    public float CriticalHitRate => _damageRollCount > 0 ? _critCount / (float)_damageRollCount : 0f;
    public int CriticalHits => _critCount;
    public int EnemiesKilled => _killCount;
    public int AmmoConsumed => _ammoConsumed;
    public float DamagePerAmmo => _ammoConsumed > 0 ? _totalDamage / _ammoConsumed : 0f;
    public int ActiveAbilityUses => _activeAbilityUses;
    public int StatusEffectsApplied => _statusEffectsApplied;
    public float AverageKnockbackDistance => _knockbackSamples > 0 ? _knockbackDistanceTotal / _knockbackSamples : 0f;
    public float AverageTimeToKill => _timeToKillSamples > 0 ? _timeToKillTotal / _timeToKillSamples : 0f;
    public float LastDamage => _lastDamage;
    public int HitCount => _hitCount;
    public int ActiveProjectileCount => _sandbox?.ProjectilePool != null ? _sandbox.ProjectilePool.ActiveLeasedCount : 0;
    public int ActiveEffectCount => _sandbox?.PresentationController != null ? _sandbox.PresentationController.ActiveVfxCount : 0;
    public int TotalEffectPoolCapacity => _sandbox?.PresentationController != null ? _sandbox.PresentationController.TotalVfxCapacity : 0;
    public int ActiveAudioVoiceCount => _sandbox?.PresentationController != null ? _sandbox.PresentationController.ActiveAudioVoiceCount : 0;
    public int EffectSuppressionCount => _sandbox?.PresentationController != null ? _sandbox.PresentationController.SuppressionCount : 0;
    public float FrameTimeMilliseconds => _smoothedFrameTimeMs;
    public long ManagedMemoryBytes => System.GC.GetTotalMemory(false);

    private void Update()
    {
        float sample = Time.unscaledDeltaTime * 1000f;
        _smoothedFrameTimeMs = _smoothedFrameTimeMs <= 0f
            ? sample
            : Mathf.Lerp(_smoothedFrameTimeMs, sample, 0.08f);
    }

    private void OnEnable()
    {
        WeaponDamageResolver.OnDamageResolved += OnDamageResolved;
        ResetMetrics();
    }

    private void OnDisable()
    {
        WeaponDamageResolver.OnDamageResolved -= OnDamageResolved;
    }

    public void Bind(WeaponTestingSandboxManager sandbox)
    {
        _sandbox = sandbox;
    }

    public void ResetMetrics()
    {
        _totalDamage = 0f;
        _damageWindowStart = Time.time;
        _hitCount = 0;
        _critCount = 0;
        _damageRollCount = 0;
        _killCount = 0;
        _ammoConsumed = 0;
        _activeAbilityUses = 0;
        _statusEffectsApplied = 0;
        _knockbackDistanceTotal = 0f;
        _knockbackSamples = 0;
        _timeToKillTotal = 0f;
        _timeToKillSamples = 0;
        _lastDamage = 0f;
        _smoothedFrameTimeMs = 0f;
    }

    public void RecordDamage(float amount)
    {
        if (amount <= 0f)
            return;

        _totalDamage += amount;
        _lastDamage = amount;
        _hitCount++;
    }

    public void RecordKill(float timeToKill)
    {
        _killCount++;
        if (timeToKill > 0f)
        {
            _timeToKillTotal += timeToKill;
            _timeToKillSamples++;
        }
    }

    public void RecordAmmoConsumed(float amount)
    {
        _ammoConsumed += Mathf.Max(0, Mathf.RoundToInt(amount));
    }

    public void RecordActiveAbilityUse()
    {
        _activeAbilityUses++;
    }

    public void RecordStatusEffectApplied()
    {
        _statusEffectsApplied++;
    }

    public void RecordKnockbackDistance(float distance)
    {
        if (distance <= 0.001f)
            return;

        _knockbackDistanceTotal += distance;
        _knockbackSamples++;
    }

    public string BuildSummary()
    {
        WeaponInstance current = _sandbox != null ? _sandbox.CurrentManualWeapon : null;
        WeaponStatOverride stats = _sandbox != null ? _sandbox.StatOverride : null;
        float heatPercent = _sandbox != null && _sandbox.HeatOverride != null ? _sandbox.HeatOverride.NormalizedHeat * 100f : 0f;

        StringBuilder sb = new StringBuilder(512);
        sb.AppendLine("=== WEAPON TEST SUMMARY ===");
        sb.Append("Weapon: ").AppendLine(current?.Data != null ? current.Data.DisplayName : "None");
        sb.Append("Level: ").AppendLine(current != null ? current.Level.ToString() : "-");
        sb.Append("Path: ").AppendLine(FormatPath(current));
        sb.Append("Heat: ").Append(heatPercent.ToString("0.#")).AppendLine("%");
        if (stats != null)
        {
            sb.Append("Manual Stat Overrides: ").AppendLine(stats.UseOverrides ? "Enabled" : "Disabled");
            if (stats.UseOverrides)
            {
                sb.Append("Override Damage Multiplier: ").AppendLine(stats.DamageMultiplier.ToString("0.###"));
                sb.Append("Override Attack Speed Multiplier: ").AppendLine(stats.AttackSpeedMultiplier.ToString("0.###"));
                sb.Append("Override Projectile Size Multiplier: ").AppendLine(stats.ProjectileAreaSizeMultiplier.ToString("0.###"));
            }
        }

        PassiveItemTestingController passives = _sandbox != null ? _sandbox.PassiveItemController : null;
        if (passives != null)
        {
            sb.Append("Passive Test Mode: ").AppendLine(passives.PassiveBaselineMode ? "Passive Baseline" : "Manual Overrides");
            for (int i = 0; i < PassiveItemTestingController.Slots.Count; i++)
            {
                PassiveItemTestingController.SlotDescriptor slot = PassiveItemTestingController.Slots[i];
                sb.AppendLine(passives.BuildSlotSummary(slot.Slot, slot.SlotIndex));
            }
            sb.Append("Effective Passive Stats: ").AppendLine(passives.EffectiveStatsSummary);
            sb.AppendLine(passives.HealthShieldSummary);
            sb.AppendLine(passives.DropProbeSummary);
            if (passives.OverridesMaskPassives)
                sb.AppendLine(passives.OverrideWarning);
        }
        sb.Append("Total Damage: ").AppendLine(TotalDamage.ToString("0.#"));
        sb.Append("DPS: ").AppendLine(DamagePerSecond.ToString("0.#"));
        sb.Append("Ammo Spent: ").AppendLine(AmmoConsumed.ToString());
        sb.Append("Damage Per Ammo: ").AppendLine(DamagePerAmmo.ToString("0.###"));
        sb.Append("Crit Rate: ").Append((CriticalHitRate * 100f).ToString("0.#")).AppendLine("%");
        sb.Append("Kills: ").AppendLine(EnemiesKilled.ToString());
        sb.Append("Average TTK: ").Append(AverageTimeToKill.ToString("0.###")).AppendLine("s");
        sb.Append("Status Effects Applied: ").AppendLine(StatusEffectsApplied.ToString());
        sb.Append("Average Knockback Distance: ").Append(AverageKnockbackDistance.ToString("0.###")).AppendLine("m");
        sb.Append("Projectiles / FX / Voices: ")
            .Append(ActiveProjectileCount).Append(" / ")
            .Append(ActiveEffectCount).Append(" / ")
            .AppendLine(ActiveAudioVoiceCount.ToString());
        sb.Append("FX Pool Capacity / Suppressed: ")
            .Append(TotalEffectPoolCapacity).Append(" / ")
            .AppendLine(EffectSuppressionCount.ToString());
        sb.Append("Frame Time: ").Append(FrameTimeMilliseconds.ToString("0.##")).AppendLine(" ms");
        sb.Append("Managed Memory: ").Append((ManagedMemoryBytes / (1024f * 1024f)).ToString("0.##")).AppendLine(" MB");
        sb.Append("===========================");
        return sb.ToString();
    }

    public void ExportToConsole()
    {
        Debug.Log(BuildSummary(), this);
    }

    private void OnDamageResolved(WeaponDamageRoll roll)
    {
        if (_sandbox != null && !_sandbox.IsSandboxWeapon(roll.Weapon))
            return;

        _damageRollCount++;
        if (roll.IsCritical)
            _critCount++;
    }

    private static string FormatPath(WeaponInstance weapon)
    {
        if (weapon == null || weapon.SelectedPath == WeaponUpgradePath.None || !weapon.HasAdvancedPath)
            return "None";

        WeaponUpgradePathData data = WeaponMath.GetPathData(weapon);
        if (data != null && !string.IsNullOrWhiteSpace(data.PathName))
            return data.PathName;

        return weapon.SelectedPath.ToString();
    }
}
