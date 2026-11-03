using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// Enlaza los buffs temporales con los VFX Graph que cuelgan del Player (hijo "VfxPowerUps").
/// Cada <see cref="VisualEffect"/> se asocia por el nombre de su asset: Vfx_Damage = ExtraDamage,
/// Vfx_Speed = ExtraSpeed, Vfx_Scavening = ExtraScavenging. Quedan apagados hasta que el powerup se activa
/// y se desactivan unos segundos después de pararlos, para que las partículas terminen de apagarse.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerPowerupVfx : MonoBehaviour
{
    private const float DeactivateDelay = 2.5f;

    private readonly Dictionary<TemporaryPowerupType, List<VisualEffect>> _effects = new();
    private readonly Dictionary<VisualEffect, float> _pendingDeactivate = new();
    private readonly List<VisualEffect> _expired = new();

    private void Awake()
    {
        foreach (VisualEffect effect in GetComponentsInChildren<VisualEffect>(true))
        {
            if (!TryResolveType(effect, out TemporaryPowerupType type))
                continue;

            if (!_effects.TryGetValue(type, out List<VisualEffect> list))
                _effects[type] = list = new List<VisualEffect>();

            list.Add(effect);
            effect.Stop();
            effect.gameObject.SetActive(false);
        }
    }

    private void Update()
    {
        if (_pendingDeactivate.Count == 0)
            return;

        _expired.Clear();
        foreach (KeyValuePair<VisualEffect, float> pending in _pendingDeactivate)
        {
            if (Time.time >= pending.Value)
                _expired.Add(pending.Key);
        }

        for (int i = 0; i < _expired.Count; i++)
        {
            VisualEffect effect = _expired[i];
            _pendingDeactivate.Remove(effect);
            if (effect != null)
                effect.gameObject.SetActive(false);
        }
    }

    public bool Has(TemporaryPowerupType type) => _effects.ContainsKey(type);

    /// <summary>Enciende los VFX del powerup. Devuelve false si ese tipo no tiene VFX enlazado.</summary>
    public bool Show(TemporaryPowerupType type)
    {
        if (!_effects.TryGetValue(type, out List<VisualEffect> list))
            return false;

        for (int i = 0; i < list.Count; i++)
        {
            VisualEffect effect = list[i];
            if (effect == null)
                continue;

            _pendingDeactivate.Remove(effect);
            effect.gameObject.SetActive(true);
            effect.Reinit();
            effect.Play();
        }

        return true;
    }

    /// <summary>Deja de emitir; el objeto se apaga tras <see cref="DeactivateDelay"/> segundos.</summary>
    public void Hide(TemporaryPowerupType type)
    {
        if (!_effects.TryGetValue(type, out List<VisualEffect> list))
            return;

        for (int i = 0; i < list.Count; i++)
        {
            VisualEffect effect = list[i];
            if (effect == null || !effect.gameObject.activeSelf)
                continue;

            effect.Stop();
            _pendingDeactivate[effect] = Time.time + DeactivateDelay;
        }
    }

    private static bool TryResolveType(VisualEffect effect, out TemporaryPowerupType type)
    {
        string assetName = effect.visualEffectAsset != null ? effect.visualEffectAsset.name : string.Empty;
        switch (assetName)
        {
            case "Vfx_Damage":
                type = TemporaryPowerupType.ExtraDamage;
                return true;
            case "Vfx_Speed":
                type = TemporaryPowerupType.ExtraSpeed;
                return true;
            case "Vfx_Scavening":
            case "Vfx_Scavenging":
                type = TemporaryPowerupType.ExtraScavenging;
                return true;
            default:
                type = default;
                return false;
        }
    }
}
