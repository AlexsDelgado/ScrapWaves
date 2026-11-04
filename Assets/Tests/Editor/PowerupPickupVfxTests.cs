using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public sealed class PowerupPickupVfxTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private GameObject _player;
    private GameObject _floor;
    private PlayerHealth _health;
    private TemporaryPowerupController _controller, _previousController;
    private PowerupBalanceProfile _profile, _previousProfile;
    private PresentationAccessibilityState _previousAccessibility;
    private Scene _scene;
    private float _previousTimeScale;
    private int _previousPauseLocks;

    [SetUp]
    public void SetUp()
    {
        _previousController = TemporaryPowerupController.Instance;
        _previousProfile = (PowerupBalanceProfile)typeof(PowerupBalanceProfile)
            .GetField("s_Active", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        _previousAccessibility = PresentationAccessibilityRuntime.Current;
        _previousTimeScale = Time.timeScale;
        _previousPauseLocks = GameplayPause.LockCount;
        _profile = ScriptableObject.CreateInstance<PowerupBalanceProfile>();
        _profile.hideFlags = HideFlags.HideAndDontSave;
        PowerupBalanceProfile.SetActive(_profile);
        GameplayPause.Reset();
        Time.timeScale = 1f;
        PresentationAccessibilityRuntime.Apply(PresentationAccessibilityState.Default);
        _scene = EditorSceneManager.NewPreviewScene();
        _player = new GameObject("Isolated pickup VFX test");
        SceneManager.MoveGameObjectToScene(_player, _scene);
        // Nuke integration cannot touch enemies in the user's open scene.
        _player.transform.position = Vector3.one * 100000f;
        _floor = new GameObject("Isolated pickup ground");
        SceneManager.MoveGameObjectToScene(_floor, _scene);
        _floor.transform.position = _player.transform.position - Vector3.up * 1.5f;
        _floor.AddComponent<BoxCollider>().size = new Vector3(80f, 1f, 80f);
        Physics.SyncTransforms();
        _player.AddComponent<CapsuleCollider>().height = 2f;
        _health = _player.AddComponent<PlayerHealth>();
        _health.FullHeal();
        _controller = _player.AddComponent<TemporaryPowerupController>();
        Call(_controller, "Awake");
    }

    [TearDown]
    public void TearDown()
    {
        try
        {
            var effect = _player != null ? _player.GetComponent<PowerupPickupVfx>() : null;
            // Non-ExecuteAlways components need their runtime callbacks driven explicitly in EditMode.
            if (effect != null) Call(effect, "OnDestroy");
            Object.DestroyImmediate(_player);
            if (_floor != null) Object.DestroyImmediate(_floor);
            if (_scene.IsValid()) EditorSceneManager.ClosePreviewScene(_scene);
        }
        finally
        {
            typeof(TemporaryPowerupController).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, _previousController);
            PowerupBalanceProfile.SetActive(_previousProfile);
            Object.DestroyImmediate(_profile);
            GameplayPause.Reset();
            for (int i = 0; i < _previousPauseLocks; i++) GameplayPause.Push();
            Time.timeScale = _previousTimeScale;
            PresentationAccessibilityRuntime.Apply(_previousAccessibility);
        }
    }

    [Test]
    public void FullHealPickup_RestoresGameplay_AndOnlyPickupStartsTheGreenEffect()
    {
        Set(_health, "_currentHealth", 50);
        _health.Heal(15);
        Assert.That(_player.GetComponent<PowerupPickupVfx>(), Is.Null, "Ordinary healing must not trigger the pickup burst.");
        _health.SetShieldConfig(3, 6f);
        Set(_health, "_shieldCharges", 0);
        Set(_health, "_burnRemaining", 4f);
        int notifications = 0, amount = 0;
        _health.OnHealed += value => { notifications++; amount += value; };
        _controller.Apply(TemporaryPowerupType.FullHeal);
        var effect = _player.GetComponent<PowerupPickupVfx>();
        Tick(effect, .15f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(100));
        Assert.That(_health.ShieldCharges, Is.EqualTo(3));
        Assert.That(_health.IsBurning, Is.False);
        Assert.That(notifications, Is.EqualTo(1));
        Assert.That(amount, Is.EqualTo(35));
        Assert.That(Renderer(effect, "_heal").enabled, Is.True);
        Assert.That(Get(effect, "_nuke"), Is.Null);
        Assert.That(Get(_controller, "_buffParticles"), Is.Null, "Full Heal must not create the old generic particles.");
        Assert.That(_health.GrantedInvulnerabilityRemaining, Is.Zero);
    }

    [Test]
    public void NukePickup_IsSeparate_StaysAtDetonation_AndRetainsConfiguredReach()
    {
        Set(_health, "_currentHealth", 65);
        // An airborne pickup must launch debris from the floor, not the player's height.
        _floor.transform.position -= Vector3.up * 6f;
        Physics.SyncTransforms();
        _controller.Apply(TemporaryPowerupType.Nuke);
        var effect = _player.GetComponent<PowerupPickupVfx>();
        int fragments = (int)Get(effect, "_nukeFragmentCount");
        Assert.That(fragments, Is.InRange(3, 8));
        var origins = (Vector3[])Get(effect, "_fragmentOrigins");
        var radii = (float[])Get(effect, "_fragmentRadii");
        Bounds bodyBounds = _player.GetComponent<CapsuleCollider>().bounds;
        float bodyRadius = Mathf.Max(bodyBounds.extents.x, bodyBounds.extents.z);
        float groundY = _floor.GetComponent<BoxCollider>().bounds.max.y;
        float detonationY = ((Vector3)Get(effect, "_nukeOrigin")).y;
        for (int i = 0; i < fragments; i++)
        {
            float requiredDistance = bodyRadius + 1f + radii[i];
            Assert.That(new Vector2(origins[i].x, origins[i].z).magnitude,
                Is.InRange(requiredDistance, requiredDistance + .5f));
            Assert.That(origins[i].y, Is.EqualTo(groundY - detonationY + .08f).Within(.01f));
        }
        Tick(effect, .20f);
        Assert.That(_health.CurrentHealth, Is.EqualTo(65));
        Assert.That(Get(effect, "_heal"), Is.Null);
        Assert.That(Get(_controller, "_buffParticles"), Is.Null);
        Assert.That((float)Get(effect, "_nukeRadius"),
            Is.EqualTo(_profile.Nuke.Radius.Evaluate(_controller.CurrentLevelT)).Within(.001f));
        Vector3 origin = Root(effect, "_nuke").transform.position;
        float firstWidth = Mesh(effect, "_nuke").bounds.size.x;
        float firstHeight = Mesh(effect, "_nuke").bounds.size.y;
        Assert.That(firstHeight, Is.GreaterThan(firstWidth * .2f), "The Nuke must include a raised dome.");
        _player.transform.position += Vector3.forward * 40f;
        Tick(effect, .25f);
        Assert.That(Root(effect, "_nuke").transform.position, Is.EqualTo(origin));
        Assert.That(Mesh(effect, "_nuke").bounds.size.x, Is.GreaterThan(firstWidth));
        Assert.That(Mesh(effect, "_nuke").bounds.size.y, Is.GreaterThan(firstHeight), "The dome expands with the ground wave.");
        Tick(effect, .6f);
        var mesh = Mesh(effect, "_nuke");
        var uv = new List<Vector4>(); mesh.GetUVs(0, uv);
        var vertices = mesh.vertices;
        float nearestDust = float.MaxValue, farthestDust = 0f;
        int metalVertices = 0;
        for (int i = 0; i < uv.Count; i++)
        {
            if (uv[i].z >= 5f) metalVertices++;
            if (uv[i].z < 3f || uv[i].z >= 3.5f) continue;
            float distance = new Vector2(vertices[i].x, vertices[i].z).magnitude;
            nearestDust = Mathf.Min(nearestDust, distance);
            farthestDust = Mathf.Max(farthestDust, distance);
        }
        Assert.That(metalVertices, Is.GreaterThan(0), "The burst must contain solid metal fragments.");
        Assert.That(farthestDust - nearestDust, Is.GreaterThan((float)Get(effect, "_nukeRadius") * .35f),
            "Dust must occupy the wake, not only a narrow ring at the wave front.");
        Tick(effect, .75f);
        uv.Clear(); mesh.GetUVs(0, uv);
        Assert.That(uv.Exists(value => value.z >= 3f && value.z < 3.5f), Is.True,
            "Some dust should still dissipate after the 1.55-second shockwave has faded.");
        Assert.That(Renderer(effect, "_nuke").enabled, Is.True);
        Tick(effect, 1f);
        Assert.That(Renderer(effect, "_nuke").enabled, Is.False);
        Object.DestroyImmediate(_floor); _floor = null;
        Physics.SyncTransforms();
        effect.PlayNuke(_player.transform.position, 20f);
        Assert.That((int)Get(effect, "_nukeFragmentCount"), Is.Zero, "Missing ground must not create floating debris.");
    }

    [Test]
    public void NukePickup_UsesProfileRadiusAndReplacesActiveBurst()
    {
        _profile.Nuke.Radius = new PowerupBalanceProfile.LevelRange(12f, 140f);
        PowerupPickupVfx effect = null;
        Mesh mesh = null;
        GameObject root = null;
        Vector3 previousOrigin = default;
        foreach (var sample in new (float level, float radius)[] { (0f, 12f), (.5f, 76f), (1f, 140f) })
        {
            Set(_profile, "_forcedLevelT", sample.level);
            _player.transform.position += Vector3.right * 5f;
            Physics.SyncTransforms();
            _controller.Apply(TemporaryPowerupType.Nuke);
            effect = _player.GetComponent<PowerupPickupVfx>();
            Assert.That(_controller.CurrentLevelT, Is.EqualTo(sample.level));
            Assert.That((float)Get(effect, "_nukeRadius"), Is.EqualTo(sample.radius).Within(.001f));
            Assert.That((float)Get(effect, "_nukeAge"), Is.Zero, "A quick second pickup restarts the active burst.");
            if (root != null)
            {
                Assert.That(Root(effect, "_nuke"), Is.SameAs(root));
                Assert.That(Mesh(effect, "_nuke"), Is.SameAs(mesh));
                Assert.That(root.transform.position, Is.Not.EqualTo(previousOrigin));
            }
            root = Root(effect, "_nuke");
            mesh = Mesh(effect, "_nuke");
            Assert.That(root.transform.position, Is.EqualTo((Vector3)Get(effect, "_nukeOrigin")));
            previousOrigin = root.transform.position;
            Tick(effect, .95f);
            Assert.That(DomeRadius(mesh), Is.EqualTo(sample.radius).Within(.001f),
                "The visible wave must reach the configured damage radius, including values above 100.");
            Assert.That(Renderer(effect, "_nuke").enabled, Is.True);
        }
    }

    [Test]
    public void NukeFragments_StayClearOfMovingPlayer_AndScreenSilhouette()
    {
        var effect = PowerupPickupVfx.Ensure(_player);
        Vector3 originalPlayerPosition = _player.transform.position;
        effect.PlayNuke(originalPlayerPosition, 20f);
        Tick(effect, .12f);
        var retired = (bool[])Get(effect, "_fragmentRetired");
        Assert.That(retired[0], Is.False);
        var origins = (Vector3[])Get(effect, "_fragmentOrigins");
        var velocities = (Vector3[])Get(effect, "_fragmentVelocities");
        Vector3 detonation = (Vector3)Get(effect, "_nukeOrigin");
        float travel = (1f - Mathf.Exp(-.12f * .8f)) / .8f;
        Vector3 piece = detonation + origins[0] + new Vector3(velocities[0].x, 0f, velocities[0].z) * travel;
        _player.transform.position = new Vector3(piece.x, originalPlayerPosition.y, piece.z);
        Physics.SyncTransforms();
        Tick(effect, 0f);
        Assert.That(retired[0], Is.True, "A moving player must never be inside visible debris.");
        _player.transform.position = originalPlayerPosition;
        Physics.SyncTransforms();
        Tick(effect, .1f);
        Assert.That(retired[0], Is.True, "A removed fragment must not reappear from the player's body.");

        var cameraObject = new GameObject("Isolated pickup camera");
        SceneManager.MoveGameObjectToScene(cameraObject, _scene);
        var camera = cameraObject.AddComponent<Camera>();
        camera.pixelRect = new Rect(0f, 0f, 1280f, 720f);
        camera.aspect = 16f / 9f; camera.fieldOfView = 60f;
        camera.transform.position = originalPlayerPosition + new Vector3(0f, 4f, -7f);
        camera.transform.LookAt(originalPlayerPosition + Vector3.up * .2f);
        Set(effect, "_camera", camera);
        Bounds body = _player.GetComponent<CapsuleCollider>().bounds;
        object[] screenArguments = { body, default(Rect) };
        bool guarded = (bool)typeof(PowerupPickupVfx).GetMethod("TryGetPlayerScreenRect", Hidden).Invoke(effect, screenArguments);
        Assert.That(guarded, Is.True);
        var clearance = typeof(PowerupPickupVfx).GetMethod("IsFragmentClearOfPlayer", Hidden);
        Assert.That((bool)clearance.Invoke(effect, new object[] { new Vector3(0f, .08f, 3f), .3f, body, true, screenArguments[1] }),
            Is.False, "Debris behind the torso must not look like it is coming out of the player.");
        Assert.That((bool)clearance.Invoke(effect, new object[] { new Vector3(4f, .08f, 0f), .3f, body, true, screenArguments[1] }), Is.True);
    }

    [Test]
    public void ReducedEffects_FreezeGeometryAndLowerIntensity_ForBothPickups()
    {
        var effect = PowerupPickupVfx.Ensure(_player);
        foreach (var state in new[] { PresentationAccessibilityState.Default.WithReducedMotion(true),
            PresentationAccessibilityState.Default.WithReducedFlash(true) })
        {
            PresentationAccessibilityRuntime.Apply(state);
            const float radius = 47f;
            effect.PlayFullHeal(); effect.PlayNuke(_player.transform.position, radius);
            Tick(effect, .3f);
            var heal = Mesh(effect, "_heal").vertices;
            var nuke = Mesh(effect, "_nuke").vertices;
            Assert.That(DomeRadius(Mesh(effect, "_nuke")), Is.EqualTo(radius).Within(.001f),
                "Reduced motion and reduced flash must still communicate the full damage radius.");
            Tick(effect, .2f);
            Assert.That(Mesh(effect, "_heal").vertices, Is.EqualTo(heal));
            Assert.That(Mesh(effect, "_nuke").vertices, Is.EqualTo(nuke));
            foreach (string name in new[] { "_heal", "_nuke" })
            {
                var block = new MaterialPropertyBlock(); Renderer(effect, name).GetPropertyBlock(block);
                Assert.That(block.GetFloat("_Opacity"), Is.LessThanOrEqualTo(.32f));
            }
        }
    }

    [Test]
    public void PauseDeathResetAndDisable_ClearOrFreezeBothEffects()
    {
        var effect = PowerupPickupVfx.Ensure(_player);
        effect.PlayFullHeal(); effect.PlayNuke(_player.transform.position, 20f);
        Tick(effect, .15f);
        Time.timeScale = 0f;
        Tick(effect, 1f);
        Assert.That((float)Get(effect, "_healAge"), Is.EqualTo(.15f).Within(.001f));
        Assert.That((float)Get(effect, "_nukeAge"), Is.EqualTo(.15f).Within(.001f));
        Time.timeScale = 1f;
        GameplayPause.Push();
        Tick(effect, 1f);
        Assert.That((float)Get(effect, "_healAge"), Is.EqualTo(.15f).Within(.001f));
        Assert.That((float)Get(effect, "_nukeAge"), Is.EqualTo(.15f).Within(.001f));
        GameplayPause.Pop();
        _health.FullHeal(resetPhase: true);
        Assert.That(Renderer(effect, "_heal").enabled, Is.False);
        Assert.That(Renderer(effect, "_nuke").enabled, Is.False);
        effect.PlayFullHeal(); Tick(effect, .1f);
        Set(_health, "_isDead", true); Tick(effect, .1f);
        Assert.That(Renderer(effect, "_heal").enabled, Is.False);
        _health.FullHeal();
        effect.PlayNuke(_player.transform.position, 20f); Tick(effect, .1f);
        effect.enabled = false; Call(effect, "OnDisable");
        Assert.That(Renderer(effect, "_nuke").enabled, Is.False);
    }

    [Test]
    public void RepeatedPickups_ReuseResources_AndDestroyOwnedMeshes()
    {
        var effect = PowerupPickupVfx.Ensure(_player);
        effect.PlayFullHeal(); effect.PlayNuke(_player.transform.position, 20f);
        Mesh heal = Mesh(effect, "_heal"), nuke = Mesh(effect, "_nuke");
        GameObject healRoot = Root(effect, "_heal"), nukeRoot = Root(effect, "_nuke");
        for (int i = 0; i < 15; i++)
        {
            effect.PlayFullHeal(); effect.PlayNuke(_player.transform.position, 30f);
            for (int frame = 0; frame < 18; frame++)
            {
                Tick(effect, .15f);
                Assert.That(heal.vertexCount + nuke.vertexCount, Is.LessThan(2000));
            }
        }
        Assert.That(Mesh(effect, "_heal"), Is.SameAs(heal));
        Assert.That(Mesh(effect, "_nuke"), Is.SameAs(nuke));
        Assert.That(Root(effect, "_heal"), Is.SameAs(healRoot));
        Assert.That(Root(effect, "_nuke"), Is.SameAs(nukeRoot));
        Assert.That(heal.vertexCount + nuke.vertexCount, Is.LessThan(2000));
        Assert.That(healRoot.GetComponent<Collider>(), Is.Null);
        Assert.That(nukeRoot.GetComponent<Collider>(), Is.Null);
        Assert.That(nukeRoot.GetComponent<Rigidbody>(), Is.Null, "Cosmetic debris must not enter gameplay physics.");
        Assert.That(_player.GetComponentsInChildren<ParticleSystem>(true), Is.Empty);
        Call(effect, "OnDestroy");
        Object.DestroyImmediate(effect);
        Assert.That(heal == null && nuke == null && healRoot == null && nukeRoot == null, Is.True);
    }

    [Test]
    public void Material_IsIncludedInBuild_AndCompilesOnTheCurrentGpu()
    {
        var material = Resources.Load<Material>("VFX/PowerupPickup");
        Assert.That(material, Is.Not.Null);
        Assert.That(material.shader.isSupported, Is.True);
        ShaderUtil.CompilePass(material, 0, true);
        Assert.That(ShaderUtil.ShaderHasError(material.shader), Is.False);
        Assert.That(material.GetFloat("_Opacity"), Is.EqualTo(1f));
    }

    private static float DomeRadius(Mesh mesh)
    {
        var uv = new List<Vector4>();
        mesh.GetUVs(0, uv);
        Vector3[] vertices = mesh.vertices;
        float radius = 0f;
        for (int i = 0; i < uv.Count; i++)
            if (uv[i].z >= 4f && uv[i].z < 4.5f)
                radius = Mathf.Max(radius, new Vector2(vertices[i].x, vertices[i].z).magnitude);
        return radius;
    }

    private static object Get(object obj, string name) => obj.GetType().GetField(name, Hidden).GetValue(obj);
    private static void Set(object obj, string name, object value) => obj.GetType().GetField(name, Hidden).SetValue(obj, value);
    private static void Call(object obj, string method) => obj.GetType().GetMethod(method, Hidden).Invoke(obj, null);
    private static void Tick(PowerupPickupVfx effect, float delta) => typeof(PowerupPickupVfx).GetMethod("Tick", Hidden).Invoke(effect, new object[] { delta });
    private static T Resource<T>(PowerupPickupVfx effect, string layer, string name) => (T)Get(effect, layer).GetType().GetField(name).GetValue(Get(effect, layer));
    private static Mesh Mesh(PowerupPickupVfx effect, string layer) => Resource<Mesh>(effect, layer, "Mesh");
    private static MeshRenderer Renderer(PowerupPickupVfx effect, string layer) => Resource<MeshRenderer>(effect, layer, "Renderer");
    private static GameObject Root(PowerupPickupVfx effect, string layer) => Resource<GameObject>(effect, layer, "Root");
}
