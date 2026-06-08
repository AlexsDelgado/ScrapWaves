using UnityEngine;

[DisallowMultipleComponent]
public sealed class WeaponDebugGizmos : MonoBehaviour
{
    public bool ShowTargetingCone = true;
    public bool ShowProjectilePaths = true;
    public bool ShowExplosionRadius = true;
    public bool ShowDamageNumbers = true;
    public bool ShowKnockbackVectors = true;
    public bool ShowWeaponHitboxes = true;
    public bool ShowStatusEffectIcons = true;
    public bool ShowDpsWindow = true;

    private WeaponTestingSandboxManager _sandbox;

    public void Bind(WeaponTestingSandboxManager sandbox)
    {
        _sandbox = sandbox;
    }

    private void OnDrawGizmos()
    {
        if (_sandbox == null || _sandbox.PlayerTransform == null)
            return;

        WeaponInstance weapon = _sandbox.CurrentManualWeapon;
        if (weapon?.Data == null)
            return;

        Transform player = _sandbox.PlayerTransform;
        Vector3 origin = _sandbox.ProjectileSpawn != null ? _sandbox.ProjectileSpawn.position : player.position + Vector3.up;
        Vector3 forward = _sandbox.CurrentAimDirection.sqrMagnitude > 0.0001f ? _sandbox.CurrentAimDirection.normalized : player.forward;

        DrawTargeting(weapon, origin, forward);
        DrawWeaponSpecific(weapon, origin, forward);
    }

    private void DrawTargeting(WeaponInstance weapon, Vector3 origin, Vector3 forward)
    {
        if (!ShowTargetingCone)
            return;

        Gizmos.color = new Color(0.2f, 0.7f, 1f, 0.65f);
        float range = weapon.Data.BaseRange;
        float cone = weapon.Data.WeaponType == WeaponType.Flamethrower ? weapon.Data.Flamethrower.FlameManualConeAngle : 90f;
        DrawCone(origin, forward, range, cone);
    }

    private void DrawWeaponSpecific(WeaponInstance weapon, Vector3 origin, Vector3 forward)
    {
        switch (weapon.Data.WeaponType)
        {
            case WeaponType.Flamethrower:
                DrawFlamethrower(weapon, origin, forward);
                break;
            case WeaponType.RocketLauncher:
                DrawRocketLauncher(weapon, origin, forward);
                break;
            case WeaponType.Mortar:
                DrawMortar(weapon, origin, forward);
                break;
            case WeaponType.AutomaticCannon:
                DrawAutomaticCannon(weapon, origin, forward);
                break;
            case WeaponType.RotatingBlade:
                DrawRotatingBlade(weapon, origin, forward);
                break;
        }
    }

    private void DrawFlamethrower(WeaponInstance weapon, Vector3 origin, Vector3 forward)
    {
        FlamethrowerTuning tuning = weapon.Data.Flamethrower;
        if (ShowWeaponHitboxes)
        {
            Gizmos.color = new Color(1f, 0.35f, 0.05f, 0.85f);
            DrawCone(origin, forward, weapon.Data.BaseRange, tuning.FlameManualConeAngle);
        }

        if (ShowExplosionRadius)
        {
            Gizmos.color = new Color(1f, 0.1f, 0.02f, 0.7f);
            Gizmos.DrawWireSphere(_sandbox.PlayerTransform.position, tuning.FlameActiveRadius);
        }
    }

    private void DrawRocketLauncher(WeaponInstance weapon, Vector3 origin, Vector3 forward)
    {
        RocketLauncherTuning tuning = weapon.Data.RocketLauncher;
        Vector3 target = origin + forward * weapon.Data.BaseRange;

        if (ShowProjectilePaths)
        {
            Gizmos.color = new Color(1f, 0.9f, 0.1f, 0.8f);
            Gizmos.DrawLine(origin, target);
        }

        if (ShowExplosionRadius)
        {
            Gizmos.color = new Color(1f, 0.45f, 0.05f, 0.75f);
            Gizmos.DrawWireSphere(target, tuning.RocketManualExplosionRadius);
        }
    }

