using UnityEngine;

/// <summary>
/// One spawn lease: reserve gameplay immediately, place normally, then reveal only the model.
/// Combat/targeting read this gate rather than changing the ownership of existing AI components.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-80)]
public sealed class EnemyEmergence : MonoBehaviour, IEnemySpawnLifecycle
{
    private enum Phase { PendingPlacement, Emerging, Ready, Cancelled }

    [SerializeField] private Transform _visualPivot;
    [SerializeField, Min(0f)] private float _minimumDuration = 0.6f;
    [SerializeField, Min(0f)] private float _maximumDuration = 1f;
    [SerializeField, Min(0f)] private float _burialClearance = 0.15f;
    [SerializeField] private AnimationCurve _riseCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    private Phase _phase;
    private EnemyHealth _health;
    private Rigidbody _body;
    private Collider[] _colliders;
    private bool[] _collidersWereEnabled;
    private Renderer[] _renderers;
    private bool[] _renderersWereForcedOff;
    private Transform _cachedPivot;
    private Vector3 _restLocalPosition;
    private Quaternion _restLocalRotation;
    private Vector3 _restLocalScale;
    private Vector3 _buriedLocalOffset;
    private Vector3 _restRootPosition;
    private RigidbodyConstraints _bodyConstraints;
    private bool _bodyDetectCollisions;
    private bool _ownsCollision;
    private bool _ownsPhysics;
    private bool _hidesRenderers;
    private bool _suspended;
    private bool _excluded;
    private float _elapsed;

    public Transform VisualPivot => _visualPivot;
    public float MinimumDuration => _minimumDuration;
    public float MaximumDuration => _maximumDuration;
    public float SampledDuration { get; private set; }
    public float NormalizedProgress => _phase == Phase.Ready ? 1f : SampledDuration > 0f ? Mathf.Clamp01(_elapsed / SampledDuration) : 0f;
    public bool IsEmerging => !_excluded && _phase == Phase.Emerging;
    public bool BlocksGameplayNow => !_excluded && _phase != Phase.Ready && gameObject.activeInHierarchy;

    public static bool BlocksGameplay(Component target)
    {
        if (target == null) return false;
        EnemyEmergence emergence = target.GetComponentInParent<EnemyEmergence>();
        return emergence != null && emergence.BlocksGameplayNow;
    }

    public static bool BlocksGameplay(Transform target) => BlocksGameplay((Component)target);

