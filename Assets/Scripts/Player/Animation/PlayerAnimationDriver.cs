using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Presentation only: a manually evaluated controller, bounded procedural aim and recoil.
/// Weapons call EvaluatePoseForWeapons before their existing ticks and resolve aim again
/// from the updated muzzle. Confirmed shots queue skeletal recoil for the next pose;
/// existing cosmetic barrel recoil stays immediate without moving emission points.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(100)]
public sealed class PlayerAnimationDriver : MonoBehaviour
{
    [Header("Authored rig and gameplay")]
    [SerializeField] private Animator _animator;
    [SerializeField] private RuntimeAnimatorController _controllerAsset;
    [SerializeField] private PlayerMovement _movement;
    [SerializeField] private PlayerHealth _health;
    [SerializeField] private WeaponManager _weapons;
    [SerializeField] private ReticleAimProvider _aimProvider;
    [SerializeField] private WeaponPresentationController _presentation;
    private PlayerWearableSurfaceFollower _wearableFollower;
    [SerializeField] private Transform _chest;
    [SerializeField] private Transform _rightUpperArm;
    [SerializeField] private Transform _rightForearm;
    [SerializeField] private Transform _rightHand;
    [SerializeField] private Transform _leftUpperArm;
    [SerializeField] private Transform _leftForearm;
    [SerializeField] private Transform _leftHand;

    [Header("Locomotion")]
    [SerializeField, Min(0.1f), Tooltip("Metres per second represented by the full locomotion blend.")]
    private float _referenceMoveSpeed = 5f;
    [SerializeField, Min(0.1f), Tooltip("Metres per second represented by the authored sideways and backward run strides.")]
    private float _referenceSideAndBackwardSpeed = 4.4f;
    [SerializeField, Min(0.01f)] private float _locomotionPlayback = 1f;
    [SerializeField, Min(0f)] private float _velocityBlendTime = 0.08f;
    [SerializeField, Min(0f)] private float _stateBlendTime = 0.09f;
    [SerializeField, Min(0f)] private float _jumpPoseTime = 0.2f;
    [SerializeField, Min(0f)] private float _landingPoseTime = 0.22f;

    [Header("Upper body")]
    [SerializeField, Range(0f, 1f)] private float _upperBodyWeight = 1f;
    [SerializeField, Min(0f)] private float _actionBlendTime = 0.045f;
    [SerializeField, Min(0.01f)] private float _firePoseTime = 0.28f;
    [SerializeField, Min(0.01f)] private float _slashPoseTime = 0.55f;
    [SerializeField, Min(0.01f)] private float _hitPoseTime = 0.34f;

    [Header("Aim limits and body turn")]
    [SerializeField, Range(0f, 1f)] private float _aimWeight = 1f;
    [SerializeField, Range(0f, 1f)] private float _supportArmWeight = 0.85f;
    [SerializeField, Range(0f, 80f)] private float _chestYawLimit = 40f;
    [SerializeField, Range(0f, 45f)] private float _chestPitchLimit = 22f;
    [SerializeField, Range(0f, 110f)] private float _armYawLimit = 95f;
    [SerializeField, Range(0f, 85f)] private float _aimUpLimit = 70f;
    [SerializeField, Range(0f, 85f)] private float _aimDownLimit = 60f;
    [SerializeField, Range(0f, 150f)] private float _armCorrectionLimit = 110f;
    [SerializeField, Range(10f, 100f)] private float _bodyTurnThreshold = 60f;
    [SerializeField, Min(0.01f)] private float _aimSmoothTime = 0.055f;
    [SerializeField, Min(0f)] private float _bodyTurnHoldTime = 0.1f;

    [Header("Confirmed-shot recoil")]
    [SerializeField, Range(0f, 15f)] private float _recoilDegrees = 3f;
    [SerializeField, Range(0f, 20f)] private float _maximumRecoil = 7f;
    [SerializeField, Min(0.01f)] private float _recoilRecovery = 18f;

