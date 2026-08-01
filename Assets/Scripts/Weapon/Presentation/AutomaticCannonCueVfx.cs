using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public enum AutomaticCannonVfxStyle
{
    AutomaticShot,
    ManualShot,
    Impact,
    CriticalImpact,
    WeakPointImpact,
    BaseActive
}

[DisallowMultipleComponent]
public sealed class AutomaticCannonCueVfx : MonoBehaviour, IWeaponVfxPrewarm
{
    private const int RingSegments = 24;

    [SerializeField] private AutomaticCannonVfxStyle _style;
    [SerializeField] private Color _primaryColor = new(1f, 0.42f, 0.04f, 1f);
    [SerializeField] private Color _coreColor = new(1f, 0.94f, 0.62f, 1f);
    [SerializeField] private Texture2D _muzzleFlashTexture;
    [SerializeField] private Texture2D _sparkTexture;
    [SerializeField, Min(0.02f)] private float _lifetime = 0.16f;
    [SerializeField, Min(0.05f)] private float _size = 1f;

    private readonly List<LineRenderer> _lines = new();
    private readonly List<ParticleSystem> _particleSystems = new();
    private readonly List<Material> _runtimeMaterials = new();
    private static Texture2D s_smokeTexture;
    private Material _material;
    private float _elapsed;
    private bool _built;

    public AutomaticCannonVfxStyle Style => _style;
    public int RuntimeLineCount => _lines.Count;
    public int RuntimeParticleSystemCount => _particleSystems.Count;
    public bool HasAuthoredTextures => _muzzleFlashTexture != null && _sparkTexture != null;

    public void Prewarm()
    {
        EnsureBuilt();
        SetLinesEnabled(false);
    }

    private void Awake()
    {
        EnsureBuilt();
    }

    private void OnEnable()
    {
        EnsureBuilt();
        _elapsed = 0f;
        SetLinesEnabled(true);
        RenderFrame(0f);
    }

    private void Update()
    {
        _elapsed += Time.unscaledDeltaTime;
        RenderFrame(Mathf.Clamp01(_elapsed / Mathf.Max(0.02f, _lifetime)));
    }

    private void OnDisable()
    {
        SetLinesEnabled(false);
    }

    private void OnDestroy()
    {
        DestroyRuntimeObject(_material);

        for (int i = 0; i < _runtimeMaterials.Count; i++)
            DestroyRuntimeObject(_runtimeMaterials[i]);
    }

    private void OnValidate()
    {
        _lifetime = Mathf.Max(0.02f, _lifetime);
        _size = Mathf.Max(0.05f, _size);
    }

    private void EnsureBuilt()
    {
        if (_built)
            return;

        _built = true;
        _material = CreateLineMaterial();

        switch (_style)
        {
            case AutomaticCannonVfxStyle.AutomaticShot:
                BuildLines(0, loopFirstTwo: false);
                break;
            case AutomaticCannonVfxStyle.ManualShot:
                BuildLines(0, loopFirstTwo: false);
                break;
            case AutomaticCannonVfxStyle.Impact:
                BuildLines(3, loopFirstTwo: false);
                break;
            case AutomaticCannonVfxStyle.CriticalImpact:
                BuildLines(5, loopFirstTwo: false);
                break;
            case AutomaticCannonVfxStyle.WeakPointImpact:
                BuildLines(4, loopFirstTwo: false);
                break;
            case AutomaticCannonVfxStyle.BaseActive:
                BuildLines(5, loopFirstTwo: true);
                break;
        }

        BuildParticleLayers();
    }

    private void BuildLines(int count, bool loopFirstTwo)
    {
        for (int i = 0; i < count; i++)
        {
            bool loop = loopFirstTwo && i < 2;
            LineRenderer line = CreateLine($"Cannon {_style} Line {i + 1}", loop);
            if (loop)
                line.positionCount = RingSegments;
            _lines.Add(line);
        }
    }

    private LineRenderer CreateLine(string objectName, bool loop)
    {
        GameObject lineObject = new(objectName);
        lineObject.transform.SetParent(transform, false);

        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        line.useWorldSpace = false;
        line.loop = loop;
        line.positionCount = loop ? RingSegments : 2;
        line.alignment = LineAlignment.View;
        line.textureMode = LineTextureMode.Stretch;
        line.numCapVertices = 2;
        line.numCornerVertices = 2;
        line.generateLightingData = false;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.lightProbeUsage = LightProbeUsage.Off;
        line.reflectionProbeUsage = ReflectionProbeUsage.Off;
        line.sortingOrder = 30;
        line.sharedMaterial = _material;
        return line;
    }

