using UnityEngine;

public sealed class HeadHunterChargeVfx : MonoBehaviour
{
    private const int SegmentCount = 72;
    private const float StartRadius = 0.045f;
    private const float EndRadius = 0.78f;
    private const float StartWidth = 0.018f;
    private const float EndWidth = 0.095f;

    private static readonly Color ChargeColor = new(1f, 1f, 1f, 0.98f);
    private static Material s_lineMaterial;

    private Transform _firePoint;
    private LineRenderer _ring;
    private float _chargeDuration = 1f;
    private float _elapsed;
    private Vector3 _direction = Vector3.forward;

    public static HeadHunterChargeVfx Spawn(Transform firePoint, Vector3 direction, float chargeDuration)
    {
        if (firePoint == null)
            return null;

        GameObject go = new("[HeadHunterChargeVfx]");
        HeadHunterChargeVfx vfx = go.AddComponent<HeadHunterChargeVfx>();
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
        DestroySelf();
    }

    private void Initialize(Transform firePoint, Vector3 direction, float chargeDuration)
    {
        _firePoint = firePoint;
        _chargeDuration = Mathf.Max(0.05f, chargeDuration);
        if (direction.sqrMagnitude > 0.0001f)
            _direction = direction.normalized;

        _ring = CreateRing();
        SetChargeProgress(0f, _direction);
    }

    private LineRenderer CreateRing()
    {
        GameObject child = new("Head Hunter Charge Ring");
        child.transform.SetParent(transform, false);

        LineRenderer ring = child.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = SegmentCount;
        ring.material = GetLineMaterial();
        ring.numCornerVertices = 3;
        ring.numCapVertices = 3;
        ring.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ring.receiveShadows = false;
        return ring;
    }

    private void Update()
    {
        if (_firePoint == null)
        {
            DestroySelf();
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
        if (_ring == null)
            return;

        float eased = 1f - Mathf.Pow(1f - progress, 3f);
        float radius = Mathf.Lerp(StartRadius, EndRadius, eased);
        _ring.widthMultiplier = Mathf.Lerp(StartWidth, EndWidth, progress);

        Color color = ChargeColor;
        color.a *= Mathf.Lerp(0.55f, 1f, progress);
        _ring.startColor = color;
        _ring.endColor = color;

        for (int i = 0; i < SegmentCount; i++)
        {
            float angle = i / (float)SegmentCount * Mathf.PI * 2f;
            _ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
        }
    }

    private void DestroySelf()
    {
        if (Application.isPlaying)
            Destroy(gameObject);
        else
            DestroyImmediate(gameObject);
    }

    private static Material GetLineMaterial()
    {
        if (s_lineMaterial != null)
            return s_lineMaterial;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        s_lineMaterial = new Material(shader)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        return s_lineMaterial;
    }
}
