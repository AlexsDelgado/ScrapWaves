using UnityEngine;

[DisallowMultipleComponent]
public sealed class MortarShellImpact : MonoBehaviour
{
    private const int ArcSegments = 18;

    private Vector3 _start;
    private Vector3 _target;
    private float _travelTime;
    private float _elapsed;
    private float _arcHeight;
    private float _explosionRadius;
    private float _falloff;
    private int _damage;
    private float _knockback;
    private LineRenderer _line;
    private readonly Vector3[] _arcPoints = new Vector3[ArcSegments + 1];

    public static MortarShellImpact Launch(
        Vector3 start,
        Vector3 target,
        float travelTime,
        float arcHeight,
        int damage,
        float explosionRadius,
        float falloff,
        float knockback)
    {
        GameObject go = new GameObject("MortarShellImpact");
        MortarShellImpact shell = go.AddComponent<MortarShellImpact>();
        shell.Configure(start, target, travelTime, arcHeight, damage, explosionRadius, falloff, knockback);
        return shell;
    }

    private void Configure(
        Vector3 start,
        Vector3 target,
        float travelTime,
        float arcHeight,
        int damage,
        float explosionRadius,
        float falloff,
        float knockback)
    {
        _start = start;
        _target = target;
        _travelTime = Mathf.Max(0.05f, travelTime);
        _arcHeight = Mathf.Max(0f, arcHeight);
        _damage = Mathf.Max(1, damage);
        _explosionRadius = Mathf.Max(0f, explosionRadius);
        _falloff = Mathf.Clamp01(falloff);
        _knockback = Mathf.Max(0f, knockback);
        transform.position = _start;
        BuildLineRenderer();
        UpdateArcVisual();
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;
        float t = Mathf.Clamp01(_elapsed / _travelTime);
        transform.position = GetArcPoint(t);

        if (t >= 1f)
            Detonate();
    }

    private void BuildLineRenderer()
    {
        _line = gameObject.AddComponent<LineRenderer>();
        _line.useWorldSpace = true;
        _line.widthMultiplier = 0.06f;
        _line.positionCount = _arcPoints.Length;
        _line.material = new Material(Shader.Find("Sprites/Default"));
        _line.startColor = new Color(1f, 0.85f, 0.2f, 0.9f);
        _line.endColor = new Color(1f, 0.35f, 0.05f, 0.25f);
    }

    private void UpdateArcVisual()
    {
        for (int i = 0; i < _arcPoints.Length; i++)
        {
            float t = i / (float)(_arcPoints.Length - 1);
            _arcPoints[i] = GetArcPoint(t);
        }

        _line.SetPositions(_arcPoints);
    }

    private Vector3 GetArcPoint(float t)
    {
        Vector3 point = Vector3.Lerp(_start, _target, t);
        point.y += Mathf.Sin(t * Mathf.PI) * _arcHeight;
        return point;
    }

    private void Detonate()
    {
        ExplosionRadiusVfx.Spawn(_target, _explosionRadius);

        Collider[] hits = Physics.OverlapSphere(_target, _explosionRadius);
        for (int i = 0; i < hits.Length; i++)
        {
            IDamageable damageable = hits[i].GetComponentInParent<IDamageable>();
            if (damageable == null)
                continue;

            float distance = Vector3.Distance(_target, hits[i].transform.position);
            float t = _explosionRadius <= 0f ? 1f : Mathf.Clamp01(distance / _explosionRadius);
            float falloffScale = Mathf.Lerp(1f, 1f - _falloff, t);
            int finalDamage = Mathf.Max(1, Mathf.RoundToInt(_damage * falloffScale));
            if (damageable.ApplyDamage(finalDamage))
                EnemyKnockbackReceiver.TryApply(damageable, _target, _knockback * falloffScale);
        }

        Destroy(gameObject);
    }

    private void OnDrawGizmos()
    {
        if (_explosionRadius <= 0f)
            return;

        Gizmos.color = new Color(1f, 0.62f, 0.05f, 0.8f);
        Gizmos.DrawWireSphere(_target, _explosionRadius);
    }
}
