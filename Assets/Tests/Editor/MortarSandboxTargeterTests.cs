using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Real saved sandbox, animated production muzzle and current-frame authored targeter.</summary>
public sealed class MortarSandboxTargeterTests
{
    private const string ScenePath = "Assets/Scenes/Testing/WeaponTestingSandbox.unity";
    private const string SetupKey = "MortarSandboxTargeterTests.SceneSetup";
    private const string OutputVariable = "SCRAPWAVES_MORTAR_SANDBOX_OUTPUT";
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly int FlightSegments = (int)typeof(MortarTrajectory)
        .GetField("SegmentsPerFlight", BindingFlags.Static | BindingFlags.NonPublic).GetRawConstantValue();
    private Mouse _mouse;
    private Keyboard _keyboard;
    private Action _inputHandler;
    private Key[] _keys = Array.Empty<Key>();
    private RuntimeProbe _probe;
    private float _previousTimeScale, _previousCaptureDelta;
    private bool _runtimeStateCaptured;
    private bool _fireHeld;
    private WeaponPresentationController _presentation;
    private Action<WeaponFeedbackEvent, WeaponFeedbackContext> _feedbackHandler;

    [Serializable] private sealed class SavedSetup
    {
        public SavedScene[] Scenes;
        public bool RestoreBatchEmptyScene;
    }
    [Serializable] private sealed class SavedScene { public string Path; public bool Loaded; public bool Active; }

    [DefaultExecutionOrder(120)] // After the HUD (100) and authored marker (110).
    private sealed class RuntimeProbe : MonoBehaviour
    {
        public Action Sample;
        public int LastFrame = -1;
        private void LateUpdate()
        {
            if (LastFrame == Time.frameCount) return;
            LastFrame = Time.frameCount;
            Sample?.Invoke();
        }
    }

    [UnityTest]
    public IEnumerator SavedSandbox_ManualTargeterTracksAnimatedMuzzleWithoutSelfOrGhostContacts()
    {
        SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
        Assert.That(previous.All(scene => !string.IsNullOrEmpty(scene.path)) || Application.isBatchMode, Is.True,
            "Save an untitled scene before running the saved-sandbox probe.");
        for (int index = 0; index < UnityEngine.SceneManagement.SceneManager.sceneCount; index++)
            Assert.That(UnityEngine.SceneManagement.SceneManager.GetSceneAt(index).isDirty && !Application.isBatchMode,
                Is.False, "Save modified scenes before running this probe; the test never saves or discards user edits.");
        SessionState.SetString(SetupKey, JsonUtility.ToJson(new SavedSetup
        {
            RestoreBatchEmptyScene = Application.isBatchMode
                && (previous.Length == 0 || previous.Any(scene => string.IsNullOrEmpty(scene.path))),
            Scenes = previous.Select(scene => new SavedScene
            { Path = scene.path, Loaded = scene.isLoaded, Active = scene.isActive }).ToArray()
        }));
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        yield return new EnterPlayMode();
        // Allocate gameplay references after the EnterPlayMode domain reload.
        yield return RunSandbox();
    }

