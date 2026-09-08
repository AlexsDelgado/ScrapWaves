using UnityEngine;

/// <summary>Pickup de 1 Scrap meta (Spec mid-run drop).</summary>
[RequireComponent(typeof(WorldPickup))]
public class ScrapPickup : MonoBehaviour, IPickable
{
    private ScrapPickupPool _pool;
    private bool _consumed;

    public void Activate(ScrapPickupPool pool)
    {
        _pool = pool;
        _consumed = false;
    }

    public void OnPickedUp()
    {
        if (_consumed)
            return;
        _consumed = true;

        if (SaveManager.Instance != null)
        {
            SaveManager.Instance.AddScrap(1);
            SaveManager.Instance.SaveNow();
        }

        if (_pool != null)
            _pool.Release(gameObject);
        else
            Destroy(gameObject);
    }
}
