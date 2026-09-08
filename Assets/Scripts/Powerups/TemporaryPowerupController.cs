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
    private float _fxEndsAt = -1f;
    private TemporaryPowerupType _activeBuff;
    private bool _hasActiveBuff;
    private ParticleSystem _buffParticles;
    private ParticleSystem.EmissionModule _buffEmission;
    private const float BuffEmissionRate = 15f;
    private const float BuffEmissionWindDownRate = 4f;
    private const float BuffBurstCount = 10f;
    private static readonly Vector3 BuffParticleLocalPos = new(0f, 0.9f, -0.35f);

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

        UpdateBuffFx(now);
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
                ShowBuffFx(new Color(1f, 1f, 1f, 0.7f), duration);
                EmitLog($"[Powerup Invulnerability] Duration  base: 0.00 || +buff: {duration:0.00}");
                break;
            }
            case TemporaryPowerupType.FullHeal:
                ApplyFullHeal();
                ShowBuffFx(new Color(1f, 0.3f, 0.55f, 0.85f), 1.75f);
                break;
            case TemporaryPowerupType.Nuke:
                ApplyNuke(t);
                ShowBuffFx(new Color(1f, 0.85f, 0.15f, 0.9f), 1.75f);
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
        ShowBuffFx(color, duration);
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
        // Keep FX if invulnerability (or a short FX timer) is still running.
        float now = Time.time;
        if ((_invulnEndsAt <= 0f || now >= _invulnEndsAt) && (_fxEndsAt <= 0f || now >= _fxEndsAt))
            HideBuffFx();
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

    private void ShowBuffFx(Color color, float duration)
    {
        EnsureBuffParticles();
        color.a = Mathf.Clamp01(Mathf.Max(color.a, 0.55f));
        _fxEndsAt = Time.time + Mathf.Max(0.1f, duration);

        ApplyParticleColor(color);
        _buffEmission.rateOverTime = BuffEmissionRate;
        if (!_buffParticles.gameObject.activeSelf)
            _buffParticles.gameObject.SetActive(true);
        _buffParticles.Clear(true);
        _buffParticles.Play(true);
        _buffParticles.Emit(Mathf.RoundToInt(BuffBurstCount));
    }

    private void HideBuffFx()
    {
        _fxEndsAt = -1f;
        if (_buffParticles == null)
            return;

        _buffEmission.rateOverTime = 0f;
        _buffParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        _buffParticles.gameObject.SetActive(false);
    }

    private void UpdateBuffFx(float now)
    {
        if (_buffParticles == null || !_buffParticles.gameObject.activeSelf)
            return;

        float ends = Mathf.Max(_buffEndsAt, _invulnEndsAt, _fxEndsAt);
        if (ends <= 0f || now >= ends)
        {
            if (_buffParticles.particleCount <= 0)
                HideBuffFx();
            else
            {
                _buffEmission.rateOverTime = 0f;
                _buffParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                if (!_buffParticles.IsAlive(true))
                    HideBuffFx();
            }
            return;
        }

        float remaining = ends - now;
        _buffEmission.rateOverTime = remaining <= 2.5f ? BuffEmissionWindDownRate : BuffEmissionRate;
    }

    private void ApplyParticleColor(Color color)
    {
        if (_buffParticles == null)
            return;

        var main = _buffParticles.main;
        main.startColor = color;

        var colorOverLifetime = _buffParticles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(color, 0f),
                new GradientColorKey(color, 1f)
            },
            new[]
            {
                new GradientAlphaKey(color.a, 0f),
                new GradientAlphaKey(0f, 1f)
            });
        colorOverLifetime.color = gradient;
    }

    private void EnsureBuffParticles()
    {
        if (_buffParticles != null)
            return;

        GameObject go = new("[PowerupBuffParticles]");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = BuffParticleLocalPos;
        go.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        _buffParticles = go.AddComponent<ParticleSystem>();
        _buffEmission = _buffParticles.emission;

        var main = _buffParticles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.35f, 0.85f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
        main.startRotation = 0f;
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 64;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        _buffEmission.enabled = true;
        _buffEmission.rateOverTime = 0f;

        var shape = _buffParticles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 18f;
        shape.radius = 0.08f;
        shape.length = 0.2f;

        var colorOverLifetime = _buffParticles.colorOverLifetime;
        colorOverLifetime.enabled = true;

        var sizeOverLifetime = _buffParticles.sizeOverLifetime;
        sizeOverLifetime.enabled = true;
        AnimationCurve sizeCurve = new();
        sizeCurve.AddKey(0f, 1f);
        sizeCurve.AddKey(1f, 0.15f);
        sizeOverLifetime.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
            ?? Shader.Find("Particles/Standard Unlit")
            ?? Shader.Find("Sprites/Default");
        if (shader != null)
            renderer.material = new Material(shader);

        go.SetActive(false);
    }
}
