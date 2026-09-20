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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Records the production runtime graph, including weapon aim, for visual run-cycle review.</summary>
public sealed class PlayerRunPreviewTests
{
    private const string SetupKey = "PlayerRunPreviewTests.SceneSetup";
    private const string Output = ".utmp/player-animation/runtime-motion";
    private const float FrameTime = 1f / 30f;
    private readonly List<Object> _cleanup = new();

    [Serializable] private sealed class SavedSetup { public SavedScene[] Scenes; }
    [Serializable] private sealed class SavedScene { public string Path; public bool Loaded; public bool Active; }

    [UnityTest]
    public IEnumerator ProductionRun_RecordsUnarmedAndAimedPosesAtGameplaySpeed()
    {
        SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
        Assert.That(previous.All(scene => !string.IsNullOrEmpty(scene.path)) || Application.isBatchMode, Is.True,
            "Save an untitled Editor scene before running this runtime preview.");
        Assert.That(Enumerable.Range(0, SceneManager.sceneCount).All(index => !SceneManager.GetSceneAt(index).isDirty),
            Is.True, "Save scene changes before running the runtime preview; the test never discards unsaved work.");
        SessionState.SetString(SetupKey, JsonUtility.ToJson(new SavedSetup
        {
            Scenes = previous.Select(scene => new SavedScene
            { Path = scene.path, Loaded = scene.isLoaded, Active = scene.isActive }).ToArray()
        }));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        // Runtime references must be acquired after the EnterPlayMode domain reload.
        yield return RecordRuntimeRun();
    }

