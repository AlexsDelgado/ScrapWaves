using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

// These are native-loop tests: no private movement methods or Physics.Simulate calls.
// Optional rendered evidence: SCRAPWAVES_MOVEMENT_CAPTURE_DIRECTORY=<absolute directory>.
public sealed class MovementSlopeAirRuntimeTests
{
    private sealed class Sample
    {
        public Vector3 Position, Velocity;
        public bool Grounded, Sliding, Crouching;
    }

    [DefaultExecutionOrder(-300)]
    private sealed class InputPump : MonoBehaviour
    {
        public Keyboard Keyboard;
        public Key[] Keys = Array.Empty<Key>();
        public int FixedTicks, Updates;
        public Action BeforeNextPhysics;
        private void Update()
        {
            InputSystem.QueueStateEvent(Keyboard, new KeyboardState(Keys));
            InputSystem.Update();
            Keyboard.MakeCurrent();
            Updates++;
        }
        private void FixedUpdate()
        {
            FixedTicks++;
            if (Updates == 0 || BeforeNextPhysics == null) return;
            Action action = BeforeNextPhysics;
            BeforeNextPhysics = null;
            action();
        }
    }

    [DefaultExecutionOrder(300)]
    private sealed class Probe : MonoBehaviour
    {
        public PlayerMovement Movement;
        public Rigidbody Body;
        public PlayerAnimationDriver Animation;
        public InputPump Pump;
        public bool Recording;
        public readonly List<Sample> Samples = new();
        public Camera Camera;
        public string Directory, Label;
        public TextMesh Title, Status;
        private int _lastCaptureTick = -3, _frame;
        private RenderTexture _target;
        private Texture2D _pixels;

        private void FixedUpdate()
        {
            if (!Recording) return;
            Samples.Add(new Sample { Position = Body.position, Velocity = Body.linearVelocity,
                Grounded = Movement.IsGroundedOnSurface, Sliding = Movement.IsSliding, Crouching = Movement.IsCrouching });
        }

