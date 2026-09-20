using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Bounded Play Mode smoke test of the saved sandbox, production prefab and live weapon ticks.</summary>
public sealed class PlayerAnimationSandboxTests
{
    private const string ScenePath = "Assets/Scenes/Testing/WeaponTestingSandbox.unity";
    private const string SetupKey = "PlayerAnimationSandboxTests.SceneSetup";
    private const string OutputDirectory = ".utmp/player-animation";
    private Mouse _mouse;
    private bool _fireHeld;
    private Action _inputHandler;
    private RuntimeInputProbe _inputProbe;
    private WeaponPresentationController _presentation;
    private Action<WeaponFeedbackEvent, WeaponFeedbackContext> _feedbackHandler;

    [Serializable] private sealed class SavedSetup { public SavedScene[] Scenes; }
    [Serializable] private sealed class SavedScene { public string Path; public bool Loaded; public bool Active; }

    [DefaultExecutionOrder(-51)] // Immediately before WeaponTestingSandboxManager (-50).
    private sealed class RuntimeInputProbe : MonoBehaviour
    {
        public Mouse Device;
        public bool Capturing;
        public int SampledFrames { get; private set; }
        public int HeldFrames { get; private set; }
        public int LastSampleFrame { get; private set; } = -1;

        public void BeginCapture()
        {
            SampledFrames = HeldFrames = 0;
            Capturing = true;
        }

        private void Update()
        {
            if (!Capturing || LastSampleFrame == Time.frameCount) return;
            LastSampleFrame = Time.frameCount;
            SampledFrames++;
            // Read exactly the gameplay buffer the sandbox will consume. The
            // EditMode test coroutine can resume with Editor input buffers active.
            if (Device != null && Device.added && Mouse.current == Device && Device.leftButton.isPressed)
                HeldFrames++;
        }
    }

    [UnityTest]
    public IEnumerator SavedSandbox_MovingManualAndAutomaticWeaponsKeepAnimatedOrigins()
    {
        SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
        Assert.That(previous.All(scene => !string.IsNullOrEmpty(scene.path)) || Application.isBatchMode, Is.True,
            "Save an untitled Editor scene before running the sandbox smoke test.");
        SessionState.SetString(SetupKey, JsonUtility.ToJson(new SavedSetup
        {
            Scenes = previous.Select(scene => new SavedScene
            { Path = scene.path, Loaded = scene.isLoaded, Active = scene.isActive }).ToArray()
        }));
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        yield return new EnterPlayMode();
        // Captured runtime locals must be allocated after the EnterPlayMode domain reload.
        yield return RunSandboxScenario();
    }