    private const int BaseLayer = 0;
    private const int UpperLayer = 1;
    private const int FullBodyLayer = 2;
    private PlayableGraph _graph;
    private AnimatorControllerPlayable _controller;
    private bool _savedRootMotion;
    private AnimatorCullingMode _savedCullingMode;
    private bool _subscribed;
    private bool _dead;
    private bool _sustained;
    private bool _charging;
    private bool _hasAimedPose;
    private bool _hasManualOverride;
    private bool _restartAction;
    private bool _firstPose = true;
    private int _lastEvaluatedFrame = -1;
    private int _lastHealth;
    private int _baseState;
    private int _upperState;
    private int _jumpState;
    private int _actionState;
    private float _jumpRemaining;
    private float _landRemaining;
    private float _actionRemaining;
    private float _hitRemaining;
    private float _aimYaw;
    private float _aimPitch;
    private float _recoil;
    private float _layerWeight;
    private float _runFollowThroughBlend;
    private float _armedRunPoseWeight;
    private Transform _neck;
    private Transform _head;
    private Transform _rightShoulder;
    private Transform _leftShoulder;
    private Transform _hips;
    private Transform _leftThigh;
    private Transform _rightThigh;
    private Transform _leftShin;
    private Transform _rightShin;
    private bool _hasDashDirection;
    private float _dashDirectionYaw;
    private float _forwardDashDirectionYaw;
    private bool _backwardDash;
    private Vector2 _moveBlend;
    private WeaponInstance _manualWeapon;
    private WeaponInstance _manualOverride;
    private WeaponInstance _sustainedWeapon;
    private Quaternion _aimedRightRotation;
    private Quaternion _aimedLeftRotation;

    private static readonly int MoveX = Animator.StringToHash("MoveX");
    private static readonly int MoveY = Animator.StringToHash("MoveY");
    private static readonly int Speed = Animator.StringToHash("Speed");
    private static readonly int LocomotionRate = Animator.StringToHash("LocomotionRate");
    private static readonly int Locomotion = Animator.StringToHash("Base Layer.Locomotion");
    private static readonly int Crouch = Animator.StringToHash("Base Layer.Crouch");
    private static readonly int Jump = Animator.StringToHash("Base Layer.Jump");
    private static readonly int AirJump = Animator.StringToHash("Base Layer.AirJump");
    private static readonly int Fall = Animator.StringToHash("Base Layer.Fall");
    private static readonly int Land = Animator.StringToHash("Base Layer.Land");
    private static readonly int Slide = Animator.StringToHash("Base Layer.Slide");
    private static readonly int Dash = Animator.StringToHash("Base Layer.Dash");
    private static readonly int DashBackward = Animator.StringToHash("Base Layer.DashBackward");
    private static readonly int Stun = Animator.StringToHash("Base Layer.Stun");
    private static readonly int Aim = Animator.StringToHash("Upper Body.Aim");
    private static readonly int Fire = Animator.StringToHash("Upper Body.Fire");
    private static readonly int Slash = Animator.StringToHash("Upper Body.Slash");
    private static readonly int Flame = Animator.StringToHash("Upper Body.Flame");
    private static readonly int Hit = Animator.StringToHash("Upper Body.Hit");
    private static readonly int Death = Animator.StringToHash("Full Body.Death");
    private static readonly int Empty = Animator.StringToHash("Full Body.Empty");

    public bool IsDeathPlaying => _dead;
    public int LastEvaluatedFrame => _lastEvaluatedFrame;
    public int GraphEvaluationCount { get; private set; }
    public float CurrentAimYaw => _aimYaw;
    public Animator RigAnimator => _animator;

    // The sandbox owns a different inventory from the disabled gameplay manager on
    // its player prefab. Supplying that runtime avoids reading the inactive inventory.
    public void SetManualWeaponOverride(WeaponInstance weapon)
    {
        _hasManualOverride = true;
        _manualOverride = weapon;
    }

    public void ClearManualWeaponOverride()
    {
        _hasManualOverride = false;
        _manualOverride = null;
    }

    public void Configure(Animator animator, Transform chest,
        Transform rightUpperArm, Transform rightForearm, Transform rightHand,
        Transform leftUpperArm, Transform leftForearm, Transform leftHand)
    {
        Unsubscribe();
        DestroyGraph();
        _animator = animator;
        _controllerAsset = animator != null ? animator.runtimeAnimatorController : null;
        _chest = chest;
        _rightUpperArm = rightUpperArm;
        _rightForearm = rightForearm;
        _rightHand = rightHand;
        _leftUpperArm = leftUpperArm;
        _leftForearm = leftForearm;
        _leftHand = leftHand;
        ResolveReferences();
        if (Application.isPlaying && isActiveAndEnabled)
        {
            Subscribe();
            ResetPresentation();
        }
    }

    private void OnEnable()
    {
        ResolveReferences();
        Subscribe();
        ResetPresentation();
    }

    private void OnDisable()
    {
        Unsubscribe();
        DestroyGraph();
    }

