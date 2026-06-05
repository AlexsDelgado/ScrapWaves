using UnityEngine;

public sealed class RotatingBladeVfx : MonoBehaviour
{
    private const int OrbitSegmentCount = 72;
    private const int SlashSegmentCount = 28;

    private static readonly Color OrbitColor = new(0.45f, 0.95f, 1f, 0.35f);
    private static readonly Color BladeColor = new(0.7f, 1f, 1f, 0.95f);
    private static readonly Color SlashColor = new(0.55f, 1f, 0.75f, 0.9f);
    private static readonly Color ThrustColor = new(0.25f, 0.95f, 1f, 0.85f);
    private static Material s_lineMaterial;

    private LineRenderer _orbitLine;
    private LineRenderer _bladeLine;
    private LineRenderer _slashLine;
    private LineRenderer _thrustLine;

    private float _orbitTimer;
    private float _orbitDuration;
    private float _slashTimer;
    private float _slashDuration;
    private float _thrustTimer;
    private float _thrustDuration;

    public static RotatingBladeVfx Create()
    {
        GameObject go = new("[RotatingBladeVfx]");
        RotatingBladeVfx vfx = go.AddComponent<RotatingBladeVfx>();
        vfx.Initialize();
        return vfx;
    }

    // Shows the actual automatic contact point plus a subtle orbit guide.
    public void ShowOrbit(Vector3 ownerOrigin, Vector3 bladeCenter, float hitRadius, float duration)
    {
        Initialize();

        ownerOrigin += Vector3.up * 0.14f;
        bladeCenter.y = ownerOrigin.y;
        float orbitRadius = Vector3.Distance(ownerOrigin, bladeCenter);
        if (orbitRadius <= 0.01f)
            return;

        _orbitTimer = _orbitDuration = Mathf.Max(0.01f, duration);
        _orbitLine.enabled = true;
        _orbitLine.loop = true;
        _orbitLine.positionCount = OrbitSegmentCount;
        _orbitLine.widthMultiplier = 0.035f;

        for (int i = 0; i < OrbitSegmentCount; i++)
        {
            float angle = i / (float)OrbitSegmentCount * Mathf.PI * 2f;
            Vector3 point = ownerOrigin + new Vector3(Mathf.Cos(angle) * orbitRadius, 0f, Mathf.Sin(angle) * orbitRadius);
            _orbitLine.SetPosition(i, point);
        }

        Vector3 radial = bladeCenter - ownerOrigin;
        radial.y = 0f;
        if (radial.sqrMagnitude <= 0.0001f)
            radial = Vector3.forward;

        Vector3 tangent = Vector3.Cross(Vector3.up, radial).normalized;
        float halfLength = Mathf.Max(0.2f, hitRadius);
        _bladeLine.enabled = true;
        _bladeLine.loop = false;
        _bladeLine.positionCount = 2;
        _bladeLine.widthMultiplier = Mathf.Clamp(hitRadius * 0.22f, 0.05f, 0.22f);
        _bladeLine.SetPosition(0, bladeCenter - tangent * halfLength);
        _bladeLine.SetPosition(1, bladeCenter + tangent * halfLength);

        SetLineColor(_orbitLine, OrbitColor, 1f);
        SetLineColor(_bladeLine, BladeColor, 1f);
    }

    // Draws the manual slash as an arc matching the damage cone.
    public void ShowSlash(Vector3 origin, Vector3 direction, float range, float coneAngle, float duration)
    {
        Initialize();

        direction = GetHorizontalDirection(direction);
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        origin += Vector3.up * 0.18f;
        _slashTimer = _slashDuration = Mathf.Max(0.01f, duration);
        _slashLine.enabled = true;
        _slashLine.loop = false;
        _slashLine.positionCount = SlashSegmentCount + 1;
        _slashLine.widthMultiplier = 0.16f;

        float halfAngle = Mathf.Clamp(coneAngle, 1f, 180f) * 0.5f;
        for (int i = 0; i <= SlashSegmentCount; i++)
        {
            float t = i / (float)SlashSegmentCount;
            float yaw = Mathf.Lerp(-halfAngle, halfAngle, t);
            Vector3 pointDirection = Quaternion.AngleAxis(yaw, Vector3.up) * direction;
            _slashLine.SetPosition(i, origin + pointDirection * range);
        }

        SetLineColor(_slashLine, SlashColor, 1f);
    }

    // Draws the active attack as a thick line so the gameplay hit width is readable.
    public void ShowThrust(Vector3 origin, Vector3 direction, float range, float lineWidth, float duration)
    {
        Initialize();

        direction = GetHorizontalDirection(direction);
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        origin += Vector3.up * 0.2f;
        _thrustTimer = _thrustDuration = Mathf.Max(0.01f, duration);
        _thrustLine.enabled = true;
        _thrustLine.loop = false;
        _thrustLine.positionCount = 2;
        _thrustLine.widthMultiplier = Mathf.Max(0.05f, lineWidth);
        _thrustLine.SetPosition(0, origin);
        _thrustLine.SetPosition(1, origin + direction * range);

        SetLineColor(_thrustLine, ThrustColor, 1f);
    }

    private void Initialize()
    {
        if (_orbitLine != null)
            return;

        _orbitLine = CreateLine("Blade Orbit", 0.035f);
        _bladeLine = CreateLine("Blade Contact", 0.12f);
        _slashLine = CreateLine("Blade Slash", 0.16f);
        _thrustLine = CreateLine("Blade Thrust", 0.6f);
    }

    private LineRenderer CreateLine(string childName, float width)
    {
        GameObject lineObject = new(childName);
        lineObject.transform.SetParent(transform, false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.material = GetLineMaterial();
        line.widthMultiplier = width;
        line.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 1f));
        line.numCornerVertices = 3;
        line.numCapVertices = 3;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;
        return line;
    }

    private void Update()
    {
        TickOrbit(Time.deltaTime);
        TickOneShot(_slashLine, SlashColor, ref _slashTimer, _slashDuration, Time.deltaTime);
        TickOneShot(_thrustLine, ThrustColor, ref _thrustTimer, _thrustDuration, Time.deltaTime);
    }

    private void TickOrbit(float deltaTime)
    {
        if (_orbitTimer <= 0f)
        {
            SetEnabled(_orbitLine, false);
            SetEnabled(_bladeLine, false);
            return;
        }

        _orbitTimer -= deltaTime;
        float alpha = Mathf.Clamp01(_orbitTimer / Mathf.Max(0.01f, _orbitDuration));
        SetLineColor(_orbitLine, OrbitColor, alpha);
        SetLineColor(_bladeLine, BladeColor, alpha);
    }

    private void TickOneShot(LineRenderer line, Color color, ref float timer, float duration, float deltaTime)
    {
        if (timer <= 0f)
        {
            SetEnabled(line, false);
            return;
        }

        timer -= deltaTime;
        float alpha = Mathf.Clamp01(timer / Mathf.Max(0.01f, duration));
        SetLineColor(line, color, alpha);
    }

    private static void SetEnabled(LineRenderer line, bool enabled)
    {
        if (line != null)
            line.enabled = enabled;
    }

    private static void SetLineColor(LineRenderer line, Color color, float alphaMultiplier)
    {
        if (line == null)
            return;

        Color visible = color;
        visible.a *= alphaMultiplier;
        line.startColor = visible;
        line.endColor = visible;
    }

    private static Vector3 GetHorizontalDirection(Vector3 direction)
    {
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
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

        s_lineMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        return s_lineMaterial;
    }
}
