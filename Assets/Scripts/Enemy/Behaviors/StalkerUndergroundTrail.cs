using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Bounded cosmetic ground disturbance. Never moves the boss or participates in collisions.</summary>
[DisallowMultipleComponent]
public sealed class StalkerUndergroundTrail : MonoBehaviour
{
    [SerializeField] private GameObject _moundPrefab;
    [SerializeField, Range(4, 64)] private int _capacity = 32;
    [SerializeField, Min(.25f), Tooltip("World metres between mounds, based on actual underground travel.")]
    private float _spacing = 3f;
    [SerializeField, Min(.02f)] private float _growSeconds = .12f;
    [SerializeField, Min(0f)] private float _holdSeconds = 1.5f;
    [SerializeField, Min(.02f)] private float _shrinkSeconds = .45f;
    [SerializeField, Min(.1f), Tooltip("World-space footprint; independent of boss root scale.")]
    private float _width = 2.8f;
    [SerializeField, Min(.05f)] private float _height = .6f;
    [SerializeField, Min(.2f)] private float _groundTolerance = 3f;

    private sealed class Mound
    {
        public Transform Transform;
        public Vector3 Scale;
        public float Age;
        public bool Active;
    }
    private Mound[] _pool;
    private Transform _worldRoot;
    private int _next;
    private bool _tracking, _bridging;
    private Vector3 _last, _bridgeFrom, _bridgeTo;
    private float _remainder, _bridgeProgress;
    private LayerMask _mask;
    private Collider _support;
    private readonly System.Random _random = new(59117);
    public int ActiveCount { get; private set; }
    public int EmittedCount { get; private set; }
    public int Capacity => _pool != null ? _pool.Length : Mathf.Clamp(_capacity, 4, 64);
    public Vector3 LastEmissionPosition { get; private set; }

    public void BeginTrack(Vector3 surface, LayerMask mask, Collider support)
    {
        if (!isActiveAndEnabled) return;
        _tracking = true; _bridging = false; _last = surface; _remainder = 0;
        _mask = mask; _support = support;
        Emit(surface);
    }

    public void Track(Vector3 surface, LayerMask mask, Collider support)
    {
        if (!isActiveAndEnabled) return;
        if (!_tracking) { BeginTrack(surface, mask, support); return; }
        _mask = mask; _support = support;
        float distance = Vector3.ProjectOnPlane(surface - _last, Vector3.up).magnitude;
        if (distance < .0001f) { _last = surface; return; }
        float spacing = Mathf.Max(.25f, _spacing);
        int count = Mathf.FloorToInt((_remainder + distance) / spacing);
        // Skip obsolete samples on huge relocations; consume the whole segment without building a backlog.
        int first = Mathf.Max(0, count - Capacity);
        for (int i = first; i < count; i++)
        {
            float travelled = spacing - _remainder + i * spacing;
            Emit(Vector3.Lerp(_last, surface, travelled / distance));
        }
        _remainder = (_remainder + distance) % spacing;
        _last = surface;
    }

    public void BeginBridge(Vector3 from, Vector3 to, LayerMask mask, Collider support)
    {
        if (!isActiveAndEnabled) return;
        if (!_tracking) BeginTrack(from, mask, support);
        else Track(from, mask, support);
        _bridging = true; _bridgeFrom = from; _bridgeTo = to; _bridgeProgress = 0;
    }

    public void AdvanceBridge(float progress)
    {
        if (!_bridging) return;
        _bridgeProgress = Mathf.Max(_bridgeProgress, Mathf.Clamp01(progress));
        Track(Vector3.Lerp(_bridgeFrom, _bridgeTo, _bridgeProgress), _mask, _support);
    }

    public void EndTravel(bool finishBridge = false)
    {
        if (finishBridge) AdvanceBridge(1);
        _tracking = _bridging = false;
    }

    private void Update() => Tick(Time.deltaTime);

