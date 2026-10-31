using UnityEngine;

/// <summary>Cosmetic follower of the charger AI. Movement, collision and damage remain gameplay-owned.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class ChaserAnimationDriver : MonoBehaviour, IEnemySpawnLifecycle
{
    private static readonly int IdleHash = Animator.StringToHash("Idle");
    private static readonly int WalkHash = Animator.StringToHash("Walk");
    private static readonly int ChargeHash = Animator.StringToHash("Charge");
    private static readonly int DashHash = Animator.StringToHash("Dash");
    private static readonly int RecoverHash = Animator.StringToHash("Recover");
    private static readonly int WalkSpeedHash = Animator.StringToHash("WalkSpeed");
    private static readonly int ChargePhaseHash = Animator.StringToHash("ChargePhase");
    private static readonly int DashPhaseHash = Animator.StringToHash("DashPhase");
    private static readonly int RecoverPhaseHash = Animator.StringToHash("RecoverPhase");

    [SerializeField] private Animator _animator;
    [SerializeField, Min(0.01f), Tooltip("Planar units per second at unit world scale that match one authored Walk cycle per clip duration.")]
    private float _walkUnitsPerSecond = 2.125f;
    [SerializeField, Min(0f)] private float _blendSeconds = 0.06f;
    [SerializeField, Min(0.01f)] private float _anticipationSeconds = 1f / 3f;
    [SerializeField, Min(0f)] private float _speedSmoothingSeconds = 0.08f;
    [SerializeField, Min(0.01f)] private float _maximumWalkSpeed = 4f;

    private ChargerEnemyBehavior _charger;
    private EnemyHealth _health;
    private EnemyEmergence _emergence;
    private EnemyVerticalEngagement _engagement;
    private SimpleFollow _simpleFollow;
    private EnemyFollow _enemyFollow;
    private Transform _cachedVisual;
    private Vector3 _visualPosition;
    private Quaternion _visualRotation;
    private Vector3 _visualScale;
    private Vector3 _previousPosition;
    private int _stateHash;
    private bool _hasState;
    private bool _dead;
    private bool _ownsAnimatorSuspension;
    private bool _animatorWasEnabled;
    private bool _needsPositionSample;
    private float _smoothedWalkSpeed;

    public Animator AnimationAnimator => _animator;
    public string CurrentStateName { get; private set; } = "Idle";
    public float PhaseNormalized { get; private set; }
    public float WalkPlaybackSpeed => _smoothedWalkSpeed;

    public void Configure(Animator animator, float walkUnitsPerSecond = 2.125f)
    {
        RestoreAnimatorEvaluation();
        RestoreVisualOffset();
        _animator = animator;
        _walkUnitsPerSecond = Mathf.Max(0.01f, walkUnitsPerSecond);
        _cachedVisual = null;
        CacheComponents();
        ResetPresentation();
    }

    private void Awake() => CacheComponents();

    private void OnEnable()
    {
        CacheComponents();
        if (_health != null)
        {
            _health.OnDied -= OnDied;
            _health.OnDied += OnDied;
        }
        ResetPresentation();
    }

    private void OnDisable()
    {
        if (_health != null) _health.OnDied -= OnDied;
        ResetPresentation();
    }

    private void Update() => TickPresentation(Time.deltaTime);

    private void CacheComponents()
    {
        _charger = GetComponent<ChargerEnemyBehavior>();
        _health = GetComponent<EnemyHealth>();
        _emergence = GetComponent<EnemyEmergence>();
        _engagement = GetComponent<EnemyVerticalEngagement>();
        _simpleFollow = GetComponent<SimpleFollow>();
        _enemyFollow = GetComponent<EnemyFollow>();
        if (_animator == null) return;
        _animator.applyRootMotion = false;
        Transform visual = _animator.transform;
        if (visual == transform || _cachedVisual == visual) return;
        _cachedVisual = visual;
        _visualPosition = visual.localPosition;
        _visualRotation = visual.localRotation;
        _visualScale = visual.localScale;
    }

    /// <summary>Samples gameplay after its movement update, without advancing timers or enabling any AI.</summary>
    public void TickPresentation(float deltaTime)
    {
        Vector3 displacement = _needsPositionSample ? Vector3.zero : transform.position - _previousPosition;
        _previousPosition = transform.position;
        _needsPositionSample = false;
        if (_animator == null || !gameObject.activeInHierarchy) return;

        if (_dead || (_health != null && _health.CurrentHealth <= 0))
        {
            // Leave the last pose intact for detached death snapshots, before the pool resets it.
            _animator.speed = 0f;
            return;
        }

        if (_engagement != null && _engagement.IsDormant)
        {
            SuspendAnimatorEvaluation();
            _smoothedWalkSpeed = 0f;
            return;
        }
        bool waking = _ownsAnimatorSuspension;
        RestoreAnimatorEvaluation();
        if (waking) displacement = Vector3.zero;
        if (!CanWriteAnimator()) return;
        _animator.speed = GameplayPause.IsUiPaused ? 0f : 1f;
        if (GameplayPause.IsUiPaused) return;

        bool blocked = _charger == null || !_charger.isActiveAndEnabled
            || (_emergence != null && _emergence.BlocksGameplayNow)
            || EnemyVerticalEngagement.IsDisengaged(this);
        if (blocked)
        {
            _smoothedWalkSpeed = 0f;
            Show(IdleHash, "Idle", 0f);
            return;
        }

        switch (_charger.CurrentPresentationPhase)
        {
            case ChargerEnemyBehavior.PresentationPhase.Charging:
                _smoothedWalkSpeed = 0f;
                Show(ChargeHash, "Charge", _charger.PresentationElapsedSeconds / Mathf.Max(0.01f, _anticipationSeconds));
                break;
            case ChargerEnemyBehavior.PresentationPhase.Dashing:
                _smoothedWalkSpeed = 0f;
                Show(DashHash, "Dash", _charger.PresentationNormalizedProgress);
                break;
            case ChargerEnemyBehavior.PresentationPhase.Recover:
                _smoothedWalkSpeed = 0f;
                Show(RecoverHash, "Recover", _charger.PresentationNormalizedProgress);
                break;
            default:
                bool canMove = _charger.CurrentPresentationPhase == ChargerEnemyBehavior.PresentationPhase.Overheat
                    || (_simpleFollow != null && _simpleFollow.isActiveAndEnabled)
                    || (_enemyFollow != null && _enemyFollow.isActiveAndEnabled);
                ShowLocomotion(canMove ? displacement : Vector3.zero, deltaTime);
                break;
        }
    }

    private void ShowLocomotion(Vector3 displacement, float deltaTime)
    {
        displacement.y = 0f;
        Vector3 scale = transform.lossyScale;
        float planarScale = Mathf.Max(0.01f, Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)));
        float rawSpeed = deltaTime > 0f ? displacement.magnitude / deltaTime / planarScale / Mathf.Max(0.01f, _walkUnitsPerSecond) : 0f;
        rawSpeed = Mathf.Clamp(rawSpeed, 0f, _maximumWalkSpeed);
        float blend = _speedSmoothingSeconds > 0f ? 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / _speedSmoothingSeconds) : 1f;
        _smoothedWalkSpeed = Mathf.Lerp(_smoothedWalkSpeed, rawSpeed, blend);
        if (_smoothedWalkSpeed < 0.01f)
        {
            _smoothedWalkSpeed = 0f;
            Show(IdleHash, "Idle", 0f);
        }
        else Show(WalkHash, "Walk", 0f);
    }

    private bool CanWriteAnimator() => _animator != null && _animator.isActiveAndEnabled && _animator.runtimeAnimatorController != null;

    private void Show(int stateHash, string stateName, float phase)
    {
        PhaseNormalized = Mathf.Clamp01(phase);
        CurrentStateName = stateName;
        // Keep the outgoing phase unchanged while the incoming phase blends from its own start.
        if (stateHash == ChargeHash) _animator.SetFloat(ChargePhaseHash, PhaseNormalized);
        else if (stateHash == DashHash) _animator.SetFloat(DashPhaseHash, PhaseNormalized);
        else if (stateHash == RecoverHash) _animator.SetFloat(RecoverPhaseHash, PhaseNormalized);
        _animator.SetFloat(WalkSpeedHash, _smoothedWalkSpeed);
        if (_hasState && _stateHash == stateHash) return;
        if (_hasState && _blendSeconds > 0f)
            _animator.CrossFadeInFixedTime(stateHash, _blendSeconds, 0, 0f);
        else
            _animator.Play(stateHash, 0, 0f);
        _stateHash = stateHash;
        _hasState = true;
    }

    private void OnDied()
    {
        _dead = true;
        if (_animator != null) _animator.speed = 0f;
    }

    private void SuspendAnimatorEvaluation()
    {
        if (_ownsAnimatorSuspension || _animator == null) return;
        _animatorWasEnabled = _animator.enabled;
        _ownsAnimatorSuspension = true;
        _animator.enabled = false;
    }

    private void RestoreAnimatorEvaluation()
    {
        if (!_ownsAnimatorSuspension) return;
        if (_animator != null) _animator.enabled = _animatorWasEnabled;
        _ownsAnimatorSuspension = false;
        _hasState = false;
    }

    private void RestoreVisualOffset()
    {
        if (_cachedVisual == null) return;
        _cachedVisual.localPosition = _visualPosition;
        _cachedVisual.localRotation = _visualRotation;
        _cachedVisual.localScale = _visualScale;
    }

    private void ResetPresentation()
    {
        RestoreAnimatorEvaluation();
        RestoreVisualOffset();
        _previousPosition = transform.position;
        _needsPositionSample = true;
        _smoothedWalkSpeed = 0f;
        PhaseNormalized = 0f;
        CurrentStateName = "Idle";
        _hasState = false;
        _dead = false;
        if (_animator == null) return;
        _animator.applyRootMotion = false;
        _animator.speed = 1f;
        if (CanWriteAnimator())
        {
            _animator.SetFloat(ChargePhaseHash, 0f);
            _animator.SetFloat(DashPhaseHash, 0f);
            _animator.SetFloat(RecoverPhaseHash, 0f);
            Show(IdleHash, "Idle", 0f);
        }
    }

    public void OnPoolSpawn()
    {
        CacheComponents();
        ResetPresentation();
    }

    public void OnPoolDespawn() => ResetPresentation();
}
