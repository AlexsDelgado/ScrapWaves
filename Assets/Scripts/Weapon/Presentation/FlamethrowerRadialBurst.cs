using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[Serializable]
public sealed class FlamethrowerRadialBurstSettings
{
    [Tooltip("Fallback visual lifetime. Production Q reads the gameplay wave clock.")]
    [Range(.8f, 1.2f)] public float VisualDuration = .85f;
    [Range(32, 96)] public int BoundarySegments = 64;
    [Range(8, 32)] public int FlameTongues = 20;
    [Range(0, 40)] public int Embers = 24;
    [Tooltip("Billboard flames using the fired flame material. Instances follow the expanding Q front.")]
    [Range(0, 64)] public int FlameInstances = 32;
    [Range(.06f, .3f)] public float FlameInstanceSizeFraction = .18f;
    [Range(0f, 1f)] public float FlameInstanceOpacity = .85f;
    [Tooltip("Width of the flame trail behind the leading boundary as a fraction of radius. The center stays clear.")]
    [Range(.15f, .45f)] public float ClearCenterFraction = .23f;
    [Range(.04f, .28f)] public float FlameHeightFraction = .17f;
    [Range(.02f, .1f)] public float FlameWidthFraction = .055f;
    public Color FireOuterColor = new(1f, .19f, .015f);
    public Color FireHotColor = new(1f, .86f, .20f);
    [Range(0f, .3f)] public float FuelTintFraction = .12f;
    [Tooltip("Normalized visual-time opacity of the thin leading boundary. Radius follows the gameplay clock.")]
    public AnimationCurve IgnitionOpacity = new(new Keyframe(0f, .65f), new Keyframe(.1f, 1f), new Keyframe(.72f, 1f), new Keyframe(.85f, .8f), new Keyframe(1f, 0f));
    public AnimationCurve FlameOpacity = new(new Keyframe(0f, .65f), new Keyframe(.1f, 1f), new Keyframe(.72f, .85f), new Keyframe(1f, 0f));
    public AnimationCurve EmberOpacity = new(new Keyframe(0f, 0f), new Keyframe(.15f, .65f), new Keyframe(.72f, .5f), new Keyframe(1f, 0f));
    [Tooltip("Small decorative tongue motion within the moving boundary. Cannot alter wave radius or timing.")]
    public AnimationCurve OutwardTravel = new(new Keyframe(0f, .35f), new Keyframe(.22f, 1f), new Keyframe(1f, 1f));

    public void Sanitize()
    {
        VisualDuration = Mathf.Clamp(VisualDuration, .8f, 1.2f);
        BoundarySegments = Mathf.Clamp(BoundarySegments, 32, 96);
        FlameTongues = Mathf.Clamp(FlameTongues, 8, 32);
        Embers = Mathf.Clamp(Embers, 0, 40);
        FlameInstances = Mathf.Clamp(FlameInstances, 0, 64);
        FlameInstanceSizeFraction = Mathf.Clamp(FlameInstanceSizeFraction, .06f, .3f);
        FlameInstanceOpacity = Mathf.Clamp01(FlameInstanceOpacity);
        ClearCenterFraction = Mathf.Clamp(ClearCenterFraction, .15f, .45f);
        FlameHeightFraction = Mathf.Clamp(FlameHeightFraction, .04f, .28f);
        FlameWidthFraction = Mathf.Clamp(FlameWidthFraction, .02f, .1f);
        FuelTintFraction = Mathf.Clamp(FuelTintFraction, 0f, .3f);
        IgnitionOpacity ??= AnimationCurve.Linear(0f, .8f, 1f, 0f);
        FlameOpacity ??= AnimationCurve.Linear(0f, 1f, 1f, 0f);
        EmberOpacity ??= AnimationCurve.Linear(0f, .8f, 1f, 0f);
        OutwardTravel ??= AnimationCurve.Linear(0f, .35f, 1f, 1f);
    }
}

/// <summary>Three bounded mesh draws and one pooled flame batch, all driven by the damage wave clock.</summary>
internal sealed class FlamethrowerRadialBurst : IDisposable
{
    const int TongueSections = 6;
    const int MaximumFlameInstances = 64;
    readonly ParticleSystem.Particle[] _flameParticles = new ParticleSystem.Particle[MaximumFlameInstances];
    readonly List<Vector4> _flameClipSpheres = new(MaximumFlameInstances);
    readonly ParticleSystemRenderer _flameRenderer;
    int _flameInstanceCount;
    const int MaximumTongues = 32, MaximumEmbers = 40;
    readonly Transform _root;
    readonly FlamethrowerRadialBurstSettings _settings;
    readonly Mesh[] _meshes = new Mesh[3];
    readonly Material _material;
    readonly MaterialPropertyBlock _properties = new();
    readonly Vector3[] _flameVertices;
    readonly Vector3[] _emberVertices;
    int _tongues, _embers;
    float _radius, _maximumRadius, _duration, _intensity, _emission, _flash;
    FlamethrowerActiveWave.WaveClock _clock;
    bool _cold;
    Color _outer, _hot;

