using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Samples the opening animation at the exit's authoritative charge progress.
/// Clip duration never changes when the exit becomes ready, and the final pose is held.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-70)]
public sealed class CompactorDoorPresentation : MonoBehaviour, System.IDisposable
{
    [SerializeField] private ExitDoor _exitDoor;
    [SerializeField] private Animator _animator;
    [SerializeField] private AnimationClip _openingClip;
    [SerializeField] private Renderer _suctionRenderer;
    [SerializeField] private Collider _closedDoorCollider;
    [SerializeField] private Transform _movingPanel;
    [SerializeField] private float _closedPanelHeight;
    [SerializeField] private Vector3 _closedColliderLocalPosition;

    private static readonly int ActivityId = Shader.PropertyToID("_Activity");

    private ExitDoor _subscribedDoor;
    private PlayableGraph _graph;
    private AnimationClipPlayable _openingPlayable;
    private Animator _graphAnimator;
    private AnimatorCullingMode _previousCullingMode;
    private bool _previousApplyRootMotion;
    private MaterialPropertyBlock _propertyBlock;
    private float _sampledProgress = -1f;

    /// <summary>Assigns an imported Generic clip and its own Animator, without an Animator Controller.</summary>
    public void Configure(
        ExitDoor exitDoor,
        Animator animator,
        AnimationClip openingClip,
        Renderer suctionRenderer = null,
        Collider closedDoorCollider = null)
    {
        Unsubscribe();
        DestroyGraph();
        SetSuctionActivity(0f);
        _exitDoor = exitDoor;
        _animator = animator;
        _openingClip = openingClip;
        _suctionRenderer = suctionRenderer;
        _closedDoorCollider = closedDoorCollider;

        if (isActiveAndEnabled)
        {
            Subscribe();
            Synchronize();
        }
    }

    /// <summary>The solid panel follows the sampled rig, including partial opening and resets.</summary>
    public void ConfigureMovingPanel(Transform panel)
    {
        _movingPanel = panel;
        if (_movingPanel != null)
            _closedPanelHeight = transform.InverseTransformPoint(_movingPanel.position).y;
        if (_closedDoorCollider != null)
            _closedColliderLocalPosition = _closedDoorCollider.transform.localPosition;
        Synchronize();
    }

    private void OnEnable()
    {
        if (_exitDoor == null)
            _exitDoor = GetComponentInParent<ExitDoor>();
        if (_animator == null)
            _animator = GetComponentInChildren<Animator>();

        Subscribe();
        Synchronize();
    }

    /// <summary>
    /// Releases animation resources. Editor tools must call this before destroying
    /// temporary instances because edit-mode MonoBehaviours may not receive OnDestroy.
    /// </summary>
    public void Dispose()
    {
        Unsubscribe();
        DestroyGraph();
        SetSuctionActivity(0f);
    }

    private void OnDisable() => Dispose();

    private void OnDestroy() => Dispose();

    private void FixedUpdate()
    {
        // ExitDoor advances first; transforms are sampled before the following physics step.
        Synchronize();
    }

    private void LateUpdate()
    {
        // Also catches a reset, late activation, or the final Ready -> Used transition.
        Synchronize();
    }

    private void Subscribe()
    {
        Unsubscribe();
        if (_exitDoor == null)
            return;

        _subscribedDoor = _exitDoor;
        _subscribedDoor.OnChargeStarted += Synchronize;
        _subscribedDoor.OnChargeProgress += HandleChargeProgress;
        _subscribedDoor.OnDoorReady += Synchronize;
    }

    private void Unsubscribe()
    {
        if (_subscribedDoor != null)
        {
            _subscribedDoor.OnChargeStarted -= Synchronize;
            _subscribedDoor.OnChargeProgress -= HandleChargeProgress;
            _subscribedDoor.OnDoorReady -= Synchronize;
        }

        _subscribedDoor = null;
    }

    private void Synchronize()
    {
        if (!isActiveAndEnabled)
            return;

        ExitDoorState state = _exitDoor != null ? _exitDoor.State : ExitDoorState.Locked;
        ApplyPresentation(state, _exitDoor != null ? _exitDoor.ChargeNormalized : 0f);
    }

    private void HandleChargeProgress(float progress)
    {
        if (isActiveAndEnabled && _exitDoor != null)
            ApplyPresentation(_exitDoor.State, progress);
    }

    private void ApplyPresentation(ExitDoorState state, float chargeProgress)
    {
        bool isOpen = state == ExitDoorState.Ready;
        bool isCharging = state == ExitDoorState.Charging;
        bool hasStarted = isCharging || state == ExitDoorState.Used;
        float progress = isOpen ? 1f : hasStarted ? Mathf.Clamp01(chargeProgress) : 0f;

        if (_closedDoorCollider != null)
            _closedDoorCollider.enabled = true;

        SetSuctionActivity(isOpen ? 1f : hasStarted ? Mathf.Lerp(0.2f, 1f, progress) : 0f);

        if (!EnsureGraph())
            return;

        if (progress != _sampledProgress)
        {
            // Used may occur before 100%; hold the winning pose without jumping to the clip end.
            _openingPlayable.SetTime(progress * (double)_openingClip.length);
            _openingPlayable.SetDone(false);
            _graph.Evaluate(0f);
            _sampledProgress = progress;
        }
        if (_movingPanel != null && _closedDoorCollider != null)
        {
            float displacement = transform.InverseTransformPoint(_movingPanel.position).y - _closedPanelHeight;
            Vector3 offset = _closedDoorCollider.transform.parent.InverseTransformVector(
                transform.TransformVector(Vector3.up * displacement));
            _closedDoorCollider.transform.localPosition = _closedColliderLocalPosition + offset;
        }
    }

    private bool EnsureGraph()
    {
        if (_graph.IsValid())
            return true;
        if (_animator == null || !_animator.isActiveAndEnabled || _openingClip == null || _openingClip.legacy)
            return false;

        _graphAnimator = _animator;
        _previousCullingMode = _animator.cullingMode;
        _previousApplyRootMotion = _animator.applyRootMotion;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        _animator.applyRootMotion = false;

        _graph = PlayableGraph.Create($"Compactor opening: {name}");
        _graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        _openingPlayable = AnimationClipPlayable.Create(_graph, _openingClip);
        _openingPlayable.SetApplyFootIK(false);
        _openingPlayable.SetApplyPlayableIK(false);
        _openingPlayable.SetSpeed(0d);
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(_graph, "Door", _animator);
        output.SetSourcePlayable(_openingPlayable);
        _graph.Play();
        _sampledProgress = -1f;
        return true;
    }

    private void DestroyGraph()
    {
        if (_graph.IsValid())
            _graph.Destroy();

        if (_graphAnimator != null)
        {
            _graphAnimator.cullingMode = _previousCullingMode;
            _graphAnimator.applyRootMotion = _previousApplyRootMotion;
        }

        _graphAnimator = null;
        _openingPlayable = default;
        _sampledProgress = -1f;
    }

    private void SetSuctionActivity(float activity)
    {
        if (_suctionRenderer == null)
            return;

        _propertyBlock ??= new MaterialPropertyBlock();
        _suctionRenderer.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetFloat(ActivityId, activity);
        _suctionRenderer.SetPropertyBlock(_propertyBlock);
    }
}
