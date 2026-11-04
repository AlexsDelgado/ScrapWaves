using UnityEngine;

/// <summary>
/// Observes the player's physics body crossing the finite doorway from local +Z to -Z.
/// Samples before the next door step, so the path is checked against the panel poses
/// that actually bounded the completed physics step, not an interpolated render pose.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(ExitDoor))]
[DefaultExecutionOrder(-90)]
public sealed class ExitDoorTraversal : MonoBehaviour
{
    [SerializeField] private ExitDoor _door;
    [SerializeField] private BoxCollider _panel;
    [SerializeField] private Bounds _aperture = new(new Vector3(0f, 1.67f, .36f), new Vector3(2.52f, 2.65f, .24f));

    private ExitDoor _subscribedDoor;
    private PlayerMovement _player;
    private Rigidbody _body;
    private CapsuleCollider _capsule;
    private PlayerHealth _health;
    private Vector3 _previousCenter;
    private Vector3 _previousExtents;
    private Vector3 _previousRadii;
    private Vector3 _previousSegment;
    private bool _hasSample;
    private bool _approachedFromArena;

    public ExitDoor Door => _door;
    public Bounds Aperture => _aperture;
    public BoxCollider Panel => _panel;

    public void Configure(ExitDoor door, BoxCollider panel, Bounds aperture)
    {
        Unsubscribe();
        _door = door;
        _panel = panel;
        _aperture = aperture;
        ResetTracking();
        if (isActiveAndEnabled) Subscribe();
    }

    private void OnEnable()
    {
        if (_door == null) _door = GetComponent<ExitDoor>();
        ResetTracking();
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
        BindPlayer(null);
        ResetTracking();
    }

    private void Subscribe()
    {
        Unsubscribe();
        if (_door == null) return;
        _subscribedDoor = _door;
        _subscribedDoor.OnChargeStarted += ResetTracking;
        _subscribedDoor.OnChargeReset += ResetTracking;
    }

    private void Unsubscribe()
    {
        if (_subscribedDoor == null) return;
        _subscribedDoor.OnChargeStarted -= ResetTracking;
        _subscribedDoor.OnChargeReset -= ResetTracking;
        _subscribedDoor = null;
    }

    public void ResetTracking()
    {
        _hasSample = false;
        _approachedFromArena = false;
    }

    private void FixedUpdate()
    {
        Transform player = PlayerMovement.PlayerTransform;
        SamplePlayer(player != null ? player.GetComponent<PlayerMovement>() : null);
    }

    private void Update()
    {
        // FixedUpdate stops at timeScale zero. Keep observing paused repositioning so
        // it cannot be mistaken for a physics traversal when simulation resumes.
        if (!GameplayPause.IsUiPaused && Time.timeScale > 0f) return;
        Transform player = PlayerMovement.PlayerTransform;
        SamplePlayer(player != null ? player.GetComponent<PlayerMovement>() : null);
    }

    /// <summary>Samples a completed physics pose. Used by the motor loop and focused physics tests.</summary>
    public bool SamplePlayer(PlayerMovement player)
    {
        if (_player != player) BindPlayer(player);
        if (!isActiveAndEnabled || _door == null || !_door.isActiveAndEnabled
            || player == null || player.transform != PlayerMovement.PlayerTransform || !player.isActiveAndEnabled
            || _body == null || _capsule == null || !_capsule.enabled || _capsule.isTrigger
            || _capsule.attachedRigidbody != _body || _health == null || !_health.IsAlive
            || _panel == null || !_panel.enabled || !_panel.gameObject.activeInHierarchy
            || _aperture.size.x <= 0f || _aperture.size.y <= 0f || _aperture.size.z <= 0f)
        {
            ResetTracking();
            return false;
        }

        GetCapsulePose(out Vector3 center, out Vector3 extents, out Vector3 radii, out Vector3 segment);
        if (!_door.CanTraverse)
        {
            // A stationary pause holds a legitimate crossing. Repositioning while paused
            // must not turn the old arena-side sample into a new crossing on resume.
            bool stationaryPause = (_door.State == ExitDoorState.Charging || _door.State == ExitDoorState.Ready)
                && GameManager.Instance != null && GameManager.Instance.IsPlaying
                && (GameplayPause.IsUiPaused || Time.timeScale <= 0f)
                && (!_hasSample || ((center - _previousCenter).sqrMagnitude <= .0000000001f
                    && (extents - _previousExtents).sqrMagnitude <= .0000000001f));
            if (!stationaryPause)
                ResetTracking();
            return false;
        }

        float panelTop = PanelBoundsInDoorSpace().max.y;
        bool fullyInArena = center.z - extents.z >= _aperture.max.z;

        if (!_hasSample)
        {
            _approachedFromArena = fullyInArena;
            StoreSample(center, extents, radii, segment);
            return false;
        }

        bool crossed = false;
        if (_approachedFromArena)
        {
            Vector3 sweptExtents = Vector3.Max(extents, _previousExtents);
            Vector3 sweptRadii = Vector3.Max(radii, _previousRadii);
            float halfSegment = Mathf.Max(Mathf.Abs(segment.y), Mathf.Abs(_previousSegment.y));
            bool upright = IsUpright(segment) && IsUpright(_previousSegment);
            if (!PathFitsOpening(_previousCenter, center, sweptExtents, sweptRadii, halfSegment, upright, panelTop))
                _approachedFromArena = false;
            else if (center.z + extents.z <= _aperture.min.z && center.z < _previousCenter.z)
            {
                // Completion occurs only after the trailing side of the movement capsule clears.
                crossed = _door.TryCompleteTraversal(player);
                _approachedFromArena = false;
            }
        }

        // Returning completely to the arena permits a fresh attempt after a rejected path.
        if (fullyInArena) _approachedFromArena = true;
        StoreSample(center, extents, radii, segment);
        return crossed;
    }

