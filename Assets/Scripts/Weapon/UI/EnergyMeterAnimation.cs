using System;

/// <summary>Presentation state only; does not change the player's heat, XP or level.</summary>
internal sealed class EnergyMeterAnimation
{
    private const float PickupDuration = 0.4f;
    private const float LevelDuration = 0.55f;
    private bool _initialized;
    private bool _overheatTarget;
    private int _level;
    private float _pickupAge = PickupDuration;
    private float _levelAge = LevelDuration;

    public bool IsExperience { get; private set; }
    public float Fill { get; private set; }
    public float TargetFill { get; private set; }
    public float Overheat { get; private set; }
    public float HeatBurst { get; private set; }
    public float PickupSweep => _pickupAge < PickupDuration ? _pickupAge / PickupDuration : -1f;
    public float LevelSweep => _levelAge < LevelDuration ? _levelAge / LevelDuration : -1f;

    public void SetHeat(float value, bool overheating)
    {
        bool first = !_initialized || IsExperience;
        IsExperience = false;
        TargetFill = overheating ? 1f : Clamp01(value);
        if (first)
        {
            Fill = TargetFill;
            Overheat = overheating ? 1f : 0f;
            HeatBurst = 0f;
        }
        else if (overheating && !_overheatTarget)
        {
            HeatBurst = 1f;
            Fill = 1f;
        }
        if (TargetFill == 0f)
            Fill = 0f;
        _overheatTarget = overheating;
        _initialized = true;
    }

    public void SetExperience(float value, int level)
    {
        value = Clamp01(value);
        bool first = !_initialized || !IsExperience;
        IsExperience = true;
        if (first || level < _level)
        {
            Fill = value;
            _pickupAge = PickupDuration;
            _levelAge = LevelDuration;
        }
        else if (level > _level)
        {
            // Complete this level visually, then reveal the latest remainder.
            // Multiple levels in one award coalesce into one short sweep.
            Fill = 1f;
            _levelAge = 0f;
            _pickupAge = PickupDuration;
        }
        else if (value > TargetFill + 0.0001f && LevelSweep < 0f)
        {
            _pickupAge = 0f;
        }
        else if (value < TargetFill && LevelSweep < 0f)
        {
            Fill = value;
            _pickupAge = PickupDuration;
        }
        TargetFill = value;
        _level = level;
        _initialized = true;
    }

    public void Advance(float deltaTime)
    {
        if (!_initialized || deltaTime <= 0f)
            return;
        float dt = Math.Min(deltaTime, 0.1f);
        _pickupAge = Math.Min(PickupDuration, _pickupAge + dt);
        if (IsExperience && LevelSweep >= 0f)
        {
            _levelAge = Math.Min(LevelDuration, _levelAge + dt);
            Fill = LevelSweep >= 0f ? 1f : TargetFill;
        }
        else
        {
            Fill = Ease(Fill, TargetFill, dt, 14f);
        }
        Overheat = Ease(Overheat, _overheatTarget ? 1f : 0f, dt, 8f);
        HeatBurst = Math.Max(0f, HeatBurst - dt * 1.8f);
    }

    private static float Ease(float from, float to, float dt, float speed)
    {
        float value = to + (from - to) * (float)Math.Exp(-dt * speed);
        return Math.Abs(value - to) < 0.0001f ? to : value;
    }

    public void SnapToTarget()
    {
        Fill = TargetFill;
        Overheat = _overheatTarget ? 1f : 0f;
        HeatBurst = 0f;
        _pickupAge = PickupDuration;
        _levelAge = LevelDuration;
    }

    private static float Clamp01(float value) => float.IsNaN(value) ? 0f : Math.Max(0f, Math.Min(1f, value));
}
