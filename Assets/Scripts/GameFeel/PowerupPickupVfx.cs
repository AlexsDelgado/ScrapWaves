using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Local, reusable pickup feedback. It never applies healing, damage or camera effects.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(160)]
public sealed class PowerupPickupVfx : MonoBehaviour
{
    private enum FragmentKind { Metal, RustedMetal, Glass }
    private const float HealDuration = 1.25f, NukeDuration = 2.6f, ShockwaveDuration = 1.55f;
    private const float FragmentPlayerClearance = 1f;
    private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
    private static readonly Color Green = new(.12f, 1.45f, .38f, 1f);
    private static readonly Color Mint = new(.5f, 1.65f, .88f, 1f);
    private static readonly Color Amber = new(1.7f, .68f, .12f, 1f);
    private Layer _heal, _nuke;
    private Material _material;
    private PlayerHealth _health;
    private CapsuleCollider _body;
    private Camera _camera;
    private bool _subscribed;
    private float _healAge = -1f, _nukeAge = -1f, _nukeRadius;
    private Vector3 _nukeOrigin;
    private int _nukeFragmentCount;
    // Cosmetic randomness stays separate from Unity gameplay random state.
    private readonly System.Random _fragmentRandom = new();
    private readonly Vector3[] _fragmentOrigins = new Vector3[8];
    private readonly Vector3[] _fragmentVelocities = new Vector3[8];
    private readonly FragmentKind[] _fragmentKinds = new FragmentKind[8];
    private readonly float[] _fragmentVariants = new float[8];
    private readonly float[] _fragmentRadii = new float[8];
    private readonly bool[] _fragmentRetired = new bool[8];
    private readonly RaycastHit[] _fragmentGroundHits = new RaycastHit[16];

    public static PowerupPickupVfx Ensure(GameObject owner)
    {
        if (owner == null) return null;
        var effect = owner.GetComponent<PowerupPickupVfx>() ?? owner.AddComponent<PowerupPickupVfx>();
        effect.BindOwner();
        return effect;
    }

    private void OnEnable() => BindOwner();
    private void BindOwner()
    {
        if (_health == null) _health = GetComponent<PlayerHealth>();
        if (_body == null) _body = GetComponent<CapsuleCollider>();
        if (_health == null || _subscribed || !isActiveAndEnabled) return;
        _health.OnPlayerDied += Clear;
        _health.OnReset += Clear;
        _subscribed = true;
    }

    public void PlayFullHeal()
    {
        if (!CanPlay()) return;
        _heal ??= new Layer("[Full Heal VFX]", gameObject, _material);
        _healAge = 0f;
        DrawHeal();
    }

    public void PlayNuke(Vector3 origin, float radius)
    {
        if (!CanPlay() || float.IsNaN(radius) || float.IsInfinity(radius) || radius <= 0f) return;
        _nuke ??= new Layer("[Nuke Pickup VFX]", gameObject, _material);
        _nukeOrigin = origin + (FootPosition() - transform.position);
        _nukeRadius = Mathf.Clamp(radius, .1f, 100f);
        PrepareNukeFragments();
        _nukeAge = 0f;
        DrawNuke();
    }

