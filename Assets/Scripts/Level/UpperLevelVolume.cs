using UnityEngine;

/// <summary>The single authored upper region and the roots that positively identify Base support.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class UpperLevelVolume : MonoBehaviour
{
    [SerializeField, Tooltip("Terrain and other genuine base support roots. Never assign the shared map root or upper geometry.")]
    private Transform[] _baseSurfaceRoots = System.Array.Empty<Transform>();

    [Header("Overheat damage")]
    [SerializeField, Min(1f), Tooltip("Raw damage of the first upper-level hit in each Overheat event, before normal player mitigation.")]
    private float _startingDamage = 1f;
    [SerializeField, Min(1f), Tooltip("Multiplies unrounded damage after each hazard tick. Leaving and re-entering the volume does not reset escalation during the same event.")]
    private float _damageMultiplier = 1.3f;

    public float StartingDamage => Mathf.Max(1f, _startingDamage);
    public float DamageMultiplier => Mathf.Max(1f, _damageMultiplier);

    private BoxCollider _box;
    private BoxCollider Box => _box != null ? _box : (_box = GetComponent<BoxCollider>());
    public bool IsConfigured => Box != null && _baseSurfaceRoots != null &&
        System.Array.Exists(_baseSurfaceRoots, root => root != null);
    public bool IsAvailable => isActiveAndEnabled && Box != null && Box.enabled;

    public bool Contains(Vector3 worldPoint)
    {
        if (!IsAvailable) return false;
        Vector3 local = transform.InverseTransformPoint(worldPoint) - Box.center;
        Vector3 half = Box.size * .5f;
        return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
    }

    public bool IsBaseSupport(Collider support)
    {
        if (support == null || !support.enabled || !support.gameObject.activeInHierarchy || _baseSurfaceRoots == null)
            return false;
        foreach (Transform root in _baseSurfaceRoots)
            if (root != null && (support.transform == root || support.transform.IsChildOf(root))) return true;
        return false;
    }

    private void Reset() => GetComponent<BoxCollider>().isTrigger = true;

    private void OnDrawGizmosSelected()
    {
        if (Box == null) return;
        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, .3f, .05f, .12f);
        Gizmos.DrawCube(Box.center, Box.size);
        Gizmos.color = new Color(1f, .4f, .1f, .9f);
        Gizmos.DrawWireCube(Box.center, Box.size);
        Gizmos.matrix = previous;
    }
}