    public void Tick(float deltaTime)
    {
        if (_pool == null) return;
        float grow = Mathf.Max(.02f, _growSeconds), shrink = Mathf.Max(.02f, _shrinkSeconds);
        foreach (var mound in _pool)
        {
            if (!mound.Active) continue;
            if (mound.Transform == null) { mound.Active = false; ActiveCount--; continue; }
            mound.Age += Mathf.Max(0, deltaTime);
            float amount = mound.Age < grow ? Mathf.SmoothStep(0, 1, mound.Age / grow)
                : 1f - Mathf.SmoothStep(0, 1, (mound.Age - grow - _holdSeconds) / shrink);
            if (mound.Age >= grow + _holdSeconds + shrink)
            {
                mound.Transform.gameObject.SetActive(false); mound.Active = false; ActiveCount--;
            }
            else mound.Transform.localScale = mound.Scale * amount;
        }
    }

    public void ResetTrail()
    {
        _tracking = _bridging = false; _remainder = 0; _next = 0;
        ActiveCount = EmittedCount = 0;
        if (_pool == null) return;
        foreach (var mound in _pool)
        {
            mound.Active = false;
            // Scene unloading can destroy the independent pool before its owner is disabled.
            if (mound.Transform != null) mound.Transform.gameObject.SetActive(false);
        }
    }

    private void Emit(Vector3 sample)
    {
        if (_moundPrefab == null || !TryGround(sample, out RaycastHit hit)) return;
        float width = _width * Mathf.Lerp(.85f, 1.15f, (float)_random.NextDouble());
        // Reject or reduce unsupported edges rather than leaving a dome floating over a platform ledge.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            bool supported = true;
            for (int axis = 0; axis < 4; axis++)
            {
                Vector3 offset = Quaternion.AngleAxis(axis * 90, Vector3.up) * Vector3.right * width * .42f;
                if (!hit.collider.Raycast(new Ray(hit.point + offset + Vector3.up * 2, Vector3.down), out _, 4))
                    supported = false;
            }
            if (supported) break;
            width *= .6f;
            if (attempt == 2) return;
        }
        EnsurePool();
        var mound = _pool[_next]; _next = (_next + 1) % _pool.Length;
        if (!mound.Active) ActiveCount++;
        mound.Active = true; mound.Age = 0;
        mound.Scale = new Vector3(width, _height * Mathf.Lerp(.85f, 1.15f, (float)_random.NextDouble()), width);
        mound.Transform.SetPositionAndRotation(hit.point - hit.normal * .025f,
            Quaternion.FromToRotation(Vector3.up, hit.normal) * Quaternion.Euler(0, (float)_random.NextDouble() * 360, 0));
        mound.Transform.localScale = Vector3.zero;
        mound.Transform.gameObject.SetActive(true);
        LastEmissionPosition = hit.point; EmittedCount++;
    }

    private bool TryGround(Vector3 sample, out RaycastHit hit)
    {
        Ray ray = new(sample + Vector3.up * _groundTolerance, Vector3.down);
        float range = _groundTolerance * 2;
        if (_support != null && !_support.isTrigger && _support.Raycast(ray, out hit, range))
            return hit.normal.y > .5f;
        int terrain = LayerMask.NameToLayer("Terrain");
        int terrainMask = terrain >= 0 ? _mask.value & (1 << terrain) : 0;
        if (Physics.Raycast(ray, out hit, range, terrainMask, QueryTriggerInteraction.Ignore)) return hit.normal.y > .5f;
        return Physics.Raycast(ray, out hit, range, _mask, QueryTriggerInteraction.Ignore) && hit.normal.y > .5f;
    }

    private void EnsurePool()
    {
        if (_pool != null && _worldRoot != null) return;
        ActiveCount = 0; _next = 0;
        _worldRoot = new GameObject(name + " underground mound pool").transform;
        SceneManager.MoveGameObjectToScene(_worldRoot.gameObject, gameObject.scene);
        _pool = new Mound[Mathf.Clamp(_capacity, 4, 64)];
        for (int i = 0; i < _pool.Length; i++)
        {
            var item = Instantiate(_moundPrefab, _worldRoot);
            item.SetActive(false); _pool[i] = new Mound { Transform = item.transform };
        }
    }

    private void OnDisable() => ResetTrail();
    private void OnDestroy()
    {
        if (_worldRoot == null) return;
        if (Application.isPlaying) Destroy(_worldRoot.gameObject); else DestroyImmediate(_worldRoot.gameObject);
    }
}