    private void BindPlayer(PlayerMovement player)
    {
        if (_health != null) _health.OnReset -= ResetTracking;
        _player = player;
        _body = player != null ? player.GetComponent<Rigidbody>() : null;
        // The root movement capsule is authoritative; the larger child hurtbox is a trigger.
        _capsule = player != null ? player.GetComponent<CapsuleCollider>() : null;
        _health = player != null ? player.GetComponent<PlayerHealth>() : null;
        if (_health != null) _health.OnReset += ResetTracking;
        ResetTracking();
    }

    private void StoreSample(Vector3 center, Vector3 extents, Vector3 radii, Vector3 segment)
    {
        _previousCenter = center;
        _previousExtents = extents;
        _previousRadii = radii;
        _previousSegment = segment;
        _hasSample = true;
    }

    private void GetCapsulePose(out Vector3 center, out Vector3 extents, out Vector3 radii, out Vector3 segment)
    {
        Vector3 scale = _capsule.transform.lossyScale;
        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
        int axis = _capsule.direction;
        float radius = _capsule.radius * Mathf.Max(scale[(axis + 1) % 3], scale[(axis + 2) % 3]);
        float halfSegment = Mathf.Max(0f, _capsule.height * scale[axis] * .5f - radius);
        Vector3 localAxis = Vector3.zero;
        localAxis[axis] = 1f;
        Vector3 worldAxis = _body.rotation * localAxis;
        Vector3 worldCenter = _body.position + _body.rotation * Vector3.Scale(_capsule.center, scale);
        Matrix4x4 inverse = _door.transform.worldToLocalMatrix;
        center = inverse.MultiplyPoint3x4(worldCenter);
        segment = inverse.MultiplyVector(worldAxis * halfSegment);
        // Projection accounts for the gameplay door's non-uniform (30,30,15) scale.
        radii = new Vector3(
            radius * new Vector3(inverse.m00, inverse.m01, inverse.m02).magnitude,
            radius * new Vector3(inverse.m10, inverse.m11, inverse.m12).magnitude,
            radius * new Vector3(inverse.m20, inverse.m21, inverse.m22).magnitude);
        extents = radii + new Vector3(Mathf.Abs(segment.x), Mathf.Abs(segment.y), Mathf.Abs(segment.z));
    }

    private Bounds PanelBoundsInDoorSpace()
    {
        Matrix4x4 matrix = _door.transform.worldToLocalMatrix * _panel.transform.localToWorldMatrix;
        Vector3 half = _panel.size * .5f;
        Bounds result = new(matrix.MultiplyPoint3x4(_panel.center), Vector3.zero);
        for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                    result.Encapsulate(matrix.MultiplyPoint3x4(_panel.center + Vector3.Scale(half, new Vector3(x, y, z))));
        return result;
    }

    private static bool IsUpright(Vector3 segment) => Mathf.Abs(segment.x) <= .00001f && Mathf.Abs(segment.z) <= .00001f;

