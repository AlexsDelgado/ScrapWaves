using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Exercises the production sword and authored VFX in a live, isolated scene.</summary>
public sealed class RotatingBladeVerticalPlaybackTests
{
    private const string SetupKey = "SwordVerticalPlayback.SceneSetup";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [Serializable] private sealed class SavedSetup { public SavedScene[] Scenes; }
    [Serializable] private sealed class SavedScene { public string Path; public bool Loaded; public bool Active; }

    [UnityTest]
    public IEnumerator LiveThrustsFollowPitchAndExpire()
    {
        SessionState.SetString(SetupKey, JsonUtility.ToJson(new SavedSetup
        {
            Scenes = EditorSceneManager.GetSceneManagerSetup().Select(s => new SavedScene
            { Path = s.path, Loaded = s.isLoaded, Active = s.isActive }).ToArray()
        }));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        yield return RunLiveScenario();
    }

    [UnityTearDown]
    public IEnumerator RestoreScene()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
        string saved = SessionState.GetString(SetupKey, "");
        SessionState.EraseString(SetupKey);
        if (!string.IsNullOrEmpty(saved))
        {
            var setup = JsonUtility.FromJson<SavedSetup>(saved);
            if (setup.Scenes.Length > 0 && setup.Scenes.All(s => !string.IsNullOrEmpty(s.Path)))
                EditorSceneManager.RestoreSceneManagerSetup(setup.Scenes.Select(s => new SceneSetup
                { path = s.Path, isLoaded = s.Loaded, isActive = s.Active }).ToArray());
        }
    }

    private IEnumerator RunLiveScenario()
    {
        Assert.That(Application.isPlaying, Is.True);
        var cleanup = new List<Object>();
        var registered = new List<Transform>();
        Time.timeScale = 1f;
        string output = Environment.GetEnvironmentVariable("SWORD_PREVIEW_OUTPUT");
        var frames = new List<Color[]>();
        var trace = new List<string> { "scenario,aim_x,aim_y,aim_z,range,damage,ammo,cooldown" };
        const int width = 800, height = 500;
        T Track<T>(T value) where T : Object { cleanup.Add(value); return value; }
        var material = Track(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
        material.color = new Color(0.25f, 0.32f, 0.4f);
        GameObject Primitive(string name, Vector3 position, Vector3 scale, PrimitiveType type)
        {
            var go = Track(GameObject.CreatePrimitive(type));
            go.name = name; go.transform.position = position; go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            Object.Destroy(go.GetComponent<Collider>());
            return go;
        }
        var owner = Primitive("Sword owner (test fixture)", new Vector3(0, 4, 0), new Vector3(.5f, .7f, .5f), PrimitiveType.Capsule);
        var cameraObject = Track(new GameObject("Playback observer"));
        var observer = cameraObject.AddComponent<Camera>();
        observer.transform.position = new Vector3(15, 7, 0);
        observer.transform.LookAt(new Vector3(0, 4, 3));
        observer.orthographic = true; observer.orthographicSize = 6.7f; observer.aspect = (float)width / height;
        observer.clearFlags = CameraClearFlags.SolidColor;
        observer.backgroundColor = new Color(.035f, .05f, .08f);
        observer.nearClipPlane = .1f; observer.farClipPlane = 100;
        var reticleCameraObject = Track(new GameObject("Gameplay aim camera"));
        var aimCamera = reticleCameraObject.AddComponent<Camera>();
        aimCamera.enabled = false; aimCamera.tag = "MainCamera";
        var provider = owner.AddComponent<ReticleAimProvider>();
        typeof(ReticleAimProvider).GetField("_aimCamera", Private).SetValue(provider, aimCamera);
        typeof(ReticleAimProvider).GetField("_ignoredRoot", Private).SetValue(provider, owner.transform);
        var asset = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/RotatingBlade.asset");
        Assert.That(asset, Is.Not.Null);
        var data = Track(Object.Instantiate(asset));
        var titleObject = Track(new GameObject("Scenario caption"));
        var title = titleObject.AddComponent<TextMesh>();
        title.fontSize = 48; title.characterSize = .09f; title.color = Color.white;
        title.anchor = TextAnchor.UpperLeft;
        titleObject.transform.rotation = observer.transform.rotation;
        titleObject.transform.position = observer.transform.position + observer.transform.forward * 15
            - observer.transform.right * 9.8f + observer.transform.up * 6.2f;
        var rt = Track(new RenderTexture(width, height, 24));
        var pixels = Track(new Texture2D(width, height, TextureFormat.RGB24, false));
        var target = Primitive("Damage target", Vector3.zero, Vector3.one * .38f, PrimitiveType.Sphere);
        var damageable = target.AddComponent<PlaybackDamageable>();
        EnemyRegistry.Register(target.transform); registered.Add(target.transform);
        var decoy = Primitive("Horizontal miss target", Vector3.zero, Vector3.one * .25f, PrimitiveType.Sphere);
        var decoyDamage = decoy.AddComponent<PlaybackDamageable>();
        EnemyRegistry.Register(decoy.transform); registered.Add(decoy.transform);
        try
        {
            // Wait for destruction of fixture colliders; aim has no accidental self-occlusion.
            yield return null;
            float[] pitches = { 35f, -35f, 0f, 55f };
            string[] names = { "BASE Q / UP", "BASE Q / DOWN", "BASE Q / LEVEL", "MULTI-BLADE Q / UP" };
            for (int scenario = 0; scenario < pitches.Length; scenario++)
            {
                bool multi = scenario == 3;
                var runtime = new WeaponInstance
                {
                    Data = data, State = WeaponState.Manual, Level = multi ? 7 : 1,
                    SelectedPath = multi ? WeaponUpgradePath.PathA : WeaponUpgradePath.None,
                    CurrentAmmo = 100f
                };
                var weapon = new RotatingBladeWeapon(null, null, null);
                weapon.Setup(runtime, owner.transform, null, null);
                Vector3 forward = Quaternion.AngleAxis(-pitches[scenario], Vector3.right) * Vector3.forward;
                aimCamera.transform.SetPositionAndRotation(owner.transform.position - forward * 3f, Quaternion.LookRotation(forward));
                Assert.That(provider.TryGetAimSolution(owner.transform.position, data.BaseRange, false, out var aim), Is.True);
                target.transform.position = owner.transform.position + aim.Direction * 2.7f;
                decoy.transform.position = owner.transform.position + (scenario == 2 ? Vector3.right : Vector3.forward) * 2.7f;
                Physics.SyncTransforms();
                damageable.TotalDamage = decoyDamage.TotalDamage = 0;
                title.text = names[scenario] + "\nLive Unity playback / production sword VFX";
                weapon.UseActiveAbility(aim.Direction);
                var vfx = (RotatingBladeVfx)typeof(RotatingBladeWeapon).GetField("_vfx", Private).GetValue(weapon);
                Assert.That(vfx, Is.Not.Null);
                cleanup.Add(vfx.gameObject);
                var line = vfx.GetComponentsInChildren<LineRenderer>().Single(l => l.name == "Blade Thrust");
                Vector3[] damageLine = (Vector3[])typeof(RotatingBladeWeapon).GetField("_activeLinePoints", Private).GetValue(weapon);
                Assert.That(Vector3.Distance(line.GetPosition(0), damageLine[0]), Is.LessThan(.001f));
                Assert.That(Vector3.Distance(line.GetPosition(1), damageLine[1]), Is.LessThan(.001f));
                Assert.That(Vector3.Angle(line.GetPosition(1) - line.GetPosition(0), aim.Direction), Is.LessThan(.02f));
                Assert.That(damageable.TotalDamage, Is.GreaterThan(0));
                Assert.That(decoyDamage.TotalDamage, Is.Zero);
                Assert.That(vfx.isActiveAndEnabled, Is.True);
                Vector3 midpoint = (damageLine[0] + damageLine[1]) * .5f;
                observer.transform.position = midpoint + new Vector3(15, 3, -3);
                observer.transform.LookAt(midpoint);
                observer.orthographicSize = Mathf.Max(6.7f, Vector3.Distance(damageLine[0], damageLine[1]) * .6f);
                titleObject.transform.rotation = observer.transform.rotation;
                titleObject.transform.position = observer.transform.position + observer.transform.forward * 15
                    - observer.transform.right * (observer.orthographicSize * observer.aspect * .92f)
                    + observer.transform.up * (observer.orthographicSize * .92f);
                foreach (Vector3 endpoint in damageLine)
                {
                    Vector3 viewport = observer.WorldToViewportPoint(endpoint);
                    Assert.That(viewport.x, Is.InRange(.05f, .95f));
                    Assert.That(viewport.y, Is.InRange(.05f, .85f));
                }
                // Capture the actual activation frame; subsequent frames exercise expiry.
                Assert.That(line.enabled && vfx.ActiveThrustSurfaceCount > 0, Is.True);
                if (!string.IsNullOrEmpty(output))
                {
                    var oldActive = RenderTexture.active;
                    var oldTarget = observer.targetTexture;
                    try
                    {
                        observer.targetTexture = rt; observer.Render(); RenderTexture.active = rt;
                        pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                        frames.Add(pixels.GetPixels());
                    }
                    finally { observer.targetTexture = oldTarget; RenderTexture.active = oldActive; }
                }
                if (multi)
                {
                    double timeout = Time.realtimeSinceStartupAsDouble + 3;
                    while (runtime.AbilityCooldownTimer <= 0 && Time.realtimeSinceStartupAsDouble < timeout)
                    {
                        weapon.TickManual(Time.deltaTime, Vector3.down, false);
                        yield return null;
                    }
                    Assert.That(damageable.TotalDamage, Is.EqualTo(3));
                    Assert.That(decoyDamage.TotalDamage, Is.Zero);
                }
                Assert.That(runtime.AbilityCooldownTimer, Is.GreaterThan(0));
                float ammo = runtime.CurrentAmmo;
                int hits = damageable.TotalDamage;
                weapon.UseActiveAbility(Vector3.back);
                Assert.That(runtime.CurrentAmmo, Is.EqualTo(ammo), "Cooldown must reject another Q.");
                Assert.That(damageable.TotalDamage, Is.EqualTo(hits));
                trace.Add(FormattableString.Invariant($"{names[scenario]},{aim.Direction.x},{aim.Direction.y},{aim.Direction.z},{Vector3.Distance(damageLine[0], damageLine[1])},{hits},{ammo},{runtime.AbilityCooldownTimer}"));
                // The EditMode runner continues through EnterPlayMode; explicitly wait for
                // live frames instead of yielding a coroutine-only WaitForSeconds object.
                float expireAt = Time.time + data.RotatingBlade.BladeVisualDuration + .15f;
                double expiryTimeout = Time.realtimeSinceStartupAsDouble + 5;
                while (Time.time < expireAt && Time.realtimeSinceStartupAsDouble < expiryTimeout)
                    yield return null;
                yield return null;
                Assert.That(line.enabled, Is.False);
                Assert.That(vfx.ActiveThrustSurfaceCount, Is.Zero);
                Object.Destroy(vfx.gameObject);
                yield return null;
            }

            // A close camera-ray obstacle must resolve a different aim from camera-forward,
            // and the sword must preserve that supplied solution.
            var wall = Track(new GameObject("Close aim obstacle"));
            wall.transform.position = new Vector3(.65f, 4.8f, 1.5f);
            wall.AddComponent<BoxCollider>().size = new Vector3(1f, 1f, .2f);
            aimCamera.transform.position = owner.transform.position + new Vector3(.65f, .8f, -3);
            aimCamera.transform.rotation = Quaternion.identity;
            Physics.SyncTransforms();
            Assert.That(provider.TryGetAimSolution(owner.transform.position, data.BaseRange, false, out var obstacleAim), Is.True);
            Assert.That(obstacleAim.Direction.y, Is.GreaterThan(.1f));
            var obstacleRuntime = new WeaponInstance { Data = data, State = WeaponState.Manual, CurrentAmmo = 100 };
            var obstacleWeapon = new RotatingBladeWeapon(null, null, null);
            obstacleWeapon.Setup(obstacleRuntime, owner.transform, null, null);
            obstacleWeapon.UseActiveAbility(obstacleAim.Direction);
            var obstacleVfx = (RotatingBladeVfx)typeof(RotatingBladeWeapon).GetField("_vfx", Private).GetValue(obstacleWeapon);
            cleanup.Add(obstacleVfx.gameObject);
            var obstacleLine = obstacleVfx.GetComponentsInChildren<LineRenderer>().Single(l => l.name == "Blade Thrust");
            Assert.That(Vector3.Angle(obstacleLine.GetPosition(1) - obstacleLine.GetPosition(0), obstacleAim.Direction), Is.LessThan(.02f));
            trace.Add("close_obstacle_aim_preserved");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                var sheet = Track(new Texture2D(width * 2, height * 2, TextureFormat.RGB24, false));
                for (int i = 0; i < frames.Count; i++)
                    sheet.SetPixels((i % 2) * width, (1 - i / 2) * height, width, height, frames[i]);
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(output, "sword-q-live-preview.png"), sheet.EncodeToPNG());
                File.WriteAllLines(Path.Combine(output, "sword-q-playback.csv"), trace);
            }
        }
        finally
        {
            foreach (var item in registered) if (item != null) EnemyRegistry.Unregister(item);
            for (int i = cleanup.Count - 1; i >= 0; i--)
                if (cleanup[i] != null) Object.Destroy(cleanup[i]);
        }
    }

    private sealed class PlaybackDamageable : MonoBehaviour, IDamageable
    {
        public int TotalDamage;
        public bool ApplyDamage(int amount) { TotalDamage += amount; return true; }
    }
}