    private void DrawMortar(WeaponInstance weapon, Vector3 origin, Vector3 forward)
    {
        MortarTuning tuning = weapon.Data.Mortar;
        Vector3 target = origin + forward * weapon.Data.BaseRange;

        if (ShowProjectilePaths)
        {
            Gizmos.color = new Color(1f, 0.85f, 0.05f, 0.8f);
            DrawArc(origin, target, tuning.MortarArcHeight);
        }

        if (ShowExplosionRadius)
        {
            Gizmos.color = new Color(1f, 0.45f, 0.05f, 0.75f);
            Gizmos.DrawWireSphere(target, tuning.MortarManualExplosionRadius);
            Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
            Gizmos.DrawWireSphere(target, tuning.MortarManualAccuracyRadius);
        }
    }

    private void DrawAutomaticCannon(WeaponInstance weapon, Vector3 origin, Vector3 forward)
    {
        AutomaticCannonTuning tuning = weapon.Data.AutomaticCannon;
        if (ShowProjectilePaths)
        {
            Gizmos.color = new Color(0.8f, 1f, 0.2f, 0.8f);
            for (int i = 0; i < tuning.CannonManualBurstCount; i++)
            {
                Vector3 start = origin + forward * (tuning.CannonManualLineSpacing * i);
                Gizmos.DrawLine(start, start + forward * weapon.Data.BaseRange);
            }
        }

        if (ShowWeaponHitboxes)
        {
            Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
            DrawCone(origin, forward, weapon.Data.BaseRange, tuning.CannonAbilityScatterRadius);
        }
    }

    private void DrawRotatingBlade(WeaponInstance weapon, Vector3 origin, Vector3 forward)
    {
        RotatingBladeTuning tuning = weapon.Data.RotatingBlade;
        float size = _sandbox.StatOverride != null ? _sandbox.StatOverride.ProjectileAreaSizeMultiplier : 1f;

        if (ShowWeaponHitboxes)
        {
            Gizmos.color = new Color(0.75f, 0.95f, 1f, 0.85f);
            Gizmos.DrawWireSphere(_sandbox.PlayerTransform.position, (tuning.BladeOrbitRadius + tuning.BladeContactRadius) * size);

            Gizmos.color = new Color(0.6f, 1f, 0.65f, 0.85f);
            DrawCone(origin, forward, tuning.BladeManualSlashRange * size, tuning.BladeManualSlashAngle);
        }

        if (ShowProjectilePaths)
        {
            Gizmos.color = new Color(0.2f, 1f, 1f, 0.85f);
            Gizmos.DrawLine(origin, origin + forward * tuning.BladeActiveThrustRange * size);
        }
    }

    private static void DrawCone(Vector3 origin, Vector3 forward, float range, float angle)
    {
        forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        Vector3 flatForward = forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude <= 0.0001f)
            flatForward = Vector3.forward;
        flatForward.Normalize();

        Quaternion left = Quaternion.AngleAxis(-angle * 0.5f, Vector3.up);
        Quaternion right = Quaternion.AngleAxis(angle * 0.5f, Vector3.up);
        Vector3 leftPoint = origin + left * flatForward * range;
        Vector3 rightPoint = origin + right * flatForward * range;

        Gizmos.DrawLine(origin, leftPoint);
        Gizmos.DrawLine(origin, rightPoint);
        int segments = 20;
        Vector3 previous = leftPoint;
        for (int i = 1; i <= segments; i++)
        {
            float t = i / (float)segments;
            float yaw = Mathf.Lerp(-angle * 0.5f, angle * 0.5f, t);
            Vector3 next = origin + Quaternion.AngleAxis(yaw, Vector3.up) * flatForward * range;
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }

    private static void DrawArc(Vector3 start, Vector3 end, float height)
    {
        Vector3 previous = start;
        for (int i = 1; i <= 24; i++)
        {
            float t = i / 24f;
            Vector3 point = Vector3.Lerp(start, end, t);
            point.y += Mathf.Sin(t * Mathf.PI) * height;
            Gizmos.DrawLine(previous, point);
            previous = point;
        }
    }
}
