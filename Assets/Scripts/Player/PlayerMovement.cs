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
    [SerializeField, Min(0.01f), Tooltip("Tiempo de suavizado (segundos) para girar hacia la cámara/strafe. Más alto = giro más lento y suave.")]
    private float _facingSmoothTime = 0.08f;
    [SerializeField, Min(0f), Tooltip("Grados adicionales de giro hacia el costado al strafear con A/D, para que no se vea tan estático mirando siempre a cámara.")]
    private float _strafeTurnAngle = 10f;
    [SerializeField, Min(0f), Tooltip("Degrees per second while the body is aligning to reticle aim. Zero uses movement rotation speed.")]
    private float _aimFacingRotationSpeed = 720f;

    [SerializeField, Min(0.1f)] private float _baseMoveAcceleration = 75f;
    [SerializeField, Min(0.1f)] private float _baseFriction = 10f;
    [SerializeField, Min(0.1f), Tooltip("Ground release braking in metres/second squared; bounded so velocity never reverses at rest.")]
    private float _stoppingDeceleration = 35f;
    [SerializeField, Min(0.1f)] private float _reversalAcceleration = 90f;
    [SerializeField, Min(0.1f), Tooltip("Recovery rate for speed above the normal cap, including expired knockback and dash windows.")]
    private float _overspeedDeceleration = 35f;
    [SerializeField, Range(0.05f, 1f)] private float _crouchSpeedMultiplier = 0.5f;
    [SerializeField, Range(0f, 89f)] private float _maximumGroundAngle = 50f;
    [SerializeField, Min(0f)] private float _jumpBufferTime = 0.12f;
    [SerializeField, Min(0f)] private float _dashBufferTime = 0.12f;
    [SerializeField, Min(0f)] private float _coyoteTime = 0.1f;
    [SerializeField, Min(0f)] private float _groundCheckExtraDistance = 0.08f;
    [SerializeField] private LayerMask _groundMask = (1 << 0) | (1 << 7);
    [SerializeField, Min(0f)] private float _airFrictionMultiplier = 0.2f;
    [SerializeField, Min(0f)] private float _crouchAccelerationMultiplier = 0.2f;
    [SerializeField, Min(0f)] private float _slideFrictionMultiplier = 0.1f;
    [SerializeField, Min(0.01f)] private float _dashDuration = 0.25f;
    [SerializeField, Min(0.01f)] private float _groundedDashRegenTime = 2f;
    [SerializeField, Min(0.01f)] private float _airborneDashRegenTime = 4f;
    [SerializeField, Min(1f)] private float _slideStartSpeedMultiplier = 1.5f;
    [SerializeField, Min(0f)] private float _minSlideSpeed = 2f;
    [SerializeField, Min(0f), Tooltip("Ground dash recovery braking relative to base friction. 3.5 gives 35 m/s squared at base friction 10.")]
    private float _postDashFrictionMultiplier = 3.5f;
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
    private ThirdPersonCamera _movementCamera;
    private readonly RaycastHit[] _groundHits = new RaycastHit[32];
    private Vector3 _groundNormal = Vector3.up;
    private float _lastGroundedTime = float.NegativeInfinity;
    private float _jumpRequestTime = float.NegativeInfinity;
    private float _dashRequestTime = float.NegativeInfinity;
    private Vector3 _bufferedDashDirection;
    private bool _groundJumpConsumed;

    private Vector2 _moveInput;
    private Vector3 _moveDirectionWorld;
    private Vector3 _slideDirectionWorld;
    private Vector3 _dashLaunchDirectionWorld;
    private int _dashLaunchFrame = -1;
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
    private float _launchTimer;
    private float _launchGroundGrace;
    private float _stunTimer;
    private float _momentumPreservingStunTimer;
    private float _aimFacingTimer;
    private float _slowTimer;
    private float _slowMultiplier = 1f;
    private int _remainingAirJumps;
    private int _currentDashCharges;
    private Vector3 _aimFacingDirection;
    private Vector3 _momentumPreservingStunVelocity;
    private bool _hasMomentumPreservingStunVelocity;

    // Exposes camera-relative movement direction for weapons that aim from movement input.
    public Vector3 CurrentMoveDirectionWorld => _moveDirectionWorld;

    /// <summary>El jugador está aturdido (input de movimiento/salto/dash bloqueado).</summary>
    public bool IsStunned => _stunTimer > 0f;
    private bool IsMovementInputLocked => IsStunned || _momentumPreservingStunTimer > 0f;

    /// <summary>El jugador está en contacto con el suelo (para detección de vibraciones enemigas).</summary>
    public bool IsGroundedOnSurface => _isGrounded;
    public Vector3 GroundNormal => _groundNormal;

    /// <summary>Synchronize an authored grounded placement without emitting a second landing event.</summary>
    public void SynchronizeGroundedPlacement()
    {
        _isGrounded = IsGrounded();
        _wasGrounded = _isGrounded;
    }

    // Read-only presentation state; gameplay remains the owner of all action timing.
    public bool IsCrouching => _isCrouching;
    public bool IsSliding => _isSliding;
    public bool IsDashing => _isDashing;
    public Vector3 CurrentVelocity => _rb != null ? _rb.linearVelocity : Vector3.zero;

    /// <summary>Actual planar dash travel, including momentum and collision deflection.</summary>
    public Vector3 CurrentDashDirectionWorld
    {
        get
        {
            if (!_isDashing) return Vector3.zero;
            // Present launch intent during its first render frame; later physics
            // contacts may deflect the actual travel used by presentation.
            if (_dashLaunchFrame == Time.frameCount) return _dashLaunchDirectionWorld;
            Vector3 velocity = CurrentVelocity;
            velocity.y = 0f;
            return velocity.sqrMagnitude > .0001f ? velocity.normalized : Vector3.zero;
        }
    }

    public int CurrentDashCharges => _currentDashCharges;

    public int MaxDashCharges => _stats != null ? Mathf.Max(0, _stats.GetStatInt(StatType.DashCharges)) : 0;

    public int RemainingAirJumps => _remainingAirJumps;

    public int MaxAirJumps => _stats != null ? Mathf.Max(0, _stats.GetStatInt(StatType.AirJumps)) : 0;

    /// <summary>Refills movement resources after passive stats are changed at runtime.</summary>
    public void RefreshPassiveResources()
    {
        if (_stats == null)
            _stats = GetComponent<PlayerStats>();

        _remainingAirJumps = _stats != null
            ? Mathf.Max(0, _stats.GetStatInt(StatType.AirJumps))
            : 0;
        _dashRegenTimer = 0f;
        SyncDashChargesToStatMax();
    }

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

    /// <summary>
    /// Tira al jugador hacia <paramref name="towardPoint"/> con aceleración continua (Destroyer, succión).
    /// Reutiliza la ventana de knockback para que la fuerza no sea recortada por el speed-cap normal.
    /// Call once per physics tick while suction is active.
    /// </summary>
    public void ApplyPull(Vector3 towardPoint, float acceleration)
    {
        if (acceleration <= 0f || _rb == null)
            return;

        Vector3 dir = towardPoint - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f)
            return;
        dir.Normalize();

        _rb.AddForce(dir * acceleration, ForceMode.Acceleration);
        _knockbackTimer = Mathf.Max(_knockbackTimer, _knockbackWindow);
    }

    /// <summary>
    /// Lanzamiento balístico (jump pad). Durante el vuelo no hay aceleración, fricción ni speed-cap.
    /// Al volver a tocar suelo después de un breve margen, la velocidad se anula para no patinar.
    /// </summary>
    public void LaunchWithVelocity(Vector3 velocity, float duration)
    {
        if (_rb == null || duration <= 0f)
            return;

        StopSlide(false);
        CancelDash();
        ClearBufferedActions();
        _groundJumpConsumed = true;
        _isGrounded = false;
        _rb.linearVelocity = velocity;
        _launchTimer = duration;
        _launchGroundGrace = 0.45f;
    }

    public bool IsLaunching => _launchTimer > 0f;

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

    public void ApplyMomentumPreservingStun(float seconds, bool triggerStunFeedback = true, bool freezePlanarVelocity = true)
    {
        if (seconds <= 0f)
            return;

        if (triggerStunFeedback)
            ApplyStun(seconds);
        if (_rb == null)
            return;

        _momentumPreservingStunTimer = Mathf.Max(_momentumPreservingStunTimer, seconds);
        if (freezePlanarVelocity && !_hasMomentumPreservingStunVelocity)
            _momentumPreservingStunVelocity = _rb.linearVelocity;

        CancelDash();
        StopSlide(false);

        if (!freezePlanarVelocity)
        {
            _hasMomentumPreservingStunVelocity = false;
            _momentumPreservingStunVelocity = Vector3.zero;
            return;
        }

        _hasMomentumPreservingStunVelocity = true;
        Vector3 velocity = _rb.linearVelocity;
        _rb.linearVelocity = new Vector3(0f, velocity.y, 0f);
    }

    /// <summary>
    /// Ralentiza el movimiento durante <paramref name="seconds"/>. Refresca duración y conserva
    /// el multiplicador más bajo (más lento).
    /// </summary>
    public void ApplySlow(float speedMultiplier, float seconds)
    {
        if (seconds <= 0f)
            return;

        speedMultiplier = Mathf.Clamp(speedMultiplier, 0.05f, 1f);
        _slowMultiplier = Mathf.Min(_slowMultiplier, speedMultiplier);
        _slowTimer = Mathf.Max(_slowTimer, seconds);
    }

    public void ApplyWeaponDash(Vector3 worldDirection, float speed, float seconds)
    {
        if (_rb == null || seconds <= 0f || speed <= 0f)
            return;

        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude <= 0.0001f)
            return;

        Vector3 direction = worldDirection.normalized;
        StopSlide(false);
        StopCrouch();
        _isDashing = true;
        _dashTimer = Mathf.Max(_dashTimer, seconds);

        Vector3 desiredVelocity = direction * speed;
        _dashLaunchDirectionWorld = direction;
        _dashLaunchFrame = Time.frameCount;
        SetPlanarVelocity(desiredVelocity);
        OnDashStarted?.Invoke();
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
        if (_runtimeFrictionlessMaterial != null)
        {
            if (Application.isPlaying) Destroy(_runtimeFrictionlessMaterial);
            else DestroyImmediate(_runtimeFrictionlessMaterial);
        }
    }

    // Resolve camera reference and initialize jump and dash counters.
    private void Start()
    {
        if (_cameraTransform == null && Camera.main != null) _cameraTransform = Camera.main.transform;

        _isGrounded = IsGrounded();
        _wasGrounded = _isGrounded;
        RefreshPassiveResources();
    }

    // Read buffered player inputs and trigger stateful actions.
    private void Update()
    {
        if (GameplayPause.IsUiPaused)
        {
            ClearBufferedActions();
            _moveInput = Vector2.zero;
            _moveDirectionWorld = Vector3.zero;
            Keyboard pausedKeyboard = Keyboard.current;
            _crouchHeld = pausedKeyboard != null && (pausedKeyboard.leftCtrlKey.isPressed || pausedKeyboard.rightCtrlKey.isPressed);
            if (!_crouchHeld) StopCrouchOrSlide();
            return;
        }

        if (_cameraTransform == null) return;

        ReadInput();
        // Release is cleanup, even during hit-stop, stun or ballistic transport.
        if (!_crouchHeld) StopCrouchOrSlide();
        if (IsMovementInputLocked || IsLaunching)
            ClearBufferedActions();
        else
        {
            if (_jumpPressed) _jumpRequestTime = Time.unscaledTime;
            if (_dashPressed)
            {
                _dashRequestTime = Time.unscaledTime;
                _bufferedDashDirection = _moveDirectionWorld;
            }
            if (_crouchPressed && Time.timeScale > 0f) TryStartCrouchOrSlide();
        }

        _jumpPressed = false;
        _crouchPressed = false;
        _crouchReleased = false;
        _dashPressed = false;
    }

    // Run physics movement, friction, dash timer, and grounded transitions.
    private void FixedUpdate()
    {
        if (_rb == null || _cameraTransform == null || GameplayPause.IsUiPaused || Time.timeScale <= 0f) return;

        UpdateGroundedState();
        if (!_crouchHeld) StopCrouchOrSlide();
        ConsumeBufferedActions();
        if (!IsLaunching)
            HoldMomentumPreservingStun();

        if (IsLaunching)
        {
            _launchTimer = Mathf.Max(0f, _launchTimer - Time.fixedDeltaTime);
            if (_launchGroundGrace > 0f)
                _launchGroundGrace = Mathf.Max(0f, _launchGroundGrace - Time.fixedDeltaTime);
            else if (_isGrounded && _rb.linearVelocity.y <= 1f)
            {
                _rb.linearVelocity = Vector3.zero;
                _launchTimer = 0f;
            }
        }
        else if (_isDashing)
        {
            HandleDashTimer();
        }
        else
        {
            HandleMovement();
        }

        TickPostDashFrictionWindow();
        TickKnockbackWindow();
        TickStun();
        TickMomentumPreservingStun();
        TickSlow();
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
            _moveDirectionWorld = Vector3.zero;
            _crouchHeld = false;
            _jumpPressed = _dashPressed = _crouchPressed = _crouchReleased = false;
            return;
        }

        _moveInput = ReadWasd();
        if (_moveInput.sqrMagnitude > 1f) _moveInput.Normalize();

        Vector3 flatForward = CameraPlanarForward();
        Vector3 flatRight = Vector3.Cross(Vector3.up, flatForward);
        _moveDirectionWorld = flatForward * _moveInput.y + flatRight * _moveInput.x;
        if (_moveDirectionWorld.sqrMagnitude > 0.0001f) _moveDirectionWorld.Normalize();

        _jumpPressed = keyboard.spaceKey.wasPressedThisFrame;
        _dashPressed = keyboard.leftShiftKey.wasPressedThisFrame || keyboard.rightShiftKey.wasPressedThisFrame;

        bool crouchNowHeld = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
        _crouchPressed = crouchNowHeld && !_crouchHeld;
        _crouchReleased = !crouchNowHeld && _crouchHeld;
        _crouchHeld = crouchNowHeld;
    }

    // Rotate through Rigidbody ownership and integrate the bounded planar motor.
    private void HandleMovement()
    {
        // Aturdido: sin rotación ni aceleración por input (la fricción sigue para frenar).
        if (IsMovementInputLocked)
        {
            HandlePlanarMotor(false);
            return;
        }

        // Over-the-shoulder: el personaje siempre mira hacia donde mira la cámara (salvo
        // slide, que conserva el rumbo del deslizamiento, o un aim-facing forzado), con un
        // pequeño giro extra al strafear (A/D) para que no se vea estático. Así "S" retrocede
        // en sentido contrario a cámara en vez de darse vuelta a mirarla, y el disparo (que
        // apunta según cámara/reticle) siempre coincide con hacia dónde mira el personaje.
        Vector3 aimFacingDirection = Vector3.zero;
        bool useAimFacing = !_isSliding && TryGetAimFacingDirection(out aimFacingDirection);
        Vector3 cameraFacing = CameraPlanarForward();
        Vector3 strafedFacing = Quaternion.AngleAxis(_moveInput.x * _strafeTurnAngle, Vector3.up) * cameraFacing;
        Vector3 facingDirection = _isSliding ? _slideDirectionWorld : useAimFacing ? aimFacingDirection : strafedFacing;
        if (facingDirection.sqrMagnitude > 0.0001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(facingDirection);
            if (useAimFacing && _aimFacingRotationSpeed > 0f)
            {
                // Lock de aim: giro a velocidad constante, más brusco a propósito.
                _rb.MoveRotation(Quaternion.RotateTowards(_rb.rotation, targetRotation, _aimFacingRotationSpeed * Time.fixedDeltaTime));
            }
            else
            {
                // Movimiento normal: suavizado exponencial (independiente del framerate),
                // se siente mucho más orgánico que un giro a velocidad constante.
                float t = 1f - Mathf.Exp(-Time.fixedDeltaTime / _facingSmoothTime);
                _rb.MoveRotation(Quaternion.Slerp(_rb.rotation, targetRotation, t));
            }
        }

        HandlePlanarMotor(true);
    }

    private Vector3 CameraPlanarForward()
    {
        if (_movementCamera == null || _movementCamera.transform != _cameraTransform)
            _movementCamera = _cameraTransform != null ? _cameraTransform.GetComponent<ThirdPersonCamera>() : null;
        return FlattenOnXZ(_movementCamera != null && _movementCamera.isActiveAndEnabled ? _movementCamera.GameplayForward : _cameraTransform.forward);
    }

    private void SetPlanarVelocity(Vector3 planar)
    {
        _rb.linearVelocity = new Vector3(planar.x, _rb.linearVelocity.y, planar.z);
    }

    // One bounded integration owns motor velocity. External impulses remain physics-owned.
    private void HandlePlanarMotor(bool allowInput)
    {
        Vector3 planar = Vector3.ProjectOnPlane(_rb.linearVelocity, Vector3.up);
        float scale = DebugSpeedTool.LocomotionScale;
        float maxSpeed = Mathf.Max(0.1f, _stats.GetMoveSpeed() * scale * GetSlowMultiplier());
        if (_isCrouching) maxSpeed *= _crouchSpeedMultiplier;
        bool hasInput = allowInput && !_isSliding && _moveDirectionWorld.sqrMagnitude > 0.0001f;
        Vector3 target = hasInput ? _moveDirectionWorld * maxSpeed : Vector3.zero;

        bool coastingCharge = _momentumPreservingStunTimer > 0f && !_hasMomentumPreservingStunVelocity;
        if (_isSliding || _postDashFrictionTimer > 0f || _knockbackTimer > 0f || coastingCharge)
        {
            // Preserve authored slide/impulse grace, but steering cannot add overspeed energy.
            if (hasInput)
            {
                float acceleration = _baseMoveAcceleration * scale * 0.35f;
                if (_isCrouching) acceleration *= _crouchAccelerationMultiplier;
                float inheritedLimit = Mathf.Max(planar.magnitude, maxSpeed);
                planar = Vector3.ClampMagnitude(planar + _moveDirectionWorld * acceleration * Time.fixedDeltaTime, inheritedLimit);
            }
            float friction = _baseFriction * scale;
            if (!_isGrounded) friction *= _airFrictionMultiplier;
            if (_isSliding && _isGrounded) friction *= _slideFrictionMultiplier;
            if (_postDashFrictionTimer > 0f) friction *= _postDashFrictionMultiplier;
            if (_knockbackTimer > 0f) friction *= _knockbackFrictionMultiplier;
            SetPlanarVelocity(Vector3.MoveTowards(planar, Vector3.zero, friction * Time.fixedDeltaTime));
            return;
        }

        float rate = hasInput ? _baseMoveAcceleration : (_isGrounded ? _stoppingDeceleration : _baseFriction * _airFrictionMultiplier);
        if (hasInput && Vector3.Dot(planar, target) < 0f) rate = _reversalAcceleration;
        if (_isCrouching && hasInput) rate *= _crouchAccelerationMultiplier;
        if (planar.magnitude > maxSpeed + 0.001f) rate = _overspeedDeceleration;
        SetPlanarVelocity(Vector3.MoveTowards(planar, target, rate * scale * Time.fixedDeltaTime));
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

    private void HoldMomentumPreservingStun()
    {
        if (!_hasMomentumPreservingStunVelocity || _momentumPreservingStunTimer <= 0f || _rb == null)
            return;

        Vector3 velocity = _rb.linearVelocity;
        _rb.linearVelocity = new Vector3(0f, velocity.y, 0f);
    }

    private void TickMomentumPreservingStun()
    {
        if (_momentumPreservingStunTimer <= 0f)
            return;

        _momentumPreservingStunTimer = Mathf.Max(0f, _momentumPreservingStunTimer - Time.fixedDeltaTime);
        if (_momentumPreservingStunTimer > 0f)
            return;

        if (_hasMomentumPreservingStunVelocity && _rb != null)
        {
            Vector3 velocity = _rb.linearVelocity;
            _rb.linearVelocity = new Vector3(_momentumPreservingStunVelocity.x, velocity.y, _momentumPreservingStunVelocity.z);
            _postDashFrictionTimer = Mathf.Max(_postDashFrictionTimer, _postDashFrictionDuration);
        }

        _hasMomentumPreservingStunVelocity = false;
        _momentumPreservingStunVelocity = Vector3.zero;
    }

    private void TickSlow()
    {
        if (_slowTimer <= 0f)
        {
            _slowMultiplier = 1f;
            return;
        }

        _slowTimer = Mathf.Max(0f, _slowTimer - Time.fixedDeltaTime);
        if (_slowTimer <= 0f)
            _slowMultiplier = 1f;
    }

    private float GetSlowMultiplier() => _slowTimer > 0f ? _slowMultiplier : 1f;

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
    private bool TryJump()
    {
        if (!_groundJumpConsumed && (_isGrounded || Time.time - _lastGroundedTime <= _coyoteTime))
        {
            PerformJump(false);
            return true;
        }

        if (_remainingAirJumps > 0)
        {
            _remainingAirJumps--;
            PerformJump(true);
            return true;
        }
        return false;
    }

    // Set a repeatable vertical launch speed from the jump-height stat.
    private void PerformJump(bool isAirJump)
    {
        float jumpHeight = Mathf.Max(0.01f, _stats.GetStat(StatType.JumpHeight));
        float g = Mathf.Abs(Physics.gravity.y);
        float jumpSpeed = Mathf.Sqrt(2f * g * jumpHeight);

        Vector3 lv = _rb.linearVelocity;
        lv.y = jumpSpeed;
        _rb.linearVelocity = lv;
        _groundJumpConsumed = true;
        _isGrounded = false;
        _lastGroundedTime = float.NegativeInfinity;

        if (_isSliding) StopSlide(false);

        if (isAirJump) OnAirJump?.Invoke();
        else OnJump?.Invoke();
    }

    private void ClearBufferedActions()
    {
        _jumpRequestTime = _dashRequestTime = float.NegativeInfinity;
    }

    private void ConsumeBufferedActions()
    {
        if (IsMovementInputLocked || IsLaunching)
        {
            ClearBufferedActions();
            return;
        }
        float now = Time.unscaledTime;
        if (now - _jumpRequestTime > _jumpBufferTime) _jumpRequestTime = float.NegativeInfinity;
        else if (TryJump()) _jumpRequestTime = float.NegativeInfinity;

        if (now - _dashRequestTime > _dashBufferTime) _dashRequestTime = float.NegativeInfinity;
        else if (!_isDashing && _currentDashCharges > 0)
        {
            Vector3 currentIntent = _moveDirectionWorld;
            _moveDirectionWorld = _bufferedDashDirection;
            TryDash();
            _moveDirectionWorld = currentIntent;
            _dashRequestTime = float.NegativeInfinity;
        }
    }

    // Enter crouch, or slide when grounded speed is high enough.
    private void TryStartCrouchOrSlide()
    {
        if (_isDashing)
        {
            return;
        }

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

        OnSlideStarted?.Invoke();
    }

    // Stop slide and optionally fall back to held crouch.
    private void StopSlide(bool crouchIfHeld)
    {
        if (!_isSliding) return;

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
        return Mathf.Max(0.1f, _stats.GetMoveSpeed() * DebugSpeedTool.LocomotionScale) * _slideStartSpeedMultiplier;
    }

    // DashSpeed remains a boost above the movement stat, but not above arbitrary old momentum.
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

        float dashBoost = Mathf.Max(0.1f, _stats.GetStat(StatType.DashSpeed) * DebugSpeedTool.LocomotionScale);
        float dashSpeed = Mathf.Max(0.1f, _stats.GetMoveSpeed() * DebugSpeedTool.LocomotionScale) + dashBoost;
        Vector3 desiredVelocity = dashDirection.normalized * dashSpeed;
        _dashLaunchDirectionWorld = dashDirection.normalized;
        _dashLaunchFrame = Time.frameCount;
        SetPlanarVelocity(desiredVelocity);

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

        CancelDash();
        _postDashFrictionTimer = _postDashFrictionDuration;

        if (_crouchHeld && !IsMovementInputLocked) TryStartCrouchOrSlide();
    }

    private void CancelDash()
    {
        bool wasDashing = _isDashing;
        _isDashing = false;
        _dashTimer = 0f;
        if (wasDashing) OnDashEnded?.Invoke();
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
        if (_isGrounded)
        {
            _lastGroundedTime = Time.time;
            _groundJumpConsumed = false;
        }

        if (_isGrounded && !_wasGrounded)
        {
            _remainingAirJumps = Mathf.Max(0, _stats.GetStatInt(StatType.AirJumps));
            OnLanded?.Invoke();

            if (_crouchHeld && !_isDashing && !IsLaunching && !IsMovementInputLocked && CanStartSlide())
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

        if (!_isSliding && _crouchHeld && !_isDashing && !IsLaunching && !IsMovementInputLocked && CanStartSlide()) StartSlide();
    }

    // Probe the lower capsule sphere, qualifying support rather than treating any ray hit as ground.
    private bool IsGrounded()
    {
        _groundNormal = Vector3.up;
        // Uphill contact naturally has positive Y velocity. Suppress re-grounding only after a jump/launch.
        if (_ownCollider == null || (_groundJumpConsumed && _rb != null && _rb.linearVelocity.y > 0.1f)) return false;
        Bounds bounds = _ownCollider.bounds;
        float radius = Mathf.Min(bounds.extents.x, bounds.extents.z);
        if (_ownCollider is CapsuleCollider capsule && capsule.direction == 1)
        {
            Vector3 scale = transform.lossyScale;
            radius = capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        }
        float skin = Mathf.Min(0.02f, radius * 0.1f);
        float probeRadius = Mathf.Max(0.01f, radius - skin);
        Vector3 lowerSphere = bounds.center - Vector3.up * Mathf.Max(0f, bounds.extents.y - radius);
        Vector3 origin = lowerSphere + Vector3.up * 0.05f;
        float distance = 0.05f + skin + _groundCheckExtraDistance;
        int count = Physics.SphereCastNonAlloc(origin, probeRadius, Vector3.down, _groundHits, distance, _groundMask, QueryTriggerInteraction.Ignore);
        RaycastHit[] hits = count == _groundHits.Length
            ? Physics.SphereCastAll(origin, probeRadius, Vector3.down, distance, _groundMask, QueryTriggerInteraction.Ignore)
            : _groundHits;
        if (hits != _groundHits) count = hits.Length;
        float closest = float.PositiveInfinity;
        float minimumUp = Mathf.Cos(_maximumGroundAngle * Mathf.Deg2Rad);
        for (int i = 0; i < count; i++)
        {
            Collider surface = hits[i].collider;
            if (surface == null || surface.transform == transform || surface.transform.IsChildOf(transform) ||
                surface.GetComponentInParent<EnemyHealth>() != null || Vector3.Dot(hits[i].normal, Vector3.up) < minimumUp ||
                hits[i].distance >= closest) continue;
            closest = hits[i].distance;
            _groundNormal = hits[i].normal;
        }
        return closest < float.PositiveInfinity;
    }

    // Initialize current dash charges to stat-provided maximum.
    private void SyncDashChargesToStatMax()
    {
        int maxCharges = _stats != null ? Mathf.Max(0, _stats.GetStatInt(StatType.DashCharges)) : 0;
        _currentDashCharges = maxCharges;
        OnDashChargesChanged?.Invoke(_currentDashCharges, maxCharges);
    }

    public float AuthoredMoveAcceleration => _baseMoveAcceleration;
    public float AuthoredFriction => _baseFriction;
    public float AuthoredDashDuration => _dashDuration;

    // Return current horizontal speed ignoring vertical velocity.
    public float PlanarSpeed => _rb == null ? 0f : CurrentPlanarSpeed();

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