    private IEnumerator RunSandboxScenario()
    {
        Assert.That(Application.isPlaying, Is.True);
        Time.timeScale = 1f;
        Time.captureDeltaTime = 1f / 60f;
        yield return null; // Let the saved scene finish Start and initial loadout setup.
        WeaponTestingSandboxManager sandbox = Object.FindFirstObjectByType<WeaponTestingSandboxManager>();
        Assert.That(sandbox, Is.Not.Null, ScenePath);
        Transform player = sandbox.PlayerTransform;
        Assert.That(player, Is.Not.Null);
        PlayerAnimationDriver driver = player.GetComponent<PlayerAnimationDriver>();
        PlayerWeaponMountController mounts = player.GetComponent<PlayerWeaponMountController>();
        Rigidbody body = player.GetComponent<Rigidbody>();
        Assert.That(driver, Is.Not.Null);
        Assert.That(driver.RigAnimator, Is.Not.Null);
        Assert.That(mounts, Is.Not.Null);
        Assert.That(body, Is.Not.Null);
        Assert.That(sandbox.ProjectileSpawn, Is.SameAs(mounts.MainFirePoint));
        Assert.That(sandbox.ProjectileSpawn.IsChildOf(driver.RigAnimator.transform), Is.True);
        Assert.That(sandbox.ProjectileSpawn.name, Is.EqualTo("Main Weapon Fire Point"));
        foreach (WeaponType type in new[] { WeaponType.AutomaticCannon, WeaponType.Flamethrower,
                     WeaponType.RocketLauncher, WeaponType.Mortar, WeaponType.RotatingBlade })
            Assert.That(mounts.GetAnimatedSocket(type), Is.Not.Null, type.ToString());

        _mouse = InputSystem.AddDevice<Mouse>("Animation sandbox validation mouse");
        _mouse.MakeCurrent();
        _inputProbe = player.gameObject.AddComponent<RuntimeInputProbe>();
        _inputProbe.Device = _mouse;
        // An EditMode UnityTest can pump Editor and Dynamic input buffers between
        // coroutine yields. Set the virtual device after each input pass so native
        // focus events cannot replace Mouse.current before the sandbox Update.
        _inputHandler = ApplyTestMouseState;
        InputSystem.onAfterUpdate += _inputHandler;
        _presentation = sandbox.PresentationController;
        Assert.That(_presentation, Is.Not.Null);
        var report = new StringBuilder();
        report.AppendLine("Saved sandbox Play Mode animation smoke test");
        report.AppendLine("Scene: " + ScenePath);
        report.AppendLine("Unity: " + Application.unityVersion);
        report.AppendLine("Three separate three-slot loadouts, 30 rendered frames each; simulation delta 1/60 s.");
        report.AppendLine("Movement is injected through the player's Rigidbody; manual fire uses a temporary Input System mouse.");
        report.AppendLine("This is correctness and visual evidence, not a frame-time benchmark.");
        Directory.CreateDirectory(OutputDirectory);

        WeaponType[][] loadouts =
        {
            new[] { WeaponType.AutomaticCannon, WeaponType.Flamethrower, WeaponType.RocketLauncher },
            new[] { WeaponType.RotatingBlade, WeaponType.AutomaticCannon, WeaponType.Mortar },
            new[] { WeaponType.Flamethrower, WeaponType.RocketLauncher, WeaponType.AutomaticCannon }
        };
        Vector3[] motion = { Vector3.forward, Vector3.right, Vector3.back };
        for (int scenario = 0; scenario < loadouts.Length; scenario++)
        {
            _fireHeld = false;
            ApplyTestMouseState();
            for (int slot = 0; slot < WeaponTestingSandboxManager.WeaponSlots; slot++)
                sandbox.SetWeaponSlot(slot, loadouts[scenario][slot]);
            sandbox.SelectManualSlot(0);
            sandbox.RefillAllAmmo();
            sandbox.Spawner.EnemyHealth = 100000;
            Vector3 target = player.position + player.forward * 5f;
            target.y = 0.9f;
            sandbox.Spawner.SetZoneCenters(target, target, target, target, target);
            sandbox.Spawner.SpawnSingleDummy();

            IWeaponBehaviour[] behaviours = ReadBehaviours(sandbox);
            Assert.That(behaviours.Count(weapon => weapon != null), Is.EqualTo(3));
            var shots = new HashSet<WeaponType>();
            int manualEvents = 0;
            int automaticEvents = 0;
            var errors = new List<string>();
            _feedbackHandler = (kind, context) =>
            {
                if (kind != WeaponFeedbackEvent.ShotFired && kind != WeaponFeedbackEvent.SustainedFireStarted) return;
                if (context.Mode == WeaponFeedbackMode.Manual)
                {
                    manualEvents++;
                    if (driver.LastEvaluatedFrame != Time.frameCount) errors.Add("Manual weapon fired before current-frame pose.");
                    // Blade swings intentionally use GetOwnerOrigin() for their
                    // player-centred melee cone; emitted shots use the arm tip.
                    Vector3 expectedOrigin = context.WeaponType == WeaponType.RotatingBlade
                        ? player.position : sandbox.ProjectileSpawn.position;
                    if (Vector3.Distance(context.Origin, expectedOrigin) > 0.001f)
                        errors.Add("Manual feedback origin differs from its weapon's expected source: " + context.WeaponType);
                }
                if (context.Mode != WeaponFeedbackMode.Automatic) return;
                automaticEvents++;
                shots.Add(context.WeaponType);
                IWeaponBehaviour behaviour = behaviours.FirstOrDefault(weapon => weapon?.Runtime == context.Weapon);
                AutomaticWeaponMount mount = mounts.GetAssignedMount(behaviour);
                if (mount == null) { errors.Add("Missing automatic mount: " + context.WeaponType); return; }
                if (driver.LastEvaluatedFrame != Time.frameCount) errors.Add("Weapon fired before current-frame pose.");
                if (Vector3.Distance(context.Origin, mount.Muzzle.position) > 0.001f)
                    errors.Add("Feedback origin differs from animated muzzle: " + context.WeaponType);
                if (context.WeaponType == WeaponType.RocketLauncher && Vector3.Dot(context.Direction, Vector3.up) < 0.99f)
                    errors.Add("Automatic rocket lost world-up launch.");
                if (context.WeaponType == WeaponType.Flamethrower && Vector3.Dot(context.Direction, player.forward) < 0.99f)
                    errors.Add("Automatic flame lost body-forward direction.");
                if (context.WeaponType == WeaponType.AutomaticCannon && Vector3.Dot(context.Direction, player.forward) < 0f)
                    errors.Add("Automatic cannon fired outside the body's front half.");
            };
            _presentation.FeedbackEmitted += _feedbackHandler;
            Vector3 initialPosition = player.position;
            Vector3 initialMuzzle = player.InverseTransformPoint(sandbox.ProjectileSpawn.position);
            float muzzleTravel = 0f;
            float initialAmmo = sandbox.CurrentManualWeapon.CurrentAmmo;
            int initialEvaluations = driver.GraphEvaluationCount;
            _inputProbe.BeginCapture();
            for (int frame = 0; frame < 30; frame++)
            {
                _fireHeld = true;
                ApplyTestMouseState();
                Vector3 velocity = player.TransformDirection(motion[scenario]) * 2.5f;
                body.linearVelocity = new Vector3(velocity.x, body.linearVelocity.y, velocity.z);
                yield return null;
                // Inspect evidence recorded by the gameplay loop; the test's
                // Editor callback need not share the gameplay input buffer.
                Assert.That(driver.LastEvaluatedFrame, Is.EqualTo(Time.frameCount));
                Assert.That(_inputProbe.LastSampleFrame, Is.EqualTo(Time.frameCount),
                    "Held-input evidence must come from this gameplay frame, not an Editor input update.");
                Assert.That(sandbox.CurrentManualWeapon.State, Is.EqualTo(WeaponState.Manual));
                Assert.That(((IWeaponFireOriginReceiver)sandbox.CurrentManualBehaviour).FireOrigin.Muzzle,
                    Is.SameAs(sandbox.ProjectileSpawn));
                for (int slot = 1; slot < behaviours.Length; slot++)
                {
                    AutomaticWeaponMount mount = mounts.GetAssignedMount(behaviours[slot]);
                    Assert.That(mount, Is.Not.Null, loadouts[scenario][slot].ToString());
                    Assert.That(mount.transform.parent, Is.SameAs(mounts.GetAnimatedSocket(loadouts[scenario][slot])));
                    Assert.That(((IWeaponFireOriginReceiver)behaviours[slot]).FireOrigin.Muzzle, Is.SameAs(mount.Muzzle));
                }
                muzzleTravel = Mathf.Max(muzzleTravel,
                    Vector3.Distance(initialMuzzle, player.InverseTransformPoint(sandbox.ProjectileSpawn.position)));
            }
            _inputProbe.Capturing = false;
            int heldInputFrames = _inputProbe.HeldFrames;
            _presentation.FeedbackEmitted -= _feedbackHandler;
            _feedbackHandler = null;
            string stem = "sandbox-" + (scenario + 1) + "-" + loadouts[scenario][0];
            Capture(player, stem + "-front.png", true);
            Capture(player, stem + "-back.png", false);
            report.AppendLine($"{scenario + 1}: manual {loadouts[scenario][0]}, automatic {loadouts[scenario][1]} + {loadouts[scenario][2]}; " +
                $"manual events {manualEvents}, automatic events {automaticEvents}, held input frames {heldInputFrames}, " +
                $"ammo {initialAmmo:F2} -> {sandbox.CurrentManualWeapon.CurrentAmmo:F2}, active projectiles {sandbox.ProjectilePool.ActiveLeasedCount}, " +
                $"animated local muzzle travel {muzzleTravel:F4} m.");
            File.WriteAllText(Path.Combine(OutputDirectory, "runtime-sandbox-smoke.txt"), report.ToString());
            Assert.That(errors, Is.Empty, string.Join("\n", errors));
            Assert.That(_inputProbe.SampledFrames, Is.EqualTo(30),
                "Each rendered gameplay frame must contribute exactly one input observation.");
            Assert.That(heldInputFrames, Is.EqualTo(30), "The temporary mouse must supply held input in the gameplay input buffer.");
            Assert.That(manualEvents, Is.GreaterThan(0), "Manual fire must be confirmed by gameplay.");
            Assert.That(automaticEvents, Is.GreaterThan(0), "Automatic fire must continue during manual input.");
            Assert.That(shots, Does.Contain(loadouts[scenario][1]));
            Assert.That(shots, Does.Contain(loadouts[scenario][2]));
            Assert.That(sandbox.CurrentManualWeapon.CurrentAmmo, Is.LessThan(initialAmmo));
            Assert.That(Vector3.Distance(player.position, initialPosition), Is.GreaterThan(0.05f));
            Assert.That(muzzleTravel, Is.GreaterThan(0.0001f), "The manual muzzle must move relative to the gameplay root.");
            Assert.That(driver.GraphEvaluationCount - initialEvaluations, Is.EqualTo(30), "Pose advances exactly once per rendered frame.");
            Assert.That(player.GetComponentsInChildren<Projectile>(true), Is.Empty, "Travelling projectiles must not be parented to the player.");

            sandbox.SelectManualSlot(1);
            Assert.That(((IWeaponFireOriginReceiver)behaviours[1]).FireOrigin.Muzzle, Is.SameAs(sandbox.ProjectileSpawn));
            Assert.That(mounts.GetAssignedMount(behaviours[0]), Is.Not.Null);
            sandbox.ForceAutomaticMode();
            Assert.That(mounts.GetAssignedMount(behaviours[1]), Is.Not.Null);
            sandbox.ForceManualMode();
            Assert.That(((IWeaponFireOriginReceiver)behaviours[1]).FireOrigin.Muzzle, Is.SameAs(sandbox.ProjectileSpawn));
        }
        _fireHeld = false;
        ApplyTestMouseState();
        File.WriteAllText(Path.Combine(OutputDirectory, "runtime-sandbox-smoke.txt"), report.ToString());
    }

