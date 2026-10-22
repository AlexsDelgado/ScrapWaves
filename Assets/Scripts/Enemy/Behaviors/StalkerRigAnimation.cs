using System;
using UnityEngine;

/// <summary>Presentation-only bridge from the worm's authoritative phases to its authored rig.</summary>
[DisallowMultipleComponent]
public sealed class StalkerRigAnimation : MonoBehaviour
{
    public enum Phase { Hidden, Emergence, Ascent, ApexDive, Dive, Reburrow, Dead }

    private const float EmergenceDuration = 0.3f;
    private const float ApexDiveDuration = 0.6f;
    private const float CrossFadeDuration = 0.06f;
    private static readonly int EmergenceState = UnityEngine.Animator.StringToHash("Base Layer.Emergence");
    private static readonly int AscentState = UnityEngine.Animator.StringToHash("Base Layer.Ascent");
    private static readonly int ApexDiveState = UnityEngine.Animator.StringToHash("Base Layer.ApexDive");
    private static readonly int DiveState = UnityEngine.Animator.StringToHash("Base Layer.Dive");
    private static readonly int ReburrowState = UnityEngine.Animator.StringToHash("Base Layer.ImpactReburrow");

    [SerializeField] private Animator _animator;
    [SerializeField] private Transform _visualRoot;
    [SerializeField] private SkinnedMeshRenderer _bodyRenderer;
    [SerializeField] private Transform[] _bones = Array.Empty<Transform>();
    [SerializeField] private Transform[] _collisionSegments = Array.Empty<Transform>();
    [SerializeField, Range(15f, 60f)] private float _maximumJointBend = 45f;

    private EnemyHealth _health;
    private Matrix4x4 _bindVisualToRoot;
    private Quaternion _bindVisualRotation;
    private Vector3[] _restPositions;
    private Quaternion[] _restRotations;
    private Vector3[] _restScales;
    private float _phaseElapsed;
    private bool _configured;
    private bool _deathHandled;
    private bool _detachedForDeath;
    private StalkerBodyChain _chain;
    private bool _chainReady;
    private Vector3 _restSocketInRoot;
    private Quaternion _restHeadRotationInRoot;
    private Vector3 _chainScale;
    private Bounds[] _boneBounds;
    private bool[] _hasBoneBounds;

    public Phase CurrentPhase { get; private set; } = Phase.Hidden;
    public Animator Animator => _animator;
    public float MaximumJointBend => Mathf.Clamp(_maximumJointBend, 0f, 60f);
    public float BodyLength => _bodyRenderer != null && _bodyRenderer.sharedMesh != null
        ? _bodyRenderer.sharedMesh.bounds.size.y * Mathf.Abs(transform.lossyScale.y) : 0f;

    private void Awake()
    {
        _health = GetComponent<EnemyHealth>();
        CaptureBindings();
    }

    private void OnEnable()
    {
        if (_health == null) _health = GetComponent<EnemyHealth>();
        if (_health != null) _health.OnDied += OnDied;
        _deathHandled = false;
        if (!_configured) CaptureBindings();
        ResetPose();
    }

    private void OnDisable()
    {
        if (_health != null) _health.OnDied -= OnDied;
        ResetPose();
    }

    private void Update() => Step(Time.deltaTime);
    private void LateUpdate()
    {
        ApplyFlightPose();
        SyncCollisionPose();
    }

    public void Configure(Animator animator, Transform visualRoot, SkinnedMeshRenderer bodyRenderer,
        Transform[] bones, Transform[] collisionSegments)
    {
        _animator = animator;
        _visualRoot = visualRoot;
        _bodyRenderer = bodyRenderer;
        _bones = bones == null ? Array.Empty<Transform>() : (Transform[])bones.Clone();
        _collisionSegments = collisionSegments == null ? Array.Empty<Transform>() : (Transform[])collisionSegments.Clone();
        _detachedForDeath = false;
        _deathHandled = false;
        CaptureBindings();
        ResetPose();
    }

    public void BeginJump()
    {
        if (!CanAnimate()) return;
        ResetPose();
        PlayPhase(Phase.Emergence, EmergenceState, true);
        _animator.Update(0f);
        ApplyFlightPose();
        SyncCollisionPose();
    }

    public void BeginDive()
    {
        if (CanAnimate()) PlayPhase(Phase.ApexDive, ApexDiveState, false);
    }

    public void BeginReburrow()
    {
        if (CanAnimate()) PlayPhase(Phase.Reburrow, ReburrowState, false);
    }

    /// <summary>Call after the AI hides the renderers. Gameplay still owns visibility and immunity.</summary>
    public void EnterBurrow() => ResetPose();

