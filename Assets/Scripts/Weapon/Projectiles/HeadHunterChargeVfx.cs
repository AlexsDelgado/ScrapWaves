using UnityEngine;

/// <summary>
/// Debug/accessibility fallback used only when no production feedback sink is installed.
/// Production Head Hunter charge presentation is an authored pooled prefab.
/// </summary>
public sealed class HeadHunterChargeVfx : MonoBehaviour
{
    private const int SegmentCount = 72;
    private static Material s_debugLineMaterial;

    private Transform _firePoint;
    private LineRenderer _ring;
    private float _chargeDuration = 1f;
    private float _elapsed;
    private Vector3 _direction = Vector3.forward;

    public static HeadHunterChargeVfx Spawn(Transform firePoint, Vector3 direction, float chargeDuration)
    {
        if (firePoint == null)
            return null;

        GameObject fallback = new("[Debug HeadHunter Charge]");
        HeadHunterChargeVfx vfx = fallback.AddComponent<HeadHunterChargeVfx>();
        vfx.Initialize(firePoint, direction, chargeDuration);
        return vfx;
    }

    public void SetChargeProgress(float progress, Vector3 direction)
    {
        if (direction.sqrMagnitude > 0.0001f)
            _direction = direction.normalized;
        UpdateTransform();
        DrawRing(Mathf.Clamp01(progress));
    }

    public void Dismiss()
    {
        if (Application.isPlaying)
            Destroy(gameObject);
        else
            DestroyImmediate(gameObject);
    }

    private void Initialize(Transform firePoint, Vector3 direction, float chargeDuration)
    {
        _firePoint = firePoint;
        _chargeDuration = Mathf.Max(0.05f, chargeDuration);
        if (direction.sqrMagnitude > 0.0001f)
            _direction = direction.normalized;

        GameObject child = new("Debug Charge Radius");
        child.transform.SetParent(transform, false);
        _ring = child.AddComponent<LineRenderer>();
        _ring.useWorldSpace = false;
        _ring.loop = true;
        _ring.positionCount = SegmentCount;
        _ring.sharedMaterial = GetDebugMaterial();
        _ring.numCornerVertices = 2;
        _ring.numCapVertices = 2;
        _ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _ring.receiveShadows = false;
        SetChargeProgress(0f, _direction);
    }

    private void Update()
    {
        if (_firePoint == null)
        {
            Dismiss();
            return;
        }

        _elapsed += Time.deltaTime;
        SetChargeProgress(_elapsed / _chargeDuration, _direction);
    }

    private void UpdateTransform()
    {
        if (_firePoint == null)
            return;
        transform.position = _firePoint.position;
        Vector3 up = Mathf.Abs(Vector3.Dot(_direction, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
        transform.rotation = Quaternion.LookRotation(_direction, up);
    }

    private void DrawRing(float progress)
    {
        float eased = 1f - Mathf.Pow(1f - progress, 3f);
        float radius = Mathf.Lerp(0.045f, 0.78f, eased);
        _ring.widthMultiplier = Mathf.Lerp(0.018f, 0.095f, progress);
        Color color = new(1f, 1f, 1f, Mathf.Lerp(0.55f, 0.98f, progress));
        _ring.startColor = color;
        _ring.endColor = color;
        for (int i = 0; i < SegmentCount; i++)
        {
            float angle = i / (float)SegmentCount * Mathf.PI * 2f;
            _ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
        }
    }

    private static Material GetDebugMaterial()
    {
        if (s_debugLineMaterial != null)
            return s_debugLineMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        s_debugLineMaterial = new Material(shader)
        {
            name = "Head Hunter Debug Radius",
            hideFlags = HideFlags.HideAndDontSave
        };
        return s_debugLineMaterial;
    }
}