    private void PrepareNukeFragments()
    {
        int desiredCount = _fragmentRandom.Next(3, 9); // Inclusive 3-8.
        _nukeFragmentCount = 0;
        PhysicsScene physicsScene = gameObject.scene.GetPhysicsScene();
        BillboardAxes(out _, out _);
        Bounds playerBounds = PlayerBounds();
        bool screenGuard = TryGetPlayerScreenRect(playerBounds, out Rect playerScreenRect);
        Vector3 centerOffset = playerBounds.center - _nukeOrigin;
        centerOffset.y = 0f;
        int firstKind = _fragmentRandom.Next(3), kindStep = _fragmentRandom.Next(2) == 0 ? 1 : 2;
        // Roll each launch once. Frames and pauses reuse these values without changing course.
        // Retry gaps at ledges, but never invent floating debris when no floor is found.
        for (int attempt = 0; attempt < desiredCount * 4 && _nukeFragmentCount < desiredCount; attempt++)
        {
            int i = _nukeFragmentCount;
            FragmentKind kind = (FragmentKind)(i < 3 ? (firstKind + i * kindStep) % 3 : _fragmentRandom.Next(3));
            float variant = (float)_fragmentRandom.NextDouble();
            // This sphere encloses every vertex through its entire rotation, including glass tips.
            float fragmentRadius = Vector3.Scale(FragmentSize(kind, variant), new Vector3(.5f, .6f, .5f)).magnitude;
            float minimumRadius = HorizontalRadius(playerBounds) + FragmentPlayerClearance + fragmentRadius + .025f;
            float angle = (float)_fragmentRandom.NextDouble() * Mathf.PI * 2f;
            float distance = Mathf.Sqrt(Mathf.Lerp(minimumRadius * minimumRadius,
                (minimumRadius + .45f) * (minimumRadius + .45f), (float)_fragmentRandom.NextDouble()));
            Vector3 offset = centerOffset + new Vector3(Mathf.Cos(angle) * distance, 0f, Mathf.Sin(angle) * distance);
            if (!TryGetFragmentGround(physicsScene, offset, out Vector3 launchOrigin)) continue;
            if (!IsFragmentClearOfPlayer(launchOrigin, fragmentRadius, playerBounds, screenGuard, playerScreenRect)) continue;
            _nukeFragmentCount++;
            _fragmentOrigins[i] = launchOrigin;
            _fragmentRadii[i] = fragmentRadius;
            _fragmentRetired[i] = false;
            // Include all three finishes when at least three ground launches are available.
            // The first set is shuffled; additional pieces and their wear vary per pickup.
            _fragmentKinds[i] = kind;
            _fragmentVariants[i] = variant;
            // Scatter away from the blast, with enough angular spread to avoid repeated spokes.
            float heading = angle + ((float)_fragmentRandom.NextDouble() * 2f - 1f) * Mathf.PI * .35f;
            float horizontalSpeed = 5f + (float)_fragmentRandom.NextDouble() * 3f;
            float upwardSpeed = 5.4f + (float)_fragmentRandom.NextDouble() * 2.5f;
            _fragmentVelocities[i] = new Vector3(Mathf.Cos(heading) * horizontalSpeed,
                upwardSpeed, Mathf.Sin(heading) * horizontalSpeed);
        }
    }