        private void LateUpdate()
        {
            if (!Recording || Camera == null || string.IsNullOrEmpty(Directory) ||
                Pump.FixedTicks - _lastCaptureTick < 3) return;
            _lastCaptureTick = Pump.FixedTicks;
            Vector3 focus = Body.position + Vector3.up * .4f;
            Camera.transform.position = focus + new Vector3(11f, 5f, -6f);
            Camera.transform.LookAt(focus);
            Title.text = Label;
            Status.text = string.Format(CultureInfo.InvariantCulture,
                "Speed {0:F2} m/s    Vertical {1:F2} m/s\nGrounded {2}    Slide {3}    Ctrl {4}\nNative Unity physics + player prefab animation",
                Movement.PlanarSpeed, Body.linearVelocity.y, Movement.IsGroundedOnSurface,
                Movement.IsSliding, Array.IndexOf(Pump.Keys, Key.LeftCtrl) >= 0);
            if (_target == null)
            {
                System.IO.Directory.CreateDirectory(Directory);
                _target = new RenderTexture(960, 540, 24);
                _pixels = new Texture2D(960, 540, TextureFormat.RGB24, false);
                Camera.targetTexture = _target;
                File.WriteAllText(Path.Combine(Directory, "frames.csv"), "frame,fixedTick,stage,x,y,z,vx,vy,vz,grounded,sliding,crouching,animationEvaluations\n");
            }
            Camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _target;
            _pixels.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            _pixels.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(Path.Combine(Directory, $"frame-{_frame:D4}.png"), _pixels.EncodeToPNG());
            Vector3 p = Body.position, v = Body.linearVelocity;
            File.AppendAllText(Path.Combine(Directory, "frames.csv"), string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9},{10},{11},{12}\n",
                _frame++, Pump.FixedTicks, Label, p.x, p.y, p.z, v.x, v.y, v.z,
                Movement.IsGroundedOnSurface, Movement.IsSliding, Movement.IsCrouching, Animation.GraphEvaluationCount));
        }

        private void OnDestroy()
        {
            if (Camera != null) Camera.targetTexture = null;
            if (_target != null) { _target.Release(); Object.Destroy(_target); }
            if (_pixels != null) Object.Destroy(_pixels);
        }
    }

    private sealed class Rig : IDisposable
    {
        public readonly GameObject Root, Actor;
        public readonly PlayerMovement Movement;
        public readonly Rigidbody Body;
        public readonly PlayerAnimationDriver Animation;
        public readonly InputPump Pump;
        public readonly Probe Probe;
        public float WalkSpeed => Actor.GetComponent<PlayerStats>().GetMoveSpeed();
        private readonly Keyboard _keyboard;
        private readonly InputSettings.BackgroundBehavior _background;
        private readonly InputSettings.EditorInputBehaviorInPlayMode _editorInput;
        private readonly InputSettings.UpdateMode _updateMode;
        private readonly bool _runInBackground;
        private readonly float _step, _maximumDeltaTime;
        private readonly int _captureFramerate;

        public Rig(Vector3 position, string captureName = null)
        {
            _background = InputSystem.settings.backgroundBehavior;
            _editorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            _updateMode = InputSystem.settings.updateMode;
            _runInBackground = Application.runInBackground;
            _step = Time.fixedDeltaTime;
            _maximumDeltaTime = Time.maximumDeltaTime;
            _captureFramerate = Time.captureFramerate;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            Application.runInBackground = true;
            Time.fixedDeltaTime = .02f;
            // Keep native callbacks deterministic when shader import or PNG capture stalls a render frame.
            Time.maximumDeltaTime = .02f;
            Time.captureFramerate = 50;
            Time.timeScale = 1f;
            GameplayPause.Reset();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _keyboard.MakeCurrent();
            Root = new GameObject("Isolated movement runtime fixture");
            Root.SetActive(false);
            Actor = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab"), Root.transform);
            // Remove unrelated gameplay before activation; inventory, saves, combat and startup never run.
            RemoveUnrelatedGameplay(Actor);
            Actor.transform.localScale = Vector3.one * 1.2f;
            Actor.transform.SetPositionAndRotation(position, Quaternion.identity);
            Movement = Actor.GetComponent<PlayerMovement>();
            Body = Actor.GetComponent<Rigidbody>();
            Animation = Actor.GetComponent<PlayerAnimationDriver>();
            GameObject input = new GameObject("Native test input (forward +Z)");
            input.transform.SetParent(Root.transform);
            typeof(PlayerMovement).GetField("_cameraTransform", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(Movement, input.transform);
            Pump = input.AddComponent<InputPump>();
            Pump.Keyboard = _keyboard;
            Probe = input.AddComponent<Probe>();
            Probe.Movement = Movement; Probe.Body = Body; Probe.Animation = Animation; Probe.Pump = Pump;
            Root.SetActive(true);
            Movement.enabled = true;
            Animation.enabled = true;
            Animation.SetManualWeaponOverride(null);
            string directory = Environment.GetEnvironmentVariable("SCRAPWAVES_MOVEMENT_CAPTURE_DIRECTORY");
            if (!string.IsNullOrEmpty(directory) && captureName != null) SetupCapture(Path.Combine(directory, captureName));
            Physics.SyncTransforms();
        }

        private static void RemoveUnrelatedGameplay(GameObject actor)
        {
            var pending = new List<MonoBehaviour>();
            foreach (MonoBehaviour script in actor.GetComponentsInChildren<MonoBehaviour>(true))
                if (!(script is PlayerMovement) && !(script is PlayerStats) && !(script is PlayerAnimationDriver))
                    pending.Add(script);
            // Unity rejects deletion of a required component while its dependent still exists.
            // Remove dependent scripts first while the entire prefab is inactive (no Awake/Start).
            while (pending.Count > 0)
            {
                int next = pending.FindIndex(candidate => !pending.Exists(dependent =>
                    dependent != candidate && dependent.gameObject == candidate.gameObject && Requires(dependent, candidate.GetType())));
                Assert.That(next, Is.GreaterThanOrEqualTo(0), "Unrelated prefab scripts contain a RequireComponent cycle.");
                MonoBehaviour script = pending[next];
                pending.RemoveAt(next);
                Object.DestroyImmediate(script);
            }
        }

        private static bool Requires(MonoBehaviour dependent, Type component)
        {
            foreach (RequireComponent requirement in dependent.GetType().GetCustomAttributes(typeof(RequireComponent), true))
                if ((requirement.m_Type0 != null && requirement.m_Type0.IsAssignableFrom(component)) ||
                    (requirement.m_Type1 != null && requirement.m_Type1.IsAssignableFrom(component)) ||
                    (requirement.m_Type2 != null && requirement.m_Type2.IsAssignableFrom(component))) return true;
            return false;
        }

        private void SetupCapture(string directory)
        {
            GameObject go = new GameObject("Movement evidence camera", typeof(Camera));
            go.transform.SetParent(Root.transform);
            Camera camera = go.GetComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 5.5f;
            camera.aspect = 16f / 9f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .065f, .10f);
            camera.nearClipPlane = .1f; camera.farClipPlane = 200f;
            Probe.Camera = camera; Probe.Directory = directory;
            Probe.Title = Text(camera.transform, new Vector3(-9f, 4.6f, 8f), .13f, new Color(.4f, 1f, .85f));
            Probe.Status = Text(camera.transform, new Vector3(-9f, -3.6f, 8f), .095f, Color.white);
            GameObject light = new GameObject("Evidence light", typeof(Light));
            light.transform.SetParent(Root.transform);
            light.transform.rotation = Quaternion.Euler(40f, -25f, 0f);
            light.GetComponent<Light>().type = LightType.Directional;
            light.GetComponent<Light>().intensity = 2f;
        }

        private static TextMesh Text(Transform parent, Vector3 position, float size, Color color)
        {
            GameObject go = new GameObject("Evidence caption", typeof(TextMesh));
            go.transform.SetParent(parent, false); go.transform.localPosition = position;
            TextMesh text = go.GetComponent<TextMesh>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 48; text.characterSize = size; text.color = color;
            text.anchor = TextAnchor.UpperLeft;
            go.GetComponent<Renderer>().sharedMaterial = text.font.material;
            return text;
        }

        public GameObject Surface(float degrees, float centerHeight, float length = 100f)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = $"Procedural support {degrees} degrees";
            floor.transform.SetParent(Root.transform);
            floor.transform.position = Vector3.up * centerHeight;
            floor.transform.localScale = new Vector3(16f, 1f, length);
            floor.transform.rotation = Quaternion.Euler(degrees, 0f, 0f);
            Physics.SyncTransforms();
            return floor;
        }

        public void StartTrial(Vector3 position, Vector3 velocity, Key[] keys)
        {
            Pump.Keys = keys;
            Pump.BeforeNextPhysics = () =>
            {
                Body.position = position; Body.linearVelocity = velocity;
                Physics.SyncTransforms();
                Movement.SynchronizeGroundedPlacement();
                Probe.Samples.Clear(); Probe.Recording = true;
            };
        }

        public void Dispose()
        {
            Object.DestroyImmediate(Root);
            InputSystem.RemoveDevice(_keyboard);
            InputSystem.settings.backgroundBehavior = _background;
            InputSystem.settings.editorInputBehaviorInPlayMode = _editorInput;
            InputSystem.settings.updateMode = _updateMode;
            Application.runInBackground = _runInBackground;
            Time.fixedDeltaTime = _step;
            Time.maximumDeltaTime = _maximumDeltaTime;
            Time.captureFramerate = _captureFramerate;
            Time.timeScale = 1f; GameplayPause.Reset();
        }
    }

    [UnityTest]
    public IEnumerator NativeDownhillWalk_StartsSlide_ThenLedgeRestoresAirControl()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        using (var rig = new Rig(new Vector3(0f, 6.5f, -2f), "downhill-ledge"))
        {
            rig.Surface(20f, 4f, 18f);
            rig.Pump.Keys = new[] { Key.W };
            rig.Probe.Label = "DOWNHILL / WALKING"; rig.Probe.Recording = true;
            yield return Wait(rig, () => rig.Probe.Samples.Exists(s => s.Grounded && PlanarSpeed(s.Velocity) >= rig.WalkSpeed - .01f), 3f);
            yield return Ticks(rig, 15);
            Assert.That(rig.Movement.IsGroundedOnSurface, Is.True);
            Assert.That(rig.Movement.PlanarSpeed, Is.LessThan(rig.WalkSpeed * 1.5f), "Entry must be reached by walking, without a dash or assigned velocity.");
            int slideEvents = 0; rig.Movement.OnSlideStarted += () => slideEvents++;
            rig.Pump.Keys = new[] { Key.W, Key.LeftCtrl };
            rig.Probe.Label = "DOWNHILL / CTRL STARTS SLIDE";
            yield return Wait(rig, () => rig.Movement.IsSliding, 1f);
            Assert.That(slideEvents, Is.EqualTo(1));
            Assert.That(rig.Movement.IsGroundedOnSurface, Is.True);
            yield return Wait(rig, () => !rig.Movement.IsGroundedOnSurface, 3f);
            Assert.That(rig.Movement.IsSliding, Is.False, "Leaving support must end sliding on the first airborne physics tick.");
            Assert.That(rig.Movement.PlanarSpeed, Is.GreaterThan(2f));
            float speedAtEdge = rig.Movement.PlanarSpeed;
            rig.Pump.Keys = new[] { Key.W, Key.D, Key.LeftCtrl };
            rig.Probe.Label = "AIRBORNE / CTRL HELD + STEERING";
            yield return Ticks(rig, 15);
            Assert.That(rig.Movement.IsGroundedOnSurface, Is.False);
            Assert.That(rig.Movement.IsSliding, Is.False);
            Assert.That(rig.Body.linearVelocity.x, Is.GreaterThan(2f), "The ended slide must not suppress ordinary air steering.");
            Assert.That(rig.Movement.PlanarSpeed, Is.GreaterThan(rig.WalkSpeed * .8f));
            Assert.That(rig.Animation.GraphEvaluationCount, Is.GreaterThan(10));
            Assert.That(BaseAnimationState(rig.Animation), Is.EqualTo(Animator.StringToHash("Base Layer.Fall")));
            TestContext.WriteLine($"Native downhill walk-to-slide: starts={slideEvents}, ledge speed={speedAtEdge:F3}, steered airborne velocity={rig.Body.linearVelocity}, animation evaluations={rig.Animation.GraphEvaluationCount}.");
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator NativeJumpAndCrouchFromEligibleDownhillWalk_NeverPulsesSlide()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        using (var rig = new Rig(new Vector3(0f, 6.5f, -2f), "jump-crouch"))
        {
            rig.Surface(20f, 4f);
            rig.Pump.Keys = new[] { Key.W };
            rig.Probe.Label = "DOWNHILL WALK / BEFORE JUMP"; rig.Probe.Recording = true;
            yield return Wait(rig, () => rig.Probe.Samples.Exists(s => s.Grounded && PlanarSpeed(s.Velocity) >= rig.WalkSpeed - .01f), 3f);
            yield return Ticks(rig, 10);
            int jumps = 0, slides = 0;
            rig.Movement.OnJump += () => jumps++;
            rig.Movement.OnSlideStarted += () => slides++;
            rig.Pump.Keys = new[] { Key.W, Key.Space, Key.LeftCtrl };
            rig.Probe.Label = "JUMP + CTRL / MOMENTUM CONTINUES";
            yield return Wait(rig, () => jumps == 1, 2f);
            Assert.That(slides, Is.Zero, "The buffered jump owns this tick before downhill slide eligibility is considered.");
            Assert.That(rig.Movement.IsGroundedOnSurface, Is.False);
            Assert.That(rig.Movement.IsSliding, Is.False);
            yield return Ticks(rig, 15);
            Assert.That(BaseAnimationState(rig.Animation), Is.EqualTo(Animator.StringToHash("Base Layer.Jump")));
            Assert.That(rig.Movement.PlanarSpeed, Is.EqualTo(rig.WalkSpeed).Within(.02f));
            yield return Ticks(rig, 35);
            Assert.That(rig.Movement.IsGroundedOnSurface, Is.False);
            Assert.That(rig.Movement.IsSliding, Is.False);
            Assert.That(BaseAnimationState(rig.Animation), Is.EqualTo(Animator.StringToHash("Base Layer.Fall")));
            Assert.That(rig.Movement.PlanarSpeed, Is.EqualTo(rig.WalkSpeed).Within(.02f));
            Assert.That(slides, Is.Zero);
            TestContext.WriteLine($"Native downhill walk -> Space+Ctrl: jumpEvents={jumps}, slideEvents={slides}, final airborne velocity={rig.Body.linearVelocity}.");
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator NativeIdleOnWalkableSlope_DoesNotCreep()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        foreach (float slope in new[] { 20f, -20f, 35f })
        {
            using (var rig = new Rig(new Vector3(0f, 1.25f, 0f)))
            {
                rig.Surface(slope, -.5f);
                rig.Pump.Keys = Array.Empty<Key>();
                yield return Ticks(rig, 25);
                Assert.That(rig.Movement.IsGroundedOnSurface, Is.True, $"slope={slope}");
                Vector3 settled = rig.Body.position;
                yield return Ticks(rig, 50);
                Assert.That(Vector3.Distance(settled, rig.Body.position), Is.LessThan(.05f), $"Idle capsule slid on slope={slope}.");
                Assert.That(rig.Body.linearVelocity.magnitude, Is.LessThan(.2f), $"slope={slope}");
            }
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator NativeUphillWalk_ClimbsAtWalkSpeed()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        using (var rig = new Rig(new Vector3(0f, 1.25f, 0f)))
        {
            rig.Surface(-30f, -.5f);
            rig.Pump.Keys = new[] { Key.W };
            yield return Wait(rig, () => rig.Probe.Samples.Exists(s => s.Grounded && PlanarSpeed(s.Velocity) >= rig.WalkSpeed - .05f), 3f);
            float startHeight = rig.Body.position.y;
            yield return Ticks(rig, 50);
            Assert.That(rig.Movement.IsGroundedOnSurface, Is.True);
            Assert.That(rig.Body.position.y, Is.GreaterThan(startHeight + .5f), "Walking uphill must gain height.");
            Assert.That(rig.Movement.PlanarSpeed, Is.EqualTo(rig.WalkSpeed).Within(.1f));
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator NativeWalkingOnFlatAndUphill_RemainsCrouch()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        foreach (float slope in new[] { 0f, -20f })
        {
            using (var rig = new Rig(new Vector3(0f, 1.25f, 0f)))
            {
                rig.Surface(slope, -.5f);
                rig.Pump.Keys = new[] { Key.W };
                rig.Probe.Recording = true;
                yield return Wait(rig, () => rig.Probe.Samples.Exists(s => s.Grounded && PlanarSpeed(s.Velocity) >= rig.WalkSpeed - .01f), 3f);
                int slideEvents = 0; rig.Movement.OnSlideStarted += () => slideEvents++;
                rig.Pump.Keys = new[] { Key.W, Key.LeftCtrl };
                yield return Ticks(rig, 10);
                Assert.That(rig.Movement.IsGroundedOnSurface, Is.True, $"slope={slope}");
                Assert.That(rig.Movement.IsCrouching, Is.True);
                Assert.That(rig.Movement.IsSliding, Is.False);
                Assert.That(slideEvents, Is.Zero, $"Walking at slope={slope} must not get the downhill exception.");
            }
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator NativeCrouchDuringJumpAndFall_MatchesOrdinaryAirVelocity()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        foreach (bool jumping in new[] { false, true })
        foreach (bool moving in new[] { false, true })
        {
            List<Sample> baseline = null;
            foreach (bool crouch in new[] { false, true })
            {
                using (var rig = new Rig(new Vector3(0f, jumping ? 1.21f : 12f, 0f)))
                {
                    if (jumping) rig.Surface(0f, -.5f);
                    var keys = new List<Key>();
                    if (moving) keys.Add(Key.W);
                    if (jumping) keys.Add(Key.Space);
                    if (crouch) keys.Add(Key.LeftCtrl);
                    int jumps = 0, slides = 0;
                    rig.Movement.OnJump += () => jumps++;
                    rig.Movement.OnSlideStarted += () => slides++;
                    rig.Probe.Label = jumping ? "JUMP / CTRL + FORWARD HELD" : "FALL / CROUCH AIR MOMENTUM";
                    int measuredTicks = jumping ? 45 : 20;
                    rig.StartTrial(new Vector3(0f, jumping ? 1.21f : 12f, 0f), new Vector3(0f, jumping ? 0f : -3f, rig.WalkSpeed), keys.ToArray());
                    yield return Wait(rig, () => rig.Probe.Samples.Count >= measuredTicks, 5f);
                    List<Sample> samples = rig.Probe.Samples.GetRange(0, measuredTicks);
                    Assert.That(slides, Is.Zero, "Jump+crouch must never pulse a grounded slide before the jump.");
                    if (jumping) Assert.That(jumps, Is.EqualTo(1));
                    foreach (Sample sample in samples)
                    {
                        Assert.That(sample.Grounded, Is.False);
                        Assert.That(sample.Sliding, Is.False);
                    }
                    if (!crouch) baseline = samples;
                    else for (int i = 0; i < samples.Count; i++)
                        Assert.That(Vector3.Distance(samples[i].Velocity, baseline[i].Velocity), Is.LessThan(.025f),
                            $"Crouch altered native airborne velocity at tick {i}; jump={jumping}, movement={moving}.");
                    TestContext.WriteLine($"Native airborne trial jump={jumping}, input={moving}, crouch={crouch}: first={samples[0].Velocity}, last={samples[samples.Count - 1].Velocity}, jumpEvents={jumps}, slideEvents={slides}.");
                }
            }
        }
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator NativeLandingWithCrouchHeld_ReevaluatesSupportAndSpeed()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        // A short ordinary fall isolates landing eligibility from long air overspeed recovery.
        foreach (float slope in new[] { 0f, 20f })
        foreach (float speedFraction in new[] { .05f, 1f, 2f })
        {
            using (var rig = new Rig(new Vector3(0f, 1.8f, 0f)))
            {
                rig.Surface(slope, -.5f);
                float speed = rig.WalkSpeed * speedFraction;
                int slides = 0; rig.Movement.OnSlideStarted += () => slides++;
                Key[] keys = speedFraction == 1f ? new[] { Key.W, Key.LeftCtrl } : new[] { Key.LeftCtrl };
                rig.StartTrial(new Vector3(0f, 1.8f, 0f), new Vector3(0f, -5f, speed), keys);
                yield return Wait(rig, () => rig.Probe.Samples.Exists(s => s.Grounded), 2f);
                Sample landing = rig.Probe.Samples.Find(s => s.Grounded);
                bool shouldSlide = speedFraction == 2f || (slope > 0f && speedFraction == 1f);
                Assert.That(landing.Sliding, Is.EqualTo(shouldSlide), $"Held crouch landing: slope={slope}, initial speed={speed}, landed velocity={landing.Velocity}.");
                Assert.That(landing.Crouching, Is.EqualTo(!shouldSlide));
                if (speedFraction >= 1f)
                    Assert.That(PlanarSpeed(landing.Velocity), Is.GreaterThan(rig.WalkSpeed * .5f), "Landing must not zero walking or sliding momentum.");
                foreach (Sample sample in rig.Probe.Samples)
                    if (!sample.Grounded) Assert.That(sample.Sliding, Is.False);
                Assert.That(slides, Is.EqualTo(shouldSlide ? 1 : 0));
                TestContext.WriteLine($"Native held-crouch landing slope={slope}, initial speed={speed}, landed velocity={landing.Velocity}, slide={landing.Sliding}.");
            }
        }
        yield return new ExitPlayMode();
    }

    private static IEnumerator Ticks(Rig rig, int count)
    {
        int target = rig.Pump.FixedTicks + count;
        yield return Wait(rig, () => rig.Pump.FixedTicks >= target, 5f);
    }

    private static float PlanarSpeed(Vector3 velocity) => new Vector2(velocity.x, velocity.z).magnitude;
    private static int BaseAnimationState(PlayerAnimationDriver animation) =>
        (int)typeof(PlayerAnimationDriver).GetField("_baseState", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(animation);

    private static IEnumerator Wait(Rig rig, Func<bool> condition, float seconds)
    {
        // Initial graphics compilation may take seconds without representing gameplay time.
        float deadline = Time.realtimeSinceStartup + Mathf.Max(20f, seconds);
        while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(condition(), Is.True, $"Native runtime timed out: ticks={rig.Pump.FixedTicks}, updates={rig.Pump.Updates}, position={rig.Body.position}, velocity={rig.Body.linearVelocity}, grounded={rig.Movement.IsGroundedOnSurface}, slide={rig.Movement.IsSliding}.");
    }

    [UnityTearDown]
    public IEnumerator RestoreMode()
    {
        if (Application.isPlaying) { Time.timeScale = 1f; yield return new ExitPlayMode(); }
    }
}