    private IEnumerator RunSandbox()
    {
        _previousTimeScale = Time.timeScale;
        _previousCaptureDelta = Time.captureDeltaTime;
        _runtimeStateCaptured = true;
        Time.timeScale = 1f;
        Time.captureDeltaTime = 1f / 60f;
        yield return null;
        WeaponTestingSandboxManager sandbox = Object.FindFirstObjectByType<WeaponTestingSandboxManager>();
        Assert.That(sandbox, Is.Not.Null);
        Transform owner = sandbox.PlayerTransform;
        Assert.That(owner, Is.Not.Null);
        Camera camera = owner.GetComponent<ReticleAimProvider>().AimCamera;
        ThirdPersonCamera orbit = camera.GetComponent<ThirdPersonCamera>();
        PlayerAnimationDriver animation = owner.GetComponent<PlayerAnimationDriver>();
        PlayerWeaponMountController mounts = owner.GetComponent<PlayerWeaponMountController>();
        PlayerMovement movement = owner.GetComponent<PlayerMovement>();
        Rigidbody body = owner.GetComponent<Rigidbody>();
        ReticleHud hud = owner.GetComponent<ReticleHud>();
        Assert.That(orbit, Is.Not.Null);
        Assert.That(hud, Is.Not.Null);
        Assert.That(animation, Is.Not.Null);
        Assert.That(body, Is.Not.Null);
        Assert.That(sandbox.ProjectileSpawn, Is.SameAs(mounts.MainFirePoint));
        Assert.That(sandbox.ProjectileSpawn.IsChildOf(animation.RigAnimator.transform), Is.True);
        sandbox.Spawner.ClearEnemies();
        sandbox.SetWeaponSlot(0, WeaponType.Mortar);
        sandbox.SetWeaponSlot(1, null);
        sandbox.SetWeaponSlot(2, null);
        sandbox.SelectManualSlot(0);
        sandbox.RefillAllAmmo();
        var mortar = sandbox.CurrentManualBehaviour as MortarWeapon;
        Assert.That(mortar, Is.Not.Null);
        float initialAmmo = sandbox.CurrentManualWeapon.CurrentAmmo;
        orbit.SetLookBlockedByUi(true);
        orbit.CameraFeedbackScale = 0f;
        _mouse = InputSystem.AddDevice<Mouse>("Mortar saved-sandbox no-fire mouse");
        _keyboard = InputSystem.AddDevice<Keyboard>("Mortar saved-sandbox movement keyboard");
        _inputHandler = ApplyInput;
        InputSystem.onAfterUpdate += _inputHandler;
        ApplyInput();

        string output = Environment.GetEnvironmentVariable(OutputVariable);
        if (string.IsNullOrWhiteSpace(output)) output = ".utmp/mortar-sandbox-targeter";
        Directory.CreateDirectory(output);
        string captures = Path.Combine(output, "captures");
        Directory.CreateDirectory(captures);
        var csv = new StringBuilder();
        csv.AppendLine("scenario,frame,pose_frame,owner_x,owner_y,owner_z,muzzle_x,muzzle_y,muzzle_z,muzzle_local_x,muzzle_local_y,muzzle_local_z,view_x,view_y,view_z,displayed_aim_x,displayed_aim_y,displayed_aim_z,resolved,range,selected_x,selected_y,selected_z,selected_collider,predicted,predicted_collider,predicted_type,hit_x,hit_y,hit_z,normal_x,normal_y,normal_z,hit_distance,trajectory_distance,marker_active,marker_x,marker_y,marker_z,marker_contact_x,marker_contact_y,marker_contact_z,marker_radius,marker_bounds_x,marker_bounds_y,marker_bounds_z,grounded,velocity_y,blast_annulus_tangent_radius");
        var errors = new List<string>();
        int sampled = 0, currentScenario = -1, framesInScenario = 0;
        string scenarioName = "settling";
        bool capturing = false;
        bool simulateLegacyReload = Environment.GetEnvironmentVariable("SCRAPWAVES_MORTAR_SIMULATE_LEGACY_RELOAD") == "1";
        bool legacyMigrationInjected = false;
        float maximumRenderedRadiusError = 0f;
        Vector3 initialLocalMuzzle = Vector3.zero;
        float maxLocalMuzzleMotion = 0f, maxJumpRise = 0f, maxPlanarTravel = 0f;
        Vector3 phaseStart = Vector3.zero;
        RaycastHit[] predictionBuffer = new RaycastHit[64];
        _probe = owner.gameObject.AddComponent<RuntimeProbe>();
        _probe.Sample = () =>
        {
            if (!capturing) return;
            sampled++;
            framesInScenario++;
            bool resolved = mortar.TryResolveManualAim(sandbox.CurrentAimDirection, out MortarManualAimSolution aim);
            bool predicted = mortar.TryGetManualTerrainImpact(sandbox.CurrentAimDirection, out RaycastHit hit);
            Physics.Raycast(aim.SurfacePoint + Vector3.up * .04f, Vector3.down, out RaycastHit selected,
                .08f, ~0, QueryTriggerInteraction.Ignore);
            Vector3 view = orbit.GameplayCenterRay.direction;
            MortarLandingIndicatorVfx marker = (MortarLandingIndicatorVfx)typeof(ReticleHud)
                .GetField("_authoredMortarMarker", PrivateInstance).GetValue(hud);
            bool active = marker != null && marker.gameObject.activeInHierarchy;
            Vector3 markerPosition = marker != null ? marker.transform.position : Vector3.zero;
            Vector3 markerContact = marker != null ? marker.ContactPoint : Vector3.zero;
            Vector3 boundsSize = Vector3.zero;
            if (marker != null)
            {
                Renderer[] renderers = marker.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length > 0)
                {
                    Bounds bounds = renderers[0].bounds;
                    foreach (Renderer renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                    boundsSize = bounds.size;
                }
            }
            Vector3 localMuzzle = owner.InverseTransformPoint(sandbox.ProjectileSpawn.position);
            maxLocalMuzzleMotion = Mathf.Max(maxLocalMuzzleMotion, Vector3.Distance(initialLocalMuzzle, localMuzzle));
            maxJumpRise = Mathf.Max(maxJumpRise, owner.position.y - phaseStart.y);
            maxPlanarTravel = Mathf.Max(maxPlanarTravel, Vector3.ProjectOnPlane(owner.position - phaseStart, Vector3.up).magnitude);
            float envelopeDistance = resolved && predicted ? DistanceToFlight(aim, hit.point, mortar.ManualTravelTime) : -1f;
            float renderedRadius = active && Vector3.Dot(marker.transform.up, Vector3.up) > .999f
                ? GetRenderedBlastTangentRadius(marker) : -1f;
            string prefix = scenarioName + " frame " + Time.frameCount + ": ";
            if (renderedRadius >= 0f)
            {
                float radiusError = Mathf.Abs(renderedRadius - marker.BlastRadius);
                maximumRenderedRadiusError = Mathf.Max(maximumRenderedRadiusError, radiusError);
                if (radiusError > .025f)
                    errors.Add(prefix + "Rendered blast annulus tangent radius " + renderedRadius.ToString("F4")
                        + " differs from requested radius " + marker.BlastRadius.ToString("F4"));
            }
            if (!resolved)
            {
                // Moving past the finite arena can leave no support at the chosen range.
                // This is a valid no-shot state: the current targeter must disappear.
                if (predicted || active) errors.Add(prefix + "No selected terrain, but a prediction/marker remains active.");
            }
            else if (!predicted) errors.Add(prefix + "Selected terrain exists but no first collision resolved.");
            else
            {
                if (hit.transform == owner || hit.transform.IsChildOf(owner)
                    || (hit.rigidbody != null && (hit.rigidbody.transform == owner || hit.rigidbody.transform.IsChildOf(owner))))
                    errors.Add(prefix + "Owner collider was returned: " + Hierarchy(hit.transform));
                if (envelopeDistance > mortar.ShellCollisionRadius + .03f)
                    errors.Add(prefix + "Contact is outside shell collision envelope: " + hit.point.ToString("F5")
                        + " collider=" + Hierarchy(hit.transform) + " distance=" + envelopeDistance.ToString("F5"));
                Bounds collisionBounds = hit.collider.bounds;
                collisionBounds.Expand(.04f);
                if (!collisionBounds.Contains(hit.point))
                    errors.Add(prefix + "Contact is outside its own collider bounds: " + hit.point.ToString("F5"));
                if (!active || Vector3.Distance(markerContact, hit.point) > .045f)
                    errors.Add(prefix + "Authored marker does not match current predicted contact.");
                if (Vector3.Distance(aim.Origin, sandbox.ProjectileSpawn.position) > .001f)
                    errors.Add(prefix + "Selection did not use current animated main muzzle.");
                if (MortarTrajectory.TryPredictManualCollision(aim.Origin, aim.Target, aim.ArcHeight,
                    mortar.ManualTravelTime, mortar.ShellCollisionRadius, owner, predictionBuffer,
                    MortarUpgradePayload.None, out RaycastHit direct, aim.SurfaceNormal)
                    && (direct.collider != hit.collider || Vector3.Distance(direct.point, hit.point) > .001f))
                    errors.Add(prefix + "Weapon preview differs from shared actual-flight prediction.");
            }
            if (animation.LastEvaluatedFrame != Time.frameCount)
                errors.Add(prefix + "Animated origin was not evaluated in the current frame.");
            csv.Append(scenarioName).Append(',').Append(Time.frameCount).Append(',').Append(animation.LastEvaluatedFrame);
            Append(csv, owner.position); Append(csv, sandbox.ProjectileSpawn.position); Append(csv, localMuzzle);
            Append(csv, view); Append(csv, sandbox.CurrentAimDirection);
            csv.Append(',').Append(resolved); Append(csv, aim.Range); Append(csv, aim.SurfacePoint);
            csv.Append(',').Append(Csv(Hierarchy(selected.transform)));
            csv.Append(',').Append(predicted).Append(',').Append(Csv(Hierarchy(hit.transform)))
                .Append(',').Append(hit.collider != null ? hit.collider.GetType().Name : "none");
            Append(csv, hit.point); Append(csv, hit.normal); Append(csv, hit.distance); Append(csv, envelopeDistance);
            csv.Append(',').Append(active); Append(csv, markerPosition); Append(csv, markerContact);
            Append(csv, marker != null ? marker.BlastRadius : 0f); Append(csv, boundsSize);
            csv.Append(',').Append(movement.IsGroundedOnSurface); Append(csv, body.linearVelocity.y);
            Append(csv, renderedRadius); csv.AppendLine();
            // Capture actual gameplay camera after its LateUpdate and the HUD's current marker update.
            if (framesInScenario == 24 || (currentScenario == 7 && framesInScenario == 44))
                Capture(camera, Path.Combine(captures, currentScenario + "-" + scenarioName + "-" + framesInScenario + ".png"));
        };

        Vector3 screenshotDirection = new Vector3(-.27f, -.08f, -.96f).normalized;
        Vector3[] directions =
        {
            screenshotDirection, new(0f, -.08f, 1f), new(1f, -.08f, 0f), new(-1f, -.08f, 0f),
            Quaternion.AngleAxis(20f, Vector3.right) * Vector3.forward,
            Vector3.forward, Quaternion.AngleAxis(-7.5f, Vector3.right) * Vector3.forward,
            screenshotDirection
        };
        string[] names = { "screenshot-direction", "yaw-north", "yaw-east", "yaw-west", "minimum-pitch", "level-pitch", "maximum-pitch", "move-and-jump" };
        for (int scenario = 0; scenario < directions.Length; scenario++)
        {
            capturing = false;
            _keys = Array.Empty<Key>();
            ApplyInput();
            body.position = new Vector3(scenario == 7 ? 4f : 0f, 1.1f, scenario == 7 ? 2f : 0f);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            SetOrbit(orbit, directions[scenario]);
            Physics.SyncTransforms();
            for (int settle = 0; settle < 12; settle++) yield return null;
            if (scenario == 0 && simulateLegacyReload)
            {
                // Simulate the fields added by the prototype being absent from an older
                // live instance. Existing authored bases/radius remain preserved exactly
                // as Unity's hot-reload serialization preserves them. This is an explicit
                // field-migration injection, not a claim that this test recompiles scripts.
                MortarLandingIndicatorVfx marker = Read<MortarLandingIndicatorVfx>(hud, "_authoredMortarMarker");
                Assert.That(marker, Is.Not.Null);
                typeof(MortarLandingIndicatorVfx).GetField("_baseScalesCached", PrivateInstance).SetValue(marker, false);
                foreach (string field in new[] { "_cachedBlastLayer", "_cachedTimeLayer", "_cachedCoreLayer" })
                    typeof(MortarLandingIndicatorVfx).GetField(field, PrivateInstance).SetValue(marker, null);
                typeof(MortarLandingIndicatorVfx).GetMethod("OnValidate", PrivateInstance).Invoke(marker, null);
                legacyMigrationInjected = true;
            }
            currentScenario = scenario;
            scenarioName = names[scenario];
            framesInScenario = 0;
            phaseStart = owner.position;
            initialLocalMuzzle = owner.InverseTransformPoint(sandbox.ProjectileSpawn.position);
            capturing = true;
            int frameCount = scenario == 7 ? 90 : 30;
            for (int frame = 0; frame < frameCount; frame++)
            {
                _keys = scenario == 7 ? (frame >= 22 && frame < 25 ? new[] { Key.W, Key.Space } : new[] { Key.W }) : Array.Empty<Key>();
                ApplyInput();
                yield return null;
            }
            capturing = false;
            File.WriteAllText(Path.Combine(output, "saved-sandbox-targeter.csv"), csv.ToString());
        }
        _keys = Array.Empty<Key>();
        ApplyInput();
        File.WriteAllText(Path.Combine(output, "saved-sandbox-targeter-summary.txt"),
            "Scene: " + ScenePath + "\nUnity: " + Application.unityVersion + "\nSamples: " + sampled
            + "\nLegacy field-migration simulation requested: " + simulateLegacyReload
            + "\nLegacy field-migration injection applied: " + legacyMigrationInjected
            + "\nThis simulates missing new cache fields while preserving existing bases; no scripts are recompiled by the fixture."
            + "\nMaximum rendered/requested tangent radius error: " + maximumRenderedRadiusError.ToString("F5", CultureInfo.InvariantCulture)
            + "\nNo shots requested; ammo " + initialAmmo + " -> " + sandbox.CurrentManualWeapon.CurrentAmmo
            + "\nMaximum animated local muzzle motion: " + maxLocalMuzzleMotion.ToString("F5", CultureInfo.InvariantCulture)
            + "\nMaximum planar movement: " + maxPlanarTravel.ToString("F5", CultureInfo.InvariantCulture)
            + "\nMaximum jump rise: " + maxJumpRise.ToString("F5", CultureInfo.InvariantCulture)
            + "\nFailures: " + errors.Count + "\n" + string.Join("\n", errors.Take(40)));
        Assert.That(sampled, Is.GreaterThanOrEqualTo(290));
        Assert.That(sandbox.CurrentManualWeapon.CurrentAmmo, Is.EqualTo(initialAmmo), "Probe must never fire.");
        Assert.That(maxLocalMuzzleMotion, Is.GreaterThan(.0001f), "Production animation must actually move the main muzzle.");
        Assert.That(maxPlanarTravel, Is.GreaterThan(.5f), "Movement must be observed, not merely requested.");
        Assert.That(maxJumpRise, Is.GreaterThan(.2f), "Jump must be observed through native player movement.");
        Assert.That(errors, Is.Empty, string.Join("\n", errors.Take(24)));
        yield return RunFiringPhases(sandbox, orbit, body, movement, animation, hud, camera, mortar, output, captures);
    }