    private bool TryGetFragmentGround(PhysicsScene physicsScene, Vector3 offset, out Vector3 launchOrigin)
    {
        // Same environment layers as PlayerMovement: Default and Terrain. Ignore triggers.
        const int groundMask = (1 << 0) | (1 << 7);
        Vector3 rayOrigin = _nukeOrigin + offset + Vector3.up * 4f;
        int count = physicsScene.Raycast(rayOrigin, Vector3.down, _fragmentGroundHits, 84f,
            groundMask, QueryTriggerInteraction.Ignore);
        launchOrigin = offset;
        float closestHeight = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = _fragmentGroundHits[i];
            Collider surface = hit.collider;
            if (surface == null || hit.normal.y < .45f || surface.transform.IsChildOf(transform) ||
                surface.GetComponentInParent<EnemyHealth>() != null) continue;
            float heightDifference = Mathf.Abs(hit.point.y - _nukeOrigin.y);
            if (heightDifference >= closestHeight) continue;
            closestHeight = heightDifference;
            launchOrigin.y = hit.point.y - _nukeOrigin.y + .08f;
        }
        return closestHeight < float.PositiveInfinity;
    }

    private Bounds PlayerBounds() => _body != null && _body.enabled
        ? _body.bounds : new Bounds(transform.position, new Vector3(1.25f, Height, 1.25f));

    private static float HorizontalRadius(Bounds bounds) => Mathf.Max(bounds.extents.x, bounds.extents.z);

    private bool TryGetPlayerScreenRect(Bounds playerBounds, out Rect rectangle)
    {
        rectangle = default;
        if (_camera == null || !_camera.isActiveAndEnabled) return false;
        playerBounds.Expand(.2f);
        Vector2 minimum = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 maximum = new(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < 8; i++)
        {
            Vector3 corner = playerBounds.center + Vector3.Scale(playerBounds.extents,
                new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            Vector3 projected = _camera.WorldToViewportPoint(corner);
            if (projected.z <= _camera.nearClipPlane) return false;
            minimum = Vector2.Min(minimum, new Vector2(projected.x, projected.y));
            maximum = Vector2.Max(maximum, new Vector2(projected.x, projected.y));
        }
        // No silhouette guard for an off-screen or sub-pixel player (including isolated tests).
        if ((maximum.y - minimum.y) * _camera.pixelHeight < 4f) return false;
        rectangle = Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
        return rectangle.Overlaps(new Rect(0f, 0f, 1f, 1f));
    }

    private bool IsFragmentClearOfPlayer(Vector3 localPosition, float fragmentRadius, Bounds playerBounds,
        bool screenGuard, Rect playerScreenRect)
    {
        Vector3 separation = (_nukeOrigin - playerBounds.center) + localPosition;
        float safeRadius = HorizontalRadius(playerBounds) + FragmentPlayerClearance + fragmentRadius;
        if (separation.x * separation.x + separation.z * separation.z < safeRadius * safeRadius) return false;
        if (!screenGuard) return true;
        Vector3 projected = _camera.WorldToViewportPoint(_nukeOrigin + localPosition);
        if (projected.z <= _camera.nearClipPlane + fragmentRadius) return false;
        float halfHeight = _camera.orthographic ? _camera.orthographicSize :
            Mathf.Tan(_camera.fieldOfView * Mathf.Deg2Rad * .5f) * (projected.z - fragmentRadius);
        float radiusY = fragmentRadius / Mathf.Max(.001f, halfHeight * 2f);
        float radiusX = radiusY / Mathf.Max(.01f, _camera.aspect);
        Rect fragmentRect = new(projected.x - radiusX, projected.y - radiusY, radiusX * 2f, radiusY * 2f);
        return !fragmentRect.Overlaps(playerScreenRect);
    }

    private static Vector3 FragmentSize(FragmentKind kind, float variant) => kind switch
    {
        FragmentKind.Glass => new Vector3(.25f + variant * .18f, .65f + variant * .45f, .025f + variant * .02f),
        FragmentKind.RustedMetal => new Vector3(.44f + variant * .30f, .5f + variant * .3f, .10f + variant * .06f),
        _ => new Vector3(.5f + variant * .30f, .40f + variant * .25f, .07f + variant * .05f)
    };

    private bool CanPlay()
    {
        BindOwner();
        if (!isActiveAndEnabled || _health == null || !_health.IsAlive) return false;
        if (_material == null) _material = Resources.Load<Material>("VFX/PowerupPickup");
        return _material != null && _material.shader != null && _material.shader.isSupported;
    }

    private float Height => _body != null
        ? Mathf.Clamp(_body.height * Mathf.Abs(_body.transform.lossyScale.y), .8f, 4f) : 2f;

    private Vector3 FootPosition() => _body != null
        ? _body.transform.TransformPoint(_body.center) - Vector3.up * Height * .5f
        : transform.position - Vector3.up;

    private void LateUpdate() => Tick(Time.deltaTime);
    private void Tick(float deltaTime)
    {
        if (_healAge < 0f && _nukeAge < 0f) return;
        if (_health == null || !_health.IsAlive || !_health.isActiveAndEnabled)
        { Clear(); return; }
        float delta = GameplayPause.IsUiPaused || Time.timeScale <= 0f ? 0f : Mathf.Max(0f, deltaTime);
        if (_healAge >= 0f)
        {
            _healAge += delta;
            if (_healAge >= HealDuration) { _healAge = -1f; _heal.Hide(); }
            else DrawHeal();
        }
        if (_nukeAge >= 0f)
        {
            _nukeAge += delta;
            if (_nukeAge >= NukeDuration) { _nukeAge = -1f; _nuke.Hide(); }
            else DrawNuke();
        }
    }

    private void BillboardAxes(out Vector3 right, out Vector3 up)
    {
        if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
        right = _camera != null ? _camera.transform.right : Vector3.right;
        up = _camera != null ? _camera.transform.up : Vector3.up;
    }

    private static float Envelope(float age, float duration, float attack) =>
        Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(age / attack)) *
        (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(duration * .35f, duration, age)));

    private void DrawHeal()
    {
        bool restrained = HudVfxAccessibility.StaticEffects;
        float t = restrained ? .32f : _healAge / HealDuration;
        float h = Height;
        float strength = Envelope(_healAge, HealDuration, restrained ? .25f : .10f);
        _heal.Begin(FootPosition(), strength * (restrained ? .32f : 1f));
        BillboardAxes(out Vector3 right, out Vector3 up);
        _heal.Ring((.30f + .29f * t) * h, .08f * h, .05f, Tint(Green, .55f), 72);
        _heal.Billboard(Vector3.up * h * .53f, right, up, h * .75f, h * 1.0f,
            Tint(Green, restrained ? .06f : .16f), 1f);
        if (!restrained)
        {
            // Two open, tapered ribbons lift past the torso, leaving the silhouette readable.
            for (int ribbon = 0; ribbon < 2; ribbon++)
                _heal.Helix(h, t, ribbon * Mathf.PI, Tint(ribbon == 0 ? Green : Mint, .48f));
            for (int i = 0; i < 9; i++)
            {
                float phase = Mathf.Clamp01((t - (i % 3) * .055f) / .83f);
                float angle = i * 2.399963f + phase * .35f;
                float radius = h * (.29f + .075f * Mathf.Sin(i * 3.1f));
                Vector3 p = new(Mathf.Cos(angle) * radius,
                    h * (.18f + phase * .70f), Mathf.Sin(angle) * radius);
                float life = Mathf.Sin(phase * Mathf.PI);
                float size = h * (i % 3 == 0 ? .115f : .045f);
                _heal.Billboard(p, right, up, size, size, Tint(Mint, life * .65f), i % 3 == 0 ? 2f : 1f);
            }
        }
        _heal.Upload();
    }

    private void DrawNuke()
    {
        bool restrained = HudVfxAccessibility.StaticEffects;
        float wave = restrained ? .72f : 1f - Mathf.Pow(1f - Mathf.Clamp01(_nukeAge / .9f), 2f);
        float radius = Mathf.Lerp(.65f, _nukeRadius, wave);
        float strength = Envelope(_nukeAge, ShockwaveDuration, restrained ? .25f : .075f);
        // Dust and fragments have their own lifetimes after the original shockwave fades.
        _nuke.Begin(_nukeOrigin, restrained ? strength * .27f : 1f);
        float shellAlpha = restrained ? 1f : strength;
        // The transparent shell shares the shockwave radius and clock.
        // Draw it first so the ground ring and dust remain clear over the shell.
        _nuke.Dome(radius, .62f, Tint(Amber, .075f * shellAlpha));
        _nuke.Ring(radius, Mathf.Lerp(.35f, .75f, wave), .12f, Tint(Amber, .62f * shellAlpha), 64);
        _nuke.Ring(radius * .96f, .12f, .15f, Tint(new Color(1.8f, 1.2f, .52f), .5f * shellAlpha), 64);
        BillboardAxes(out Vector3 right, out Vector3 up);
        if (!restrained)
        {
            float flash = Mathf.Pow(1f - Mathf.Clamp01(_nukeAge / .38f), 2f);
            _nuke.Billboard(Vector3.up * .7f, right, up, 3.5f, 2.5f,
                Tint(Amber, flash * .32f * strength), 1f);
            DrawNukeDust(right, up);
            DrawNukeFragments();
        }
        _nuke.Upload();
    }

    // Deterministic samples never consume gameplay's shared random sequence.
    private static float Sample(int index, int channel) =>
        Mathf.Repeat(Mathf.Sin(index * 127.1f + channel * 311.7f + 84.7f) * 43758.5453f, 1f);

    private void DrawNukeDust(Vector3 right, Vector3 up)
    {
        float scale = Mathf.Clamp(_nukeRadius / 20f, .65f, 1.6f);
        for (int i = 0; i < 56; i++)
        {
            // The wave lifts dust from many different ground positions across the disk.
            // Each puff then drifts independently, leaving a broad wake behind the front.
            float distance = _nukeRadius * Mathf.Lerp(.12f, .94f, Mathf.Sqrt((i + .5f) / 56f));
            float arrival = .9f * (1f - Mathf.Sqrt(1f - Mathf.InverseLerp(.65f, _nukeRadius, distance)));
            float age = _nukeAge - arrival - Sample(i, 0) * .04f;
            float lifetime = 1.35f + Sample(i, 1) * .55f;
            if (age <= 0f || age >= lifetime) continue;
            float life = age / lifetime;
            float opacity = Mathf.SmoothStep(0f, 1f, age / .22f) *
                (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.3f, 1f, life)));
            float drift = (1f - Mathf.Exp(-age * 1.8f)) * (1.1f + Sample(i, 2) * 1.4f) * scale;
            float angle = i * 2.399963f + .18f * Sample(i, 3);
            float radial = distance + drift;
            float lift = .24f + age * (.22f + Sample(i, 4) * .24f);
            Vector3 position = new(Mathf.Cos(angle) * radial, lift, Mathf.Sin(angle) * radial);
            float size = (1.25f + Sample(i, 5) * 1.05f) * (1f + age * .9f) * scale;
            Color color = Color.Lerp(new Color(.38f, .32f, .23f), new Color(.69f, .56f, .35f), Sample(i, 6));
            // The fractional kind carries local age to the shader; pause freezes the billowing too.
            _nuke.Billboard(position, right, up, size, size * .52f,
                Tint(color, opacity * (.11f + Sample(i, 7) * .05f)), 3f + life * .24f, i * .47f);
        }
    }

    private void DrawNukeFragments()
    {
        float age = _nukeAge;
        float alpha = Mathf.SmoothStep(0f, 1f, age / .06f) *
            (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(2.05f, NukeDuration, age)));
        if (alpha <= .001f) return;
        float reach = Mathf.Clamp(_nukeRadius / 20f, .6f, 1.25f);
        Bounds playerBounds = PlayerBounds();
        bool screenGuard = TryGetPlayerScreenRect(playerBounds, out Rect playerScreenRect);
        for (int i = 0; i < _nukeFragmentCount; i++)
        {
            if (_fragmentRetired[i]) continue;
            const float gravity = 10.8f;
            Vector3 launchOrigin = _fragmentOrigins[i], launchVelocity = _fragmentVelocities[i];
            float startHeight = launchOrigin.y, restHeight = launchOrigin.y, velocity = launchVelocity.y;
            float landing = 2f * velocity / gravity;
            float flight = Mathf.Min(age, landing), afterLanding = Mathf.Max(0f, age - landing);
            float travel = (1f - Mathf.Exp(-flight * .8f)) / .8f;
            // One small, damped bounce, then a short slide at this fragment's ground height.
            travel += Mathf.Exp(-landing * .8f) * (1f - Mathf.Exp(-afterLanding * 4f)) * .15f;
            float bounce = Mathf.Sin(Mathf.Clamp01(afterLanding / .38f) * Mathf.PI) * .24f;
            float height = age <= landing ? startHeight + velocity * flight - .5f * gravity * flight * flight : restHeight + bounce;
            Vector3 position = new(launchOrigin.x + travel * launchVelocity.x * reach,
                height, launchOrigin.z + travel * launchVelocity.z * reach);
            if (!IsFragmentClearOfPlayer(position, _fragmentRadii[i], playerBounds, screenGuard, playerScreenRect))
            {
                // Retire the whole piece if the player moves into it or its projected silhouette.
                // It cannot emerge from the body again later in this burst.
                _fragmentRetired[i] = true;
                continue;
            }
            float spin = flight + (1f - Mathf.Exp(-afterLanding * 6f)) * .12f;
            Quaternion rotation = Quaternion.Euler(i * 31f + spin * (170f + Sample(i, 10) * 350f),
                i * 73f + spin * 250f, spin * (210f + Sample(i, 11) * 290f));
            FragmentKind kind = _fragmentKinds[i];
            float variant = _fragmentVariants[i];
            Vector3 size = FragmentSize(kind, variant);
            Color color;
            switch (kind)
            {
                case FragmentKind.Glass:
                    color = Color.Lerp(new Color(.38f, .72f, .76f), new Color(.65f, .86f, .90f), variant);
                    break;
                case FragmentKind.RustedMetal:
                    color = Color.Lerp(new Color(.49f, .19f, .065f), new Color(.76f, .37f, .13f), variant);
                    break;
                default:
                    color = Color.Lerp(new Color(.55f, .62f, .63f), new Color(.80f, .84f, .82f), variant);
                    break;
            }
            Vector3 view = _camera != null ? _camera.transform.position - (_nukeOrigin + position) : Vector3.back;
            _nuke.Shard(position, rotation, size, Tint(color, alpha), view, kind, variant);
        }
    }

    private static Color Tint(Color color, float alpha) { color.a = alpha; return color; }
    private void Clear()
    {
        _healAge = _nukeAge = -1f;
        _heal?.Hide(); _nuke?.Hide();
    }
    private void OnDisable()
    {
        if (_subscribed && _health != null)
        { _health.OnPlayerDied -= Clear; _health.OnReset -= Clear; }
        _subscribed = false;
        Clear();
    }
    private void OnDestroy()
    {
        OnDisable();
        _heal?.Dispose(); _nuke?.Dispose();
        _heal = _nuke = null;
    }

    // One mesh/draw call per effect, reused on subsequent pickups; no textures,
    // particle simulations, per-frame allocations, lights, colliders or material clones.
    private sealed class Layer
    {
        public readonly GameObject Root;
        public readonly Mesh Mesh;
        public readonly MeshRenderer Renderer;
        private readonly MaterialPropertyBlock _block = new();
        private readonly List<Vector3> _vertices = new(1400);
        private readonly List<Vector4> _uv = new(1400);
        private readonly List<Color> _colors = new(1400);
        private readonly List<int> _triangles = new(4200);

        public Layer(string name, GameObject owner, Material material)
        {
            Root = new GameObject(name) { hideFlags = HideFlags.DontSave, layer = owner.layer };
            SceneManager.MoveGameObjectToScene(Root, owner.scene);
            Mesh = new Mesh { name = name, hideFlags = HideFlags.DontSave };
            Mesh.MarkDynamic();
            Root.AddComponent<MeshFilter>().sharedMesh = Mesh;
            Renderer = Root.AddComponent<MeshRenderer>();
            Renderer.sharedMaterial = material;
            Renderer.shadowCastingMode = ShadowCastingMode.Off;
            Renderer.receiveShadows = false;
            Renderer.lightProbeUsage = LightProbeUsage.Off;
            Renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            Renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            Renderer.enabled = false;
        }
        public void Begin(Vector3 position, float opacity)
        {
            Root.transform.SetPositionAndRotation(position, Quaternion.identity);
            _vertices.Clear(); _uv.Clear(); _colors.Clear(); _triangles.Clear();
            _block.SetFloat(OpacityId, opacity);
            Renderer.SetPropertyBlock(_block);
            Renderer.enabled = opacity > .001f;
        }
        public void Ring(float radius, float width, float y, Color tint, int segments)
        {
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments, b = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 da = new(Mathf.Cos(a), 0, Mathf.Sin(a)), db = new(Mathf.Cos(b), 0, Mathf.Sin(b));
                Vector3 lift = Vector3.up * y;
                Quad(lift + da * (radius - width * .5f), lift + da * (radius + width * .5f),
                    lift + db * (radius + width * .5f), lift + db * (radius - width * .5f), tint, 0);
            }
        }
        public void Dome(float radius, float heightRatio, Color tint)
        {
            // Shared vertices keep both simultaneous pickup effects below 2,000 vertices.
            const int segments = 48, stacks = 8;
            int start = _vertices.Count;
            for (int row = 0; row <= stacks; row++)
            {
                float v = row / (float)stacks;
                float latitude = v * Mathf.PI * .5f;
                float horizontal = Mathf.Cos(latitude) * radius;
                float y = .12f + Mathf.Sin(latitude) * radius * heightRatio;
                for (int column = 0; column <= segments; column++)
                {
                    float u = column / (float)segments;
                    float angle = u * Mathf.PI * 2f;
                    _vertices.Add(new Vector3(Mathf.Cos(angle) * horizontal, y, Mathf.Sin(angle) * horizontal));
                    _uv.Add(new Vector4(u, v, 4f, heightRatio));
                    _colors.Add(tint);
                }
            }
            for (int row = 0; row < stacks; row++)
            for (int column = 0; column < segments; column++)
            {
                int a = start + row * (segments + 1) + column;
                int b = a + segments + 1;
                _triangles.Add(a); _triangles.Add(b); _triangles.Add(b + 1);
                _triangles.Add(a); _triangles.Add(b + 1); _triangles.Add(a + 1);
            }
        }
        public void Helix(float height, float time, float phase, Color tint)
        {
            const int segments = 28;
            for (int i = 0; i < segments; i++)
            {
                float a = i / (float)segments, b = (i + 1f) / segments;
                Vector3 pa = Point(a), pb = Point(b);
                float wa = Mathf.Sin(a * Mathf.PI) * height * .035f;
                float wb = Mathf.Sin(b * Mathf.PI) * height * .035f;
                Quad(pa - Vector3.up * wa, pa + Vector3.up * wa,
                    pb + Vector3.up * wb, pb - Vector3.up * wb, tint, 0);
            }
            Vector3 Point(float progress)
            {
                float angle = phase + progress * 4.4f + time * 1.6f;
                float radius = height * (.30f + .04f * Mathf.Sin(progress * Mathf.PI));
                return new Vector3(Mathf.Cos(angle) * radius,
                    height * (.12f + progress * .60f + time * .20f), Mathf.Sin(angle) * radius);
            }
        }
        public void Billboard(Vector3 center, Vector3 right, Vector3 up, float width, float height, Color tint, float kind, float seed = 0)
        {
            right *= width * .5f; up *= height * .5f;
            Quad(center - right - up, center - right + up, center + right + up, center + right - up, tint, kind, seed);
        }
        public void Shard(Vector3 center, Quaternion rotation, Vector3 size, Color tint, Vector3 view,
            FragmentKind kind, float variant)
        {
            float shaderKind = 5f + (int)kind, seed = variant * 91f;
            // Glass has a sharp triangular silhouette; metals use irregular solid plates.
            Vector3 a = center + rotation * Vector3.Scale(new Vector3(-.5f, -.4f, -.5f), size);
            Vector3 b = center + rotation * Vector3.Scale(new Vector3(.5f, -.25f, -.5f), size);
            Vector3 thickness = rotation * (Vector3.forward * size.z);
            if (kind == FragmentKind.Glass)
            {
                Vector3 c = center + rotation * Vector3.Scale(new Vector3(-.3f + variant * .45f, .6f, -.5f), size);
                Vector3 d = a + thickness, e = b + thickness, f = c + thickness;
                Face(a, c, b, b, true); Face(d, e, f, f, true);
                Face(a, b, e, d, false); Face(b, c, f, e, false); Face(c, a, d, f, false);
            }
            else
            {
                bool rusted = kind == FragmentKind.RustedMetal;
                Vector3 c = center + rotation * Vector3.Scale(new Vector3(rusted ? .18f + variant * .20f : .42f, .5f, -.5f), size);
                Vector3 d = center + rotation * Vector3.Scale(new Vector3(-.5f, rusted ? .18f + variant * .16f : .38f, -.5f), size);
                Vector3 e = a + thickness, f = b + thickness, g = c + thickness, h = d + thickness;
                Face(a, d, c, b, false); Face(e, f, g, h, false);
                Face(a, b, f, e, false); Face(b, c, g, f, false);
                Face(c, d, h, g, false); Face(d, a, e, h, false);
            }

            void Face(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, bool triangle)
            {
                Vector3 normal = Vector3.Cross(v1 - v0, v2 - v0).normalized;
                // The shared transparent pass does not write depth, so omit rear faces explicitly.
                if (Vector3.Dot(normal, view) <= 0f) return;
                float diffuse = Mathf.Max(0f, Vector3.Dot(normal, new Vector3(-.408f, .816f, .408f)));
                float light = kind == FragmentKind.Glass ? .8f + .2f * diffuse : .48f + .58f * diffuse;
                Color shaded = new(tint.r * light, tint.g * light, tint.b * light, tint.a);
                if (!triangle) { Quad(v0, v1, v2, v3, shaded, shaderKind, seed); return; }
                int n = _vertices.Count;
                _vertices.Add(v0); _vertices.Add(v1); _vertices.Add(v2);
                _uv.Add(new Vector4(0, 0, shaderKind, seed)); _uv.Add(new Vector4(1, 0, shaderKind, seed));
                _uv.Add(new Vector4(.35f, 1, shaderKind, seed));
                _colors.Add(shaded); _colors.Add(shaded); _colors.Add(shaded);
                _triangles.Add(n); _triangles.Add(n + 1); _triangles.Add(n + 2);
            }
        }
        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color tint, float kind, float seed = 0)
        {
            int n = _vertices.Count;
            _vertices.Add(a); _vertices.Add(b); _vertices.Add(c); _vertices.Add(d);
            _uv.Add(new Vector4(0, 0, kind, seed)); _uv.Add(new Vector4(0, 1, kind, seed));
            _uv.Add(new Vector4(1, 1, kind, seed)); _uv.Add(new Vector4(1, 0, kind, seed));
            for (int i = 0; i < 4; i++) _colors.Add(tint);
            _triangles.Add(n); _triangles.Add(n + 1); _triangles.Add(n + 2);
            _triangles.Add(n); _triangles.Add(n + 2); _triangles.Add(n + 3);
        }
        public void Upload()
        {
            Mesh.Clear();
            Mesh.SetVertices(_vertices); Mesh.SetUVs(0, _uv); Mesh.SetColors(_colors);
            Mesh.SetTriangles(_triangles, 0); Mesh.RecalculateBounds();
        }
        public void Hide() { if (Renderer != null) Renderer.enabled = false; }
        public void Dispose()
        {
            Hide();
            if (Application.isPlaying) { Object.Destroy(Root); Object.Destroy(Mesh); }
            else { Object.DestroyImmediate(Root); Object.DestroyImmediate(Mesh); }
        }
    }
}
