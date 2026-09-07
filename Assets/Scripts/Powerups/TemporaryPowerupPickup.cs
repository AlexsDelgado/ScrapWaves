using UnityEngine;

/// <summary>Pickup de power-up temporal: se recoge caminando (sin imán ni PickupRange de stats).</summary>
[RequireComponent(typeof(WorldPickup))]
public class TemporaryPowerupPickup : MonoBehaviour, IPickable
{
    private TemporaryPowerupType _type;
    private TemporaryPowerupPool _pool;
    private bool _consumed;

    public void Activate(TemporaryPowerupPool pool, TemporaryPowerupType type)
    {
        _pool = pool;
        _type = type;
        _consumed = false;
        if (TryGetComponent(out WorldPickup worldPickup))
            worldPickup.ConfigureForManualCollection(1f);
        ApplyColor(type);
    }

    public void OnPickedUp()
    {
        if (_consumed)
            return;
        _consumed = true;

        TemporaryPowerupController controller = TemporaryPowerupController.Instance;
        if (controller == null)
            controller = FindAnyObjectByType<TemporaryPowerupController>();
        controller?.Apply(_type);

        if (_pool != null)
            _pool.Release(gameObject);
        else
            Destroy(gameObject);
    }

    private void ApplyColor(TemporaryPowerupType type)
    {
        if (!TryGetComponent(out Renderer renderer))
            return;

        Color c = type switch
        {
            TemporaryPowerupType.ExtraDamage => new Color(1f, 0.4f, 0.1f),
            TemporaryPowerupType.ExtraSpeed => new Color(0.3f, 0.7f, 1f),
            TemporaryPowerupType.ExtraScavenging => new Color(0.3f, 0.95f, 0.4f),
            TemporaryPowerupType.Invulnerability => new Color(1f, 1f, 1f),
            TemporaryPowerupType.FullHeal => new Color(1f, 0.3f, 0.55f),
            TemporaryPowerupType.Nuke => new Color(1f, 0.85f, 0.15f),
            _ => Color.white
        };
        renderer.material.color = c;
    }
}