    private IEnumerator RecordRuntimeRun()
    {
        Assert.That(Application.isPlaying, Is.True);
        Time.timeScale = 1f;
        Time.captureDeltaTime = FrameTime;
        Directory.CreateDirectory(Output);
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab");
        Assert.That(prefab, Is.Not.Null);
        GameObject actor = Track(Object.Instantiate(prefab));
        actor.name = "Disposable production runtime run preview";
        PlayerAnimationDriver driver = actor.GetComponent<PlayerAnimationDriver>();
        Assert.That(driver, Is.Not.Null);
        // Retain the production rig, driver, serialized tuning and movement state source.
        // Input, inventory, UI and combat are not part of this isolated pose capture.
        foreach (MonoBehaviour behaviour in actor.GetComponentsInChildren<MonoBehaviour>(true))
            behaviour.enabled = behaviour == driver;
        foreach (Collider collider in actor.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        PlayerMovement movement = actor.GetComponent<PlayerMovement>();
        Assert.That(movement, Is.Not.Null);
        typeof(PlayerMovement).GetField("_isGrounded", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(movement, true);
        // Prevent a disabled reticle component from resolving a different test-camera target
        // if LateUpdate is the first pose consumer during the Unity Test Runner's player loop.
        typeof(PlayerAnimationDriver).GetField("_aimProvider", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(driver, null);
        typeof(PlayerAnimationDriver).GetField("_weapons", BindingFlags.Instance | BindingFlags.NonPublic)
            .SetValue(driver, null);
        Rigidbody body = actor.GetComponent<Rigidbody>();
        Assert.That(body, Is.Not.Null);
        body.useGravity = false;
        body.constraints = RigidbodyConstraints.FreezeRotation | RigidbodyConstraints.FreezePositionY;
        body.interpolation = RigidbodyInterpolation.None;
        body.linearDamping = 0f;
        Transform visual = driver.RigAnimator.transform;
        Transform hand = FindBone(visual, "hand.R");
        Transform forearm = FindBone(visual, "forearm.R");
        Transform leftArm = FindBone(visual, "upper_arm.L");
        Transform hips = FindBone(visual, "spine");
        SkinnedMeshRenderer[] skins = visual.GetComponentsInChildren<SkinnedMeshRenderer>();
        Assert.That(skins, Is.Not.Empty);
        Mesh[] baked = skins.Select(_ => Track(new Mesh { name = "Disposable evaluated runtime skin" })).ToArray();
        for (int index = 0; index < skins.Length; index++)
        {
            var sampled = new GameObject("Runtime evaluated skin snapshot");
            sampled.transform.SetParent(skins[index].transform, false);
            sampled.AddComponent<MeshFilter>().sharedMesh = baked[index];
            sampled.AddComponent<MeshRenderer>().sharedMaterials = skins[index].sharedMaterials;
            skins[index].enabled = false;
        }

        Camera camera = Track(new GameObject("Runtime run preview camera")).AddComponent<Camera>();
        camera.enabled = false;
        camera.scene = actor.scene;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(.10f, .12f, .15f);
        camera.orthographic = true;
        camera.orthographicSize = 1.35f;
        camera.nearClipPlane = .01f;
        Light light = Track(new GameObject("Runtime run preview key light")).AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 2f;
        light.shadows = LightShadows.Soft;
        light.transform.rotation = Quaternion.Euler(40f, 135f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(.65f, .65f, .65f);
        RenderSettings.fog = false;
        GameObject ground = Track(GameObject.CreatePrimitive(PrimitiveType.Plane));
        ground.name = "Runtime run preview contact plane";
        ground.transform.localScale = Vector3.one * .45f;
        ground.GetComponent<Collider>().enabled = false;
        Material groundMaterial = Track(new Material(Shader.Find("Universal Render Pipeline/Lit")));
        groundMaterial.SetColor("_BaseColor", new Color(.15f, .17f, .20f));
        groundMaterial.SetFloat("_Smoothness", 0f);
        ground.GetComponent<Renderer>().sharedMaterial = groundMaterial;
        WeaponData weapon = Track(ScriptableObject.CreateInstance<WeaponData>());
        weapon.WeaponType = WeaponType.AutomaticCannon;
        WeaponInstance manual = new() { Data = weapon, State = WeaponState.Manual, CurrentAmmo = 100f };
        var metrics = new StringBuilder("mode,frame,hip_height,left_arm_x,left_arm_y,left_arm_z,right_aim_error_degrees\n");

        foreach (bool armed in new[] { false, true })
        {
            string mode = armed ? "Armed" : "Unarmed";
            driver.SetManualWeaponOverride(armed ? manual : null);
            driver.ResetPresentation();
            // Allow the graph binding, velocity blend and aim smoothing to settle before recording.
            for (int warmup = 0; warmup < 30; warmup++)
            {
                body.linearVelocity = actor.transform.forward * 5f;
                yield return null;
                driver.EvaluatePoseForWeapons(FrameTime, hand.position + actor.transform.forward * 30f);
            }
            int initialEvaluations = driver.GraphEvaluationCount;
            float maximumAimError = 0f;
            Quaternion firstLeftArm = leftArm.localRotation;
            float supportArmExcursion = 0f;
            for (int frame = 0; frame < 120; frame++)
            {
                body.linearVelocity = actor.transform.forward * 5f;
                yield return null;
                driver.EvaluatePoseForWeapons(FrameTime, hand.position + actor.transform.forward * 30f);
                Assert.That(driver.LastEvaluatedFrame, Is.EqualTo(Time.frameCount), mode + ": stale runtime pose.");
                Assert.That(movement.CurrentVelocity.z, Is.EqualTo(5f).Within(.01f));
                float aimError = Vector3.Angle(hand.position - forearm.position, actor.transform.forward);
                maximumAimError = Mathf.Max(maximumAimError, aimError);
                supportArmExcursion = Mathf.Max(supportArmExcursion,
                    Quaternion.Angle(firstLeftArm, leftArm.localRotation));
                for (int index = 0; index < skins.Length; index++) skins[index].BakeMesh(baked[index]);
                Vector3 focus = actor.transform.position + Vector3.up * .15f;
                camera.transform.position = focus + new Vector3(-6f, .8f, 2f);
                camera.transform.LookAt(focus);
                ground.transform.position = actor.transform.position + Vector3.down * .748f;
                Capture(camera, Path.Combine(Output, $"{mode}-{frame:D3}.png"));
                Vector3 localArm = leftArm.localEulerAngles;
                metrics.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2:F5},{3:F3},{4:F3},{5:F3},{6:F3}",
                    mode, frame, hips.position.y - actor.transform.position.y, localArm.x, localArm.y, localArm.z, aimError));
            }
            File.WriteAllText(Path.Combine(Output, "runtime-run-metrics.csv"), metrics.ToString());
            Assert.That(driver.GraphEvaluationCount - initialEvaluations, Is.EqualTo(120),
                "The production pose must advance once per captured frame.");
            Assert.That(supportArmExcursion, Is.GreaterThan(10f),
                mode + ": the actual runtime support arm must retain visible stride motion.");
            if (armed) Assert.That(maximumAimError, Is.LessThan(45f),
                "The run must retain forward weapon aim throughout the stride.");
        }
        File.WriteAllText(Path.Combine(Output, "capture-info.txt"),
            "Production player prefab and PlayerAnimationDriver, including the actual controller graph and procedural aim.\n" +
            "Unarmed and manual AutomaticCannon modes: 120 frames each at 30 fps, 5 m/s forward.\n" +
            "Movement input is disabled; grounded state and Rigidbody velocity are supplied by the test.\n" +
            "Camera and contact plane follow the translating actor. CPU mesh snapshots are baked from each evaluated runtime pose.\n" +
            "No imported clip sampling or substitute animation graph is used for these captures.\n");
    }

    private static Transform FindBone(Transform root, string name) =>
        root.GetComponentsInChildren<Transform>(true).Single(bone => bone.name == name);

    private T Track<T>(T item) where T : Object { _cleanup.Add(item); return item; }

    private static void Capture(Camera camera, string path)
    {
        RenderTexture target = RenderTexture.GetTemporary(480, 480, 24);
        RenderTexture previous = RenderTexture.active;
        Texture2D image = new(480, 480, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 480, 480), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(target);
            Object.DestroyImmediate(image);
        }
    }

    [UnityTearDown]
    public IEnumerator RestoreScene()
    {
        for (int index = _cleanup.Count - 1; index >= 0; index--)
            if (_cleanup[index] != null) Object.DestroyImmediate(_cleanup[index]);
        _cleanup.Clear();
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