    private void ResolveReferences()
    {
        _wearableFollower = GetComponent<PlayerWearableSurfaceFollower>();
        if (_animator == null) _animator = GetComponentInChildren<Animator>(true);
        if (_controllerAsset == null && _animator != null) _controllerAsset = _animator.runtimeAnimatorController;
        if (_movement == null) _movement = GetComponent<PlayerMovement>();
        if (_health == null) _health = GetComponent<PlayerHealth>();
        if (_weapons == null) _weapons = GetComponent<WeaponManager>();
        if (_aimProvider == null) _aimProvider = GetComponent<ReticleAimProvider>();
        if (_presentation == null) _presentation = GetComponent<WeaponPresentationController>();
        // These are presentation bones of the authored rig, not extra gameplay
        // attachment points. Resolve once rather than searching during each pose.
        _neck = _head = _rightShoulder = _leftShoulder = null;
        _hips = _leftThigh = _rightThigh = _leftShin = _rightShin = null;
        if (_animator != null)
        {
            foreach (Transform bone in _animator.GetComponentsInChildren<Transform>(true))
            {
                switch (bone.name)
                {
                    case "spine": _hips = bone; break;
                    case "thigh.L": _leftThigh = bone; break;
                    case "thigh.R": _rightThigh = bone; break;
                    case "shin.L": _leftShin = bone; break;
                    case "shin.R": _rightShin = bone; break;
                    case "spine.004": _neck = bone; break;
                    case "spine.006": _head = bone; break;
                    case "shoulder.R": _rightShoulder = bone; break;
                    case "shoulder.L": _leftShoulder = bone; break;
                }
            }
        }
    }

    private void Subscribe()
    {
        if (_subscribed) return;
        if (_movement != null)
        {
            _movement.OnJump += HandleJump;
            _movement.OnAirJump += HandleAirJump;
            _movement.OnLanded += HandleLanded;
        }
        if (_health != null)
        {
            _health.OnHealthChanged += HandleHealthChanged;
            _health.OnPlayerDied += HandleDeath;
        }
        if (_presentation != null) _presentation.FeedbackEmitted += HandleFeedback;
        _subscribed = true;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        if (_movement != null)
        {
            _movement.OnJump -= HandleJump;
            _movement.OnAirJump -= HandleAirJump;
            _movement.OnLanded -= HandleLanded;
        }
        if (_health != null)
        {
            _health.OnHealthChanged -= HandleHealthChanged;
            _health.OnPlayerDied -= HandleDeath;
        }
        if (_presentation != null) _presentation.FeedbackEmitted -= HandleFeedback;
        _subscribed = false;
    }