    public Renderer[] Renderers { get; }
    public ParticleSystem FlameParticles { get; }
    public float Radius => _radius;
    public float MaximumRadius => _maximumRadius;
    public float Duration => _duration;
    public int TongueCount => _tongues;
    public int EmberCount => _embers;
    public Mesh BoundaryMesh => _meshes[0];

    public FlamethrowerRadialBurst(Transform parent, FlamethrowerRadialBurstSettings settings, Shader authoredShader)
    {
        _settings = settings;
        _settings.Sanitize();
        var root = new GameObject("Q Radial Ignition");
        root.transform.SetParent(parent, false);
        _root = root.transform;
        Shader shader = authoredShader != null ? authoredShader : Shader.Find("ScrapWaves/GameFeel/Flamethrower Radial Ignition");
        if (shader == null) throw new InvalidOperationException("Flamethrower radial ignition shader is missing.");
        _material = new Material(shader) { name = "Q Radial Ignition (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        // Reuse the regular attack's authored detail without cloning its material or texture.
        var regularFlame = Resources.Load<Material>("GameFeel/ManualFlame");
        if (regularFlame != null) _material.mainTexture = regularFlame.mainTexture;
        Renderers = new Renderer[3];
        string[] names = { "Moving Fire Boundary", "Sharp Flame Tongues", "Short Embers" };
        for (int i = 0; i < 3; i++)
        {
            var go = new GameObject(names[i]); go.transform.SetParent(_root, false);
            var mesh = new Mesh { name = names[i], hideFlags = HideFlags.HideAndDontSave }; mesh.MarkDynamic();
            _meshes[i] = mesh; go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>(); renderer.sharedMaterial = _material;
            renderer.shadowCastingMode = ShadowCastingMode.Off; renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off; renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            Renderers[i] = renderer;
        }
        BuildFootprint();
        _flameVertices = BuildStrips(_meshes[1], MaximumTongues * 2, TongueSections, true);
        _emberVertices = BuildStrips(_meshes[2], MaximumEmbers * 2, 2, false);

        var flames = new GameObject("Moving Flame Wisps");
        flames.transform.SetParent(_root, false);
        FlameParticles = flames.AddComponent<ParticleSystem>();
        var main = FlameParticles.main;
        main.playOnAwake = false;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = MaximumFlameInstances;
        main.startSpeed = 0f;
        main.startSize3D = true;
        main.gravityModifier = 0f;
        var emission = FlameParticles.emission; emission.enabled = false;
        var shape = FlameParticles.shape; shape.enabled = false;
        FlameParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        FlameParticles.Pause();
        _flameRenderer = flames.GetComponent<ParticleSystemRenderer>();
        _flameRenderer.sharedMaterial = regularFlame;
        // Match the fired flames' vertex contract: UV2 pads TEXCOORD0 before the clipping sphere.
        _flameRenderer.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
        {
            ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color,
            ParticleSystemVertexStream.UV, ParticleSystemVertexStream.UV2,
            ParticleSystemVertexStream.Custom1XYZW
        });
        _flameRenderer.renderMode = ParticleSystemRenderMode.Billboard;
        _flameRenderer.alignment = ParticleSystemRenderSpace.View;
        _flameRenderer.sortMode = ParticleSystemSortMode.Distance;
        _flameRenderer.minParticleSize = 0f;
        _flameRenderer.maxParticleSize = 1f;
        _flameRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _flameRenderer.receiveShadows = false;
        _flameRenderer.lightProbeUsage = LightProbeUsage.Off;
        _flameRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        _flameRenderer.enabled = false;
    }