    public void ResetPose()
    {
        // The death helper owns the detached Animator, including after this gameplay root dies.
        if (_detachedForDeath) return;
        _chainReady = false;
        CurrentPhase = Phase.Hidden;
        _phaseElapsed = 0f;
        if (!_configured || _animator == null) return;
        _animator.enabled = true;
        _animator.applyRootMotion = false;
        _animator.speed = 1f;
        _animator.Rebind();
        _animator.Update(0f);
        for (int i = 0; i < _bones.Length; i++)
        {
            if (_bones[i] == null) continue;
            _bones[i].localPosition = _restPositions[i];
            _bones[i].localRotation = _restRotations[i];
            _bones[i].localScale = _restScales[i];
        }
        _animator.enabled = false;
        SyncCollisionPose();
    }

    /// <summary>Advances presentation clocks only. Unity evaluates the Animator; previews may call Animator.Update separately.</summary>
    public void Step(float deltaTime)
    {
        if (!CanAnimate() || CurrentPhase == Phase.Hidden || CurrentPhase == Phase.Dead) return;
        _animator.speed = PlaybackSpeed();
        if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
        _phaseElapsed += deltaTime * _animator.speed;
        if (CurrentPhase == Phase.Emergence && _phaseElapsed >= EmergenceDuration)
            PlayPhase(Phase.Ascent, AscentState, false);
        else if (CurrentPhase == Phase.ApexDive && _phaseElapsed >= ApexDiveDuration)
            PlayPhase(Phase.Dive, DiveState, false);
    }

    /// <summary>After Animator evaluation, applies the persistent chain driven only by actual head movement.</summary>
    public void ApplyFlightPose()
    {
        if (!CanAnimate() || CurrentPhase == Phase.Hidden || CurrentPhase == Phase.Dead) return;
        if (_chain == null || (_chainScale - transform.lossyScale).sqrMagnitude > 0.000001f)
            CreateChain();
        Vector3 socket = transform.TransformPoint(_restSocketInRoot);
        Quaternion headRotation = transform.rotation * _restHeadRotationInRoot;
        if (!_chainReady)
        {
            _chain.Reset(socket, headRotation);
            _chainReady = true;
        }
        else _chain.Step(socket, headRotation, MaximumJointBend);

        // The solver lives in gameplay world space. Compose hit feedback once on the visual
        // result, while SyncCollisionPose strips it back off the gameplay collision proxies.
        Quaternion neutralToVisual = _visualRoot.rotation * Quaternion.Inverse(_bindVisualRotation) * Quaternion.Inverse(transform.rotation);
        for (int i = 1; i < _bones.Length; i++)
        {
            Transform bone = _bones[i];
            // Live body curves no longer add a competing authored pose over the chain.
            bone.localPosition = _restPositions[i];
            bone.localRotation = Quaternion.Inverse(bone.parent.rotation) * neutralToVisual * _chain.GetRotation(i - 1);
        }
    }

    private void CreateChain()
    {
        var lengths = new float[_bones.Length - 1];
        for (int i = 0; i < lengths.Length - 1; i++)
            lengths[i] = transform.TransformVector(_restPositions[i + 2]).magnitude;
        int tail = _bones.Length - 1;
        float tailLength = _hasBoneBounds != null && _hasBoneBounds[tail]
            ? Mathf.Max(0.01f, -_boneBounds[tail].min.y) : _restPositions[tail].magnitude;
        lengths[lengths.Length - 1] = transform.TransformVector(Vector3.down * tailLength).magnitude;
        _chain = new StalkerBodyChain(lengths);
        _chainScale = transform.lossyScale;
        _chainReady = false;
    }

    /// <summary>Conservative world-space top of the animated rigid-weight geometry, including hit feedback.</summary>
    public float GetHighestBodyPoint()
    {
        if (!_configured || _boneBounds == null) return transform.position.y;
        float highest = float.NegativeInfinity;
        for (int i = 0; i < _bones.Length; i++)
        {
            if (!_hasBoneBounds[i] || _bones[i] == null) continue;
            Matrix4x4 matrix = _bones[i].localToWorldMatrix;
            Vector3 extents = _boneBounds[i].extents;
            float top = matrix.MultiplyPoint3x4(_boneBounds[i].center).y
                + Mathf.Abs(matrix.m10) * extents.x + Mathf.Abs(matrix.m11) * extents.y + Mathf.Abs(matrix.m12) * extents.z;
            highest = Mathf.Max(highest, top);
        }
        // An unreadable replacement mesh must fail conservatively rather than hide at
        // head depth. The authored readable mesh normally uses the tight bone bounds.
        return float.IsNegativeInfinity(highest) && _bodyRenderer != null ? _bodyRenderer.bounds.max.y : highest;
    }

