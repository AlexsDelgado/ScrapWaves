using UnityEngine;

/// <summary>Designer-authored variation, sampled once for each spawned enemy life.</summary>
[DisallowMultipleComponent]
public sealed class EnemyStatRandomizer : MonoBehaviour, IEnemySpawnLifecycle
{
    [Header("Per-spawn multipliers (X = minimum, Y = maximum)")]
    [SerializeField, Tooltip("Multiplies authored HP together with existing difficulty and cycle scaling. 1 / 1 is unchanged.")]
    private Vector2 _healthMultiplierRange = Vector2.one;
    [SerializeField, Tooltip("Uniform body and hitbox scale relative to the authored root. Does not change attack ranges or rewards.")]
    private Vector2 _sizeMultiplierRange = Vector2.one;
    [SerializeField, Tooltip("Ordinary movement only. Does not affect attack dashes, launches, projectiles, climbing, hover, or knockback.")]
    private Vector2 _movementSpeedMultiplierRange = Vector2.one;

    private Vector3 _authoredRootScale;
    private bool _hasAuthoredRootScale;

    public bool HasSampledForSpawn { get; private set; }
    public float HealthMultiplier { get; private set; } = 1f;
    public float SizeMultiplier { get; private set; } = 1f;
    public float MovementSpeedMultiplier { get; private set; } = 1f;

    /// <summary>Called explicitly after pool resets and before placement measures the body.</summary>
    public static void BeginSpawn(GameObject enemy)
    {
        if (enemy != null)
            enemy.GetComponent<EnemyStatRandomizer>()?.BeginSpawn();
    }

    public void BeginSpawn()
    {
        if (HasSampledForSpawn || !isActiveAndEnabled || IsExcludedSpawn())
            return;

        if (!_hasAuthoredRootScale)
        {
            _authoredRootScale = transform.localScale;
            _hasAuthoredRootScale = true;
        }

        HealthMultiplier = Sample(_healthMultiplierRange);
        SizeMultiplier = Sample(_sizeMultiplierRange);
        MovementSpeedMultiplier = Sample(_movementSpeedMultiplierRange);
        HasSampledForSpawn = true;
        transform.localScale = _authoredRootScale * SizeMultiplier;
        GetComponent<EnemyHealth>()?.SetSpawnHealthMultiplier(HealthMultiplier);
    }

    // A tutorial marker belongs to the lesson instance, so shared production prefabs keep their component.
    private bool IsExcludedSpawn() => GetComponentInParent<TutorialEnemy>() != null
        || GetComponent<BossKeyDrop>() != null
        || GetComponent<GigaWormBehavior>() != null
        || GetComponent<DestroyerBehavior>() != null;

    public void EndSpawn()
    {
        if (_hasAuthoredRootScale)
            transform.localScale = _authoredRootScale;
        HasSampledForSpawn = false;
        HealthMultiplier = SizeMultiplier = MovementSpeedMultiplier = 1f;
        // Health is reset at the next checkout. Never heal a dying enemy during death callbacks.
    }

    // Activation and pool notifications precede placement; neither is a sampling boundary.
    public void OnPoolSpawn() { }
    public void OnPoolDespawn() => EndSpawn();

    private static float Sample(Vector2 range)
    {
        range = ValidateRange(range);
        return range.x == range.y ? range.x : Random.Range(range.x, range.y);
    }

    private static Vector2 ValidateRange(Vector2 range)
    {
        float minimum = ValidateEndpoint(range.x);
        float maximum = ValidateEndpoint(range.y);
        return new Vector2(Mathf.Min(minimum, maximum), Mathf.Max(minimum, maximum));
    }

    private static float ValidateEndpoint(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) || value <= 0f ? 1f : value;

    private void OnValidate()
    {
        _healthMultiplierRange = ValidateRange(_healthMultiplierRange);
        _sizeMultiplierRange = ValidateRange(_sizeMultiplierRange);
        _movementSpeedMultiplierRange = ValidateRange(_movementSpeedMultiplierRange);
    }
}
