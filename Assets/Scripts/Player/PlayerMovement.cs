using System;
using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PlayerStats))]
public class PlayerMovement : MonoBehaviour
{
    private static PlayerMovement s_Instance;

    public static Transform PlayerTransform => s_Instance != null ? s_Instance.transform : null;

    public event Action OnJump;
    public event Action OnAirJump;
    public event Action OnLanded;
    public event Action OnCrouchStarted;
    public event Action OnCrouchEnded;
    public event Action OnSlideStarted;
    public event Action OnSlideEnded;
    public event Action OnDashStarted;
    public event Action OnDashEnded;
    public event Action<int, int> OnDashChargesChanged;
    public event Action OnStunned;

    [SerializeField] private Transform _cameraTransform;
    [SerializeField] private float _rotationSpeed = 540f;
    [SerializeField, Min(0f), Tooltip("Degrees per second while the body is aligning to reticle aim. Zero uses movement rotation speed.")]
    private float _aimFacingRotationSpeed = 720f;

    [SerializeField, Min(0.1f)] private float _baseMoveAcceleration = 38f;
    [SerializeField, Min(0.1f)] private float _baseFriction = 9f;
    [SerializeField, Min(0f)] private float _groundCheckExtraDistance = 0.08f;
    [SerializeField] private LayerMask _groundMask = ~0;
    [SerializeField, Min(0f)] private float _airFrictionMultiplier = 0.2f;
    [SerializeField, Min(0f)] private float _crouchAccelerationMultiplier = 0.2f;
    [SerializeField, Min(0f)] private float _slideFrictionMultiplier = 0.1f;
    [SerializeField, Min(0.01f)] private float _dashDuration = 0.25f;
    [SerializeField, Min(0.01f)] private float _groundedDashRegenTime = 2f;
    [SerializeField, Min(0.01f)] private float _airborneDashRegenTime = 4f;
    [SerializeField, Min(1f)] private float _slideStartSpeedMultiplier = 1.5f;
    [SerializeField, Min(0f)] private float _minSlideSpeed = 2f;
    [SerializeField, Min(0f)] private float _postDashFrictionMultiplier = 0.15f;
    [SerializeField, Min(0f)] private float _postDashFrictionDuration = 0.18f;

    [Header("External effects (enemy hooks)")]
    [SerializeField, Min(0f), Tooltip("Tras un empuje, ventana sin speed-cap y con fricción reducida para que el impulso no se anule al instante.")]
    private float _knockbackWindow = 0.3f;
    [SerializeField, Range(0f, 1f), Tooltip("Multiplicador de fricción durante la ventana de empuje (0 = sin fricción).")]
    private float _knockbackFrictionMultiplier = 0.1f;

    [SerializeField, Tooltip("Applies a zero-friction physics material to the movement collider so wall contacts do not grip the player.")]
    private bool _useFrictionlessMovementMaterial = true;

    private Rigidbody _rb;
    private PlayerStats _stats;
    private Collider _ownCollider;
    private PhysicsMaterial _runtimeFrictionlessMaterial;

    private Vector2 _moveInput;
    private Vector3 _moveDirectionWorld;
    private Vector3 _slideDirectionWorld;
    private bool _jumpPressed;
    private bool _crouchHeld;
    private bool _crouchPressed;
    private bool _crouchReleased;
    private bool _dashPressed;

    private bool _isGrounded;
    private bool _wasGrounded;
    private bool _isCrouching;
    private bool _isSliding;
    private bool _isDashing;

    private float _dashTimer;
    private float _dashRegenTimer;
    private float _postDashFrictionTimer;
    private float _knockbackTimer;
    private float _stunTimer;
    private float _aimFacingTimer;
    private int _remainingAirJumps;
    private int _currentDashCharges;
    private Vector3 _aimFacingDirection;

    // Exposes camera-relative movement direction for weapons that aim from movement input.
    public Vector3 CurrentMoveDirectionWorld => _moveDirectionWorld;

    /// <summary>El jugador está aturdido (input de movimiento/salto/dash bloqueado).</summary>
    public bool IsStunned => _stunTimer > 0f;

    public int CurrentDashCharges => _currentDashCharges;

    public int MaxDashCharges => _stats != null ? Mathf.Max(0, _stats.GetStatInt(StatType.DashCharges)) : 0;

