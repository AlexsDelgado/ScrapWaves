using UnityEngine;

public sealed class FlamethrowerFuelPuddle : MonoBehaviour
{
    private Vector3 _center;
    private float _radius;
    private int _damagePerTick;
    private float _remainingDuration;
    private float _tickInterval;
    private float _tickTimer;

    public static FlamethrowerFuelPuddle Spawn(Vector3 center, float radius, int damagePerTick, float duration, float tickInterval)
    {
        GameObject go = new("FlamethrowerFuelPuddle");
        FlamethrowerFuelPuddle puddle = go.AddComponent<FlamethrowerFuelPuddle>();
        puddle.Configure(center, radius, damagePerTick, duration, tickInterval);
        return puddle;
    }

    private void Configure(Vector3 center, float radius, int damagePerTick, float duration, float tickInterval)
    {
        _center = center;
        _radius = Mathf.Max(0.1f, radius);
        _damagePerTick = Mathf.Max(1, damagePerTick);
        _remainingDuration = Mathf.Max(0.1f, duration);
        _tickInterval = Mathf.Max(0.05f, tickInterval);
        _tickTimer = 0f;
        transform.position = center;

        if (Application.isPlaying)
            ExplosionRadiusVfx.Spawn(center, _radius);
    }

    private void Update()
    {
        _remainingDuration -= Time.deltaTime;
        _tickTimer -= Time.deltaTime;

        while (_tickTimer <= 0f && _remainingDuration > 0f)
        {
            WeaponRadialDamage.Apply(_center, _radius, _damagePerTick, falloff: 0f, knockback: 0f, maxTargets: 64);
            _tickTimer += _tickInterval;
        }

        if (_remainingDuration <= 0f)
            Destroy(gameObject);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.45f, 0.05f, 0.85f);
        Gizmos.DrawWireSphere(transform.position, _radius);
    }
}
