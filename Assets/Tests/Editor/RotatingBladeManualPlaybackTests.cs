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

/// <summary>Captures unmodified-duration manual swings using production weapon code in Play Mode.</summary>
public sealed class RotatingBladeManualPlaybackTests
{
    private const string SetupKey = "SwordManualPlayback.SceneSetup";
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    [Serializable] private sealed class SavedSetup { public SavedScene[] Scenes; }
    [Serializable] private sealed class SavedScene { public string Path; public bool Loaded; public bool Active; }

    [UnityTest]
    public IEnumerator ManualSwingDamagesFullConeAndExpiresDuringLivePlayback()
    {
        SessionState.SetString(SetupKey, JsonUtility.ToJson(new SavedSetup
        {
            Scenes = EditorSceneManager.GetSceneManagerSetup().Select(s => new SavedScene
            { Path = s.path, Loaded = s.isLoaded, Active = s.isActive }).ToArray()
        }));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        yield return CaptureLiveSwing();
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

    private IEnumerator CaptureLiveSwing()
    {
        Assert.That(Application.isPlaying, Is.True);
        string output = Environment.GetEnvironmentVariable("SWORD_MANUAL_PREVIEW_OUTPUT");
        string stage = Environment.GetEnvironmentVariable("SWORD_MANUAL_PREVIEW_STAGE") == "before" ? "before" : "after";
        var cleanup = new List<Object>();
        var targets = new List<Transform>();
        float priorCaptureDelta = Time.captureDeltaTime;
        float priorTimeScale = Time.timeScale;
        const int width = 720, height = 500;
        T Track<T>(T value) where T : Object { cleanup.Add(value); return value; }
        Material Material(Color color)
        {
            var result = Track(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            result.color = color; return result;
        }
        var playerMaterial = Material(new Color(1f, .42f, .08f));
        var targetMaterial = Material(new Color(.1f, .48f, .56f));
        GameObject Sphere(string name, Vector3 position, float size, Material material)
        {
            var result = Track(GameObject.CreatePrimitive(PrimitiveType.Sphere));
            result.name = name; result.transform.position = position; result.transform.localScale = Vector3.one * size;
            result.GetComponent<Renderer>().sharedMaterial = material;
            Object.Destroy(result.GetComponent<Collider>());
            return result;
        }
        var owner = Sphere("Orange player marker", Vector3.zero, .24f, playerMaterial);
        var data = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/RotatingBlade.asset")));
        float range = data.RotatingBlade.BladeManualRange;
        float halfAngle = data.RotatingBlade.BladeManualConeAngle * .5f;
        PlaybackDamageable Target(string name, float radius, float angle)
        {
            var go = Sphere(name, Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * (range * radius), .13f, targetMaterial);
            var damage = go.AddComponent<PlaybackDamageable>();
            targets.Add(go.transform); EnemyRegistry.Register(go.transform); return damage;
        }
        var near = Target("Inner cone hit", .3f, -10f);
        var middle = Target("Mid cone hit", .6f, 5f);
        var far = Target("Outer cone hit", .95f, 15f);
        var outside = Target("Outside cone miss", .6f, halfAngle + 10f);
        var camera = Track(new GameObject("Manual slash playback observer")).AddComponent<Camera>();
        camera.orthographic = true; camera.orthographicSize = range * .78f; camera.aspect = (float)width / height;
        camera.transform.SetPositionAndRotation(new Vector3(0, 10, range * .52f), Quaternion.LookRotation(Vector3.down, Vector3.forward));
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.04f, .055f, .08f);
        camera.nearClipPlane = .1f; camera.farClipPlane = 30f;
        var title = Track(new GameObject("Manual comparison caption")).AddComponent<TextMesh>();
        title.fontSize = 42; title.characterSize = .055f; title.color = Color.white; title.anchor = TextAnchor.UpperLeft;
        title.transform.rotation = camera.transform.rotation;
        title.transform.position = camera.transform.position + camera.transform.forward * 8
            - camera.transform.right * (camera.orthographicSize * camera.aspect * .92f)
            + camera.transform.up * (camera.orthographicSize * .91f);
        var rt = Track(new RenderTexture(width, height, 24));
        var pixels = Track(new Texture2D(width, height, TextureFormat.RGB24, false));
        var strip = Track(new Texture2D(width * 3, height, TextureFormat.RGB24, false));
        var runtime = new WeaponInstance { Data = data, Level = 1, State = WeaponState.Manual, CurrentAmmo = 100 };
        var weapon = new RotatingBladeWeapon(null, null, null);
        weapon.Setup(runtime, owner.transform, null, null);
        var trace = new List<string> { "stage,normalized_time,elapsed,vertices,near_hits,mid_hits,far_hits,outside_hits,ammo" };
        try
        {
            Time.timeScale = 1;
            Time.captureDeltaTime = data.RotatingBlade.BladeVisualDuration / 40f;
            yield return null;
            weapon.TickManual(0, Vector3.forward, true);
            var vfx = (RotatingBladeVfx)typeof(RotatingBladeWeapon).GetField("_vfx", Private).GetValue(weapon);
            Assert.That(vfx, Is.Not.Null); cleanup.Add(vfx.gameObject);
            Assert.That(vfx.isActiveAndEnabled, Is.True);
            Assert.That(near.Hits, Is.EqualTo(1)); Assert.That(middle.Hits, Is.EqualTo(1));
            Assert.That(far.Hits, Is.EqualTo(1)); Assert.That(outside.Hits, Is.Zero);
            Assert.That(runtime.CurrentAmmo, Is.EqualTo(99));
            float start = Time.time;
            float[] phases = { .2f, .5f, .7f };
            for (int i = 0; i < phases.Length; i++)
            {
                float captureAt = start + phases[i] * data.RotatingBlade.BladeVisualDuration;
                double timeout = Time.realtimeSinceStartupAsDouble + 10;
                while (Time.time < captureAt && Time.realtimeSinceStartupAsDouble < timeout) yield return null;
                Assert.That(Time.time, Is.GreaterThanOrEqualTo(captureAt));
                var surface = vfx.transform.Find("Blade Slash Surface 1").GetComponent<MeshFilter>();
                Assert.That(surface.GetComponent<MeshRenderer>().enabled, Is.True);
                title.text = stage.ToUpperInvariant() + " / " + Mathf.RoundToInt(phases[i] * 100) + "% OF SWING"
                    + "\nProduction range " + range.ToString("0.0") + " m / " + data.RotatingBlade.BladeManualConeAngle + " degrees"
                    + "\nOrange: player  |  Blue: damage test targets";
                if (!string.IsNullOrEmpty(output))
                {
                    var previousActive = RenderTexture.active;
                    var previousTarget = camera.targetTexture;
                    try
                    {
                        camera.targetTexture = rt; camera.Render(); RenderTexture.active = rt;
                        pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                        strip.SetPixels(i * width, 0, width, height, pixels.GetPixels());
                    }
                    finally { RenderTexture.active = previousActive; camera.targetTexture = previousTarget; }
                }
                trace.Add(FormattableString.Invariant($"{stage},{phases[i]},{Time.time-start},{surface.sharedMesh.vertexCount},{near.Hits},{middle.Hits},{far.Hits},{outside.Hits},{runtime.CurrentAmmo}"));
            }
            float expireAt = start + data.RotatingBlade.BladeVisualDuration + .08f;
            while (Time.time < expireAt) yield return null;
            yield return null;
            Assert.That(vfx.ActiveSlashSurfaceCount, Is.Zero);
            Assert.That(vfx.transform.Find("Blade Slash 1").GetComponent<LineRenderer>().enabled, Is.False);
            Assert.That(near.Hits, Is.EqualTo(1)); Assert.That(middle.Hits, Is.EqualTo(1)); Assert.That(far.Hits, Is.EqualTo(1));
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output); strip.Apply();
                File.WriteAllBytes(Path.Combine(output, "manual-" + stage + ".png"), strip.EncodeToPNG());
                File.WriteAllLines(Path.Combine(output, "manual-" + stage + "-playback.csv"), trace);
                string beforePath = Path.Combine(output, "manual-before.png");
                if (stage == "after" && File.Exists(beforePath))
                {
                    var before = Track(new Texture2D(2, 2));
                    Assert.That(before.LoadImage(File.ReadAllBytes(beforePath)), Is.True);
                    Assert.That(before.width, Is.EqualTo(strip.width)); Assert.That(before.height, Is.EqualTo(strip.height));
                    var comparison = Track(new Texture2D(strip.width, strip.height * 2, TextureFormat.RGB24, false));
                    comparison.SetPixels(0, height, strip.width, height, before.GetPixels());
                    comparison.SetPixels(0, 0, strip.width, height, strip.GetPixels()); comparison.Apply();
                    File.WriteAllBytes(Path.Combine(output, "sword-manual-before-after.png"), comparison.EncodeToPNG());
                }
            }
        }
        finally
        {
            Time.captureDeltaTime = priorCaptureDelta; Time.timeScale = priorTimeScale;
            foreach (var target in targets) if (target != null) EnemyRegistry.Unregister(target);
            for (int i = cleanup.Count - 1; i >= 0; i--) if (cleanup[i] != null) Object.Destroy(cleanup[i]);
        }
    }
    private sealed class PlaybackDamageable : MonoBehaviour, IDamageable
    {
        public int Hits;
        public bool ApplyDamage(int amount) { Hits++; return true; }
    }
}
