using System;
using System.Text;
using UnityEngine;

public enum TemporaryPowerupType
{
    ExtraDamage,
    ExtraSpeed,
    ExtraScavenging,
    Invulnerability,
    FullHeal,
    Nuke
}

/// <summary>
/// Aplica y mantiene buffs temporales de power-ups (Spec: Temporary Power-ups).
/// </summary>
[DisallowMultipleComponent]
public class TemporaryPowerupController : MonoBehaviour
{
    public static TemporaryPowerupController Instance { get; private set; }

    /// <summary>Activo en test_balance: loguea solo las stats que cambia cada power-up.</summary>
    public bool LogStatDeltas { get; set; }

    public event Action<string> OnPowerupLogged;

    private static readonly StatType[] ExtraDamageStats =
    {
        StatType.DamageMultiplier,
        StatType.AttackSpeedMultiplier,
        StatType.CriticalChance
    };

    private static readonly StatType[] ExtraSpeedStats =
    {
        StatType.MovementSpeed,
        StatType.JumpHeight,
        StatType.DashCharges,
        StatType.AirJumps
    };

    private static readonly StatType[] ExtraScavengingStats =
    {
        StatType.PickupRange,
        StatType.Scavenging,
        StatType.DoubleDrop
    };

    private PlayerStats _stats;
    private PlayerHealth _health;
    private PlayerMovement _movement;
    private PlayerXP _xp;
    private WeaponManager _weapons;

    private readonly object _buffSource = new();
    private float _buffEndsAt = -1f;
    private float _invulnEndsAt = -1f;
    private TemporaryPowerupType _activeBuff;
    private bool _hasActiveBuff;
    private GameObject _ringVisual;
    private Renderer _ringRenderer;
    private Color _ringColor = Color.white;

    private void Awake()
    {
        Instance = this;
        _stats = GetComponent<PlayerStats>();
        _health = GetComponent<PlayerHealth>();
        _movement = GetComponent<PlayerMovement>();
        _xp = GetComponent<PlayerXP>();
        _weapons = GetComponent<WeaponManager>();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        float now = Time.time;
        if (_hasActiveBuff && now >= _buffEndsAt)
            ClearBuff();

        if (_invulnEndsAt > 0f && now >= _invulnEndsAt)
            _invulnEndsAt = -1f;

        UpdateRingVisual(now);
    }

    public bool IsPowerupInvulnerable => _invulnEndsAt > 0f && Time.time < _invulnEndsAt;

    public void Apply(TemporaryPowerupType type)
    {
        float t = GetLevelT();
        switch (type)
        {
            case TemporaryPowerupType.ExtraDamage:
                ApplyTimedBuff(type, 15f, new Color(1f, 0.35f, 0.1f, 0.85f), () =>
                {
                    AddMul(StatType.DamageMultiplier, Mathf.Lerp(1.5f, 2.5f, t));
                    AddMul(StatType.AttackSpeedMultiplier, Mathf.Lerp(1.75f, 3f, t));
                    AddAdd(StatType.CriticalChance, Mathf.Lerp(50f, 100f, t));
                });
                break;
            case TemporaryPowerupType.ExtraSpeed:
                ApplyTimedBuff(type, 15f, new Color(0.35f, 0.75f, 1f, 0.85f), () =>
                {
                    AddMul(StatType.MovementSpeed, Mathf.Lerp(2f, 4f, t));
                    AddMul(StatType.JumpHeight, Mathf.Lerp(2f, 3f, t));
                    int dashes = Mathf.Max(0, _stats != null ? _stats.GetStatInt(StatType.DashCharges) : 0);
                    int air = Mathf.Max(0, _stats != null ? _stats.GetStatInt(StatType.AirJumps) : 0);
                    if (dashes > 0)
                        AddAdd(StatType.DashCharges, dashes * 2);
                    if (air > 0)
                        AddAdd(StatType.AirJumps, air * 2);
                    _movement?.RefreshPassiveResources();
                });
                break;
            case TemporaryPowerupType.ExtraScavenging:
                ApplyTimedBuff(type, 7.5f, new Color(0.25f, 0.9f, 0.35f, 0.85f), () =>
                {
                    AddMul(StatType.PickupRange, Mathf.Lerp(2f, 4f, t));
                    AddAdd(StatType.Scavenging, Mathf.Lerp(25f, 50f, t));
                    AddAdd(StatType.DoubleDrop, Mathf.Lerp(25f, 75f, t));
                });
                break;
            case TemporaryPowerupType.Invulnerability:
            {
                float duration = 7.5f;
                _invulnEndsAt = Time.time + duration;
                _health?.GrantInvulnerability(duration);
                ShowRing(new Color(1f, 1f, 1f, 0.45f), duration, TemporaryPowerupType.Invulnerability);
                EmitLog($"[Powerup Invulnerability] Duration  base: 0.00 || +buff: {duration:0.00}");
                break;
            }
            case TemporaryPowerupType.FullHeal:
                ApplyFullHeal();
                break;
            case TemporaryPowerupType.Nuke:
                ApplyNuke(t);
                break;
        }
    }

