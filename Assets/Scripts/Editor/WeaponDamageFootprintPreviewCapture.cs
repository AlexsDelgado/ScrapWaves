#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Controlled renders of production effects against their weapon's resolved damage geometry.</summary>
public static class WeaponDamageFootprintPreviewCapture
{
    private const int Tile = 480;
    private const uint Seed = 27331;
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly float[] Sizes = { .5f, 1f, 2f, 8f };
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    [Serializable] private sealed class Report
    {
        public string UnityVersion;
        public string Scope = "Production manual weapons; level 1; no upgrade path; zero heat; neutral player except ProjectileAreaSize.";
        public string RadiusSource = "WeaponBehaviourFactory.Create + Setup + CaptureGeometry: Manual / Explosion / Sphere.RadiusMeters";
        public string Limitation = "Controlled Edit Mode simulation, not a recorded gameplay hit test. CPU effect motion and particle seeds/timesteps are fixed; material shader noise retains the Editor render clock. No gameplay scene or asset is saved.";
        public string Reference = "Thin cyan centerline is the exact radius in the horizontal plane through the spherical explosion center, on a flat ground impact. The reference is a measurement overlay, not a gameplay effect or target-collider boundary. Grid spacing: 1 metre.";
        public uint ParticleSeed = Seed;
        public List<WeaponRecord> Weapons = new();
    }

    [Serializable] private sealed class WeaponRecord
    {
        public string WeaponAsset;
        public string Profile;
        public string Cue;
        public string ImpactPrefab;
        public string ShellPrefab;
        public float Duration;
        public float ComparisonOrthoHalfHeight;
        public float StressOrthoHalfHeight;
        public float[] Times;
        public List<SizeRecord> Sizes = new();
    }

    [Serializable] private sealed class SizeRecord
    {
        public float SizeStat;
        public float DamageRadiusMetres;
        public float FlightCollisionRadiusMetres;
    }

    [Serializable] private sealed class GameplayCameraEvidence
    {
        public string ScenePath;
        public string CameraObject;
        public string ControllerSource = "Assets/Scripts/ThirdPersonCamera.cs: GetFollowPose / Start / Update";
        public float VerticalFov;
        public float NearClip;
        public float SavedPitchDegrees;
        public float MinimumPitchDegrees;
        public float MaximumPitchDegrees;
        public float RepresentativeAimPitchDegrees;
        public float PivotHeight;
        public float ShoulderOffset;
        public float CameraHeightOffset;
        public float OrbitDistance;
        public float ImpactDistanceAheadOfPlayer;
        public Vector3 ReviewCameraPosition;
        public string Interpretation = "Perspective uses the authored vertical FOV and unobstructed follow offsets with a declared representative downward mouse-look pose. Gameplay pitch is dynamic; saved startup pitch is recorded separately. Square review viewport, no shake, no collision recovery, no live gameplay camera recording.";
    }

    [Serializable] private sealed class BlastFillFrame
    {
        public string File;
        public float Seconds;
        public string View;
        public bool DiagnosticAnnulusHidden;
    }

    [Serializable] private sealed class RocketBlastFillReport
    {
        public string UnityVersion;
        public string Scope = "Production RocketLauncher manual level 1, no upgrade path, zero heat, High quality, normal flash; ProjectileAreaSize 0.5 / 1 / 2.";
        public string RadiusSource = "WeaponBehaviourFactory.Create + Setup + CaptureGeometry: Manual / Explosion / Sphere.RadiusMeters";
        public string Rows = "Top: clean production imagery, cyan measurement overlay off. Bottom: identical production imagery with exact cyan ground-radius reference.";
        public string Diagnostic = "Main-volume diagnostic suppresses only the Resolved Damage Footprint renderer. Main blast, pressure meshes, smoke and debris remain. Production panels retain the annulus.";
        public string Limitation = "Controlled Edit Mode samples of ordinary runtime clones, not gameplay hit tests. Fixed particle seeds and CPU animation times; material noise retains the Editor render clock. No scene or production asset is saved.";
        public float[] Times;
        public WeaponRecord Production;
        public GameplayCameraEvidence GameplayCamera;
        public List<BlastFillFrame> Frames = new();
    }

    private sealed class Fixture : IDisposable
    {
        public GameObject Root;
        public PooledWeaponVfx Pool;
        public MonoBehaviour Cue;
        public Renderer[] Renderers;
        public ParticleSystem[] Particles;
        public Transform[] AnimatedRoots;
        public Quaternion[] RestRotations;
        public MethodInfo FrameMethod;
        public FieldInfo ElapsedField;
        public float Lifetime;
        public float RotationSpeed;
        public float SimulatedTime;
        public float Radius;
        public float Size;
        public float Duration;
        public bool Reduced;
        public bool Rocket;
        private bool _visible = true;
        private bool[] _rendererEnabled;

