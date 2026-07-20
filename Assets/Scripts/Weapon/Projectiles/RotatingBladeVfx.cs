using System.Collections.Generic;
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

    private readonly List<LineRenderer> _bladeLines = new();
    private readonly List<LineRenderer> _slashLines = new();
    private readonly List<float> _slashTimers = new();
    private readonly List<float> _slashDurations = new();
    private readonly List<Color> _slashColors = new();
    private LineRenderer _orbitLine;
    private LineRenderer _thrustLine;

    private float _orbitTimer;
    private float _orbitDuration;
    private float _thrustTimer;
    private float _thrustDuration;
    private int _lastOrbitFrame = -1;
    private int _visibleBladeLineCount;
    private Color _orbitColor = OrbitColor;
    private Color _bladeColor = BladeColor;
    private Color _thrustColor = ThrustColor;

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
        ShowOrbit(ownerOrigin, bladeCenter, hitRadius, duration, BladeColor);
    }

    public void ShowOrbit(Vector3 ownerOrigin, Vector3 bladeCenter, float hitRadius, float duration, Color bladeColor)
    {
        Initialize();
        BeginOrbitFrame();

        ownerOrigin += Vector3.up * 0.14f;
        bladeCenter.y = ownerOrigin.y;
        float orbitRadius = Vector3.Distance(ownerOrigin, bladeCenter);
        if (orbitRadius <= 0.01f)
            return;

        _orbitTimer = _orbitDuration = Mathf.Max(0.01f, duration);
        _orbitColor = WithAlpha(bladeColor, OrbitColor.a);
        _bladeColor = WithAlpha(bladeColor, BladeColor.a);
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

        Vector3 radialDirection = radial.normalized;
        float halfLength = Mathf.Max(0.2f, hitRadius);
        LineRenderer bladeLine = GetNextBladeLine();
        bladeLine.enabled = true;
        bladeLine.loop = false;
        bladeLine.positionCount = 2;
        bladeLine.widthMultiplier = Mathf.Clamp(hitRadius * 0.22f, 0.05f, 0.55f);
        bladeLine.SetPosition(0, bladeCenter - radialDirection * halfLength);
        bladeLine.SetPosition(1, bladeCenter + radialDirection * halfLength);

        SetLineColor(_orbitLine, _orbitColor, 1f);
        SetLineColor(bladeLine, _bladeColor, 1f);
    }

    // Draws the manual slash as an arc matching the damage cone.
    public void ShowSlash(Vector3 origin, Vector3 direction, float range, float coneAngle, float duration)
    {
        ShowSlash(origin, direction, range, coneAngle, duration, SlashColor);
    }

    public void ShowSlash(Vector3 origin, Vector3 direction, float range, float coneAngle, float duration, Color color)
    {
        Initialize();

        direction = GetHorizontalDirection(direction);
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        origin += Vector3.up * 0.18f;
        LineRenderer slashLine = GetNextSlashLine(out int index);
        float safeDuration = Mathf.Max(0.01f, duration);
        _slashTimers[index] = safeDuration;
        _slashDurations[index] = safeDuration;
        _slashColors[index] = color;
        slashLine.enabled = true;
        slashLine.loop = false;
        slashLine.positionCount = SlashSegmentCount + 1;
        slashLine.widthMultiplier = 0.16f;

        float halfAngle = Mathf.Clamp(coneAngle, 1f, 180f) * 0.5f;
        for (int i = 0; i <= SlashSegmentCount; i++)
        {
            float t = i / (float)SlashSegmentCount;
            float yaw = Mathf.Lerp(-halfAngle, halfAngle, t);
            Vector3 pointDirection = Quaternion.AngleAxis(yaw, Vector3.up) * direction;
            slashLine.SetPosition(i, origin + pointDirection * range);
        }

        SetLineColor(slashLine, color, 1f);
    }

    // Draws the active attack as a thick line so the gameplay hit width is readable.
    public void ShowThrust(Vector3 origin, Vector3 direction, float range, float lineWidth, float duration)
    {
        ShowThrust(origin, direction, range, lineWidth, duration, ThrustColor);
    }

    public void ShowThrust(Vector3 origin, Vector3 direction, float range, float lineWidth, float duration, Color color)
    {
        Initialize();

        direction = GetHorizontalDirection(direction);
        if (direction.sqrMagnitude <= 0.0001f)
            return;

        origin += Vector3.up * 0.2f;
        _thrustTimer = _thrustDuration = Mathf.Max(0.01f, duration);
        _thrustColor = color;
        _thrustLine.enabled = true;
        _thrustLine.loop = false;
        _thrustLine.positionCount = 2;
        _thrustLine.widthMultiplier = Mathf.Max(0.05f, lineWidth);
        _thrustLine.SetPosition(0, origin);
        _thrustLine.SetPosition(1, origin + direction * range);

        SetLineColor(_thrustLine, _thrustColor, 1f);
    }

    private void Initialize()
    {
        if (_orbitLine != null)
            return;

        _orbitLine = CreateLine("Blade Orbit", 0.035f);
        _thrustLine = CreateLine("Blade Thrust", 0.6f);
    }

    private void BeginOrbitFrame()
    {
        int frame = Time.frameCount;
        if (_lastOrbitFrame == frame)
            return;

        _lastOrbitFrame = frame;
        _visibleBladeLineCount = 0;
        SetBladeLinesEnabled(false);
    }

    private LineRenderer GetNextBladeLine()
    {
        if (_visibleBladeLineCount >= _bladeLines.Count)
            _bladeLines.Add(CreateLine($"Blade Contact {_bladeLines.Count + 1}", 0.12f));

        return _bladeLines[_visibleBladeLineCount++];
    }

    private LineRenderer GetNextSlashLine(out int index)
    {
        for (int i = 0; i < _slashLines.Count; i++)
        {
            if (!_slashLines[i].enabled)
            {
                index = i;
                return _slashLines[i];
            }
        }

        LineRenderer line = CreateLine($"Blade Slash {_slashLines.Count + 1}", 0.16f);
        _slashLines.Add(line);
        _slashTimers.Add(0f);
        _slashDurations.Add(0f);
        _slashColors.Add(SlashColor);
        index = _slashLines.Count - 1;
        return line;
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
        TickOneShot(_thrustLine, _thrustColor, ref _thrustTimer, _thrustDuration, Time.deltaTime);
        TickSlashLines(Time.deltaTime);
    }

    private void TickOrbit(float deltaTime)
    {
        if (_orbitTimer <= 0f)
        {
            SetEnabled(_orbitLine, false);
            SetBladeLinesEnabled(false);
            return;
        }

        _orbitTimer -= deltaTime;
        float alpha = Mathf.Clamp01(_orbitTimer / Mathf.Max(0.01f, _orbitDuration));
        SetLineColor(_orbitLine, _orbitColor, alpha);
        SetBladeLineColors(alpha);
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

    private void SetBladeLinesEnabled(bool enabled)
    {
        for (int i = 0; i < _bladeLines.Count; i++)
            SetEnabled(_bladeLines[i], enabled);
    }

    private void SetBladeLineColors(float alphaMultiplier)
    {
        for (int i = 0; i < _bladeLines.Count; i++)
            SetLineColor(_bladeLines[i], _bladeColor, alphaMultiplier);
    }

    private void TickSlashLines(float deltaTime)
    {
        for (int i = 0; i < _slashLines.Count; i++)
        {
            LineRenderer line = _slashLines[i];
            if (!line.enabled)
                continue;

            _slashTimers[i] -= deltaTime;
            if (_slashTimers[i] <= 0f)
            {
                SetEnabled(line, false);
                continue;
            }

            float alpha = Mathf.Clamp01(_slashTimers[i] / Mathf.Max(0.01f, _slashDurations[i]));
            SetLineColor(line, _slashColors[i], alpha);
        }
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

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
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