    private IEnumerator RunFiringPhases(WeaponTestingSandboxManager sandbox, ThirdPersonCamera orbit,
        Rigidbody body, PlayerMovement movement, PlayerAnimationDriver animation, ReticleHud hud,
        Camera camera, MortarWeapon mortar, string output, string captures)
    {
        Transform owner = sandbox.PlayerTransform;
        var errors = new List<string>();
        var frameCsv = new StringBuilder("scenario,frame,owner_x,owner_y,owner_z,muzzle_x,muzzle_y,muzzle_z,view_x,view_y,view_z,hud_active,hud_x,hud_y,hud_z,active_shells,visible_shell_markers,shot_events,ammo,grounded,velocity_y\n");
        var shellCsv = new StringBuilder("scenario,frame,shell_id,shell_x,shell_y,shell_z,start_x,start_y,start_z,target_x,target_y,target_z,elapsed,travel_time,radius,manual,has_prediction,predicted_x,predicted_y,predicted_z,recomputed,recomputed_collider,recomputed_x,recomputed_y,recomputed_z,indicator_visible,indicator_x,indicator_y,indicator_z,vfx_target_x,vfx_target_y,vfx_target_z,indicator_owner_distance,blast_bounds_x,blast_bounds_y,blast_bounds_z,origin_contacts\n");
        var feedbackCsv = new StringBuilder("scenario,frame,event,origin_x,origin_y,origin_z,impact_x,impact_y,impact_z,owner_x,owner_y,owner_z,muzzle_x,muzzle_y,muzzle_z,grounded,sequence\n");
        var cueCsv = new StringBuilder("scenario,frame,cue_id,style,root_x,root_y,root_z,bounds_center_x,bounds_center_y,bounds_center_z,bounds_size_x,bounds_size_y,bounds_size_z\n");
        string phase = "fire-settling";
        bool sampling = false;
        int phaseShots = 0, totalShots = 0, firstShotFrame = -1, shellSamples = 0;
        int stationaryShots = 0, movingShots = 0, airborneShots = 0;
        float minimumIndicatorOwnerDistance = float.PositiveInfinity;
        var capturesMade = new HashSet<int>();
        RaycastHit[] hits = new RaycastHit[64];
        _presentation = sandbox.PresentationController;
        _feedbackHandler = (kind, context) =>
        {
            if (!sampling || context.WeaponType != WeaponType.Mortar || context.Mode != WeaponFeedbackMode.Manual) return;
            feedbackCsv.Append(phase).Append(',').Append(Time.frameCount).Append(',').Append(kind);
            Append(feedbackCsv, context.Origin); Append(feedbackCsv, context.ImpactPosition);
            Append(feedbackCsv, owner.position); Append(feedbackCsv, sandbox.ProjectileSpawn.position);
            feedbackCsv.Append(',').Append(movement.IsGroundedOnSurface).Append(',').Append(context.ActionSequenceId).AppendLine();
            if (kind != WeaponFeedbackEvent.ShotFired) return;
            phaseShots++;
            totalShots++;
            if (firstShotFrame < 0) firstShotFrame = Time.frameCount;
            if (!movement.IsGroundedOnSurface) airborneShots++;
            else if (Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up).magnitude > .5f) movingShots++;
            else stationaryShots++;
            if (Vector3.Distance(context.Origin, sandbox.ProjectileSpawn.position) > .001f)
                errors.Add(phase + ": real shot feedback did not use current animated main muzzle.");
            if (animation.LastEvaluatedFrame != Time.frameCount)
                errors.Add(phase + ": real shot occurred before current animation pose.");
        };
        _presentation.FeedbackEmitted += _feedbackHandler;
        _probe.Sample = () =>
        {
            if (!sampling) return;
            MortarLandingIndicatorVfx hudMarker = Read<MortarLandingIndicatorVfx>(hud, "_authoredMortarMarker");
            MortarShellVfx[] visuals = Object.FindObjectsByType<MortarShellVfx>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            int visible = 0;
            foreach (MortarShellVfx visual in visuals)
            {
                MortarShellImpact shell = visual.GetComponent<MortarShellImpact>();
                if (shell == null) { errors.Add(phase + ": active shell VFX has no shell component."); continue; }
                shellSamples++;
                Vector3 start = Read<Vector3>(shell, "_start");
                Vector3 target = Read<Vector3>(shell, "_manualLandingTarget");
                float radius = Read<float>(shell, "_collisionRadius");
                float travel = Read<float>(shell, "_travelTime");
                float arc = Read<float>(shell, "_arcHeight");
                Transform ignoredRoot = Read<Transform>(shell, "_ignoredRoot");
                bool manual = Read<bool>(shell, "_manualTargeting");
                bool predicted = MortarTrajectory.TryPredictManualCollision(start, target, arc, travel, radius,
                    ignoredRoot, hits, Read<MortarUpgradePayload>(shell, "_payload"), out RaycastHit expected,
                    Read<Vector3>(shell, "_manualLandingNormal"));
                Vector3 vfxTarget = Read<Vector3>(visual, "_target");
                Vector3 indicator = visual.LandingIndicatorPosition;
                float ownerDistance = Vector3.ProjectOnPlane(indicator - owner.position, Vector3.up).magnitude;
                Transform blast = Read<Transform>(visual, "_blastRadiusRing");
                Bounds blastBounds = blast != null && blast.TryGetComponent(out Renderer blastRenderer)
                    ? blastRenderer.bounds : default;
                string originContacts = string.Join("|", Physics.OverlapSphere(start, radius, ~0, QueryTriggerInteraction.Ignore)
                    .Where(collider => collider.transform != ignoredRoot && !collider.transform.IsChildOf(ignoredRoot)
                        && (collider.attachedRigidbody == null || (collider.attachedRigidbody.transform != ignoredRoot
                            && !collider.attachedRigidbody.transform.IsChildOf(ignoredRoot))))
                    .Select(collider => Hierarchy(collider.transform)));
                if (ignoredRoot != owner || !manual)
                    errors.Add(phase + ": authored shell lost its manual mode or player owner.");
                if (visual.LandingIndicatorVisible)
                {
                    visible++;
                    minimumIndicatorOwnerDistance = Mathf.Min(minimumIndicatorOwnerDistance, ownerDistance);
                    string prefix = phase + " frame " + Time.frameCount + " shell " + shell.GetInstanceID() + ": ";
                    if (!predicted || !shell.HasPredictedPresentationCollision)
                        errors.Add(prefix + "Landing indicator is visible without a predicted contact.");
                    else
                    {
                        if (Vector3.Distance(shell.PredictedPresentationCollisionPoint, expected.point) > .002f
                            || Vector3.Distance(vfxTarget, expected.point) > .002f)
                            errors.Add(prefix + "Shell landing indicator target differs from its own flight's first contact.");
                        if (Vector3.Distance(indicator, expected.point) > .06f)
                            errors.Add(prefix + "World-anchored shell ring differs from predicted contact: " + indicator.ToString("F4"));
                        // These phases fire at unobstructed distant floor/panels, and stop movement
                        // before reaching the target. Do not generalize this assertion to obstacles.
                        if (ownerDistance < 1f && Vector3.ProjectOnPlane(expected.point - owner.position, Vector3.up).magnitude > 4f)
                            errors.Add(prefix + "Shell ring appeared under the player despite a distant genuine collision.");
                        if (expected.transform == owner || expected.transform.IsChildOf(owner))
                            errors.Add(prefix + "Real shell preview collided with owner.");
                    }
                }
                shellCsv.Append(phase).Append(',').Append(Time.frameCount).Append(',').Append(shell.GetInstanceID());
                Append(shellCsv, shell.transform.position); Append(shellCsv, start); Append(shellCsv, target);
                Append(shellCsv, Read<float>(shell, "_elapsed")); Append(shellCsv, travel); Append(shellCsv, radius);
                shellCsv.Append(',').Append(manual).Append(',').Append(shell.HasPredictedPresentationCollision);
                Append(shellCsv, shell.PredictedPresentationCollisionPoint);
                shellCsv.Append(',').Append(predicted).Append(',').Append(Csv(Hierarchy(expected.transform)));
                Append(shellCsv, expected.point); shellCsv.Append(',').Append(visual.LandingIndicatorVisible);
                Append(shellCsv, indicator); Append(shellCsv, vfxTarget); Append(shellCsv, ownerDistance);
                Append(shellCsv, blastBounds.size); shellCsv.Append(',').Append(Csv(originContacts)).AppendLine();
            }
            // A launch/impact cue is visually distinct from both targeting systems. Record it
            // too, so a bright ring near the muzzle cannot be misidentified as a landing target.
            foreach (MortarCueVfx cue in Object.FindObjectsByType<MortarCueVfx>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                Renderer[] renderers = cue.GetComponentsInChildren<Renderer>();
                Bounds bounds = new(cue.transform.position, Vector3.zero);
                foreach (Renderer renderer in renderers) if (!(renderer is ParticleSystemRenderer)) bounds.Encapsulate(renderer.bounds);
                cueCsv.Append(phase).Append(',').Append(Time.frameCount).Append(',').Append(cue.GetInstanceID()).Append(',').Append(cue.Style);
                Append(cueCsv, cue.transform.position); Append(cueCsv, bounds.center); Append(cueCsv, bounds.size); cueCsv.AppendLine();
            }
            frameCsv.Append(phase).Append(',').Append(Time.frameCount); Append(frameCsv, owner.position);
            Append(frameCsv, sandbox.ProjectileSpawn.position); Append(frameCsv, orbit.GameplayCenterRay.direction);
            frameCsv.Append(',').Append(hudMarker != null && hudMarker.gameObject.activeInHierarchy);
            Append(frameCsv, hudMarker != null ? hudMarker.ContactPoint : Vector3.zero);
            frameCsv.Append(',').Append(visuals.Length).Append(',').Append(visible).Append(',').Append(phaseShots);
            Append(frameCsv, sandbox.CurrentManualWeapon.CurrentAmmo);
            frameCsv.Append(',').Append(movement.IsGroundedOnSurface); Append(frameCsv, body.linearVelocity.y); frameCsv.AppendLine();
            int age = firstShotFrame >= 0 ? Time.frameCount - firstShotFrame : -1;
            if ((age == 0 || age == 8 || age == 20 || age == 40) && capturesMade.Add(age))
                Capture(camera, Path.Combine(captures, phase + "-post-shot-" + age.ToString("D2") + ".png"));
        };