    /// <summary>
    /// Empuje horizontal desde <paramref name="fromPoint"/> con la fuerza dada (Chaser/Shocker).
    /// Abre una ventana sin speed-cap y con fricción reducida para que el impulso se note.
    /// </summary>
    public void ApplyKnockback(Vector3 fromPoint, float force)
    {
        if (force <= 0f || _rb == null)
            return;

        Vector3 dir = transform.position - fromPoint;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
            dir = -transform.forward;
        dir.Normalize();

        _rb.AddForce(dir * (force * _rb.mass), ForceMode.Impulse);
        _knockbackTimer = Mathf.Max(_knockbackTimer, _knockbackWindow);
    }

    /// <summary>Aturde al jugador durante <paramref name="seconds"/> (Shocker). Refresca, no apila.</summary>
    public void ApplyStun(float seconds)
    {
        if (seconds <= 0f)
            return;

        bool wasStunned = _stunTimer > 0f;
        _stunTimer = Mathf.Max(_stunTimer, seconds);
        if (!wasStunned)
            OnStunned?.Invoke();
    }

    // Cache movement components and initialize singleton and physics defaults.
    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _stats = GetComponent<PlayerStats>();
        _ownCollider = GetComponent<Collider>() ?? GetComponentInChildren<Collider>();
        s_Instance = this;