    public void Configure(in WeaponPresentationContext context, FlamethrowerCueStyle style,
        Color primary, Color core, float baseEmission, FlamethrowerActiveWave.WaveClock clock = null)
    {
        // ExplosionRadius already contains path and area-stat scaling. No authoring scale can change its meaning.
        _clock = clock;
        _maximumRadius = Mathf.Max(0f, context.ExplosionRadius);
        _duration = clock != null ? clock.VisualDuration : _settings.VisualDuration;
        _radius = 0f;
        _root.localScale = Vector3.zero;
        _intensity = context.Intensity;
        _emission = baseEmission * context.HeatEmissionMultiplier;
        _flash = context.ReducedFlash ? Mathf.Max(.18f, context.ReducedFlashIntensity) : 1f;
        _cold = style == FlamethrowerCueStyle.NitrogenActiveBurst;
        _outer = _cold ? primary : _settings.FireOuterColor;
        _hot = _cold ? core : _settings.FireHotColor;
        if (context.ReducedFlash) _hot = Color.Lerp(_hot, context.ReducedFlashColor, .35f);
        // Fuel keeps its existing persistent puddle cue; this one-shot ignition still reads as fire.
        if (style == FlamethrowerCueStyle.JellifiedActiveBurst) _outer = Color.Lerp(_outer, primary, _settings.FuelTintFraction);
        float budget = context.Quality == GameFeelQualityLevel.Low ? .4f : context.Quality == GameFeelQualityLevel.Medium ? .7f : 1f;
        _tongues = Mathf.Max(4, Mathf.RoundToInt(_settings.FlameTongues * budget));
        _flameInstanceCount = Mathf.Clamp(Mathf.RoundToInt(_settings.FlameInstances * budget), 0, MaximumFlameInstances);
        _embers = Mathf.Clamp(Mathf.RoundToInt(_settings.Embers * budget * context.HeatSparkMultiplier), 0, _settings.Embers);
    }

    public void Sample(float seconds)
    {
        if (_clock != null) seconds = _clock.Elapsed;
        _radius = _clock != null ? _clock.Radius : _maximumRadius * Mathf.Clamp01(seconds / .6f);
        _root.localScale = Vector3.one * _radius;
        float life = Mathf.Clamp01(seconds / Mathf.Max(.01f, _duration));
        bool visible = seconds < _duration && _radius > 0f && !(_clock?.Cancelled ?? false);
        if (!visible)
        {
            Hide();
            return;
        }
        float travel = Mathf.Clamp01(_settings.OutwardTravel.Evaluate(life));
        UpdateFlames(life, travel);
        UpdateEmbers(life);
        UpdateFlameInstances(life, seconds);
        _meshes[1].vertices = _flameVertices;
        _meshes[2].vertices = _emberVertices;
        SetFrame(0, visible ? Mathf.Clamp01(_settings.IgnitionOpacity.Evaluate(life)) * _flash : 0f, seconds);
        SetFrame(1, visible ? Mathf.Clamp01(_settings.FlameOpacity.Evaluate(life)) * Mathf.Lerp(.55f, 1f, _flash) : 0f, seconds);
        SetFrame(2, visible ? Mathf.Clamp01(_settings.EmberOpacity.Evaluate(life)) : 0f, seconds);
    }

    void SetFrame(int layer, float opacity, float seconds)
    {
        _properties.Clear();
        _properties.SetColor("_OuterColor", _outer);
        _properties.SetColor("_HotColor", _hot);
        _properties.SetFloat("_Opacity", opacity * Mathf.Clamp01(_intensity));
        _properties.SetFloat("_Emission", _emission * (layer == 0 ? _flash * .6f : 1f));
        _properties.SetFloat("_BurstTime", seconds);
        _properties.SetFloat("_Layer", layer);
        Renderers[layer].SetPropertyBlock(_properties);
        Renderers[layer].enabled = opacity > .001f && _radius > 0f;
    }

    public void Hide()
    {
        for (int layer = 0; layer < 3; layer++) SetFrame(layer, 0f, 0f);
        FlameParticles.SetParticles(_flameParticles, 0);
        _flameClipSpheres.Clear();
        FlameParticles.SetCustomParticleData(_flameClipSpheres, ParticleSystemCustomData.Custom1);
        _flameRenderer.enabled = false;
    }