    private static IWeaponBehaviour[] ReadBehaviours(WeaponTestingSandboxManager sandbox) =>
        (IWeaponBehaviour[])typeof(WeaponTestingSandboxManager).GetField("_behaviours", BindingFlags.Instance | BindingFlags.NonPublic)
            .GetValue(sandbox);

    private void ApplyTestMouseState()
    {
        if (_mouse == null || !_mouse.added) return;
        InputState.Change(_mouse, new MouseState { buttons = (ushort)(_fireHeld ? 1 : 0) });
        _mouse.MakeCurrent();
    }

    private static void Capture(Transform player, string name, bool front)
    {
        GameObject root = new GameObject("Animation smoke capture camera");
        Camera camera = root.AddComponent<Camera>();
        camera.scene = player.gameObject.scene;
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.07f, 0.09f, 0.12f);
        camera.orthographic = true;
        camera.orthographicSize = 1.35f;
        Vector3 focus = player.position + Vector3.up * 0.2f;
        camera.transform.position = focus + (front ? player.forward : -player.forward) * 4f + Vector3.up * 0.65f;
        camera.transform.LookAt(focus);
        RenderTexture target = RenderTexture.GetTemporary(960, 720, 24);
        RenderTexture previous = RenderTexture.active;
        Texture2D image = new Texture2D(960, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 960, 720), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(OutputDirectory, name), image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.Destroy(image);
            Object.Destroy(root);
        }
    }

    [UnityTearDown]
    public IEnumerator RestoreScene()
    {
        if (_presentation != null && _feedbackHandler != null) _presentation.FeedbackEmitted -= _feedbackHandler;
        if (_inputHandler != null) InputSystem.onAfterUpdate -= _inputHandler;
        _inputHandler = null;
        if (_inputProbe != null) Object.DestroyImmediate(_inputProbe);
        _inputProbe = null;
        if (_mouse != null && _mouse.added) InputSystem.RemoveDevice(_mouse);
        _mouse = null;
        _feedbackHandler = null;
        if (Application.isPlaying)
        {
            Time.captureDeltaTime = 0f;
            Time.timeScale = 1f;
            yield return new ExitPlayMode();
        }
        string saved = SessionState.GetString(SetupKey, "");
        if (string.IsNullOrEmpty(saved)) yield break;
        SessionState.EraseString(SetupKey);
        SavedSetup setup = JsonUtility.FromJson<SavedSetup>(saved);
        if (setup?.Scenes?.Length > 0 && setup.Scenes.All(scene => !string.IsNullOrEmpty(scene.Path)))
            EditorSceneManager.RestoreSceneManagerSetup(setup.Scenes.Select(scene => new SceneSetup
            { path = scene.Path, isLoaded = scene.Loaded, isActive = scene.Active }).ToArray());
    }
}