        _rb.interpolation = RigidbodyInterpolation.Interpolate;
        _rb.freezeRotation = true; ConfigureMovementColliderMaterial();
    }

    // Give the player collider no surface friction so pushing into walls does not prevent sideways movement.
    private void ConfigureMovementColliderMaterial()
    {
        if (!_useFrictionlessMovementMaterial || _ownCollider == null || _ownCollider.isTrigger) return;

        _runtimeFrictionlessMaterial = new PhysicsMaterial("Player Frictionless Movement")
        {
            staticFriction = 0f,
            dynamicFriction = 0f,
            bounciness = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            bounceCombine = PhysicsMaterialCombine.Minimum
        };

        _ownCollider.material = _runtimeFrictionlessMaterial;
    }

    // Clear singleton when this movement instance is destroyed.
    private void OnDestroy()
    {
        if (s_Instance == this) s_Instance = null;
    }

    // Resolve camera reference and initialize jump and dash counters.
    private void Start()
    {
        if (_cameraTransform == null && Camera.main != null) _cameraTransform = Camera.main.transform;

        _isGrounded = IsGrounded();
        _wasGrounded = _isGrounded;
        _remainingAirJumps = Mathf.Max(0, _stats.GetStatInt(StatType.AirJumps));
        SyncDashChargesToStatMax();
    }

    // Read buffered player inputs and trigger stateful actions.
    private void Update()
    {
        if (_cameraTransform == null) return;

        ReadInput();

        if (!IsStunned)
        {
            if (_jumpPressed) TryJump();
            if (_crouchPressed) TryStartCrouchOrSlide();
            if (_crouchReleased) StopCrouchOrSlide();
            if (_dashPressed) TryDash();
        }

        _jumpPressed = false;
        _crouchPressed = false;
        _crouchReleased = false;
        _dashPressed = false;
    }

    // Run physics movement, friction, dash timer, and grounded transitions.
    private void FixedUpdate()
    {
        if (_rb == null || _cameraTransform == null) return;

        UpdateGroundedState();

        if (_isDashing)
        {
            HandleDashTimer();
        }
        else
        {
            HandleMovement();
            ApplyPlanarSpeedCap();
            HandleFriction();
        }

        TickPostDashFrictionWindow();
        TickKnockbackWindow();
        TickStun();
        TickAimFacingTimer();
        HandleDashRegeneration();
    }

    public void RequestAimFacing(Vector3 worldDirection, float duration)
    {
        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude <= 0.0001f)
            return;

        _aimFacingDirection = worldDirection.normalized;
        _aimFacingTimer = Mathf.Max(_aimFacingTimer, duration);
    }

    // Poll keyboard and build normalized camera-relative movement direction.
    private void ReadInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            _moveInput = Vector2.zero;
            return;
        }

        _moveInput = ReadWasd();
        if (_moveInput.sqrMagnitude > 1f) _moveInput.Normalize();

        Vector3 flatForward = FlattenOnXZ(_cameraTransform.forward);
        Vector3 flatRight = FlattenOnXZ(_cameraTransform.right);
        _moveDirectionWorld = flatForward * _moveInput.y + flatRight * _moveInput.x;
        if (_moveDirectionWorld.sqrMagnitude > 0.0001f) _moveDirectionWorld.Normalize();

        _jumpPressed = keyboard.spaceKey.wasPressedThisFrame;
        _dashPressed = keyboard.leftShiftKey.wasPressedThisFrame || keyboard.rightShiftKey.wasPressedThisFrame;

        bool crouchNowHeld = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
        _crouchPressed = crouchNowHeld && !_crouchHeld;
        _crouchReleased = !crouchNowHeld && _crouchHeld;
        _crouchHeld = crouchNowHeld;
    }

    // Apply acceleration force and rotate player while respecting movement state priorities.
    private void HandleMovement()
    {
        // Aturdido: sin rotación ni aceleración por input (la fricción sigue para frenar).
        if (IsStunned)
            return;

        Vector3 aimFacingDirection = Vector3.zero;
        bool useAimFacing = !_isSliding && TryGetAimFacingDirection(out aimFacingDirection);
        Vector3 facingDirection = _isSliding ? _slideDirectionWorld : useAimFacing ? aimFacingDirection : _moveDirectionWorld;
        if (facingDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(facingDirection);
            float rotationSpeed = useAimFacing && _aimFacingRotationSpeed > 0f ? _aimFacingRotationSpeed : _rotationSpeed;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotationSpeed * Time.fixedDeltaTime);
        }

        if (_isSliding) return;

        float acceleration = _baseMoveAcceleration;
        if (_isCrouching) acceleration *= _crouchAccelerationMultiplier;

        if (_moveDirectionWorld.sqrMagnitude > 0.0001f)
        {
            float maxSpeed = Mathf.Max(0.1f, _stats.GetMoveSpeed());
            float speedRatio = Mathf.Clamp01(CurrentPlanarSpeed() / maxSpeed);
            float speedScaledAcceleration = acceleration * Mathf.Lerp(1f, 0.35f, speedRatio);
            _rb.AddForce(_moveDirectionWorld * speedScaledAcceleration, ForceMode.Acceleration);
        }
    }

    // Apply velocity-opposing friction adjusted by grounded, slide, and dash states.
    private void HandleFriction()
    {
        Vector3 planarV = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
        if (planarV.sqrMagnitude <= 0.0001f) return;

        float friction = _baseFriction;
        if (!_isGrounded) friction *= _airFrictionMultiplier;
        if (_isSliding && _isGrounded) friction *= _slideFrictionMultiplier;
        if (_postDashFrictionTimer > 0f) friction *= _postDashFrictionMultiplier;
        if (_knockbackTimer > 0f) friction *= _knockbackFrictionMultiplier;

        Vector3 frictionDir = -planarV.normalized;
        _rb.AddForce(frictionDir * friction, ForceMode.Acceleration);
    }


    // Clamp normal horizontal velocity without crushing slide or dash momentum.
    private void ApplyPlanarSpeedCap()
    {
        if (_isSliding || _postDashFrictionTimer > 0f || _knockbackTimer > 0f) return;

        float maxSpeed = Mathf.Max(0.1f, _stats.GetMoveSpeed());
        Vector3 planarV = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
        float planarSpeed = planarV.magnitude;
        if (planarSpeed <= maxSpeed || planarSpeed <= 0.0001f) return;

        Vector3 cappedPlanar = planarV * (maxSpeed / planarSpeed);
        _rb.linearVelocity = new Vector3(cappedPlanar.x, _rb.linearVelocity.y, cappedPlanar.z);
    }

    // Decrease post-dash friction grace timer used to preserve dash momentum.
    private void TickPostDashFrictionWindow()
    {
        if (_postDashFrictionTimer <= 0f) return;
        _postDashFrictionTimer = Mathf.Max(0f, _postDashFrictionTimer - Time.fixedDeltaTime);
    }

    // Decrease the knockback grace window used to let an external impulse register.
    private void TickKnockbackWindow()
    {
        if (_knockbackTimer <= 0f) return;
        _knockbackTimer = Mathf.Max(0f, _knockbackTimer - Time.fixedDeltaTime);
    }

    // Decrease the stun timer that blocks movement/jump/dash input.
    private void TickStun()
    {
        if (_stunTimer <= 0f) return;
        _stunTimer = Mathf.Max(0f, _stunTimer - Time.fixedDeltaTime);
    }

    private void TickAimFacingTimer()
    {
        if (_aimFacingTimer <= 0f) return;
        _aimFacingTimer = Mathf.Max(0f, _aimFacingTimer - Time.fixedDeltaTime);
    }

    private bool TryGetAimFacingDirection(out Vector3 direction)
    {
        direction = Vector3.zero;
        if (_aimFacingTimer <= 0f || _aimFacingDirection.sqrMagnitude <= 0.0001f)
            return false;

        direction = _aimFacingDirection;
        return true;
    }
    // Attempt jump using ground status and remaining air jumps.
    private void TryJump()
    {
        if (_isGrounded)
        {
            PerformJump(false);
            return;
        }

        if (_remainingAirJumps > 0)
        {
            _remainingAirJumps--;
            PerformJump(true);
        }
    }

    // Execute jump impulse from jump height stat and clear descending velocity.
    private void PerformJump(bool isAirJump)
    {
        float jumpHeight = Mathf.Max(0.01f, _stats.GetStat(StatType.JumpHeight));
        float g = Mathf.Abs(Physics.gravity.y);
        float jumpSpeed = Mathf.Sqrt(2f * g * jumpHeight);

        Vector3 lv = _rb.linearVelocity;
        if (lv.y < 0f) lv.y = 0f;
        _rb.linearVelocity = lv;
        _rb.AddForce(Vector3.up * (jumpSpeed * _rb.mass), ForceMode.Impulse);

        if (_isSliding) StopSlide(false);

        if (isAirJump) OnAirJump?.Invoke();
        else OnJump?.Invoke();
    }

    // Enter crouch, or slide when grounded speed is high enough.
    private void TryStartCrouchOrSlide()
    {
        if (_isDashing)
        {
            Debug.Log("Tried slide/crouch but currently dashing.", this);
            return;
        }

        Debug.Log($"TRY SLIDE | Grounded: {_isGrounded} | Speed: {CurrentPlanarSpeed():0.00} | Required: {GetSlideStartSpeed():0.00}", this);

        if (_isGrounded && CanStartSlide())
        {
            StartSlide();
            return;
        }

        StartCrouch();
    }

    // Exit crouch and slide states when crouch input is released.
    private void StopCrouchOrSlide()
    {
        StopSlide(false);
        StopCrouch();
    }

    // Begin crouch without duplicating start events.
    private void StartCrouch()
    {
        if (_isCrouching) return;

        _isCrouching = true;
        OnCrouchStarted?.Invoke();
    }

    // End crouch without duplicating end events.
    private void StopCrouch()
    {
        if (!_isCrouching) return;

        _isCrouching = false;
        OnCrouchEnded?.Invoke();
    }

    // Start slide and lock facing to current momentum or input direction.
    private void StartSlide()
    {
        if (_isSliding) return;

        Vector3 planarVelocity = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
        if (planarVelocity.sqrMagnitude > 0.0001f) _slideDirectionWorld = planarVelocity.normalized;
        else if (_moveDirectionWorld.sqrMagnitude > 0.0001f) _slideDirectionWorld = _moveDirectionWorld;
        else _slideDirectionWorld = transform.forward;

        StopCrouch();
        _isSliding = true;

        Debug.Log($"ENTERED SLIDE | Speed: {CurrentPlanarSpeed():0.00} | Grounded: {_isGrounded} | Threshold: {GetSlideStartSpeed():0.00}", this);

        OnSlideStarted?.Invoke();
    }

    // Stop slide and optionally fall back to held crouch.
    private void StopSlide(bool crouchIfHeld)
    {
        if (!_isSliding) return;

        Debug.Log($"EXITED SLIDE | Speed: {CurrentPlanarSpeed():0.00} | Grounded: {_isGrounded} | CrouchHeld: {_crouchHeld} | CrouchIfHeld: {crouchIfHeld}", this);

        _isSliding = false;
        OnSlideEnded?.Invoke();

        if (crouchIfHeld && _crouchHeld) StartCrouch();
    }

    // Check whether grounded speed passes movement-scaled slide threshold.
    private bool CanStartSlide()
    {
        return _isGrounded && CurrentPlanarSpeed() >= GetSlideStartSpeed();
    }

    // Calculate slide entry speed from current movement speed stat.
    private float GetSlideStartSpeed()
    {
        return Mathf.Max(0.1f, _stats.GetMoveSpeed()) * _slideStartSpeedMultiplier;
    }

    // Validate dash requirements and apply a sudden additive dash velocity boost.
    private void TryDash()
    {
        if (_isDashing) return;

        int maxCharges = Mathf.Max(0, _stats.GetStatInt(StatType.DashCharges));
        if (maxCharges <= 0 || _currentDashCharges <= 0) return;

        Vector3 dashDirection = _moveDirectionWorld;
        if (dashDirection.sqrMagnitude <= 0.0001f) return;

        StopSlide(false);
        StopCrouch();
        _isDashing = true;
        _dashTimer = _dashDuration;

        float dashBoost = Mathf.Max(0.1f, _stats.GetStat(StatType.DashSpeed));
        Vector3 currentPlanar = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
        Vector3 desiredVelocity = currentPlanar + (dashDirection * dashBoost);
        Vector3 velocityChange = desiredVelocity - currentPlanar;
        _rb.AddForce(velocityChange * _rb.mass, ForceMode.Impulse);

        _currentDashCharges = Mathf.Max(0, _currentDashCharges - 1);
        _dashRegenTimer = 0f;
        OnDashChargesChanged?.Invoke(_currentDashCharges, maxCharges);
        OnDashStarted?.Invoke();
    }

    // Advance dash timer and restore regular movement behavior after dash ends.
    private void HandleDashTimer()
    {
        _dashTimer -= Time.fixedDeltaTime;
        if (_dashTimer > 0f) return;

        _isDashing = false;
        _postDashFrictionTimer = _postDashFrictionDuration;
        OnDashEnded?.Invoke();

        if (_crouchHeld) TryStartCrouchOrSlide();
    }

    // Regenerate dash charges using grounded or airborne recharge rates.
    private void HandleDashRegeneration()
    {
        int maxCharges = Mathf.Max(0, _stats.GetStatInt(StatType.DashCharges));
        if (maxCharges <= 0)
        {
            if (_currentDashCharges != 0)
            {
                _currentDashCharges = 0;
                OnDashChargesChanged?.Invoke(_currentDashCharges, maxCharges);
            }
            return;
        }

        if (_currentDashCharges > maxCharges)
        {
            _currentDashCharges = maxCharges;
            OnDashChargesChanged?.Invoke(_currentDashCharges, maxCharges);
        }

        if (_currentDashCharges >= maxCharges) return;

        _dashRegenTimer += Time.fixedDeltaTime;
        float regenTime = _isGrounded ? _groundedDashRegenTime : _airborneDashRegenTime;
        if (_dashRegenTimer < regenTime) return;

        _dashRegenTimer = 0f;
        _currentDashCharges = Mathf.Min(maxCharges, _currentDashCharges + 1);
        OnDashChargesChanged?.Invoke(_currentDashCharges, maxCharges);
    }

    // Update grounded transitions and refresh air jumps when landing.
    private void UpdateGroundedState()
    {
        _wasGrounded = _isGrounded;
        _isGrounded = IsGrounded();

        if (_isGrounded && !_wasGrounded)
        {
            _remainingAirJumps = Mathf.Max(0, _stats.GetStatInt(StatType.AirJumps));
            OnLanded?.Invoke();

            if (_crouchHeld && !_isDashing)
            {
                StartSlide();
                return;
            }
        }

        if (_isSliding && !_crouchHeld)
        {
            StopSlide(false);
            return;
        }

        if (_isSliding && _isGrounded && CurrentPlanarSpeed() < _minSlideSpeed)
        {
            StopSlide(true);
            return;
        }

        if (!_isSliding && _crouchHeld && CanStartSlide()) StartSlide();
    }

    // Check for floor contact using collider-aware downward raycast.
    private bool IsGrounded()
    {
        Vector3 origin = transform.position + Vector3.up * 0.02f;
        float distance = 0.2f + _groundCheckExtraDistance;

        if (_ownCollider != null)
        {
            distance = Mathf.Max(distance, _ownCollider.bounds.extents.y + _groundCheckExtraDistance);
            origin = new Vector3(transform.position.x, _ownCollider.bounds.center.y, transform.position.z);
        }

        return Physics.Raycast(origin, Vector3.down, distance, _groundMask, QueryTriggerInteraction.Ignore);
    }

    // Initialize current dash charges to stat-provided maximum.
    private void SyncDashChargesToStatMax()
    {
        int maxCharges = Mathf.Max(0, _stats.GetStatInt(StatType.DashCharges));
        _currentDashCharges = maxCharges;
        OnDashChargesChanged?.Invoke(_currentDashCharges, maxCharges);
    }

    // Return current horizontal speed ignoring vertical velocity.
    private float CurrentPlanarSpeed()
    {
        Vector3 planarV = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
        return planarV.magnitude;
    }

    // Remove vertical component and normalize with a safe fallback direction.
    private static Vector3 FlattenOnXZ(Vector3 v)
    {
        v.y = 0f;
        if (v.sqrMagnitude < 0.0001f) return Vector3.forward;
        return v.normalized;
    }

    // Read normalized WASD axes as a 2D movement vector.
    private static Vector2 ReadWasd()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return Vector2.zero;

        float x = 0f;
        if (keyboard.aKey.isPressed) x -= 1f;
        if (keyboard.dKey.isPressed) x += 1f;

        float y = 0f;
        if (keyboard.sKey.isPressed) y -= 1f;
        if (keyboard.wKey.isPressed) y += 1f;

        return new Vector2(x, y);
    }
}
