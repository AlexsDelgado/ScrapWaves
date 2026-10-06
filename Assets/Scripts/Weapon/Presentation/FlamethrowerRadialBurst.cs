using System;
using UnityEngine;
using UnityEngine.Rendering;

[Serializable]
public sealed class FlamethrowerRadialBurstSettings
{
    [Tooltip("Seconds until the last decorative ember disappears. Must fit inside the active cue's pool duration.")]
    [Range(.2f, .7f)] public float VisualDuration = .42f;
    [Range(32, 96)] public int BoundarySegments = 64;
    [Range(8, 32)] public int FlameTongues = 20;
    [Range(0, 40)] public int Embers = 24;
    [Tooltip("Clear space for the player; only the weak ignition sheet crosses this center.")]
    [Range(.15f, .45f)] public float ClearCenterFraction = .23f;
    [Range(.04f, .28f)] public float FlameHeightFraction = .17f;
    [Range(.02f, .1f)] public float FlameWidthFraction = .055f;
    public Color FireOuterColor = new(1f, .19f, .015f);
    public Color FireHotColor = new(1f, .86f, .20f);
    [Range(0f, .3f)] public float FuelTintFraction = .12f;
    [Tooltip("Normalized-time opacity. The full damage footprint is present at time zero.")]
    public AnimationCurve IgnitionOpacity = new(new Keyframe(0f, .8f), new Keyframe(.12f, .65f), new Keyframe(.26f, 0f), new Keyframe(1f, 0f));
    public AnimationCurve FlameOpacity = new(new Keyframe(0f, .65f), new Keyframe(.07f, 1f), new Keyframe(.32f, .8f), new Keyframe(.68f, 0f), new Keyframe(1f, 0f));
    public AnimationCurve EmberOpacity = new(new Keyframe(0f, 0f), new Keyframe(.12f, .8f), new Keyframe(.4f, .5f), new Keyframe(1f, 0f));
    [Tooltip("Decorative flame travel only. The ignition boundary never shrinks or exceeds the damage radius.")]
    public AnimationCurve OutwardTravel = new(new Keyframe(0f, .35f), new Keyframe(.22f, 1f), new Keyframe(1f, 1f));

    public void Sanitize()
    {
        VisualDuration = Mathf.Clamp(VisualDuration, .2f, .7f);
        BoundarySegments = Mathf.Clamp(BoundarySegments, 32, 96);
        FlameTongues = Mathf.Clamp(FlameTongues, 8, 32);
        Embers = Mathf.Clamp(Embers, 0, 40);
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

/// <summary>Three bounded mesh draws. Every horizontal vertex stays inside the event's actual damage disc.</summary>
internal sealed class FlamethrowerRadialBurst : IDisposable
{
    const int TongueSections = 6;
    const int MaximumTongues = 32, MaximumEmbers = 40;
    readonly Transform _root;
    readonly FlamethrowerRadialBurstSettings _settings;
    readonly Mesh[] _meshes = new Mesh[3];
    readonly Material _material;
    readonly MaterialPropertyBlock _properties = new();
    readonly Vector3[] _flameVertices;
    readonly Vector3[] _emberVertices;
    int _tongues, _embers;
    float _radius, _intensity, _emission, _flash;
    bool _cold;
    Color _outer, _hot;

    public Renderer[] Renderers { get; }
    public float Radius => _radius;
    public float Duration => _settings.VisualDuration;
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
        Renderers = new Renderer[3];
        string[] names = { "Instant Damage Footprint", "Sharp Flame Tongues", "Short Embers" };
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
    }

    public void Configure(in WeaponPresentationContext context, FlamethrowerCueStyle style,
        Color primary, Color core, float baseEmission)
    {
        // ExplosionRadius already contains path and area-stat scaling. No authoring scale can change its meaning.
        _radius = Mathf.Max(0f, context.ExplosionRadius);
        _root.localScale = Vector3.one * _radius;
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
        _embers = Mathf.Clamp(Mathf.RoundToInt(_settings.Embers * budget * context.HeatSparkMultiplier), 0, _settings.Embers);
    }

    public void Sample(float seconds)
    {
        float life = Mathf.Clamp01(seconds / _settings.VisualDuration);
        bool visible = seconds < _settings.VisualDuration && _radius > 0f;
        if (!visible)
        {
            for (int layer=0; layer<3; layer++) SetFrame(layer,0f,seconds);
            return;
        }
        float travel = Mathf.Clamp01(_settings.OutwardTravel.Evaluate(life));
        UpdateFlames(life, travel);
        UpdateEmbers(life);
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
        Renderers[layer].SetPropertyBlock(_properties);
        Renderers[layer].enabled = opacity > .001f && _radius > 0f;
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
            float[] rings = { .015f, _settings.ClearCenterFraction, _settings.ClearCenterFraction, 1f, ragged, 1f };
            for (int ring = 0; ring < 6; ring++)
            {
                int index = i * 6 + ring;
                vertices[index] = axis * rings[ring] + Vector3.up * (ring < 4 ? .006f : .018f);
                uv[index] = new Vector2(.5f, i/(float)count);
                uv2[index] = new Vector2(ring >= 4 ? .55f : .16f, 0f);
                // Low flash under the player; a thin hot irregular edge marks the actual radius immediately.
                colors[index] = new Color(1f, 1f, 1f, ring == 0 ? .015f : ring == 1 ? .045f : ring < 4 ? .13f : .4f);
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
                    uv[v]=new Vector2(edge,along); uv2[v]=new Vector2(flame ? Mathf.Lerp(.9f,.05f,along) : .85f,1f);
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
            float start=_settings.ClearCenterFraction + seed*.29f;
            float reach=Mathf.Lerp(start+.10f,.68f+Hash(tongue*79+3)*.30f,travel);
            float lift=_settings.FlameHeightFraction*(.55f+seed*.45f);
            for (int wing = 0; wing < 2; wing++)
            for (int section = 0; section < TongueSections; section++)
            {
                float along=section/(float)(TongueSections-1);
                float curl=Mathf.Sin(along*5f-life*13f+seed*7f)*along*along*.065f;
                float height=.015f+lift*Mathf.Sin(along*Mathf.PI*.68f)*(1f-life*.35f)
                    + Mathf.Sin(along*8f-life*17f+seed*9f)*along*.025f;
                Vector3 center=axis*Mathf.Lerp(start,reach,along)+tangent*curl+Vector3.up*height;
                float width=(.12f+Mathf.Pow(Mathf.Max(0f,Mathf.Sin(along*Mathf.PI)),.8f))*(1f-along)*_settings.FlameWidthFraction*(.7f+seed*.6f);
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
            float radius=Mathf.Lerp(_settings.ClearCenterFraction+.08f,.7f+seed*.24f,life);
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