    private void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        _body = GetComponent<Rigidbody>();
        _excluded = GetComponent<BossKeyDrop>() != null || GetComponent<GigaWormBehavior>() != null || GetComponent<DestroyerBehavior>() != null;
        CacheVisualPose();
    }

    private void OnEnable()
    {
        if (_health == null) _health = GetComponent<EnemyHealth>();
        if (_body == null) _body = GetComponent<Rigidbody>();
        _excluded = GetComponent<BossKeyDrop>() != null || GetComponent<GigaWormBehavior>() != null || GetComponent<DestroyerBehavior>() != null;
        if (_health != null) _health.OnDied += OnDied;
        ArmNewSpawn();
    }

    private void OnValidate()
    {
        _minimumDuration = Sanitize(_minimumDuration);
        _maximumDuration = Mathf.Max(_minimumDuration, Sanitize(_maximumDuration));
        _burialClearance = Sanitize(_burialClearance);
    }

    public void Configure(Transform visualPivot, float minimumDuration, float maximumDuration, float burialClearance = 0.15f)
    {
        RestoreVisualPose();
        RestoreRendererVisibility();
        _visualPivot = visualPivot;
        _minimumDuration = minimumDuration;
        _maximumDuration = maximumDuration;
        _burialClearance = burialClearance;
        OnValidate();
        CacheVisualPose();
        if (Application.isPlaying && _phase == Phase.PendingPlacement && !_excluded) HideRenderers();
    }

    private static float Sanitize(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Max(0f, value);

    private void CacheVisualPose()
    {
        if (_visualPivot == null || _visualPivot == transform || _cachedPivot == _visualPivot) return;
        _cachedPivot = _visualPivot;
        _restLocalPosition = _visualPivot.localPosition;
        _restLocalRotation = _visualPivot.localRotation;
        _restLocalScale = _visualPivot.localScale;
        _renderers = _visualPivot.GetComponentsInChildren<Renderer>(true);
        _renderersWereForcedOff = new bool[_renderers.Length];
    }

    private void ArmNewSpawn()
    {
        RestoreOwnedState();
        CacheVisualPose();
        _elapsed = 0f;
        SampledDuration = 0f;
        _suspended = false;
        _phase = _excluded ? Phase.Ready : Phase.PendingPlacement;
        if (_excluded) return;
        CaptureAndSuppressCollision();
        HideRenderers();
    }

    public void OnPoolSpawn()
    {
        // Every pool lease activates the object first, so OnEnable owns initialization.
        // A duplicate notification must not restart an emerging or completed lease.
    }

    public void OnPoolDespawn()
    {
        CancelEmergence();
        RestoreOwnedState();
    }

    /// <summary>Seating needs the original enabled colliders, while AI remains gated and visuals hidden.</summary>
    public void PrepareForPlacement()
    {
        if (_excluded || _phase != Phase.PendingPlacement) return;
        RestoreCollisionAndPhysics();
        RestoreVisualPose();
    }

    public void BeginEmergence(Vector3 groundPoint, Vector3 groundNormal)
    {
        if (_excluded || _suspended || _phase != Phase.PendingPlacement || !gameObject.activeInHierarchy) return;
        if (_health != null && _health.CurrentHealth <= 0) { CancelEmergence(); return; }
        PrepareForPlacement();
        CacheVisualPose();
        OnValidate();

        // Disabled tutorial behaviors must not replace their lesson-authored stationary pose.
        EnemyBehaviorBase[] behaviors = GetComponents<EnemyBehaviorBase>();
        for (int i = 0; i < behaviors.Length; i++)
            if (behaviors[i].isActiveAndEnabled)
            {
                transform.position = behaviors[i].ResolveEmergenceRestPosition(groundPoint);
                break;
            }
        _restRootPosition = transform.position;
        SampledDuration = _minimumDuration == _maximumDuration ? _minimumDuration : Random.Range(_minimumDuration, _maximumDuration);
        _elapsed = 0f;
        _phase = Phase.Emerging;
        CaptureAndSuppressCollision();
        CaptureAndSuppressPhysics();
        GetComponent<EnemyKnockbackReceiver>()?.ClearPending();
        GetComponent<EnemyFollow>()?.ResetVerticalVelocity();

        if (_visualPivot != null && _visualPivot != transform)
        {
            Vector3 normal = groundNormal.sqrMagnitude > 0.001f ? groundNormal.normalized : Vector3.up;
            if (normal.y < 0.05f) normal = Vector3.up;
            float highestDistance = 0f;
            bool found = false;
            for (int i = 0; _renderers != null && i < _renderers.Length; i++)
            {
                Renderer renderer = _renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy || renderer is ParticleSystemRenderer || renderer is LineRenderer) continue;
                Bounds bounds = renderer.bounds;
                Vector3 extent = bounds.extents;
                float distance = Vector3.Dot(normal, bounds.center - groundPoint)
                    + Mathf.Abs(normal.x) * extent.x + Mathf.Abs(normal.y) * extent.y + Mathf.Abs(normal.z) * extent.z;
                highestDistance = found ? Mathf.Max(highestDistance, distance) : distance;
                found = true;
            }
            float depth = found ? Mathf.Max(0f, (highestDistance + _burialClearance) / normal.y) : Mathf.Max(1f, _burialClearance);
            _buriedLocalOffset = _visualPivot.parent.InverseTransformVector(Vector3.down * depth);
            _visualPivot.localPosition = _restLocalPosition + _buriedLocalOffset;
        }
        // Place below the surface before making the model renderable, including same-frame spawns.
        RestoreRendererVisibility();
        if (SampledDuration <= 0f) CompleteEmergence();
    }

    /// <summary>Scene/tutorial actors use their authored placement; ordinary spawners supply the selected surface.</summary>
    public void BeginAtCurrentPlacement()
    {
        if (_excluded || _suspended || _phase != Phase.PendingPlacement) return;
        EnemyStatRandomizer.BeginSpawn(gameObject);
        Vector3 originalPosition = transform.position;
        PrepareForPlacement();
        int mask = LayerMask.GetMask("Terrain", "Default");
        // Suppress our own colliders for the fallback probe, then restore them before the normal begin.
        CaptureAndSuppressCollision();
        bool found = SpawnGroundUtility.TrySampleGround(originalPosition, originalPosition.y, 0f, mask, mask, 3f, 100f, 0f,
            out _, out Vector3 groundPoint, out Vector3 groundNormal);
        RestoreCollisionAndPhysics();
        EnemyStatRandomizer variation = GetComponent<EnemyStatRandomizer>();
        if (found && variation != null && variation.HasSampledForSpawn && variation.SizeMultiplier != 1f)
            SpawnGroundUtility.SeatAboveSurface(transform, groundPoint, 0f);
        if (!found)
        {
            groundPoint = originalPosition;
            groundNormal = Vector3.up;
        }
        BeginEmergence(groundPoint, groundNormal);
    }

    private void Start()
    {
        if (_phase == Phase.PendingPlacement) BeginAtCurrentPlacement();
    }

    private void Update()
    {
        if (_phase == Phase.PendingPlacement) BeginAtCurrentPlacement();
        Simulate(Time.deltaTime);
    }

    public void Simulate(float deltaTime)
    {
        if (_phase != Phase.Emerging || _suspended || GameplayPause.IsUiPaused || deltaTime <= 0f) return;
        if (_health != null && _health.CurrentHealth <= 0) { CancelEmergence(); return; }
        _elapsed = Mathf.Min(SampledDuration, _elapsed + deltaTime);
        float progress = NormalizedProgress;
        float rise = _riseCurve != null && _riseCurve.length > 0 ? Mathf.Clamp01(_riseCurve.Evaluate(progress)) : Mathf.SmoothStep(0f, 1f, progress);
        if (_visualPivot != null && _visualPivot != transform)
            _visualPivot.localPosition = _restLocalPosition + _buriedLocalOffset * (1f - rise);
        if (_elapsed >= SampledDuration) CompleteEmergence();
    }

    private void LateUpdate()
    {
        if (_phase == Phase.Emerging && !_suspended) transform.position = _restRootPosition;
    }

    private void CompleteEmergence()
    {
        if (_phase != Phase.Emerging) return;
        RestoreOwnedState();
        _phase = Phase.Ready;
    }

    public void CancelEmergence()
    {
        if (_excluded) return;
        _phase = Phase.Cancelled;
        // Keep the last visible pose for death feedback; pool/disable cleanup restores authoring state.
    }

    private void OnDied() => CancelEmergence();

    public void SuspendForDormancy()
    {
        if (_excluded || _phase == Phase.Ready || _suspended) return;
        _suspended = true;
        // Dormancy must cache the real collider/physics state, not our temporary suppression.
        RestoreCollisionAndPhysics();
    }

    public void ResumeAfterDormancy()
    {
        if (!_suspended) return;
        _suspended = false;
        if (!gameObject.activeInHierarchy || !enabled || _excluded || _phase == Phase.Ready) return;
        if (_phase == Phase.Emerging) transform.position = _restRootPosition;
        CaptureAndSuppressCollision();
        CaptureAndSuppressPhysics();
    }

    private void CaptureAndSuppressCollision()
    {
        if (_ownsCollision) return;
        _colliders = GetComponentsInChildren<Collider>(true);
        _collidersWereEnabled = new bool[_colliders.Length];
        for (int i = 0; i < _colliders.Length; i++)
        {
            _collidersWereEnabled[i] = _colliders[i].enabled;
            _colliders[i].enabled = false;
        }
        _ownsCollision = true;
    }

    private void CaptureAndSuppressPhysics()
    {
        if (_ownsPhysics || _body == null) return;
        _bodyConstraints = _body.constraints;
        _bodyDetectCollisions = _body.detectCollisions;
        if (!_body.isKinematic)
        {
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
        }
        _body.constraints = RigidbodyConstraints.FreezeAll;
        _body.detectCollisions = false;
        _ownsPhysics = true;
    }

    private void RestoreCollisionAndPhysics()
    {
        if (_ownsCollision)
        {
            for (int i = 0; i < _colliders.Length; i++)
                if (_colliders[i] != null) _colliders[i].enabled = _collidersWereEnabled[i];
            _ownsCollision = false;
        }
        if (_ownsPhysics && _body != null)
        {
            if (!_body.isKinematic)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
            }
            _body.constraints = _bodyConstraints;
            _body.detectCollisions = _bodyDetectCollisions;
        }
        _ownsPhysics = false;
    }

    private void HideRenderers()
    {
        if (_hidesRenderers || _renderers == null) return;
        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] == null) continue;
            _renderersWereForcedOff[i] = _renderers[i].forceRenderingOff;
            _renderers[i].forceRenderingOff = true;
        }
        _hidesRenderers = true;
    }

    private void RestoreRendererVisibility()
    {
        if (!_hidesRenderers) return;
        for (int i = 0; i < _renderers.Length; i++)
            if (_renderers[i] != null) _renderers[i].forceRenderingOff = _renderersWereForcedOff[i];
        _hidesRenderers = false;
    }

    private void RestoreVisualPose()
    {
        if (_cachedPivot == null) return;
        _cachedPivot.localPosition = _restLocalPosition;
        _cachedPivot.localRotation = _restLocalRotation;
        _cachedPivot.localScale = _restLocalScale;
    }

    private void RestoreOwnedState()
    {
        RestoreCollisionAndPhysics();
        RestoreVisualPose();
        RestoreRendererVisibility();
        GetComponent<EnemyKnockbackReceiver>()?.ClearPending();
    }

    private void OnDisable()
    {
        if (_health != null) _health.OnDied -= OnDied;
        CancelEmergence();
        RestoreOwnedState();
    }
}