        float firingInitialAmmo = sandbox.CurrentManualWeapon.CurrentAmmo;
        for (int scenario = 0; scenario < 3; scenario++)
        {
            sampling = false;
            _fireHeld = false;
            _keys = Array.Empty<Key>();
            ApplyInput();
            body.position = new Vector3(scenario == 0 ? 0f : 4f, 1.1f, scenario == 0 ? 0f : 2f);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            SetOrbit(orbit, new Vector3(-.27f, -.08f, -.96f));
            Physics.SyncTransforms();
            for (int settle = 0; settle < 18; settle++) yield return null;
            phase = scenario == 0 ? "fire-idle" : scenario == 1 ? "fire-moving" : "fire-jumping";
            phaseShots = 0;
            firstShotFrame = -1;
            capturesMade.Clear();
            sampling = true;
            for (int frame = 0; frame < 90; frame++)
            {
                int fireFrame = scenario == 2 ? 20 : 6;
                _fireHeld = frame >= fireFrame && frame < fireFrame + 4;
                _keys = scenario == 0 || frame >= 40 ? Array.Empty<Key>()
                    : scenario == 2 && frame >= 6 && frame < 9 ? new[] { Key.W, Key.Space } : new[] { Key.W };
                ApplyInput();
                yield return null;
            }
            sampling = false;
            if (phaseShots != 1) errors.Add(phase + ": expected one actual ShotFired feedback event, observed " + phaseShots);
            File.WriteAllText(Path.Combine(output, "saved-sandbox-firing-frames.csv"), frameCsv.ToString());
            File.WriteAllText(Path.Combine(output, "saved-sandbox-firing-shells.csv"), shellCsv.ToString());
            File.WriteAllText(Path.Combine(output, "saved-sandbox-firing-feedback.csv"), feedbackCsv.ToString());
            File.WriteAllText(Path.Combine(output, "saved-sandbox-firing-cues.csv"), cueCsv.ToString());
        }
        _fireHeld = false;
        _keys = Array.Empty<Key>();
        ApplyInput();
        _presentation.FeedbackEmitted -= _feedbackHandler;
        _feedbackHandler = null;
        File.WriteAllText(Path.Combine(output, "saved-sandbox-firing-summary.txt"),
            "Real manual fire via temporary mouse; saved sandbox/player/camera.\nShot feedback events: " + totalShots
            + "\nStationary shots: " + stationaryShots + "\nMoving shots: " + movingShots + "\nAirborne shots: " + airborneShots
            + "\nActive shell samples: " + shellSamples + "\nMinimum shell-marker planar distance to player: "
            + minimumIndicatorOwnerDistance.ToString("F5", CultureInfo.InvariantCulture)
            + "\nAmmo: " + firingInitialAmmo + " -> " + sandbox.CurrentManualWeapon.CurrentAmmo
            + "\nFailures: " + errors.Count + "\n" + string.Join("\n", errors.Take(40)));
        Assert.That(totalShots, Is.EqualTo(3));
        Assert.That(stationaryShots, Is.GreaterThanOrEqualTo(1));
        Assert.That(movingShots, Is.GreaterThanOrEqualTo(1));
        Assert.That(airborneShots, Is.GreaterThanOrEqualTo(1), "Jumped shot must be observed in gameplay, not just requested.");
        Assert.That(shellSamples, Is.GreaterThan(3), "The real authored shells must be observed in flight.");
        Assert.That(sandbox.CurrentManualWeapon.CurrentAmmo, Is.EqualTo(firingInitialAmmo - 3f));
        Assert.That(errors, Is.Empty, string.Join("\n", errors.Take(24)));
    }

    private static T Read<T>(object instance, string field) => (T)instance.GetType().GetField(field, PrivateInstance).GetValue(instance);

    private static float GetRenderedBlastTangentRadius(MortarLandingIndicatorVfx marker)
    {
        Transform layer = Read<Transform>(marker, "_blastRadiusRing");
        if (layer == null || !layer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null) return -1f;
        Vector3[] vertices = filter.sharedMesh.vertices;
        int[] indices = filter.sharedMesh.triangles;
        if (indices.Length == 0) return -1f;
        float maximum = 0f;
        foreach (int index in indices)
            maximum = Mathf.Max(maximum, Vector3.ProjectOnPlane(
                layer.TransformPoint(vertices[index]) - marker.ContactPoint, Vector3.up).magnitude);
        return maximum;
    }

    private void ApplyInput()
    {
        if (_mouse != null && _mouse.added) { InputState.Change(_mouse, new MouseState { buttons = (ushort)(_fireHeld ? 1 : 0) }); _mouse.MakeCurrent(); }
        if (_keyboard != null && _keyboard.added) { InputState.Change(_keyboard, new KeyboardState(_keys)); _keyboard.MakeCurrent(); }
    }

    private static void SetOrbit(ThirdPersonCamera orbit, Vector3 direction)
    {
        direction.Normalize();
        typeof(ThirdPersonCamera).GetField("_pitch", PrivateInstance).SetValue(orbit, -Mathf.Asin(direction.y) * Mathf.Rad2Deg);
        typeof(ThirdPersonCamera).GetField("_yaw", PrivateInstance).SetValue(orbit, Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg);
    }

    private static float DistanceToFlight(MortarManualAimSolution aim, Vector3 point, float travelTime)
    {
        float minimum = float.PositiveInfinity;
        float maximumTime = MortarTrajectory.GetMaximumNormalizedTime(travelTime);
        Vector3 previous = aim.Origin;
        for (int index = 1; index <= Mathf.CeilToInt(maximumTime * FlightSegments); index++)
        {
            float t = Mathf.Min(maximumTime, (float)index / FlightSegments);
            Vector3 next = MortarTrajectory.EvaluateCollisionPath(aim.Origin, aim.Target, aim.ArcHeight, t);
            Vector3 segment = next - previous;
            float along = segment.sqrMagnitude > .000001f ? Mathf.Clamp01(Vector3.Dot(point - previous, segment) / segment.sqrMagnitude) : 0f;
            minimum = Mathf.Min(minimum, Vector3.Distance(point, previous + segment * along));
            previous = next;
        }
        return minimum;
    }

    private static string Hierarchy(Transform value) => value == null ? "none"
        : value.parent == null ? value.name : Hierarchy(value.parent) + "/" + value.name;
    private static string Csv(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
    private static void Append(StringBuilder text, float value) => text.Append(',').Append(value.ToString("G9", CultureInfo.InvariantCulture));
    private static void Append(StringBuilder text, Vector3 value) { Append(text, value.x); Append(text, value.y); Append(text, value.z); }

    private static void Capture(Camera camera, string path)
    {
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
        RenderTexture target = RenderTexture.GetTemporary(1280, 720, 24);
        RenderTexture previousActive = RenderTexture.active;
        RenderTexture previousTarget = camera.targetTexture;
        float previousAspect = camera.aspect;
        Texture2D texture = new(1280, 720, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.aspect = 1280f / 720f;
            camera.Render();
            RenderTexture.active = target;
            texture.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = previousTarget;
            camera.aspect = previousAspect;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(target);
            Object.Destroy(texture);
        }
    }

    [UnityTearDown]
    public IEnumerator RestoreScene()
    {
        if (_presentation != null && _feedbackHandler != null) _presentation.FeedbackEmitted -= _feedbackHandler;
        _feedbackHandler = null;
        _presentation = null;
        if (_inputHandler != null) InputSystem.onAfterUpdate -= _inputHandler;
        _inputHandler = null;
        if (_probe != null) { _probe.Sample = null; Object.DestroyImmediate(_probe); }
        _probe = null;
        if (_mouse != null && _mouse.added) InputSystem.RemoveDevice(_mouse);
        if (_keyboard != null && _keyboard.added) InputSystem.RemoveDevice(_keyboard);
        _mouse = null;
        _keyboard = null;
        if (Application.isPlaying)
        {
            Time.captureDeltaTime = _runtimeStateCaptured ? _previousCaptureDelta : 0f;
            Time.timeScale = _runtimeStateCaptured ? _previousTimeScale : 1f;
            yield return new ExitPlayMode();
        }
        string saved = SessionState.GetString(SetupKey, "");
        if (string.IsNullOrEmpty(saved)) yield break;
        SessionState.EraseString(SetupKey);
        SavedSetup setup = JsonUtility.FromJson<SavedSetup>(saved);
        if (setup?.Scenes?.Length > 0 && setup.Scenes.All(scene => !string.IsNullOrEmpty(scene.Path)))
            EditorSceneManager.RestoreSceneManagerSetup(setup.Scenes.Select(scene => new SceneSetup
            { path = scene.Path, isLoaded = scene.Loaded, isActive = scene.Active }).ToArray());
        else if (setup?.RestoreBatchEmptyScene == true)
        {
            // An isolated batch run normally starts with an untitled empty scene,
            // which RestoreSceneManagerSetup cannot reopen by path. Clear the loaded
            // sandbox explicitly so later physics tests cannot hit its player/arena.
            // Interactive untitled scenes are rejected before setup and never enter here.
            Assert.That(Application.isBatchMode, Is.True,
                "Only the batch setup that explicitly allowed an untitled scene may restore an empty scene.");
            UnityEngine.SceneManagement.Scene clean = EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.That(clean.GetRootGameObjects(), Is.Empty,
                "Saved-sandbox teardown must remove the arena/player before subsequent physics tests.");
            Assert.That(Object.FindFirstObjectByType<WeaponTestingSandboxManager>(), Is.Null);
        }
    }
}