    private void ApplyFullHeal()
    {
        int hpBefore = _health != null ? _health.CurrentHealth : 0;
        int hpMax = _health != null ? _health.MaxHealth : 0;
        float ammoBefore = _weapons != null ? _weapons.GetCurrentManualWeapon()?.CurrentAmmo ?? 0f : 0f;
        _health?.HealToFull();
        _movement?.RefreshPassiveResources();
        _weapons?.RefillManualAmmoAndResetActiveCooldown();
        float ammoAfter = _weapons != null ? _weapons.GetCurrentManualWeapon()?.CurrentAmmo ?? 0f : 0f;
        int hpAfter = _health != null ? _health.CurrentHealth : 0;
        EmitLog(
            $"[Powerup FullHeal] Health  base: {hpBefore} || +buff: {hpAfter}/{hpMax}\n" +
            $"[Powerup FullHeal] Ammo  base: {ammoBefore:0.00} || +buff: {ammoAfter:0.00}");
    }

    private void ApplyNuke(float t)
    {
        float damage = Mathf.Lerp(50f, 500f, t);
        float radius = Mathf.Lerp(20f, 40f, t);
        Vector3 origin = transform.position;
        Collider[] hits = Physics.OverlapSphere(origin, radius);
        for (int i = 0; i < hits.Length; i++)
        {
            EnemyHealth enemy = hits[i].GetComponentInParent<EnemyHealth>();
            if (enemy == null || enemy.CurrentHealth <= 0)
                continue;

            float dist = Vector3.Distance(origin, enemy.transform.position);
            float half = radius * 0.5f;
            float falloff = dist <= half ? 1f : Mathf.Lerp(1f, 0f, (dist - half) / half);
            int dmg = Mathf.Max(1, Mathf.RoundToInt(damage * falloff));
            enemy.ApplyDamage(dmg);
            EnemyKnockbackReceiver.TryApply(enemy, origin, 18f * falloff);
        }

        ExplosionRadiusVfx.Spawn(origin, radius);
        EmitLog($"[Powerup Nuke] Damage  base: 0.00 || +buff: {damage:0.00}\n[Powerup Nuke] Radius  base: 0.00 || +buff: {radius:0.00}");
    }

    private void ApplyTimedBuff(TemporaryPowerupType type, float duration, Color color, System.Action addModifiers)
    {
        ClearBuff();
        StatType[] tracked = GetTrackedStats(type);
        float[] before = Snapshot(tracked);
        _activeBuff = type;
        _hasActiveBuff = true;
        _buffEndsAt = Time.time + duration;
        addModifiers?.Invoke();
        LogStatDeltasFor(type, tracked, before);
        ShowRing(color, duration, type);
    }

    private static StatType[] GetTrackedStats(TemporaryPowerupType type)
    {
        return type switch
        {
            TemporaryPowerupType.ExtraDamage => ExtraDamageStats,
            TemporaryPowerupType.ExtraSpeed => ExtraSpeedStats,
            TemporaryPowerupType.ExtraScavenging => ExtraScavengingStats,
            _ => Array.Empty<StatType>()
        };
    }

    private float[] Snapshot(StatType[] types)
    {
        var values = new float[types.Length];
        for (int i = 0; i < types.Length; i++)
            values[i] = _stats != null ? _stats.GetStat(types[i]) : 0f;
        return values;
    }