    private bool PathFitsOpening(Vector3 from, Vector3 to, Vector3 extents, Vector3 radii,
        float halfSegment, bool upright, float panelTop)
    {
        // Clip the swept body path to the doorway slab. Testing the whole segment prevents
        // a fast dash from skipping a thin trigger or arriving behind the frame from the side.
        float start = 0f;
        float end = 1f;
        float dz = to.z - from.z;
        float front = _aperture.max.z + extents.z;
        float back = _aperture.min.z - extents.z;
        if (Mathf.Abs(dz) < .000001f)
        {
            if (from.z > front || from.z < back) return true;
        }
        else
        {
            float a = (back - from.z) / dz;
            float b = (front - from.z) / dz;
            start = Mathf.Max(start, Mathf.Min(a, b));
            end = Mathf.Min(end, Mathf.Max(a, b));
            if (start > end) return true;
        }

        // The current collider transform was installed before the completed physics step.
        // It stayed at that pose for that simulation; interpolating an older panel top
        // here would reject a jump that the actual collider had already allowed through.
        if (!upright || radii.z <= .000001f)
            return BoxFitsAt(start) && BoxFitsAt(end);

        if (Mathf.Abs(dz) < .000001f)
            return CapsuleFitsInterval(start, end);

        // Outside either slab face, only a rounded cap intersects the doorway. Requiring
        // the entire capsule AABB there would discard valid rising and falling jumps.
        float firstFace = Mathf.Clamp((_aperture.min.z - from.z) / dz, start, end);
        float secondFace = Mathf.Clamp((_aperture.max.z - from.z) / dz, start, end);
        if (firstFace > secondFace) (firstFace, secondFace) = (secondFace, firstFace);
        return CapsuleFitsInterval(start, firstFace)
            && CapsuleFitsInterval(firstFace, secondFace)
            && CapsuleFitsInterval(secondFace, end);

        bool CapsuleFitsInterval(float a, float b)
        {
            float middleZ = Mathf.LerpUnclamped(from.z, to.z, (a + b) * .5f);
            float offset = 0f;
            float slope = 0f;
            if (middleZ > _aperture.max.z)
            {
                offset = (from.z - _aperture.max.z) / radii.z;
                slope = dz / radii.z;
            }
            else if (middleZ < _aperture.min.z)
            {
                offset = (from.z - _aperture.min.z) / radii.z;
                slope = dz / radii.z;
            }

            Vector3 movement = to - from;
            return CapsuleMarginFits(from.x - _aperture.min.x, movement.x, radii.x, offset, slope, a, b)
                && CapsuleMarginFits(_aperture.max.x - from.x, -movement.x, radii.x, offset, slope, a, b)
                && CapsuleMarginFits(from.y - halfSegment - _aperture.min.y, movement.y, radii.y, offset, slope, a, b)
                && CapsuleMarginFits(_aperture.max.y - from.y - halfSegment, -movement.y, radii.y, offset, slope, a, b)
                && CapsuleMarginFits(from.y - halfSegment - panelTop, movement.y, radii.y, offset, slope, a, b);
        }

        // Conservative support for non-upright authoring; the actual player and yaw-only
        // gameplay/sandbox doors use the exact rounded cross-section above.
        bool BoxFitsAt(float t)
        {
            Vector3 position = Vector3.LerpUnclamped(from, to, t);
            const float tolerance = .00001f;
            return position.x - extents.x >= _aperture.min.x - tolerance
                && position.x + extents.x <= _aperture.max.x + tolerance
                && position.y - extents.y >= _aperture.min.y - tolerance
                && position.y + extents.y <= _aperture.max.y + tolerance
                && position.y - extents.y >= panelTop - tolerance;
        }
    }

    private static bool CapsuleMarginFits(float intercept, float velocity, float radius,
        float offset, float slope, float start, float end)
    {
        // margin(t) = intercept + velocity*t - radius*sqrt(1 - (offset+slope*t)^2).
        // It is convex on each cap interval. Its endpoints and one stationary point
        // therefore include the true worst clearance, even during a fast diagonal jump.
        if (!Fits(start) || !Fits(end)) return false;
        if (radius <= .000001f || Mathf.Abs(slope) <= .000001f) return true;

        double k = -(double)velocity / ((double)radius * slope);
        double stationaryU = k / System.Math.Sqrt(1d + k * k);
        double stationary = (stationaryU - offset) / slope;
        return stationary <= start || stationary >= end || Fits((float)stationary);

        bool Fits(float time)
        {
            float normalizedZ = Mathf.Clamp(offset + slope * time, -1f, 1f);
            float support = radius * Mathf.Sqrt(Mathf.Max(0f, 1f - normalizedZ * normalizedZ));
            return intercept + velocity * time - support >= -.00001f;
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(.2f, 1f, .4f, .8f);
        Gizmos.matrix = (_door != null ? _door.transform : transform).localToWorldMatrix;
        Gizmos.DrawWireCube(_aperture.center, _aperture.size);
        Gizmos.DrawLine(new Vector3(0f, _aperture.center.y, _aperture.max.z + .5f),
            new Vector3(0f, _aperture.center.y, _aperture.min.z - .5f));
    }
#endif
}