    /// <summary>Copies animated bones to gameplay proxies while stripping the cosmetic feedback wrapper.</summary>
    public void SyncCollisionPose()
    {
        if (!_configured || _detachedForDeath || _visualRoot == null) return;
        Matrix4x4 visualToRoot = _bindVisualToRoot * _visualRoot.worldToLocalMatrix;
        Quaternion visualToRootRotation = _bindVisualRotation * Quaternion.Inverse(_visualRoot.rotation);
        for (int i = 1; i < _bones.Length && i < _collisionSegments.Length; i++)
        {
            Transform bone = _bones[i];
            Transform proxy = _collisionSegments[i];
            if (bone == null || proxy == null || proxy.parent == null) continue;
            Vector3 rootPosition = visualToRoot.MultiplyPoint3x4(bone.position);
            Quaternion rootRotation = visualToRootRotation * bone.rotation;
            proxy.position = transform.TransformPoint(rootPosition);
            proxy.rotation = transform.rotation * rootRotation;
        }
    }

    private void PlayPhase(Phase phase, int state, bool restart)
    {
        CurrentPhase = phase;
        _phaseElapsed = 0f;
        _animator.enabled = true;
        _animator.applyRootMotion = false;
        _animator.speed = PlaybackSpeed();
        if (restart) _animator.Play(state, 0, 0f);
        else _animator.CrossFadeInFixedTime(state, CrossFadeDuration, 0, 0f);
    }

    private float PlaybackSpeed() => CurrentPhase == Phase.Reburrow ? 1f : DebugSpeedTool.EnemyScale;
    private bool CanAnimate() => _configured && !_detachedForDeath && !_deathHandled && _animator != null;

    private void CaptureBindings()
    {
        _configured = false;
        if (_animator == null || _visualRoot == null || _bodyRenderer == null || _bones == null || _bones.Length != 8) return;
        if (_animator.transform == transform || !_animator.transform.IsChildOf(_visualRoot)) return;
        if (_collisionSegments == null || _collisionSegments.Length != _bones.Length || _collisionSegments[0] != null) return;
        for (int i = 0; i < _bones.Length; i++)
            if (_bones[i] == null || (i > 0 && _collisionSegments[i] == null)) return;
        _bindVisualToRoot = transform.worldToLocalMatrix * _visualRoot.localToWorldMatrix;
        _bindVisualRotation = Quaternion.Inverse(transform.rotation) * _visualRoot.rotation;
        _restPositions = new Vector3[_bones.Length];
        _restRotations = new Quaternion[_bones.Length];
        _restScales = new Vector3[_bones.Length];
        for (int i = 0; i < _bones.Length; i++)
        {
            _restPositions[i] = _bones[i].localPosition;
            _restRotations[i] = _bones[i].localRotation;
            _restScales[i] = _bones[i].localScale;
        }
        _animator.applyRootMotion = false;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        CacheBoneBounds();
        _restSocketInRoot = transform.InverseTransformPoint(_bones[1].position);
        _restHeadRotationInRoot = Quaternion.Inverse(transform.rotation) * _bones[0].rotation;
        CreateChain();
        _configured = true;
    }

    private void CacheBoneBounds()
    {
        _boneBounds = new Bounds[_bones.Length];
        _hasBoneBounds = new bool[_bones.Length];
        Mesh mesh = _bodyRenderer.sharedMesh;
        if (mesh == null || !mesh.isReadable) return;
        Vector3[] vertices = mesh.vertices;
        BoneWeight[] weights = mesh.boneWeights;
        Matrix4x4[] bindPoses = mesh.bindposes;
        for (int i = 0; i < vertices.Length && i < weights.Length; i++)
        {
            int bone = weights[i].boneIndex0;
            if (bone < 0 || bone >= _bones.Length || bone >= bindPoses.Length) continue;
            Vector3 point = bindPoses[bone].MultiplyPoint3x4(vertices[i]);
            if (_hasBoneBounds[bone]) _boneBounds[bone].Encapsulate(point);
            else { _boneBounds[bone] = new Bounds(point, Vector3.zero); _hasBoneBounds[bone] = true; }
        }
    }

    private void OnDied()
    {
        if (_deathHandled) return;
        _deathHandled = true;
        CurrentPhase = Phase.Dead;
        // Buried DoT death resolves normally without revealing the hidden body.
        if (!_configured || _bodyRenderer == null || !_bodyRenderer.enabled || !_bodyRenderer.gameObject.activeInHierarchy) return;
        Transform rig = _animator.transform;
        _detachedForDeath = true;
        rig.SetParent(null, true);
        StalkerRigDeathVisual presentation = rig.gameObject.AddComponent<StalkerRigDeathVisual>();
        presentation.Configure(_animator, _bodyRenderer, 1.1f);
    }
}
