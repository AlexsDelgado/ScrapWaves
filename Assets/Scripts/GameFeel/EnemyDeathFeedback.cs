using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyDeathFeedback : MonoBehaviour
{
    [SerializeField] private EnemyReactionProfile _profile;
    [SerializeField, Range(0.1f, 3f), Tooltip("Scales the detached death cue without delaying enemy despawn.")]
    private float _effectIntensity = 1f;

    private EnemyHealth _health;
    private WeaponDummyEnemy _dummy;
    private Vector3 _lastDirection = Vector3.up;

    public float EffectIntensity => _effectIntensity;

    private void Awake()
    {
        _profile = EnemyReactionProfile.Resolve(_profile);
        ResolveTargets();
    }

    private void OnEnable()
    {
        ResolveTargets();
        if (_health != null)
            _health.OnDied += HandleHealthDeath;
        if (_dummy != null)
            _dummy.Died += HandleDummyDeath;
    }

    private void OnDisable()
    {
        if (_health != null)
            _health.OnDied -= HandleHealthDeath;
        if (_dummy != null)
            _dummy.Died -= HandleDummyDeath;
    }

    public static float ResolveIntensity(Transform target)
    {
        EnemyDeathFeedback feedback = Find(target);
        return feedback != null ? feedback._effectIntensity : 1f;
    }

    public static void RecordHit(in WeaponFeedbackContext context)
    {
        EnemyDeathFeedback feedback = Find(context.Target);
        if (feedback == null)
            return;
        feedback._lastDirection = context.Direction.sqrMagnitude > 0.0001f ? context.Direction.normalized : Vector3.up;
        if (context.IsKill)
            EnemyDeathReactionVfx.EnrichPending(feedback.gameObject.GetInstanceID(), feedback._lastDirection,
                context.IsCritical || context.IsWeakPoint, context.WeaponType);
    }

    private void HandleHealthDeath() => ScheduleDeath();
    private void HandleDummyDeath(WeaponDummyEnemy _) => ScheduleDeath();

    private void ScheduleDeath()
    {
        if (!EnemyReactionRuntime.Enabled)
            return;
        ResolveBounds(out Vector3 center, out float radius, out Color color);
        EnemyDeathReactionVfx.Schedule(
            gameObject.GetInstanceID(),
            center,
            _lastDirection,
            radius,
            color,
            EnemyStatusFeedback.ResolveMask(transform),
            _effectIntensity,
            EnemyReactionProfile.Resolve(_profile));
    }

    private void ResolveTargets()
    {
        if (_health == null)
            _health = GetComponentInChildren<EnemyHealth>(true);
        if (_dummy == null)
            _dummy = GetComponentInChildren<WeaponDummyEnemy>(true);
    }

    private void ResolveBounds(out Vector3 center, out float radius, out Color color)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        bool found = false;
        Bounds bounds = new(transform.position, Vector3.one);
        color = new Color(1f, 0.34f, 0.08f, 1f);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer is LineRenderer || renderer.GetComponentInParent<EnemyStatusVisual>() != null ||
                renderer.gameObject.name.StartsWith("[Enemy Hit Flash]"))
                continue;
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
                Material material = renderer.sharedMaterial;
                if (material != null)
                {
                    if (material.HasProperty("_BaseColor")) color = material.GetColor("_BaseColor");
                    else if (material.HasProperty("_Color")) color = material.color;
                }
            }
            else
                bounds.Encapsulate(renderer.bounds);
        }
        center = found ? bounds.center : transform.position + Vector3.up * 0.6f;
        radius = found ? Mathf.Clamp(bounds.extents.magnitude * 0.65f, 0.4f, 4f) : 0.75f;
        color.a = 1f;
    }

    private static EnemyDeathFeedback Find(Transform target)
    {
        if (target == null)
            return null;
        EnemyDeathFeedback feedback = target.GetComponentInParent<EnemyDeathFeedback>();
        if (feedback == null)
            feedback = target.GetComponentInChildren<EnemyDeathFeedback>(true);
        return feedback;
    }

    private void OnValidate()
    {
        _effectIntensity = Mathf.Clamp(_effectIntensity, 0.1f, 3f);
    }
}
