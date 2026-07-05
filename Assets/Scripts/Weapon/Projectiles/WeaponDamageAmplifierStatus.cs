using UnityEngine;

[DisallowMultipleComponent]
public sealed class WeaponDamageAmplifierStatus : MonoBehaviour
{
    private float _multiplier = 1f;
    private float _remainingDuration;
    private WeaponStatusAuraVfx _aura;

    public float Multiplier => _remainingDuration > 0f ? Mathf.Max(1f, _multiplier) : 1f;

    public void Refresh(float multiplier, float duration)
    {
        _multiplier = Mathf.Max(_multiplier, multiplier);
        _remainingDuration = Mathf.Max(_remainingDuration, duration);
        RefreshAura(duration);
        TryApplyDummyStatus(duration);
    }

    public static void Apply(IDamageable damageable, float multiplier, float duration)
    {
        if (damageable is not Component component || duration <= 0f)
            return;

        WeaponDamageAmplifierStatus status = component.GetComponent<WeaponDamageAmplifierStatus>();
        if (status == null)
            status = component.gameObject.AddComponent<WeaponDamageAmplifierStatus>();

        status.Refresh(multiplier, duration);
    }

    public static int ModifyDamage(IDamageable damageable, int damage)
    {
        if (damageable is not Component component)
            return damage;

        WeaponDamageAmplifierStatus status = component.GetComponent<WeaponDamageAmplifierStatus>();
        if (status == null)
            return damage;

        return Mathf.Max(1, Mathf.RoundToInt(damage * status.Multiplier));
    }

    private void Update()
    {
        if (_remainingDuration <= 0f)
        {
            Destroy(this);
            return;
        }

        _remainingDuration -= Time.deltaTime;
        if (_remainingDuration <= 0f)
            Destroy(this);
    }

    private void TryApplyDummyStatus(float duration)
    {
        WeaponDummyEnemy dummy = GetComponent<WeaponDummyEnemy>();
        if (dummy != null)
            dummy.ApplyStatus("Vulnerable", duration);
    }

    private void RefreshAura(float duration)
    {
        if (_aura == null)
            _aura = WeaponStatusAuraVfx.SpawnVulnerableAura(transform, duration);
        else
            _aura.Refresh(duration);
    }

    private void OnDisable()
    {
        _remainingDuration = 0f;
        DismissAura();
    }

    private void OnDestroy()
    {
        DismissAura();
    }

    private void DismissAura()
    {
        if (_aura != null)
            _aura.Dismiss();
        _aura = null;
    }
}