        public void Advance(float time)
        {
            SetVisible(true);
            float until = Mathf.Min(time, Duration);
            while (SimulatedTime + .000001f < until)
            {
                float step = Mathf.Min(1f / 120f, until - SimulatedTime);
                SimulatedTime += step;
                ElapsedField.SetValue(Cue, SimulatedTime);
                FrameMethod.Invoke(Cue, new object[] { Mathf.Clamp01(SimulatedTime / Lifetime) });
                for (int i = 0; i < AnimatedRoots.Length; i++)
                    if (AnimatedRoots[i] != null)
                        AnimatedRoots[i].localRotation = RestRotations[i]
                            * Quaternion.AngleAxis(RotationSpeed * SimulatedTime, Rocket ? Vector3.forward : Vector3.up);
                foreach (ParticleSystem particles in Particles)
                    particles.Simulate(step, withChildren: false, restart: false, fixedTimeStep: false);
            }
            if (time >= Duration && Pool.IsActive) Pool.Release();
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _rendererEnabled ??= new bool[Renderers.Length];
            for (int i = 0; i < Renderers.Length; i++)
            {
                Renderer renderer = Renderers[i];
                if (renderer == null) continue;
                // Isolate montage tiles without overriding the cue's own lifetime/zero-radius visibility.
                if (!visible) _rendererEnabled[i] = renderer.enabled;
                renderer.enabled = visible && _rendererEnabled[i] && Pool.IsActive;
            }
            _visible = visible;
        }

        public void Dispose()
        {
            if (Root != null) Object.DestroyImmediate(Root);
        }
    }

    private sealed class Stage : IDisposable
    {
        public Scene Scene;
        public Camera Camera;
        public RenderTexture Target;
        public Text Header;
        public Text Footer;
        public LineRenderer Circle;
        public readonly List<Object> Cleanup = new();
        private GameObject _grid;

        public Stage()
        {
            Scene = EditorSceneManager.NewPreviewScene();
            Camera = new GameObject("Damage footprint review camera", typeof(Camera)).GetComponent<Camera>();
            SceneManager.MoveGameObjectToScene(Camera.gameObject, Scene);
            Camera.enabled = false;
            Camera.clearFlags = CameraClearFlags.SolidColor;
            Camera.backgroundColor = new Color(.035f, .045f, .052f);
            Camera.orthographic = true;
            Camera.nearClipPlane = .1f;
            Camera.farClipPlane = 500f;
            Camera.allowHDR = true;
            Camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(Scene);
            UniversalAdditionalCameraData extra = Camera.GetUniversalAdditionalCameraData();
            extra.renderPostProcessing = false;
            extra.renderShadows = false;
            extra.requiresColorTexture = false;
            extra.requiresDepthTexture = false;
            Target = new RenderTexture(Tile, Tile, 24, RenderTextureFormat.ARGB32);
            Target.Create();
            Camera.targetTexture = Target;

            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            SceneManager.MoveGameObjectToScene(floor, Scene);
            floor.name = "Flat reference support - top is y=0";
            floor.transform.position = Vector3.down * .5f;
            floor.transform.localScale = new Vector3(400f, 1f, 400f);
            int terrain = LayerMask.NameToLayer("Terrain");
            if (terrain >= 0) floor.layer = terrain;
            floor.GetComponent<Renderer>().sharedMaterial = Material(new Color(.16f, .18f, .20f), 2000);
            Light light = new GameObject("Preview key", typeof(Light)).GetComponent<Light>();
            SceneManager.MoveGameObjectToScene(light.gameObject, Scene);
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(42f, -26f, 0f);
            light.color = new Color(1f, .91f, .8f);
            light.intensity = 2f;
            light.shadows = LightShadows.None;
            Circle = NewLine("Exact damage radius reference", Material(new Color(.1f, .92f, 1f), 4500));
            Circle.positionCount = 193;
            Circle.loop = false;

            GameObject ui = new("Review labels", typeof(Canvas));
            SceneManager.MoveGameObjectToScene(ui, Scene);
            Canvas canvas = ui.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = Camera;
            canvas.planeDistance = 1f;
            canvas.sortingOrder = 100;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            Header = Label(ui.transform, font, true);
            Footer = Label(ui.transform, font, false);
        }

        public void Frame(float ortho, bool oblique)
        {
            Camera.orthographic = true;
            Camera.orthographicSize = ortho;
            Camera.transform.position = oblique ? new Vector3(0f, 90f, -70f) : Vector3.up * 120f;
            Camera.transform.rotation = Quaternion.LookRotation(-Camera.transform.position, oblique ? Vector3.up : Vector3.forward);
            if (_grid != null) Object.DestroyImmediate(_grid);
            _grid = new GameObject("One metre grid");
            SceneManager.MoveGameObjectToScene(_grid, Scene);
            Material minor = Material(new Color(.205f, .235f, .255f), 2001);
            Material major = Material(new Color(.29f, .33f, .35f), 2001);
            int extent = Mathf.CeilToInt(ortho * 1.65f);
            var vertices = new List<Vector3>();
            var minorIndices = new List<int>();
            var majorIndices = new List<int>();
            for (int i = -extent; i <= extent; i++)
            {
                bool emphasized = i % 5 == 0;
                float halfWidth = ortho / Tile * (emphasized ? 1.15f : .55f);
                AddGridQuad(new Vector3(i - halfWidth, .014f, -extent), new Vector3(i + halfWidth, .014f, -extent),
                    new Vector3(i + halfWidth, .014f, extent), new Vector3(i - halfWidth, .014f, extent), emphasized);
                AddGridQuad(new Vector3(-extent, .014f, i - halfWidth), new Vector3(extent, .014f, i - halfWidth),
                    new Vector3(extent, .014f, i + halfWidth), new Vector3(-extent, .014f, i + halfWidth), emphasized);
            }
            Mesh gridMesh = new() { name = "Horizontal metre-grid quads", hideFlags = HideFlags.HideAndDontSave };
            gridMesh.SetVertices(vertices);
            gridMesh.subMeshCount = 2;
            gridMesh.SetTriangles(minorIndices, 0);
            gridMesh.SetTriangles(majorIndices, 1);
            gridMesh.RecalculateBounds();
            gridMesh.RecalculateNormals();
            Cleanup.Add(gridMesh);
            _grid.AddComponent<MeshFilter>().sharedMesh = gridMesh;
            MeshRenderer gridRenderer = _grid.AddComponent<MeshRenderer>();
            gridRenderer.sharedMaterials = new[] { minor, major };
            gridRenderer.shadowCastingMode = ShadowCastingMode.Off;
            gridRenderer.receiveShadows = false;

            void AddGridQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, bool emphasized)
            {
                int start = vertices.Count;
                vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
                List<int> indices = emphasized ? majorIndices : minorIndices;
                indices.Add(start); indices.Add(start + 2); indices.Add(start + 1);
                indices.Add(start); indices.Add(start + 3); indices.Add(start + 2);
            }
        }