    private void LogStatDeltasFor(TemporaryPowerupType type, StatType[] types, float[] before)
    {
        if (types == null || types.Length == 0)
            return;

        var builder = new StringBuilder();
        for (int i = 0; i < types.Length; i++)
        {
            float after = _stats != null ? _stats.GetStat(types[i]) : 0f;
            if (Mathf.Approximately(after, before[i]))
                continue;

            if (builder.Length > 0)
                builder.Append('\n');
            builder.Append("[Powerup ").Append(type).Append("] ")
                .Append(types[i])
                .Append("  base: ").Append(before[i].ToString("0.00"))
                .Append(" || +buff: ").Append(after.ToString("0.00"));
        }

        if (builder.Length > 0)
            EmitLog(builder.ToString());
    }

    private void EmitLog(string message)
    {
        if (!LogStatDeltas || string.IsNullOrEmpty(message))
            return;

        Debug.Log(message, this);
        OnPowerupLogged?.Invoke(message);
    }

    private void ClearBuff()
    {
        if (_stats != null)
            _stats.RemoveModifiersFromSource(_buffSource);
        _hasActiveBuff = false;
        _buffEndsAt = -1f;
        _movement?.RefreshPassiveResources();
        HideRing();
    }

    private void AddMul(StatType type, float multiplier)
    {
        if (_stats == null || _stats.GetDefinition(type) == null)
            return;
        _stats.AddModifier(new StatModifier(type, multiplier, StatUpgradeSource.TemporaryPowerup, _buffSource, StatModifierType.Multiplicative));
    }

    private void AddAdd(StatType type, float value)
    {
        if (_stats == null || _stats.GetDefinition(type) == null)
            return;
        _stats.AddModifier(new StatModifier(type, value, StatUpgradeSource.TemporaryPowerup, _buffSource, StatModifierType.Additive));
    }

    private float GetLevelT()
    {
        int level = _xp != null ? _xp.CurrentLevel : 1;
        int cap = _xp != null ? Mathf.Max(1, _xp.LevelCap) : 36;
        return Mathf.Clamp01(level / (float)cap);
    }

    private void ShowRing(Color color, float duration, TemporaryPowerupType type)
    {
        EnsureRing();
        _ringColor = color;
        _ringVisual.SetActive(true);
        if (_ringRenderer != null)
            _ringRenderer.material.color = color;
        // duration tracked via buff/invuln end times
        _ = duration;
        _ = type;
    }

    private void HideRing()
    {
        if (_ringVisual != null)
            _ringVisual.SetActive(false);
    }

    private void UpdateRingVisual(float now)
    {
        if (_ringVisual == null || !_ringVisual.activeSelf || _ringRenderer == null)
            return;

        float ends = Mathf.Max(_buffEndsAt, _invulnEndsAt);
        float remaining = ends - now;
        Color c = _ringColor;
        if (remaining > 0f && remaining <= 2.5f)
        {
            float blink = (Mathf.Sin(now * 12f) + 1f) * 0.5f;
            c.a = Mathf.Lerp(0.15f, _ringColor.a, blink);
        }

        _ringRenderer.material.color = c;
        _ringVisual.transform.position = transform.position + Vector3.up * 0.05f;
    }

    private void EnsureRing()
    {
        if (_ringVisual != null)
            return;

        _ringVisual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _ringVisual.name = "[PowerupRing]";
        _ringVisual.transform.SetParent(transform, false);
        _ringVisual.transform.localScale = new Vector3(2.2f, 0.03f, 2.2f);
        Collider col = _ringVisual.GetComponent<Collider>();
        if (col != null)
            Destroy(col);
        _ringRenderer = _ringVisual.GetComponent<Renderer>();
        if (_ringRenderer != null)
        {
            Shader shader = Shader.Find("Sprites/Default")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color");
            if (shader != null)
                _ringRenderer.material = new Material(shader);
            else if (_ringRenderer.sharedMaterial != null)
                _ringRenderer.material = new Material(_ringRenderer.sharedMaterial);
            _ringRenderer.material.color = Color.white;
        }

        _ringVisual.SetActive(false);
    }
}