    private bool EnsureGraph()
    {
        if (_graph.IsValid()) return true;
        if (_animator == null || _controllerAsset == null) return false;
        _savedRootMotion = _animator.applyRootMotion;
        _savedCullingMode = _animator.cullingMode;
        _animator.applyRootMotion = false;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        // The playable owns the controller. Removing the ordinary controller prevents
        // Unity's normal animation pass from advancing or overwriting this manual pose.
        _animator.runtimeAnimatorController = null;
        _graph = PlayableGraph.Create("Player placeholder animation");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        _controller = AnimatorControllerPlayable.Create(_graph, _controllerAsset);
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "Player rig", _animator);
        output.SetSourcePlayable(_controller);
        _graph.Play();
        _controller.Play(Locomotion, BaseLayer, 0f);
        _controller.Play(Aim, UpperLayer, 0f);
        _controller.Play(_dead ? Death : Empty, FullBodyLayer, 0f);
        _controller.SetLayerWeight(UpperLayer, 0f);
        _controller.SetLayerWeight(FullBodyLayer, _dead ? 1f : 0f);
        _baseState = Locomotion;
        _upperState = Aim;
        return true;
    }

    private void DestroyGraph()
    {
        if (!_graph.IsValid()) return;
        _graph.Destroy();
        if (_animator != null)
        {
            _animator.runtimeAnimatorController = _controllerAsset;
            _animator.applyRootMotion = _savedRootMotion;
            _animator.cullingMode = _savedCullingMode;
        }
        _hasAimedPose = false;
    }

    /// <summary>Reset cosmetic state after a health reset or re-enabling the player.</summary>
    public void ResetPresentation()
    {
        _dead = _health != null && !_health.IsAlive;
        _lastHealth = _health != null ? _health.CurrentHealth : 0;
        _jumpRemaining = _landRemaining = _actionRemaining = _hitRemaining = 0f;
        _aimYaw = _aimPitch = _recoil = _layerWeight = 0f;
        _runFollowThroughBlend = _armedRunPoseWeight = 0f;
        _hasDashDirection = false;
        _backwardDash = false;
        _dashDirectionYaw = _forwardDashDirectionYaw = 0f;
        _moveBlend = Vector2.zero;
        _manualWeapon = _sustainedWeapon = null;
        _sustained = _charging = _hasAimedPose = false;
        _restartAction = false;
        _firstPose = true;
        _lastEvaluatedFrame = -1;
        DestroyGraph();
    }

    /// <summary>Exactly one time-advancing pose evaluation before weapon gameplay.</summary>
    public void EvaluatePoseForWeapons(float deltaTime, Vector3 aimWorldPoint)
    {
        if (!isActiveAndEnabled || _lastEvaluatedFrame == Time.frameCount || !EnsureGraph()) return;
        _lastEvaluatedFrame = Time.frameCount;
        float dt = Mathf.Max(0f, _dead ? Time.unscaledDeltaTime : deltaTime);
        _hasAimedPose = false;
        if (!_dead)
        {
            UpdateLocomotion(dt);
            UpdateUpperBody(dt);
        }
        // Death alone advances during the immediate game-over pause.
        _graph.Evaluate(dt);
        GraphEvaluationCount++;
        if (!_dead)
        {
            ApplyDirectionalDashPose(dt);
            ApplyArmedRunFollowThrough(dt);
            ApplyAim(aimWorldPoint, dt);
            _recoil = Mathf.MoveTowards(_recoil, 0f, _recoilRecovery * dt);
            CacheAimedPose();
            ApplyRecoil();
        }
        // Rigid artifacts follow the final skinned-body pose before any weapon
        // consumes their muzzle positions, including paused death playback.
        _wearableFollower?.EvaluateAttachments();
        _firstPose = false;
    }

    private void LateUpdate()
    {
        if (_lastEvaluatedFrame == Time.frameCount) return;
        Vector3 origin = _rightHand != null ? _rightHand.position : transform.position + Vector3.up;
        AimSolution solution = _weapons != null ? _weapons.CurrentAimSolution : default;
        if (!solution.IsValid && _aimProvider != null)
            solution = _aimProvider.ResolveWeaponAim(origin, _weapons != null ? _weapons.GetCurrentManualWeapon() : null);
        Vector3 target = solution.IsValid ? solution.TargetPoint : origin + transform.forward * 30f;
        EvaluatePoseForWeapons(Time.deltaTime, target);
    }

    private void UpdateLocomotion(float dt)
    {
        UpdateDashDirection(dt);
        Vector3 velocity = _movement != null ? _movement.CurrentVelocity : Vector3.zero;
        Vector3 localVelocity = transform.InverseTransformDirection(velocity);
        float planarSpeed = new Vector2(localVelocity.x, localVelocity.z).magnitude;
        Vector2 desired = Vector2.ClampMagnitude(new Vector2(localVelocity.x, localVelocity.z) / _referenceMoveSpeed, 1f);
        _moveBlend = Vector2.Lerp(_moveBlend, desired, BlendFactor(dt, _velocityBlendTime));
        _controller.SetFloat(MoveX, _moveBlend.x);
        _controller.SetFloat(MoveY, _moveBlend.y);
        _controller.SetFloat(Speed, _moveBlend.magnitude);
        // Idle shares the movement blend tree, so retain its authored breathing
        // cadence at rest and gradually introduce speed-based gait playback.
        float directionalWeight = (Mathf.Abs(localVelocity.x) + Mathf.Max(0f, -localVelocity.z)) /
            Mathf.Max(0.001f, Mathf.Abs(localVelocity.x) + Mathf.Abs(localVelocity.z));
        float strideSpeed = Mathf.Lerp(_referenceMoveSpeed, _referenceSideAndBackwardSpeed, directionalWeight);
        float movingRate = Mathf.Clamp(planarSpeed / Mathf.Max(0.1f, strideSpeed), 0.35f, 2.5f);
        float movementWeight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_moveBlend.magnitude / 0.5f));
        _controller.SetFloat(LocomotionRate, _locomotionPlayback * Mathf.Lerp(1f, movingRate, movementWeight));

        int state = Locomotion;
        if (_movement != null)
        {
            if (_movement.IsStunned) state = Stun;
            else if (_movement.IsDashing) state = _backwardDash ? DashBackward : Dash;
            else if (_movement.IsSliding) state = Slide;
            else if (_jumpRemaining > 0f) state = _jumpState;
            else if (!_movement.IsGroundedOnSurface) state = Fall;
            else if (_landRemaining > 0f && planarSpeed < 0.5f) state = Land;
            else if (_movement.IsCrouching) state = Crouch;
        }
        SelectState(state, BaseLayer, ref _baseState, _stateBlendTime);
        _jumpRemaining = Mathf.Max(0f, _jumpRemaining - dt);
        _landRemaining = Mathf.Max(0f, _landRemaining - dt);
    }

    private void UpdateUpperBody(float dt)
    {
        WeaponInstance manual = _hasManualOverride ? _manualOverride :
            _weapons != null ? _weapons.GetCurrentManualWeapon() : _manualWeapon;
        if (_manualWeapon != manual || (manual != null && manual.State != WeaponState.Manual))
        {
            _sustained = _charging = false;
            _actionRemaining = 0f;
            _sustainedWeapon = null;
        }
        _manualWeapon = manual;
        bool manualActive = manual != null && manual.State == WeaponState.Manual;
        int state = _hitRemaining > 0f ? Hit : _sustained ? Flame : _actionRemaining > 0f ? _actionState : Aim;
        if (_restartAction && state == _actionState && state == _upperState)
            _controller.CrossFadeInFixedTime(state, _actionBlendTime, UpperLayer, 0f);
        _restartAction = false;
        SelectState(state, UpperLayer, ref _upperState, _actionBlendTime);
        float targetWeight = manualActive || _hitRemaining > 0f || _actionRemaining > 0f || _charging ? _upperBodyWeight : 0f;
        if (_movement != null && _movement.IsStunned) targetWeight *= 0.4f;
        _layerWeight = _firstPose ? targetWeight : Mathf.Lerp(_layerWeight, targetWeight, BlendFactor(dt, _actionBlendTime));
        _controller.SetLayerWeight(UpperLayer, _layerWeight);
        _actionRemaining = Mathf.Max(0f, _actionRemaining - dt);
        _hitRemaining = Mathf.Max(0f, _hitRemaining - dt);
    }

    private void SelectState(int state, int layer, ref int current, float blendTime)
    {
        if (state == current) return;
        _controller.CrossFadeInFixedTime(state, blendTime, layer, 0f);
        current = state;
    }

    private void UpdateDashDirection(float dt)
    {
        if (_movement != null && _movement.IsDashing && !_movement.IsStunned)
        {
            Vector3 direction = _movement.CurrentDashDirectionWorld;
            if (direction.sqrMagnitude > .0001f && (dt > 0f || !_hasDashDirection))
            {
                float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
                Vector3 local = transform.InverseTransformDirection(direction);
                // The rear step has its own support-foot timing and upright
                // posture. A little hysteresis prevents state flicker near side.
                bool backward = _backwardDash ? local.z < -.2f : local.z < -.35f;
                _dashDirectionYaw = !_hasDashDirection || backward != _backwardDash ? yaw :
                    Mathf.MoveTowardsAngle(_dashDirectionYaw, yaw, 1080f * dt);
                _hasDashDirection = true;
                _backwardDash = backward;
                if (!_backwardDash) _forwardDashDirectionYaw = _dashDirectionYaw;
            }
        }
    }

    private void ApplyDirectionalDashPose(float dt)
    {
        if (_movement == null || _hips == null || _chest == null) return;
        float weight = StateContribution(BaseLayer, Dash, out _);
        float backwardWeight = StateContribution(BaseLayer, DashBackward, out _);
        if (!_movement.IsDashing && weight + backwardWeight <= .0001f)
        {
            _hasDashDirection = false;
            _backwardDash = false;
        }
        if (!_hasDashDirection || weight <= .0001f) return;

        // Only the old forward/side Dash receives procedural redirection. Keep
        // its last non-rear direction through transitions to the authored backstep.
        // In particular, never mirror knees or reverse the backstep's lean.
        Vector3 localDirection = DashDirectionInPlayerSpace(_forwardDashDirectionYaw);
        localDirection.z = Mathf.Max(0f, localDirection.z);
        if (localDirection.sqrMagnitude > .0001f) RedirectDashPose(localDirection.normalized, weight);
    }

    private Vector3 DashDirectionInPlayerSpace(float yaw)
    {
        Vector3 worldDirection = Quaternion.AngleAxis(yaw, Vector3.up) * Vector3.forward;
        Vector3 localDirection = transform.InverseTransformDirection(worldDirection);
        localDirection.y = 0f;
        return localDirection.normalized;
    }

    private void RedirectDashPose(Vector3 localDirection, float weight)
    {
        // Rear travel uses DashBackward, including its original knee bends.
        if (localDirection.z < -.35f) return;
        Quaternion leftRotation = _leftThigh != null ? _leftThigh.rotation : Quaternion.identity;
        Quaternion rightRotation = _rightThigh != null ? _rightThigh.rotation : Quaternion.identity;
        Vector3 leftReach = LegDirection(_leftThigh, _leftShin);
        Vector3 rightReach = LegDirection(_rightThigh, _rightShin);
        Vector3 torso = transform.InverseTransformDirection(_chest.position - _hips.position);
        Vector3 redirectedTorso = new Vector3(torso.x + torso.z * localDirection.x,
            torso.y, torso.z * localDirection.z);
        if (torso.sqrMagnitude > .0001f && redirectedTorso.sqrMagnitude > .0001f)
        {
            Quaternion redirect = Quaternion.FromToRotation(transform.TransformDirection(torso),
                transform.TransformDirection(redirectedTorso));
            _hips.rotation = Quaternion.Slerp(Quaternion.identity, redirect, weight) * _hips.rotation;
        }

        // Redirect the upper-leg reach, preserving each authored knee bend and
        // ankle rotation. Lateral dashes open the stance instead of crossing the
        // knees; the foot on the travel side becomes the leading foot.
        RedirectLeg(_leftThigh, leftRotation, leftReach, localDirection, -1f, weight);
        RedirectLeg(_rightThigh, rightRotation, rightReach, localDirection, 1f, weight);
    }

    private Vector3 LegDirection(Transform thigh, Transform shin) => thigh != null && shin != null
        ? transform.InverseTransformDirection(shin.position - thigh.position) : Vector3.zero;

    private void RedirectLeg(Transform thigh, Quaternion authoredRotation, Vector3 reach,
        Vector3 direction, float side, float weight)
    {
        if (thigh == null || reach.sqrMagnitude < .0001f) return;
        Vector3 redirected = new Vector3(reach.x + side * Mathf.Abs(reach.z) * Mathf.Abs(direction.x),
            reach.y, reach.z * direction.z);
        Quaternion correction = Quaternion.FromToRotation(transform.TransformDirection(reach),
            transform.TransformDirection(redirected));
        // Restore the leg's world rotation from before the torso redirect, so
        // changing the upper-body lean cannot rotate the feet a second time.
        thigh.rotation = Quaternion.Slerp(Quaternion.identity, correction, weight) * authoredRotation;
    }

    private void ApplyArmedRunFollowThrough(float dt)
    {
        float locomotionWeight = StateContribution(BaseLayer, Locomotion, out float phase);
        float aimWeight = StateContribution(UpperLayer, Aim, out _);
        bool ordinaryRun = _baseState == Locomotion && _upperState == Aim && !_charging;
        float target = ordinaryRun ? Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_moveBlend.magnitude)) : 0f;
        _runFollowThroughBlend = _firstPose ? target :
            Mathf.Lerp(_runFollowThroughBlend, target, BlendFactor(dt, 0.12f));
        // The authored unarmed run already contains arm swing. This restores only
        // the movement that the armed Aim override would otherwise replace.
        _armedRunPoseWeight = _charging ? 0f :
            _runFollowThroughBlend * _layerWeight * locomotionWeight * aimWeight;
        if (_armedRunPoseWeight <= 0.001f) return;

        float cycle = Mathf.Repeat(phase, 1f) * Mathf.PI * 2f;
        float weight = _armedRunPoseWeight;
        float backwardShare = Mathf.Max(0f, -_moveBlend.y) /
            Mathf.Max(0.001f, Mathf.Abs(_moveBlend.x) + Mathf.Abs(_moveBlend.y));
        float armDirection = Mathf.Lerp(1f, -1f, backwardShare);
        float Swing(float lag) => Mathf.Cos(cycle - lag * Mathf.PI * 2f);
        float Impact(float lag) => Mathf.Sin(2f * cycle - lag * Mathf.PI * 2f);
        void Rotate(Transform bone, Vector3 axis, float angle)
        {
            if (bone != null) bone.rotation = Quaternion.AngleAxis(angle * weight, axis) * bone.rotation;
        }

        // Chest, shoulders and head trail each other slightly instead of moving
        // as one block. World axes avoid depending on the FBX bone-axis mapping.
        Rotate(_chest, transform.up, -3.5f * Swing(0.10f));
        Rotate(_chest, transform.forward, 1.25f * Mathf.Sin(cycle - 0.13f * Mathf.PI * 2f));
        Rotate(_chest, transform.right, 1.15f * Impact(0.12f));
        Rotate(_rightShoulder, transform.right, -1.5f * armDirection * Swing(0.08f));
        Rotate(_leftShoulder, transform.right, 2.5f * armDirection * Swing(0.10f));
        Rotate(_neck, transform.right, -0.9f * Impact(0.17f));
        Rotate(_head, transform.up, 1.5f * Swing(0.18f));
        Rotate(_head, transform.right, -0.6f * Impact(0.22f));

        // The supporting elbow travels behind the torso on the backswing, then
        // folds as it comes forward. The robotic firing arm keeps full target aim.
        Rotate(_leftUpperArm, transform.right, 55f + 20f * armDirection * Swing(0.08f));
        Rotate(_leftUpperArm, transform.forward, -4f);
        Rotate(_leftForearm, transform.right, -17.5f + 13.5f * armDirection * Swing(0.09f));
    }

    private float StateContribution(int layer, int state, out float normalizedTime)
    {
        AnimatorStateInfo current = _controller.GetCurrentAnimatorStateInfo(layer);
        normalizedTime = current.normalizedTime;
        float weight = current.fullPathHash == state ? 1f : 0f;
        if (!_controller.IsInTransition(layer)) return weight;
        AnimatorStateInfo next = _controller.GetNextAnimatorStateInfo(layer);
        float blend = Mathf.Clamp01(_controller.GetAnimatorTransitionInfo(layer).normalizedTime);
        weight *= 1f - blend;
        if (next.fullPathHash == state)
        {
            weight += blend;
            normalizedTime = next.normalizedTime;
        }
        return weight;
    }

    private void ApplyAim(Vector3 target, float dt)
    {
        if (_chest == null || _layerWeight <= 0.001f) return;
        Vector3 origin = _rightHand != null ? _rightHand.position : _chest.position;
        Vector3 direction = target - origin;
        if (direction.sqrMagnitude < 0.0001f) return;
        Vector3 local = transform.InverseTransformDirection(direction.normalized);
        float rawYaw = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
        float desiredYaw = ResolveLimitedYaw(rawYaw, _aimYaw, _armYawLimit);
        float desiredPitch = Mathf.Clamp(Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg,
            -_aimDownLimit, _aimUpLimit);
        float blend = _firstPose ? 1f : BlendFactor(dt, _aimSmoothTime);
        _aimYaw = Mathf.Lerp(_aimYaw, desiredYaw, blend);
        _aimPitch = Mathf.Lerp(_aimPitch, desiredPitch, blend);

        if (Mathf.Abs(rawYaw) > _bodyTurnThreshold && _movement != null &&
            !_movement.IsSliding && !_movement.IsDashing && !_movement.IsStunned && dt > 0f)
            _movement.RequestAimFacing(direction, _bodyTurnHoldTime);

        float weight = _aimWeight * _layerWeight;
        if (_upperState == Slash) weight *= 0.25f;
        if (_upperState == Hit) weight *= 0.4f;
        float chestYaw = Mathf.Clamp(_aimYaw * 0.5f, -_chestYawLimit, _chestYawLimit);
        float chestPitch = Mathf.Clamp(_aimPitch * 0.4f, -_chestPitchLimit, _chestPitchLimit);
        Quaternion yaw = Quaternion.AngleAxis(chestYaw * weight, transform.up);
        Quaternion pitch = Quaternion.AngleAxis(-chestPitch * weight, yaw * transform.right);
        _chest.rotation = pitch * yaw * _chest.rotation;
        Vector3 boundedDirection = transform.rotation * Quaternion.Euler(-_aimPitch, _aimYaw, 0f) * Vector3.forward;
        Vector3 boundedTarget = origin + boundedDirection * direction.magnitude;
        AimArm(_rightUpperArm, _rightForearm, _rightHand, boundedTarget, weight);
        float supportWeight = _supportArmWeight * Mathf.Lerp(1f, 0.12f, _armedRunPoseWeight);
        AimArm(_leftUpperArm, _leftForearm, _leftHand, boundedTarget, weight * supportWeight);
    }

    private void AimArm(Transform upperArm, Transform forearm, Transform hand, Vector3 target, float weight)
    {
        if (upperArm == null || forearm == null || hand == null) return;
        // Bone child positions work across FBX bone-axis conversions and preserve the
        // authored elbow bend. No Humanoid mapping, finger bones or IK package required.
        Quaternion animatedRotation = upperArm.rotation;
        for (int iteration = 0; iteration < 2; iteration++)
        {
            Vector3 forearmDirection = hand.position - forearm.position;
            Vector3 targetDirection = target - hand.position;
            if (forearmDirection.sqrMagnitude < 0.0001f || targetDirection.sqrMagnitude < 0.0001f) break;
            Quaternion correction = Quaternion.FromToRotation(forearmDirection, targetDirection);
            upperArm.rotation = Quaternion.RotateTowards(animatedRotation,
                correction * upperArm.rotation, _armCorrectionLimit);
        }
        upperArm.rotation = Quaternion.Slerp(animatedRotation, upperArm.rotation, weight);
    }

    /// <summary>Keep the selected side while aim crosses the rear -180/+180 seam.</summary>
    public static float ResolveLimitedYaw(float rawYaw, float previousYaw, float limit)
    {
        rawYaw = Mathf.DeltaAngle(0f, rawYaw);
        if (Mathf.Abs(rawYaw) > 120f && Mathf.Abs(previousYaw) > 1f && rawYaw * previousYaw < 0f)
            return Mathf.Sign(previousYaw) * limit;
        return Mathf.Clamp(rawYaw, -limit, limit);
    }

    private static float BlendFactor(float deltaTime, float smoothTime) =>
        smoothTime <= 0f ? 1f : 1f - Mathf.Exp(-deltaTime / smoothTime);

    private void CacheAimedPose()
    {
        if (_rightUpperArm != null) _aimedRightRotation = _rightUpperArm.localRotation;
        if (_leftUpperArm != null) _aimedLeftRotation = _leftUpperArm.localRotation;
        _hasAimedPose = true;
    }

    private void ApplyRecoil()
    {
        if (!_hasAimedPose || _dead) return;
        ApplyArmRecoil(_rightUpperArm, _aimedRightRotation, _recoil);
        ApplyArmRecoil(_leftUpperArm, _aimedLeftRotation, _recoil * _supportArmWeight);
    }

    private void ApplyArmRecoil(Transform upperArm, Quaternion aimedLocalRotation, float degrees)
    {
        if (upperArm == null) return;
        upperArm.localRotation = aimedLocalRotation;
        upperArm.rotation = Quaternion.AngleAxis(-degrees, transform.right) * upperArm.rotation;
    }

    private void HandleJump() { _jumpState = Jump; _jumpRemaining = _jumpPoseTime; _landRemaining = 0f; }
    private void HandleAirJump() { _jumpState = AirJump; _jumpRemaining = _jumpPoseTime; _landRemaining = 0f; }
    private void HandleLanded() { _landRemaining = _landingPoseTime; _jumpRemaining = 0f; }

    private void HandleHealthChanged()
    {
        if (_health == null) return;
        if (_dead && _health.IsAlive) ResetPresentation();
        if (_health.CurrentHealth < _lastHealth && _health.IsAlive) _hitRemaining = _hitPoseTime;
        _lastHealth = _health.CurrentHealth;
    }

    private void HandleDeath()
    {
        _dead = true;
        _sustained = _charging = false;
        _hasAimedPose = false;
        _recoil = 0f;
        if (!EnsureGraph()) return;
        _controller.SetLayerWeight(UpperLayer, 0f);
        _controller.SetLayerWeight(FullBodyLayer, 1f);
        _controller.Play(Death, FullBodyLayer, 0f);
    }

    private void HandleFeedback(WeaponFeedbackEvent feedbackEvent, WeaponFeedbackContext context)
    {
        if (_dead || context.Mode == WeaponFeedbackMode.Automatic) return;
        switch (feedbackEvent)
        {
            case WeaponFeedbackEvent.ShotFired:
                _manualWeapon = context.Weapon;
                _charging = false;
                _actionState = context.WeaponType == WeaponType.RotatingBlade ? Slash : Fire;
                _actionRemaining = _actionState == Slash ? _slashPoseTime : _firePoseTime;
                _restartAction = true;
                // Queue bone recoil until the next pre-weapon pose. Moving the arms
                // here would change a muzzle halfway through a burst and move the
                // support-forearm flame socket after its automatic tick already ran.
                float scale = _presentation != null && _presentation.RuntimeOptions.ReducedMotion ? 0.2f : 1f;
                _recoil = Mathf.Min(_maximumRecoil, _recoil + _recoilDegrees * context.EventIntensity * scale);
                break;
            case WeaponFeedbackEvent.SustainedFireStarted:
                _manualWeapon = _sustainedWeapon = context.Weapon;
                _sustained = true;
                break;
            case WeaponFeedbackEvent.SustainedFireStopped:
                if (_sustainedWeapon == context.Weapon) _sustained = false;
                break;
            case WeaponFeedbackEvent.ChargeStarted:
                _manualWeapon = context.Weapon;
                _charging = true;
                break;
            case WeaponFeedbackEvent.ChargeCancelled:
            case WeaponFeedbackEvent.AmmoEmpty:
                _charging = _sustained = false;
                break;
        }
    }
}
