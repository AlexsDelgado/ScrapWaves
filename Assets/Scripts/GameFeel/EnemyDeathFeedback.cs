using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyDeathFeedback : MonoBehaviour
{
    [SerializeField, Range(0.1f, 3f), Tooltip("Scales the detached pooled death cue without delaying enemy despawn.")]
    private float _effectIntensity = 1f;

    public float EffectIntensity => _effectIntensity;

    public static float ResolveIntensity(Transform target)
    {
        if (target == null)
            return 1f;
        EnemyDeathFeedback feedback = target.GetComponentInParent<EnemyDeathFeedback>();
        return feedback != null ? feedback._effectIntensity : 1f;
    }

    private void OnValidate()
    {
        _effectIntensity = Mathf.Clamp(_effectIntensity, 0.1f, 3f);
    }
}