        public void GameplayFrame(float ortho, GameplayCameraEvidence evidence)
        {
            Frame(ortho, false);
            Quaternion orbit = Quaternion.Euler(evidence.RepresentativeAimPitchDegrees, 0f, 0f);
            Vector3 followPosition = Vector3.back * evidence.ImpactDistanceAheadOfPlayer;
            Vector3 anchor = followPosition + Vector3.up * evidence.PivotHeight
                + orbit * new Vector3(evidence.ShoulderOffset, evidence.CameraHeightOffset, 0f);
            Camera.orthographic = false;
            Camera.fieldOfView = evidence.VerticalFov;
            Camera.nearClipPlane = evidence.NearClip;
            Camera.transform.SetPositionAndRotation(anchor + orbit * Vector3.back * evidence.OrbitDistance, orbit);
            evidence.ReviewCameraPosition = Camera.transform.position;
            // Used only to retain a thin measurement-line width; perspective projection uses the FOV.
            Camera.orthographicSize = Vector3.Distance(Camera.transform.position, Vector3.zero)
                * Mathf.Tan(Camera.fieldOfView * .5f * Mathf.Deg2Rad);
        }

        public void Reference(float radius)
        {
            Circle.widthMultiplier = Camera.orthographicSize * 2f / Tile * 1.15f;
            for (int i = 0; i < Circle.positionCount; i++)
            {
                float angle = i * Mathf.PI * 2f / (Circle.positionCount - 1);
                Circle.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, .025f, Mathf.Sin(angle) * radius));
            }
        }

        public Color32[] RenderTile(string title, string detail)
        {
            Header.text = title;
            Footer.text = detail;
            Canvas.ForceUpdateCanvases();
            Camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = Target;
            Texture2D texture = new(Tile, Tile, TextureFormat.RGB24, false);
            try
            {
                texture.ReadPixels(new Rect(0, 0, Tile, Tile), 0, 0);
                texture.Apply();
                return texture.GetPixels32();
            }
            finally { RenderTexture.active = previous; Object.DestroyImmediate(texture); }
        }

        private Material Material(Color color, int queue)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("URP Unlit shader missing.");
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, renderQueue = queue };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Cull", 0f);
            Cleanup.Add(material);
            return material;
        }

        private LineRenderer NewLine(string name, Material material)
        {
            LineRenderer line = new GameObject(name, typeof(LineRenderer)).GetComponent<LineRenderer>();
            SceneManager.MoveGameObjectToScene(line.gameObject, Scene);
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.numCapVertices = 0;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private static Text Label(Transform parent, Font font, bool top)
        {
            GameObject go = new(top ? "Title" : "Measurement", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rect.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rect.pivot = new Vector2(.5f, top ? 1f : 0f);
            rect.anchoredPosition = new Vector2(0f, top ? -8f : 8f);
            rect.sizeDelta = new Vector2(-24f, top ? 64f : 72f);
            Text label = go.GetComponent<Text>();
            label.font = font;
            label.fontSize = top ? 19 : 15;
            label.color = top ? new Color(.94f, .97f, 1f) : new Color(.55f, .88f, .93f);
            label.alignment = top ? TextAnchor.UpperLeft : TextAnchor.LowerLeft;
            label.raycastTarget = false;
            label.supportRichText = true;
            var shadow = go.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, .95f);
            shadow.effectDistance = new Vector2(1f, -1f);
            return label;
        }

        public void Dispose()
        {
            Camera.targetTexture = null;
            Target.Release();
            Object.DestroyImmediate(Target);
            EditorSceneManager.ClosePreviewScene(Scene);
            for (int i = Cleanup.Count - 1; i >= 0; i--)
                if (Cleanup[i] != null) Object.DestroyImmediate(Cleanup[i]);
        }
    }

    [MenuItem("ScrapWaves/Weapons/Capture Damage Footprints")]
    public static void Capture()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run the damage footprint capture outside Play Mode.");
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            throw new InvalidOperationException("Damage footprint capture requires graphics; omit -nographics.");
        string output = Environment.GetEnvironmentVariable("SCRAPWAVES_PROJECTILE_PREVIEW_OUTPUT")
            ?? Path.GetFullPath(".utmp/projectile-visual-size-20261103");
        Directory.CreateDirectory(output);
        Scene previous = SceneManager.GetActiveScene();
        UnityEngine.Random.State random = UnityEngine.Random.state;
        var report = new Report { UnityVersion = Application.unityVersion };
        var cleanup = new List<Object>();
        try
        {
            using var stage = new Stage();
            PlayerStats stats = WeaponStatBaselineCapture.CreateNeutralStats(cleanup, stage.Scene);
            foreach (string name in new[] { "RocketLauncher", "Mortar" })
                CaptureWeapon(name, stats, stage, output, cleanup, report);
            File.WriteAllText(Path.Combine(output, "metadata.json"), JsonUtility.ToJson(report, true));
            File.WriteAllText(Path.Combine(output, "evidence.txt"), BuildEvidence(report));
            Debug.Log("WEAPON_DAMAGE_FOOTPRINT_PREVIEW: " + output + "; production effect bindings, runtime radii, normal/reduced flash, deterministic particle sampling, and mortar flight geometry rendered.");
        }
        finally
        {
            for (int i = cleanup.Count - 1; i >= 0; i--) if (cleanup[i] != null) Object.DestroyImmediate(cleanup[i]);
            UnityEngine.Random.state = random;
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    [MenuItem("ScrapWaves/Weapons/Capture Rocket Main Blast Fill")]
    public static void CaptureRocketBlastFill()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Run the rocket blast fill capture outside Play Mode.");
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            throw new InvalidOperationException("Rocket blast fill capture requires graphics; omit -nographics.");
        string output = Environment.GetEnvironmentVariable("SCRAPWAVES_ROCKET_BLAST_PREVIEW_OUTPUT")
            ?? Path.GetFullPath(".utmp/rocket-blast-fill-20261103");
        Directory.CreateDirectory(output);
        Scene previous = SceneManager.GetActiveScene();
        UnityEngine.Random.State random = UnityEngine.Random.state;
        var cleanup = new List<Object>();
        var fixtures = new List<Fixture>();
        var report = new RocketBlastFillReport { UnityVersion = Application.unityVersion,
            Times = new[] { .04f, .08f, .12f, .24f, .40f }, GameplayCamera = ReadGameplayCameraEvidence() };
        try
        {
            using var stage = new Stage();
            PlayerStats stats = WeaponStatBaselineCapture.CreateNeutralStats(cleanup, stage.Scene);
            if (!stats.TryGetRuntimeStat(StatType.ProjectileAreaSize, out RuntimeStat sizeStat))
                throw new InvalidOperationException("Neutral preview stats are missing ProjectileAreaSize.");
            const string path = "Assets/ScriptableObjects/WeaponSO/RocketLauncher.asset";
            WeaponData original = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
            if (original == null) throw new InvalidOperationException("Missing production weapon: " + path);
            string originalState = EditorJsonUtility.ToJson(original);
            bool originalDirty = EditorUtility.IsDirty(original);
            WeaponData data = Object.Instantiate(original);
            cleanup.Add(data);
            WeaponPresentationProfile profile = data.PresentationProfile;
            if (profile == null) throw new InvalidOperationException("Production rocket has no presentation profile.");
            var instance = new WeaponInstance { Data = data, Level = 1, SelectedPath = WeaponUpgradePath.None, State = WeaponState.Manual };
            var behaviour = (BasicProjectileWeapon)WeaponBehaviourFactory.Create(data, null, null, stats.transform, null);
            behaviour.Setup(instance, stats.transform, stats, null);
            var record = new WeaponRecord { WeaponAsset = path, Profile = AssetDatabase.GetAssetPath(profile), Times = report.Times };
            report.Production = record;
            foreach (float size in Sizes.Take(3))
            {
                sizeStat.SetBaseValue(size);
                float radius = behaviour.CaptureGeometry().Guides.Single(g => g.Mode == WeaponStatMode.Manual
                    && g.Purpose == WeaponGeometryPurpose.Explosion && g.Shape == WeaponGeometryShape.Sphere).RadiusMeters;
                if (radius <= 0f || float.IsNaN(radius) || float.IsInfinity(radius))
                    throw new InvalidOperationException("Invalid production rocket damage radius.");
                record.Sizes.Add(new SizeRecord { SizeStat = size, DamageRadiusMetres = radius });
                var feedback = new WeaponFeedbackContext(instance, WeaponFeedbackMode.Manual, 0f,
                    Vector3.zero, Vector3.forward, impactPosition: Vector3.zero, impactNormal: Vector3.up,
                    surfaceType: ImpactSurfaceType.GroundDirt, explosionRadius: radius);
                if (!profile.TryResolveCue(WeaponFeedbackEvent.ProjectileImpact, in feedback, out WeaponPresentationCueData cue)
                    || cue.Cue != WeaponPresentationCue.RocketImpact || cue.VfxPrefab == null)
                    throw new InvalidOperationException("Expected the production RocketImpact ground-impact binding.");
                record.Cue = cue.Cue.ToString(); record.ImpactPrefab = AssetDatabase.GetAssetPath(cue.VfxPrefab); record.Duration = cue.Duration;
                WeaponPresentationContext context = WeaponPresentationContext.FromFeedback(cue.Cue, in feedback,
                    profile, cue, GameFeelQualityLevel.High, false);
                fixtures.Add(CreateFixture(stage.Scene, cue, in context, size, false));
            }
            float maximumRadius = record.Sizes.Max(s => s.DamageRadiusMetres);
            record.ComparisonOrthoHalfHeight = maximumRadius * 1.5f;
            report.GameplayCamera.ImpactDistanceAheadOfPlayer = maximumRadius * 3f;
            for (int frame = 0; frame < report.Times.Length; frame++)
            {
                float time = report.Times[frame];
                foreach (Fixture fixture in fixtures) fixture.Advance(time);
                foreach (bool perspective in new[] { false, true })
                {
                    if (perspective) stage.GameplayFrame(record.ComparisonOrthoHalfHeight, report.GameplayCamera);
                    else stage.Frame(record.ComparisonOrthoHalfHeight, false);
                    foreach (bool diagnostic in new[] { false, true }) CapturePanel(perspective, diagnostic, frame, time);
                }
            }
            if (originalState != EditorJsonUtility.ToJson(original) || originalDirty != EditorUtility.IsDirty(original))
                throw new InvalidOperationException("Rocket blast capture changed the production weapon asset.");
            File.WriteAllText(Path.Combine(output, "metadata.json"), JsonUtility.ToJson(report, true));
            var evidence = new StringBuilder();
            evidence.AppendLine(report.Scope); evidence.AppendLine(report.RadiusSource); evidence.AppendLine(report.Rows);
            evidence.AppendLine(report.Diagnostic); evidence.AppendLine(report.Limitation);
            evidence.AppendLine("Top-down camera uses one fixed " + F(record.ComparisonOrthoHalfHeight * 2f) + " m view height for all sizes.");
            evidence.AppendLine("Perspective source: " + report.GameplayCamera.ScenePath + " / " + report.GameplayCamera.CameraObject
                + "; FOV=" + F(report.GameplayCamera.VerticalFov) + "; allowed pitch=" + F(report.GameplayCamera.MinimumPitchDegrees)
                + ".." + F(report.GameplayCamera.MaximumPitchDegrees) + "; saved startup pitch=" + F(report.GameplayCamera.SavedPitchDegrees)
                + "; chosen representative downward aim=" + F(report.GameplayCamera.RepresentativeAimPitchDegrees) + " degrees.");
            evidence.AppendLine(report.GameplayCamera.Interpretation);
            evidence.AppendLine("Unobstructed GetFollowPose offsets: pivot=" + F(report.GameplayCamera.PivotHeight)
                + ", shoulder=" + F(report.GameplayCamera.ShoulderOffset) + ", distance=" + F(report.GameplayCamera.OrbitDistance)
                + "; impact=" + F(report.GameplayCamera.ImpactDistanceAheadOfPlayer) + " m ahead of player; same camera for all sizes.");
            foreach (SizeRecord size in record.Sizes)
                evidence.AppendLine("ProjectileAreaSize=" + F(size.SizeStat) + " -> resolved damage radius=" + F(size.DamageRadiusMetres) + " m");
            File.WriteAllText(Path.Combine(output, "evidence.txt"), evidence.ToString());
            Debug.Log("ROCKET_BLAST_FILL_PREVIEW: " + output + "; production and annulus-hidden diagnostic panels rendered in top-down and representative gameplay perspective.");

            void CapturePanel(bool perspective, bool diagnostic, int frame, float time)
            {
                string view = perspective ? "gameplay-perspective" : "topdown";
                string treatment = diagnostic ? "main-volume-diagnostic" : "production";
                string relative = view + "/" + treatment + "/frame-" + frame.ToString("D2") + ".png";
                string filename = Path.Combine(output, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(filename));
                Texture2D montage = new(Tile * fixtures.Count, Tile * 2, TextureFormat.RGB24, false);
                try
                {
                    for (int column = 0; column < fixtures.Count; column++)
                    {
                        Fixture fixture = fixtures[column];
                        fixture.SetVisible(true);
                        Renderer annulus = fixture.Renderers.SingleOrDefault(r => r != null && r.name == "Resolved Damage Footprint");
                        if (annulus == null) throw new InvalidOperationException("Cannot isolate the production rocket's exact-radius annulus for diagnostic review.");
                        bool annulusEnabled = annulus.enabled;
                        try
                        {
                            if (diagnostic) annulus.enabled = false;
                            stage.Reference(fixture.Radius);
                            for (int row = 0; row < 2; row++)
                            {
                                stage.Circle.enabled = row == 1;
                                string title = "Rocket | size " + F(fixture.Size) + "x | t=" + F(time) + " s\n"
                                    + (row == 0 ? "Clean view" : "Cyan damage-radius reference");
                                string detail = (diagnostic ? "DIAGNOSTIC: exact-radius annulus hidden" : "PRODUCTION: main blast + annulus")
                                    + "\nResolved damage radius " + F(fixture.Radius) + " m | 1 m grid\n"
                                    + (perspective ? "Representative gameplay aim | FOV " + F(report.GameplayCamera.VerticalFov)
                                        + " | pitch " + F(report.GameplayCamera.RepresentativeAimPitchDegrees)
                                        : "Top-down | shared " + F(record.ComparisonOrthoHalfHeight * 2f) + " m view height");
                                montage.SetPixels32(column * Tile, (1 - row) * Tile, Tile, Tile, stage.RenderTile(title, detail));
                            }
                        }
                        finally { annulus.enabled = annulusEnabled; fixture.SetVisible(false); stage.Circle.enabled = true; }
                    }
                    montage.Apply();
                    File.WriteAllBytes(filename, montage.EncodeToPNG());
                    report.Frames.Add(new BlastFillFrame { File = relative, Seconds = time, View = view, DiagnosticAnnulusHidden = diagnostic });
                }
                finally { Object.DestroyImmediate(montage); }
            }
        }
        finally
        {
            foreach (Fixture fixture in fixtures) fixture.Dispose();
            for (int i = cleanup.Count - 1; i >= 0; i--) if (cleanup[i] != null) Object.DestroyImmediate(cleanup[i]);
            UnityEngine.Random.state = random;
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    private static GameplayCameraEvidence ReadGameplayCameraEvidence()
    {
        const string path = "Assets/Scenes/GameplayScene.unity";
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = SceneManager.GetSceneByPath(path);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        try
        {
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            Camera camera = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Camera>(true))
                .Single(c => c.CompareTag("MainCamera") && c.GetComponent<ThirdPersonCamera>() != null);
            var controller = new SerializedObject(camera.GetComponent<ThirdPersonCamera>());
            float minimum = controller.FindProperty("_minPitch").floatValue;
            float maximum = controller.FindProperty("_maxPitch").floatValue;
            return new GameplayCameraEvidence { ScenePath = path, CameraObject = camera.name,
                VerticalFov = camera.fieldOfView, NearClip = camera.nearClipPlane,
                SavedPitchDegrees = Mathf.DeltaAngle(0f, camera.transform.eulerAngles.x),
                MinimumPitchDegrees = minimum, MaximumPitchDegrees = maximum,
                RepresentativeAimPitchDegrees = Mathf.Clamp(20f, minimum, maximum),
                PivotHeight = controller.FindProperty("_pivotHeight").floatValue,
                ShoulderOffset = controller.FindProperty("_shoulderOffset").floatValue,
                CameraHeightOffset = controller.FindProperty("_cameraHeightOffset").floatValue,
                OrbitDistance = controller.FindProperty("_cameraDistance").floatValue };
        }
        finally
        {
            if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    private static void CaptureWeapon(string name, PlayerStats stats, Stage stage, string output, List<Object> cleanup, Report report)
    {
        string path = "Assets/ScriptableObjects/WeaponSO/" + name + ".asset";
        WeaponData original = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
        if (original == null) throw new InvalidOperationException("Missing production weapon: " + path);
        string originalState = EditorJsonUtility.ToJson(original);
        bool originalDirty = EditorUtility.IsDirty(original);
        WeaponData data = Object.Instantiate(original);
        cleanup.Add(data);
        WeaponPresentationProfile profile = data.PresentationProfile;
        if (profile == null) throw new InvalidOperationException(path + " has no presentation profile.");
        var instance = new WeaponInstance { Data = data, Level = 1, SelectedPath = WeaponUpgradePath.None, State = WeaponState.Manual };
        var behaviour = (BasicProjectileWeapon)WeaponBehaviourFactory.Create(data, null, null, stats.transform, null);
        behaviour.Setup(instance, stats.transform, stats, null);
        if (!stats.TryGetRuntimeStat(StatType.ProjectileAreaSize, out RuntimeStat sizeStat))
            throw new InvalidOperationException("Neutral preview stats are missing ProjectileAreaSize.");
        var record = new WeaponRecord { WeaponAsset = path, Profile = AssetDatabase.GetAssetPath(profile) };
        report.Weapons.Add(record);
        var fixtures = new List<Fixture>();
        try
        {
            foreach (float size in Sizes)
            {
                sizeStat.SetBaseValue(size);
                WeaponGeometryGuide geometry = behaviour.CaptureGeometry().Guides.Single(g => g.Mode == WeaponStatMode.Manual
                    && g.Purpose == WeaponGeometryPurpose.Explosion && g.Shape == WeaponGeometryShape.Sphere);
                float radius = geometry.RadiusMeters;
                if (radius <= 0f || float.IsNaN(radius) || float.IsInfinity(radius))
                    throw new InvalidOperationException("Invalid runtime damage radius for " + name);
                record.Sizes.Add(new SizeRecord { SizeStat = size, DamageRadiusMetres = radius,
                    FlightCollisionRadiusMetres = behaviour is MortarWeapon mortar ? mortar.ShellCollisionRadius : 0f });
                var feedback = new WeaponFeedbackContext(instance, WeaponFeedbackMode.Manual, 0f,
                    Vector3.zero, Vector3.forward, impactPosition: Vector3.zero, impactNormal: Vector3.up,
                    surfaceType: ImpactSurfaceType.GroundDirt, explosionRadius: radius);
                WeaponPresentationCueData cue;
                // MortarShellImpact emits its authored impact cue directly; rockets use semantic routing.
                bool resolved = name == "Mortar"
                    ? profile.TryGetCueData(WeaponPresentationCue.MortarImpact, out cue)
                    : profile.TryResolveCue(WeaponFeedbackEvent.ProjectileImpact, in feedback, out cue);
                if (!resolved || cue.VfxPrefab == null)
                    throw new InvalidOperationException("No production ground impact binding for " + name);
                WeaponPresentationCue expected = name == "Mortar" ? WeaponPresentationCue.MortarImpact : WeaponPresentationCue.RocketImpact;
                if (cue.Cue != expected) throw new InvalidOperationException("Unexpected base/manual impact cue: " + cue.Cue);
                record.Cue = cue.Cue.ToString(); record.ImpactPrefab = AssetDatabase.GetAssetPath(cue.VfxPrefab); record.Duration = cue.Duration;
                for (int reduced = 0; reduced < 2; reduced++)
                {
                    WeaponPresentationContext context = WeaponPresentationContext.FromFeedback(cue.Cue, in feedback,
                        profile, cue, GameFeelQualityLevel.High, reduced != 0);
                    fixtures.Add(CreateFixture(stage.Scene, cue, in context, size, reduced != 0));
                }
            }
            record.ComparisonOrthoHalfHeight = record.Sizes[2].DamageRadiusMetres * 1.5f;
            record.StressOrthoHalfHeight = record.Sizes[3].DamageRadiusMetres * 1.5f;
            record.Times = new[] { .01f, .04f, .08f, .12f, .18f, .24f, .30f, .40f, .55f, .70f,
                record.Duration * .95f, record.Duration + 1f / 60f }.Distinct().OrderBy(t => t).ToArray();
            string directory = Path.Combine(output, name);
            Directory.CreateDirectory(Path.Combine(directory, "comparison"));
            Directory.CreateDirectory(Path.Combine(directory, "stress"));
            for (int frame = 0; frame < record.Times.Length; frame++)
            {
                float time = record.Times[frame];
                foreach (Fixture fixture in fixtures) fixture.Advance(time);
                RenderSeries(fixtures.Take(6).ToArray(), "comparison", record.ComparisonOrthoHalfHeight, false);
                RenderSeries(fixtures.Skip(6).ToArray(), "stress", record.StressOrthoHalfHeight, false);
                if (Mathf.Approximately(time, .12f) || Mathf.Approximately(time, .30f))
                {
                    RenderSeries(fixtures.Take(6).ToArray(), "comparison", record.ComparisonOrthoHalfHeight, true);
                    RenderSeries(fixtures.Skip(6).ToArray(), "stress", record.StressOrthoHalfHeight, true);
                }
                void RenderSeries(Fixture[] selected, string series, float ortho, bool oblique)
                {
                    stage.Frame(ortho, oblique);
                    int columns = selected.Length / 2;
                    Texture2D montage = new(Tile * columns, Tile * 2, TextureFormat.RGB24, false);
                    try
                    {
                        for (int column = 0; column < columns; column++)
                            for (int row = 0; row < 2; row++)
                            {
                                Fixture fixture = selected[column * 2 + row];
                                fixture.SetVisible(true);
                                stage.Reference(fixture.Radius);
                                string title = name + " | size " + F(fixture.Size) + "x\n" + (fixture.Reduced ? "Reduced flash" : "Normal") + " | High quality";
                                string detail = "CYAN = damage radius " + F(fixture.Radius) + " m | t=" + F(time) + " s\n"
                                    + "1 m grid | view height " + F(ortho * 2f) + " m\nControlled editor sample";
                                montage.SetPixels32(column * Tile, (1 - row) * Tile, Tile, Tile, stage.RenderTile(title, detail));
                                fixture.SetVisible(false);
                            }
                        montage.Apply();
                        string filename = oblique ? "oblique-" + F(time) + "s.png" : "frame-" + frame.ToString("D2") + ".png";
                        File.WriteAllBytes(Path.Combine(directory, series, filename), montage.EncodeToPNG());
                    }
                    finally { Object.DestroyImmediate(montage); }
                }
            }
            if (name == "Mortar") CaptureShell(stage, profile, record, directory);
            if (originalState != EditorJsonUtility.ToJson(original) || originalDirty != EditorUtility.IsDirty(original))
                throw new InvalidOperationException("Capture changed the production weapon asset: " + path);
        }
        finally { foreach (Fixture fixture in fixtures) fixture.Dispose(); }
    }

    private static Fixture CreateFixture(Scene scene, WeaponPresentationCueData cue, in WeaponPresentationContext context, float size, bool reduced)
    {
        // Runtime pools instantiate ordinary objects. Connected editor prefab instances
        // forbid the mortar cue's runtime hierarchy setup and misrepresent its footprint.
        GameObject root = Object.Instantiate(cue.VfxPrefab);
        SceneManager.MoveGameObjectToScene(root, scene);
        foreach (MonoBehaviour script in root.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        var fixture = new Fixture { Root = root, Radius = context.ExplosionRadius, Size = size, Reduced = reduced, Duration = cue.Duration };
        try
        {
            fixture.Cue = (MonoBehaviour)root.GetComponent<RocketLauncherCueVfx>() ?? root.GetComponent<MortarCueVfx>();
            if (fixture.Cue == null) throw new InvalidOperationException("Unsupported production cue: " + root.name);
            fixture.Rocket = fixture.Cue is RocketLauncherCueVfx;
            var fields = new SerializedObject(fixture.Cue);
            fixture.Lifetime = Mathf.Max(.02f, fields.FindProperty("_lifetime").floatValue);
            fixture.RotationSpeed = fields.FindProperty("_rotationDegreesPerSecond").floatValue;
            SerializedProperty animated = fields.FindProperty("_animatedRoots");
            fixture.AnimatedRoots = Enumerable.Range(0, animated.arraySize)
                .Select(i => (Transform)animated.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
            fixture.RestRotations = fixture.AnimatedRoots.Select(t => t != null ? t.localRotation : Quaternion.identity).ToArray();
            fixture.FrameMethod = fixture.Cue.GetType().GetMethod("ApplyFrame", PrivateInstance)
                ?? throw new MissingMethodException(fixture.Cue.GetType().Name, "ApplyFrame");
            fixture.ElapsedField = fixture.Cue.GetType().GetField("_elapsed", PrivateInstance)
                ?? throw new MissingFieldException(fixture.Cue.GetType().Name, "_elapsed");
            fixture.Particles = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < fixture.Particles.Length; i++)
            {
                fixture.Particles[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                fixture.Particles[i].useAutoRandomSeed = false;
                fixture.Particles[i].randomSeed = Seed + (uint)i * 101;
            }
            fixture.Pool = root.GetComponent<PooledWeaponVfx>() ?? root.AddComponent<PooledWeaponVfx>();
            fixture.Pool.enabled = false;
            fixture.Pool.Initialize();
            fixture.Pool.Play(in context, cue.Duration, 0f, false);
            fixture.Renderers = root.GetComponentsInChildren<Renderer>(true);
            fixture.SetVisible(false);
            return fixture;
        }
        catch { fixture.Dispose(); throw; }
    }

    private static void CaptureShell(Stage stage, WeaponPresentationProfile profile, WeaponRecord record, string directory)
    {
        GameObject prefab = profile.Mortar?.ShellPrefab;
        if (prefab == null) throw new InvalidOperationException("Production mortar profile has no shell prefab.");
        record.ShellPrefab = AssetDatabase.GetAssetPath(prefab);
        for (int group = 0; group < 2; group++)
        {
            SizeRecord[] measurements = group == 0 ? record.Sizes.Take(3).ToArray() : record.Sizes.Skip(3).ToArray();
            float ortho = measurements.Max(m => m.FlightCollisionRadiusMetres) * 4f;
            stage.Frame(ortho, false);
            Texture2D montage = new(Tile * measurements.Length, Tile, TextureFormat.RGB24, false);
            try
            {
                for (int index = 0; index < measurements.Length; index++)
                {
                    SizeRecord measurement = measurements[index];
                    GameObject root = Object.Instantiate(prefab);
                    SceneManager.MoveGameObjectToScene(root, stage.Scene);
                    try
                    {
                        foreach (MonoBehaviour script in root.GetComponentsInChildren<MonoBehaviour>(true)) script.enabled = false;
                        foreach (Collider collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                        root.transform.position = Vector3.up * 3f;
                        root.SetActive(true);
                        MortarShellVfx shell = root.GetComponent<MortarShellVfx>();
                        if (shell == null) throw new InvalidOperationException("Production mortar shell has no MortarShellVfx.");
                        shell.Prewarm();
                        Physics.SyncTransforms();
                        shell.Configure(MortarShellVisualStyle.Base, Vector3.zero, Vector3.up, measurement.DamageRadiusMetres,
                            1f, 0f, detailed: true, showLanding: false,
                            collisionRadius: measurement.FlightCollisionRadiusMetres);
                        stage.Reference(measurement.FlightCollisionRadiusMetres);
                        string title = "Mortar flight shell geometry\nSize " + F(measurement.SizeStat) + "x | production prefab";
                        string detail = "CYAN = collision radius " + F(measurement.FlightCollisionRadiusMetres) + " m\n"
                            + "1 m grid | view height " + F(ortho * 2f) + " m\nConfigured flight geometry; no trajectory simulation";
                        montage.SetPixels32(index * Tile, 0, Tile, Tile, stage.RenderTile(title, detail));
                    }
                    finally { Object.DestroyImmediate(root); }
                }
                montage.Apply();
                File.WriteAllBytes(Path.Combine(directory, group == 0 ? "landing-comparison.png" : "landing-stress.png"), montage.EncodeToPNG());
            }
            finally { Object.DestroyImmediate(montage); }
        }
    }

    private static string BuildEvidence(Report report)
    {
        var text = new StringBuilder();
        text.AppendLine(report.Scope); text.AppendLine(report.RadiusSource); text.AppendLine(report.Reference); text.AppendLine(report.Limitation);
        text.AppendLine("Comparison columns: size 0.5 / 1 / 2. Stress series: size 8, deliberately different labelled camera scale. Rows: normal / reduced flash; High quality in both.");
        text.AppendLine("Particle simulation uses the real pooled Initialize/Play lifecycle, seeds starting at " + Seed + ", fixed increments <= 1/120 s, and the production cue's ApplyFrame/elapsed animation. The pool's authored duration ends each effect; smoke/debris are not isolated or substituted.");
        text.AppendLine("Top-down reference shows the ground-plane cross-section of a spherical damage volume through its center. Target collider overlap/contact is not simulated. Exact 0.12 s and 0.30 s oblique snapshots supplement the top-down sequences.");
        foreach (WeaponRecord weapon in report.Weapons)
        {
            text.AppendLine(weapon.WeaponAsset + " -> " + weapon.Profile + " -> " + weapon.Cue + " -> " + weapon.ImpactPrefab);
            text.AppendLine("Pool duration=" + F(weapon.Duration) + " s; frames=" + string.Join(", ", weapon.Times.Select(F)));
            foreach (SizeRecord size in weapon.Sizes) text.AppendLine("ProjectileAreaSize=" + F(size.SizeStat) + " => damage radius=" + F(size.DamageRadiusMetres) + " m");
            if (!string.IsNullOrEmpty(weapon.ShellPrefab)) text.AppendLine("Mortar shell setting -> " + weapon.ShellPrefab);
        }
        return text.ToString();
    }

    private static string F(float value) => value.ToString("0.###", Invariant);
}
#endif