    private void BuildParticleLayers()
    {
        bool shot = IsShotStyle();
        if (_muzzleFlashTexture != null)
        {
            Material flashMaterial = CreateParticleMaterial(
                _muzzleFlashTexture,
                "Automatic Cannon Flash Material");
            if (flashMaterial != null)
            {
                _runtimeMaterials.Add(flashMaterial);
                CreateFlashParticles(flashMaterial);
            }
        }

        if (shot)
            return;

        if (_sparkTexture != null)
        {
            Material sparkMaterial = CreateParticleMaterial(
                _sparkTexture,
                "Automatic Cannon Spark Material");
            if (sparkMaterial != null)
            {
                _runtimeMaterials.Add(sparkMaterial);
                CreateSparkParticles(sparkMaterial);
            }
        }

        Material smokeMaterial = CreateParticleMaterial(
            GetOrCreateSmokeTexture(),
            "Automatic Cannon Smoke Material");
        if (smokeMaterial != null)
        {
            _runtimeMaterials.Add(smokeMaterial);
            CreateSmokeParticles(smokeMaterial);
        }
    }

    private void CreateFlashParticles(Material material)
    {
        bool manual = _style == AutomaticCannonVfxStyle.ManualShot;
        bool critical = _style == AutomaticCannonVfxStyle.CriticalImpact;
        bool weakPoint = _style == AutomaticCannonVfxStyle.WeakPointImpact;
        bool active = _style == AutomaticCannonVfxStyle.BaseActive;
        bool shot = _style == AutomaticCannonVfxStyle.AutomaticShot || manual;

        ParticleSystem particles = CreateParticleSystem(
            $"Cannon {_style} Flash",
            material,
            ParticleSystemRenderMode.Billboard,
            sortingOrder: 33);
        ParticleSystem.MainModule main = particles.main;
        main.startLifetime = shot
            ? (manual ? 0.04f : 0.03f)
            : (critical || weakPoint ? 0.14f : active ? 0.18f : 0.1f);
        main.startSpeed = 0f;
        float flashSize = _size * (manual ? 0.9f : critical ? 1.25f : weakPoint ? 1.05f : active ? 1.4f : 0.62f);
        main.startSize = flashSize;
        main.startRotation = _style switch
        {
            AutomaticCannonVfxStyle.ManualShot => 0.18f,
            AutomaticCannonVfxStyle.Impact => -0.12f,
            AutomaticCannonVfxStyle.CriticalImpact => 0.24f,
            AutomaticCannonVfxStyle.WeakPointImpact => -0.2f,
            AutomaticCannonVfxStyle.BaseActive => 0.08f,
            _ => 0f
        };
        main.startColor = new ParticleSystem.MinMaxGradient(Color.white);
        main.maxParticles = 1;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = false;

        ConfigureBurst(particles, 1);
        ConfigureAlphaFade(particles, 1f, 0f);
        ConfigureSizeCurve(particles, 0.72f, 1.08f, 0.24f);
    }