    void UpdateFlameInstances(float life, float seconds)
    {
        // The pool starts all child systems on Play; Q owns their simulation clock instead.
        FlameParticles.Pause();
        _flameClipSpheres.Clear();
        Vector3 origin = _root.position;
        var clipSphere = new Vector4(origin.x, origin.y, origin.z, _radius);
        float opacity = _settings.FlameInstanceOpacity * Mathf.Clamp01(_settings.FlameOpacity.Evaluate(life))
            * Mathf.Clamp01(_intensity) * Mathf.Lerp(.55f, 1f, _flash);
        for (int i = 0; i < _flameInstanceCount; i++)
        {
            float seed = Hash(i * 67 + 19);
            float phase = seed * Mathf.PI * 2f;
            float angle = (i + (seed - .5f) * .45f) / _flameInstanceCount * Mathf.PI * 2f;
            float size = _settings.FlameInstanceSizeFraction * (.8f + seed * .4f);
            float pulse = Mathf.Sin(seconds * 13f + phase);
            // Local positions/sizes inherit the same radius as the ring. No freely emitted trail can lag behind it.
            float radial = 1f - _settings.ClearCenterFraction * (.3f + seed * .4f);
            Vector3 position = Axis(angle) * radial + Vector3.up * (size * (.32f + .06f * pulse));
            Color color = Color.Lerp(_outer, _hot, .12f + seed * .28f);
            color.a = opacity * (.75f + seed * .25f);
            _flameParticles[i] = new ParticleSystem.Particle
            {
                position = position,
                startSize3D = new Vector3(size, size * (1.35f + seed * .35f), size),
                startColor = color,
                rotation = (seed - .5f) * 36f + pulse * 8f,
                startLifetime = 2f,
                remainingLifetime = 2f,
                randomSeed = (uint)(i + 1)
            };
            // The shared manual material clips/fades pixels at the wave's live radius.
            _flameClipSpheres.Add(clipSphere);
        }
        FlameParticles.SetParticles(_flameParticles, _flameInstanceCount);
        FlameParticles.SetCustomParticleData(_flameClipSpheres, ParticleSystemCustomData.Custom1);
        _properties.Clear();
        _properties.SetFloat("_CloudAge", seconds);
        _flameRenderer.SetPropertyBlock(_properties);
        _flameRenderer.enabled = _flameInstanceCount > 0 && opacity > .001f && _flameRenderer.sharedMaterial != null;
    }

    void BuildFootprint()
    {
        int count = _settings.BoundarySegments;
        var vertices = new Vector3[count * 6]; var uv = new Vector2[vertices.Length];
        var uv2 = new Vector2[vertices.Length]; var colors = new Color[vertices.Length]; var triangles = new int[count * 18];
        for (int i = 0; i < count; i++)
        {
            Vector3 axis = Axis(i / (float)count * Mathf.PI * 2f);
            float ragged = .97f + Hash(i * 31 + 7) * .023f;
            float inner = 1f - _settings.ClearCenterFraction * .85f;
            float middle = Mathf.Lerp(inner, 1f, .55f);
            float[] rings = { inner, middle, middle, ragged, ragged, 1f };
            for (int ring = 0; ring < 6; ring++)
            {
                int index = i * 6 + ring;
                vertices[index] = axis * rings[ring] + Vector3.up * (ring < 4 ? .006f : .018f);
                uv[index] = new Vector2(Mathf.InverseLerp(inner, 1f, rings[ring]), i/(float)count);
                uv2[index] = new Vector2(ring >= 4 ? .55f : .16f, 0f);
                // The annulus trails behind the leading edge. The cleared center never reads as a filled damage disc.
                colors[index] = new Color(1f, 1f, 1f, ring == 0 ? .015f : ring == 1 ? .24f : ring < 4 ? .38f : .85f);
            }
            for (int band = 0; band < 3; band++)
            {
                int a = i*6 + band*2, b = ((i+1)%count)*6 + band*2, t = i*18 + band*6;
                triangles[t]=a; triangles[t+1]=b; triangles[t+2]=a+1;
                triangles[t+3]=a+1; triangles[t+4]=b; triangles[t+5]=b+1;
            }
        }
        _meshes[0].vertices=vertices; _meshes[0].uv=uv; _meshes[0].uv2=uv2; _meshes[0].colors=colors; _meshes[0].triangles=triangles;
        _meshes[0].RecalculateBounds();
    }

    static Vector3[] BuildStrips(Mesh mesh, int strips, int sections, bool flame)
    {
        var vertices = new Vector3[strips * sections * 2]; var uv = new Vector2[vertices.Length];
        var uv2 = new Vector2[vertices.Length]; var colors = new Color[vertices.Length];
        var triangles = new int[strips * (sections-1)*6];
        for (int strip = 0; strip < strips; strip++)
        {
            for (int section = 0; section < sections; section++)
            {
                float along = section/(float)(sections-1);
                for (int edge = 0; edge < 2; edge++)
                {
                    int v=(strip*sections+section)*2+edge;
                    // The crossed strip is a smaller hot fold inside the darker outer tongue.
                    float heat = flame ? Mathf.Lerp(strip % 2 == 0 ? .65f : .95f, .05f, along) : .85f;
                    uv[v]=new Vector2(edge,along); uv2[v]=new Vector2(heat,1f);
                    colors[v]=new Color(1f,1f,1f,flame ? Mathf.Lerp(.9f,.2f,along) : 1f);
                }
                if (section == sections-1) continue;
                int a=(strip*sections+section)*2, t=(strip*(sections-1)+section)*6;
                triangles[t]=a; triangles[t+1]=a+2; triangles[t+2]=a+1;
                triangles[t+3]=a+1; triangles[t+4]=a+2; triangles[t+5]=a+3;
            }
        }
        mesh.vertices=vertices; mesh.uv=uv; mesh.uv2=uv2; mesh.colors=colors; mesh.triangles=triangles;
        mesh.bounds=new Bounds(new Vector3(0,.25f,0),new Vector3(2f,1f,2f));
        return vertices;
    }

    void UpdateFlames(float life, float travel)
    {
        for (int tongue = 0; tongue < MaximumTongues; tongue++)
        {
            float seed=Hash(tongue*43+11), angle=tongue/(float)Mathf.Max(1,_tongues)*Mathf.PI*2f+(seed-.5f)*.30f;
            Vector3 axis=Axis(angle), tangent=Axis(angle+Mathf.PI*.5f);
            float start=1f-_settings.ClearCenterFraction*.8f + seed*.07f;
            float reach=Mathf.Lerp(.95f,.975f+Hash(tongue*79+3)*.024f,travel);
            float lift=_settings.FlameHeightFraction*(.55f+seed*.45f);
            for (int wing = 0; wing < 2; wing++)
            for (int section = 0; section < TongueSections; section++)
            {
                float along=section/(float)(TongueSections-1);
                float curl=Mathf.Sin(along*5f-life*13f+seed*7f)*along*along*.022f;
                float height=.015f+lift*Mathf.Sin(along*Mathf.PI*.68f)*(1f-life*.35f)
                    + Mathf.Sin(along*8f-life*17f+seed*9f)*along*.025f;
                Vector3 center=axis*Mathf.Lerp(start,reach,along)+tangent*curl+Vector3.up*height;
                float width=(.12f+Mathf.Pow(Mathf.Max(0f,Mathf.Sin(along*Mathf.PI)),.8f))*(1f-along)*_settings.FlameWidthFraction*(.7f+seed*.6f);
                width *= wing == 0 ? 1.45f : .65f;
                Vector3 side=wing==0 ? tangent : (tangent*.3f+Vector3.up*.7f);
                int v=((tongue*2+wing)*TongueSections+section)*2;
                _flameVertices[v]=tongue<_tongues ? Clip(center-side*width) : Vector3.zero;
                _flameVertices[v+1]=tongue<_tongues ? Clip(center+side*width) : Vector3.zero;
            }
        }
    }

    void UpdateEmbers(float life)
    {
        for (int ember = 0; ember < MaximumEmbers; ember++)
        {
            float seed=Hash(ember*97+13), angle=Hash(ember*53+5)*Mathf.PI*2;
            Vector3 axis=Axis(angle), tangent=Axis(angle+Mathf.PI*.5f);
            float radius=Mathf.Lerp(.84f,.88f+seed*.10f,life);
            Vector3 center=axis*radius+Vector3.up*(.035f+Mathf.Sin(life*Mathf.PI)*(.06f+seed*.15f));
            float size=(.003f+seed*.005f)*(1f-life);
            for (int wing=0; wing<2; wing++)
            {
                Vector3 side=wing==0 ? tangent*size : Vector3.up*size;
                int v=(ember*2+wing)*4;
                _emberVertices[v]=ember<_embers ? Clip(center-side-axis*size*1.8f) : Vector3.zero;
                _emberVertices[v+1]=ember<_embers ? Clip(center+side-axis*size*1.8f) : Vector3.zero;
                _emberVertices[v+2]=ember<_embers ? Clip(center-side+axis*size*1.8f) : Vector3.zero;
                _emberVertices[v+3]=ember<_embers ? Clip(center+side+axis*size*1.8f) : Vector3.zero;
            }
        }
    }

    static Vector3 Clip(Vector3 vertex)
    {
        float radius=new Vector2(vertex.x,vertex.z).magnitude;
        if (radius>1f) { vertex.x/=radius; vertex.z/=radius; }
        return vertex;
    }
    static Vector3 Axis(float angle) => new(Mathf.Cos(angle),0f,Mathf.Sin(angle));
    static float Hash(int value) => Mathf.Repeat(Mathf.Sin(value*12.9898f)*43758.5453f,1f);
    public void Dispose()
    {
        foreach (var mesh in _meshes) Release(mesh);
        Release(_material);
        if (_root!=null) Release(_root.gameObject);
    }
    static void Release(UnityEngine.Object item)
    {
        if (item==null) return;
        if (Application.isPlaying) UnityEngine.Object.Destroy(item); else UnityEngine.Object.DestroyImmediate(item);
    }
}