    private void CreateSparkParticles(Material material)
    {
        bool manual = _style == AutomaticCannonVfxStyle.ManualShot;
        bool critical = _style == AutomaticCannonVfxStyle.CriticalImpact;
        bool weakPoint = _style == AutomaticCannonVfxStyle.WeakPointImpact;
        bool active = _style == AutomaticCannonVfxStyle.BaseActive;
        bool shot = _style == AutomaticCannonVfxStyle.AutomaticShot || manual;

        int count = _style switch
        {
            AutomaticCannonVfxStyle.AutomaticShot => 3,
            AutomaticCannonVfxStyle.ManualShot => 7,
            AutomaticCannonVfxStyle.Impact => 5,
            AutomaticCannonVfxStyle.CriticalImpact => 12,
            AutomaticCannonVfxStyle.WeakPointImpact => 9,
            AutomaticCannonVfxStyle.BaseActive => 14,
            _ => 4
        };

        ParticleSystem particles = CreateParticleSystem(
            $"Cannon {_style} Sparks",
            material,
            ParticleSystemRenderMode.Stretch,
            sortingOrder: 34);
        ParticleSystem.MainModule main = particles.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(
            shot ? 0.09f : 0.13f,
            shot ? (manual ? 0.2f : 0.14f) : (critical || weakPoint || active ? 0.3f : 0.22f));
        float speed = _size * (shot ? (manual ? 7.5f : 5.5f) : critical || weakPoint || active ? 6.5f : 4.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.55f, speed);
        float sparkSize = _size * (manual || critical || weakPoint || active ? 0.12f : 0.085f);
        main.startSize = new ParticleSystem.MinMaxCurve(sparkSize * 0.65f, sparkSize);
        main.startRotation = new ParticleSystem.MinMaxCurve(-0.18f, 0.18f);
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 1f, 1f, 0.72f),
            Color.white);
        main.gravityModifier = shot ? 0f : new ParticleSystem.MinMaxCurve(0.05f, 0.22f);
        main.maxParticles = count;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        if (shot)
        {
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = manual ? 13f : 8f;
            shape.radius = _size * (manual ? 0.08f : 0.04f);
            shape.radiusThickness = 1f;
            particles.transform.localPosition = Vector3.forward * _size * 0.08f;
        }
        else
        {
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = _size * 0.08f;
            shape.radiusThickness = 1f;
        }

        ConfigureBurst(particles, count);
        ConfigureAlphaFade(particles, 1f, 0f);

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.lengthScale = shot ? 2.8f : 2.1f;
        renderer.velocityScale = shot ? 0.28f : 0.22f;
    }

    private void CreateSmokeParticles(Material material)
    {
        bool manual = _style == AutomaticCannonVfxStyle.ManualShot;
        bool critical = _style == AutomaticCannonVfxStyle.CriticalImpact;
        bool active = _style == AutomaticCannonVfxStyle.BaseActive;
        bool shot = _style == AutomaticCannonVfxStyle.AutomaticShot || manual;
        int count = manual || critical || active ? 3 : 2;

        ParticleSystem particles = CreateParticleSystem(
            $"Cannon {_style} Residue",
            material,
            ParticleSystemRenderMode.Billboard,
            sortingOrder: 31);
        ParticleSystem.MainModule main = particles.main;
        main.startLifetime = new ParticleSystem.MinMaxCurve(
            shot ? 0.22f : 0.18f,
            shot ? (manual ? 0.42f : 0.32f) : (critical || active ? 0.5f : 0.36f));
        main.startSpeed = new ParticleSystem.MinMaxCurve(
            _size * 0.12f,
            _size * (shot ? 0.42f : 0.65f));
        float smokeSize = _size * (shot ? (manual ? 0.42f : 0.27f) : critical || active ? 0.6f : 0.38f);
        main.startSize = new ParticleSystem.MinMaxCurve(smokeSize * 0.65f, smokeSize);
        main.startRotation = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
        Color smokeColor = shot
            ? new Color(0.48f, 0.34f, 0.22f, 0.18f)
            : new Color(0.34f, 0.29f, 0.25f, 0.16f);
        main.startColor = new ParticleSystem.MinMaxGradient(smokeColor);
        main.gravityModifier = new ParticleSystem.MinMaxCurve(-0.015f, -0.04f);
        main.maxParticles = count;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.enabled = true;
        shape.shapeType = shot ? ParticleSystemShapeType.Cone : ParticleSystemShapeType.Sphere;
        shape.angle = shot ? 20f : 0f;
        shape.radius = _size * 0.06f;
        shape.radiusThickness = 1f;

        ConfigureBurst(particles, count);
        ConfigureAlphaFade(particles, 0.55f, 0f);
        ConfigureSizeCurve(particles, 0.5f, 1.15f, 1.38f);
    }

    private ParticleSystem CreateParticleSystem(
        string objectName,
        Material material,
        ParticleSystemRenderMode renderMode,
        int sortingOrder)
    {
        GameObject particleObject = new(objectName);
        particleObject.transform.SetParent(transform, false);

        ParticleSystem particles = particleObject.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.loop = false;
        main.playOnAwake = false;
        main.duration = Mathf.Max(0.05f, _lifetime);
        main.simulationSpace = IsShotStyle()
            ? ParticleSystemSimulationSpace.Local
            : ParticleSystemSimulationSpace.World;
        main.stopAction = ParticleSystemStopAction.None;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.enabled = true;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = 0f;

        ParticleSystemRenderer renderer = particleObject.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = renderMode;
        renderer.sortingOrder = sortingOrder;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        renderer.sharedMaterial = material;

        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        _particleSystems.Add(particles);
        return particles;
    }

    private bool IsShotStyle()
    {
        return _style == AutomaticCannonVfxStyle.AutomaticShot ||
               _style == AutomaticCannonVfxStyle.ManualShot;
    }

    private static void ConfigureBurst(ParticleSystem particles, int count)
    {
        ParticleSystem.EmissionModule emission = particles.emission;
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, (short)Mathf.Max(1, count))
        });
    }

    private static void ConfigureAlphaFade(ParticleSystem particles, float startAlpha, float endAlpha)
    {
        Gradient gradient = new();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 1f)
            },
            new[]
            {
                new GradientAlphaKey(startAlpha, 0f),
                new GradientAlphaKey(startAlpha * 0.72f, 0.36f),
                new GradientAlphaKey(endAlpha, 1f)
            });

        ParticleSystem.ColorOverLifetimeModule color = particles.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(gradient);
    }

    private static void ConfigureSizeCurve(
        ParticleSystem particles,
        float startScale,
        float middleScale,
        float endScale)
    {
        AnimationCurve curve = new(
            new Keyframe(0f, startScale),
            new Keyframe(0.32f, middleScale),
            new Keyframe(1f, endScale));
        ParticleSystem.SizeOverLifetimeModule size = particles.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, curve);
    }

    private void RenderFrame(float normalizedTime)
    {
        if (_lines.Count == 0)
            return;

        switch (_style)
        {
            case AutomaticCannonVfxStyle.AutomaticShot:
                RenderShot(normalizedTime, manual: false);
                break;
            case AutomaticCannonVfxStyle.ManualShot:
                RenderShot(normalizedTime, manual: true);
                break;
            case AutomaticCannonVfxStyle.Impact:
                RenderImpact(normalizedTime, critical: false, weakPoint: false);
                break;
            case AutomaticCannonVfxStyle.CriticalImpact:
                RenderImpact(normalizedTime, critical: true, weakPoint: false);
                break;
            case AutomaticCannonVfxStyle.WeakPointImpact:
                RenderImpact(normalizedTime, critical: false, weakPoint: true);
                break;
            case AutomaticCannonVfxStyle.BaseActive:
                RenderActiveStart(normalizedTime);
                break;
        }
    }

    private void RenderShot(float time, bool manual)
    {
        float fade = 1f - Smooth01(time);
        float length = _size * (manual ? 4.2f : 2.7f);
        float muzzleRadius = _size * (manual ? 0.34f : 0.2f);
        float muzzleLength = _size * (manual ? 0.9f : 0.55f);
        float outerWidth = _size * (manual ? 0.16f : 0.1f) * fade;
        float coreWidth = _size * (manual ? 0.05f : 0.032f) * fade;
        float shortenedLength = length * Mathf.Lerp(1f, 0.58f, time);

        SetSegment(
            _lines[0],
            Vector3.forward * 0.04f,
            Vector3.forward * shortenedLength,
            outerWidth,
            WithAlpha(_primaryColor, fade * 0.78f));
        SetSegment(
            _lines[1],
            Vector3.forward * 0.03f,
            Vector3.forward * (shortenedLength * 1.06f),
            coreWidth,
            WithAlpha(_coreColor, fade));

        int rayCount = _lines.Count - 2;
        for (int i = 0; i < rayCount; i++)
        {
            float angle = (Mathf.PI * 2f * i / rayCount) + (manual ? 0.2f : 0f);
            Vector3 radial = new(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            Vector3 end = radial * muzzleRadius * Mathf.Lerp(0.65f, 1.15f, time) +
                          Vector3.forward * muzzleLength * Mathf.Lerp(1f, 0.7f, time);
            SetSegment(
                _lines[i + 2],
                Vector3.forward * 0.02f,
                end,
                _size * (manual ? 0.07f : 0.045f) * fade,
                WithAlpha(i % 2 == 0 ? _coreColor : _primaryColor, fade));
        }
    }

    private void RenderImpact(float time, bool critical, bool weakPoint)
    {
        float expansion = Mathf.Sqrt(Mathf.Clamp01(time));
        float fade = 1f - Smooth01(time);
        int rayCount = _lines.Count;
        float baseLength = _size * (critical ? 1.2f : weakPoint ? 1.05f : 0.72f);
        float width = _size * (critical ? 0.075f : 0.052f) * fade;

        for (int i = 0; i < rayCount; i++)
        {
            float angle = (Mathf.PI * 2f * i / rayCount) + (i % 2) * 0.12f;
            float lengthVariation = 0.72f + (i % 3) * 0.16f;
            Vector3 planar = new(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            Vector3 start = planar * baseLength * expansion * 0.1f;
            Vector3 end = planar * baseLength * expansion * lengthVariation;
            end.z = -_size * (0.08f + (i % 2) * 0.12f) * expansion;

            Color color = i % 3 == 0 ? _coreColor : _primaryColor;
            SetSegment(_lines[i], start, end, width, WithAlpha(color, fade));
        }
    }

    private void RenderActiveStart(float time)
    {
        float expansion = Mathf.Sqrt(Mathf.Clamp01(time));
        float fade = 1f - Smooth01(time);
        float outerRadius = _size * Mathf.Lerp(0.14f, 1.15f, expansion);
        float innerRadius = _size * Mathf.Lerp(0.08f, 0.72f, expansion);

        SetRing(
            _lines[0],
            outerRadius,
            _size * 0.08f * fade,
            WithAlpha(_primaryColor, fade * 0.9f));
        SetRing(
            _lines[1],
            innerRadius,
            _size * 0.035f * fade,
            WithAlpha(_coreColor, fade));

        int rayCount = _lines.Count - 2;
        for (int i = 0; i < rayCount; i++)
        {
            float angle = Mathf.PI * 2f * i / rayCount;
            Vector3 radial = new(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            Vector3 start = radial * innerRadius * 0.25f;
            Vector3 end = radial * outerRadius * (0.78f + (i % 2) * 0.2f) +
                          Vector3.forward * _size * 0.55f * (1f - time);
            SetSegment(
                _lines[i + 2],
                start,
                end,
                _size * 0.055f * fade,
                WithAlpha(i % 2 == 0 ? _coreColor : _primaryColor, fade));
        }
    }

    private static void SetSegment(
        LineRenderer line,
        Vector3 start,
        Vector3 end,
        float width,
        Color color)
    {
        line.loop = false;
        line.positionCount = 2;
        line.SetPosition(0, start);
        line.SetPosition(1, end);
        line.startWidth = width;
        line.endWidth = width * 0.35f;
        line.startColor = color;
        line.endColor = WithAlpha(color, color.a * 0.08f);
    }

    private static void SetRing(LineRenderer line, float radius, float width, Color color)
    {
        line.loop = true;
        line.positionCount = RingSegments;
        for (int i = 0; i < RingSegments; i++)
        {
            float angle = Mathf.PI * 2f * i / RingSegments;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
        }

        line.startWidth = width;
        line.endWidth = width;
        line.startColor = color;
        line.endColor = color;
    }

    private void SetLinesEnabled(bool value)
    {
        for (int i = 0; i < _lines.Count; i++)
        {
            if (_lines[i] != null)
                _lines[i].enabled = value;
        }
    }

    private static Material CreateLineMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader == null)
            return null;

        Material material = new(shader)
        {
            name = "Automatic Cannon VFX Runtime Material",
            hideFlags = HideFlags.HideAndDontSave
        };
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", Color.white);
        return material;
    }

    private static Material CreateParticleMaterial(Texture2D texture, string materialName)
    {
        if (texture == null)
            return null;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null)
            return null;

        Material material = new(shader)
        {
            name = materialName,
            hideFlags = HideFlags.HideAndDontSave,
            mainTexture = texture
        };
        if (material.HasProperty("_BaseMap"))
            material.SetTexture("_BaseMap", texture);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", Color.white);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", Color.white);
        return material;
    }

    private static Texture2D GetOrCreateSmokeTexture()
    {
        if (s_smokeTexture != null)
            return s_smokeTexture;

        const int resolution = 64;
        s_smokeTexture = new Texture2D(
            resolution,
            resolution,
            TextureFormat.RGBA32,
            mipChain: false,
            linear: true)
        {
            name = "Automatic Cannon Soft Residue",
            hideFlags = HideFlags.HideAndDontSave,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        Color32[] pixels = new Color32[resolution * resolution];
        for (int y = 0; y < resolution; y++)
        {
            for (int x = 0; x < resolution; x++)
            {
                float normalizedX = ((x + 0.5f) / resolution) * 2f - 1f;
                float normalizedY = ((y + 0.5f) / resolution) * 2f - 1f;
                float angle = Mathf.Atan2(normalizedY, normalizedX);
                float irregularRadius = 1f +
                    Mathf.Sin(angle * 5f) * 0.07f +
                    Mathf.Sin(angle * 9f + 1.7f) * 0.035f;
                float distance = Mathf.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY);
                float alpha = Mathf.Clamp01(1f - distance / irregularRadius);
                alpha = alpha * alpha * (3f - 2f * alpha);
                pixels[y * resolution + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        s_smokeTexture.SetPixels32(pixels);
        s_smokeTexture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        return s_smokeTexture;
    }

    private static void DestroyRuntimeObject(Object runtimeObject)
    {
        if (runtimeObject == null)
            return;

        if (Application.isPlaying)
            Destroy(runtimeObject);
        else
            DestroyImmediate(runtimeObject);
    }

    private static float Smooth01(float value)
    {
        value = Mathf.Clamp01(value);
        return value * value * (3f - 2f * value);
    }

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = Mathf.Clamp01(alpha);
        return color;
    }
}
