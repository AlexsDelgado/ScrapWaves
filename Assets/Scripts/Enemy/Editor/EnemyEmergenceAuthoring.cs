#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>Targeted, repeatable migration. Never rebuilds a scene or replaces enemy art.</summary>
public static class EnemyEmergenceAuthoring
{
    public const string PivotName = "EmergenceVisual";
    public const string GameplayScenePath = "Assets/Scenes/GameplayScene.unity";

    public static readonly string[] EnemyPrefabPaths =
    {
        "Assets/Prefabs/Enemy.prefab", "Assets/Prefabs/EnemyPro.prefab",
        "Assets/Prefabs/Slime (variant).prefab", "Assets/Prefabs/Slime_Elite.prefab",
        "Assets/Prefabs/Chaser.prefab", "Assets/Prefabs/Chaser (variant).prefab",
        "Assets/Prefabs/Chaser_Elite.prefab", "Assets/Prefabs/Drone.prefab",
        "Assets/Prefabs/Drone (variant).prefab", "Assets/Prefabs/Drone_Elite.prefab"
    };

    public static readonly string[] BossPrefabPaths =
    {
        "Assets/Prefabs/Boss.prefab", "Assets/Prefabs/Boss_2.prefab",
        "Assets/Prefabs/Stalker.prefab", "Assets/Prefabs/Destroyer_Boss.prefab"
    };

    private static readonly string[] TutorialActorNames =
    {
        "1 - Charger", "2 - Junk Slime", "2 - Vigilance Drone", "2 - Charger",
        "3 - Moving slime 1", "3 - Moving slime 2", "3 - Moving slime 3", "3 - Moving slime 4"
    };
    private static readonly string[] VisualNames = { "Basurita", "Larguirucho", "Avispa2", "Bola de Tierra" };

    [MenuItem("Tools/ScrapWaves/Enemies/Apply Emergence Authoring")]
    private static void ApplyMenu() => Debug.Log(Apply());

    [MenuItem("Tools/ScrapWaves/Enemies/Validate Emergence Authoring")]
    private static void ValidateMenu() => Debug.Log(Validate());

    public static void ApplyBatch()
    {
        string report = Apply() + Environment.NewLine + Validate();
        string directory = System.IO.Path.GetFullPath(".utmp/emergence-validation");
        System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllText(System.IO.Path.Combine(directory, "authoring-report.txt"), report);
        Debug.Log(report);
        if (report.Contains("FAIL")) throw new InvalidOperationException("Emergence authoring validation failed.");
    }

    public static string Apply()
    {
        RequireSafeEditor();
        var report = new StringBuilder();
        int changedPrefabs = 0;
        int changedActors = 0;
        using (var sceneScope = new GameplaySceneScope())
        {
            TutorialEnemy[] actors = GetTutorialActors(sceneScope.Scene);
            // Finish all structural checks before the first asset save.
            foreach (string path in EnemyPrefabPaths)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try { CheckCanConfigure(root); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            foreach (TutorialEnemy actor in actors) CheckCanConfigure(actor.gameObject);
            foreach (string path in BossPrefabPaths) CheckBoss(path);

            foreach (string path in EnemyPrefabPaths)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (!ConfigureActor(root)) continue;
                    PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
                    if (!saved) throw new InvalidOperationException("Could not save " + path);
                    changedPrefabs++;
                    report.AppendLine("Updated " + path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }

            foreach (TutorialEnemy actor in actors)
                if (ConfigureActor(actor.gameObject)) changedActors++;
            if (changedActors > 0)
            {
                EditorSceneManager.MarkSceneDirty(sceneScope.Scene);
                if (!EditorSceneManager.SaveScene(sceneScope.Scene, GameplayScenePath))
                    throw new InvalidOperationException("Could not save the targeted tutorial actor migration.");
            }
            report.AppendLine($"Changed prefabs={changedPrefabs}/10, tutorial actors={changedActors}/8; bosses excluded=4.");
            report.AppendLine("New defaults: ground 0.6–1.0s; flying 0.8–1.2s; elites 0.9–1.3s. Existing tuning is preserved.");
        }
        report.AppendLine("Scene setup preserved. Original transforms, existing components and actor references verified before saving.");
        return report.ToString();
    }

    /// <summary>Read-only saved-asset validation; returns all failures for CLI callers.</summary>
    public static string Validate()
    {
        RequireSafeEditor();
        var report = new StringBuilder();
        int failures = 0;
        foreach (string path in EnemyPrefabPaths)
            ValidateOne(path, AssetDatabase.LoadAssetAtPath<GameObject>(path), report, ref failures);
        foreach (string path in BossPrefabPaths)
        {
            try { CheckBoss(path); report.AppendLine("PASS excluded " + path); }
            catch (Exception error) { failures++; report.AppendLine("FAIL " + path + ": " + error.Message); }
        }
        using (var sceneScope = new GameplaySceneScope())
        {
            try
            {
                foreach (TutorialEnemy actor in GetTutorialActors(sceneScope.Scene))
                    ValidateOne("Tutorial/" + actor.name, actor.gameObject, report, ref failures);
            }
            catch (Exception error) { failures++; report.AppendLine("FAIL tutorial coverage: " + error.Message); }
        }
        report.AppendLine($"{(failures == 0 ? "PASS" : "FAIL")} emergence authoring: 10 prefabs, 8 tutorial actors, 4 excluded bosses; failures={failures}; scene setup preserved.");
        return report.ToString();
    }

    /// <summary>Used by targeted migration and future enemy/tutorial authoring; does not save.</summary>
    public static bool ConfigureActor(GameObject root)
    {
        Transform visual = CheckCanConfigure(root);
        var snapshot = new PreservationSnapshot(root, visual);
        Transform pivot = root.transform.Find(PivotName);
        bool changed = false;
        if (pivot == null)
        {
            var pivotObject = new GameObject(PivotName);
            pivotObject.layer = root.layer;
            pivot = pivotObject.transform;
            pivot.SetParent(root.transform, false);
            pivot.SetSiblingIndex(visual.GetSiblingIndex());
            // The new parent is identity: preserve the complete authored local pose.
            visual.SetParent(pivot, false);
            RecordOverride(visual);
            changed = true;
        }

        EnemyEmergence emergence = root.GetComponent<EnemyEmergence>();
        if (emergence == null)
        {
            emergence = root.AddComponent<EnemyEmergence>();
            bool elite = root.name.EndsWith("_Elite", StringComparison.Ordinal);
            bool flying = root.GetComponent<FlyingRangedBehavior>() != null || root.GetComponent<BomberDroneBehavior>() != null;
            emergence.Configure(pivot, elite ? 0.9f : flying ? 0.8f : 0.6f, elite ? 1.3f : flying ? 1.2f : 1f, 0.15f);
            var serialized = new SerializedObject(emergence);
            serialized.FindProperty("_riseCurve").animationCurveValue = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            changed = true;
        }
        else
        {
            var serialized = new SerializedObject(emergence);
            SerializedProperty reference = serialized.FindProperty("_visualPivot");
            if (reference.objectReferenceValue != pivot)
            {
                reference.objectReferenceValue = pivot;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                changed = true;
            }
        }
        if (changed)
        {
            EditorUtility.SetDirty(emergence);
            RecordOverride(emergence);
        }
        snapshot.Verify(pivot);
        CheckConfigured(root);
        return changed;
    }

    private static Transform CheckCanConfigure(GameObject root)
    {
        if (root == null) throw new InvalidOperationException("Missing enemy asset.");
        if (root.GetComponent<BossKeyDrop>() != null || root.GetComponent<GigaWormBehavior>() != null || root.GetComponent<DestroyerBehavior>() != null)
            throw new InvalidOperationException("Boss authoring is excluded: " + root.name);
        Transform[] pivots = root.GetComponentsInChildren<Transform>(true).Where(t => t.name == PivotName).ToArray();
        if (pivots.Length > 1) throw new InvalidOperationException(root.name + " has duplicate emergence pivots.");
        Transform pivot = pivots.SingleOrDefault();
        if (pivot != null && (pivot.parent != root.transform || pivot.childCount != 1 || !IsIdentity(pivot)))
            throw new InvalidOperationException(root.name + " has an unexpected emergence hierarchy or non-neutral pivot.");

        // Imported rigs can contain meshes named like another family's visual root
        // (Basurita contains Bola de Tierra). Only the authored top-level model is movable.
        Transform visualParent = pivot != null ? pivot : root.transform;
        Transform[] matches = visualParent.Cast<Transform>().Where(t => VisualNames.Contains(t.name)).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException(root.name + " requires exactly one known visual root directly under " +
                visualParent.name + "; found " + matches.Length + ". Refusing an unexpectedly nested hierarchy.");
        Transform visual = matches[0];
        if (pivot != null)
        {
            CheckVisualOnly(pivot);
        }
        CheckVisualOnly(visual);
        if (visual.GetComponentsInChildren<Renderer>(true).Length == 0)
            throw new InvalidOperationException(root.name + " visual has no renderers.");
        if (root.GetComponents<EnemyEmergence>().Length > 1)
            throw new InvalidOperationException(root.name + " has duplicate emergence components.");
        EnemyEmergence existing = root.GetComponent<EnemyEmergence>();
        if (existing != null) CheckDuration(existing);
        return visual;
    }

    private static void CheckVisualOnly(Transform visual)
    {
        if (visual.GetComponentsInChildren<Collider>(true).Length != 0 ||
            visual.GetComponentsInChildren<Rigidbody>(true).Length != 0 ||
            visual.GetComponentsInChildren<NavMeshAgent>(true).Length != 0 ||
            visual.GetComponentsInChildren<NavMeshObstacle>(true).Length != 0)
            throw new InvalidOperationException(visual.name + " contains collision, physics or navigation components.");
    }

    private static void CheckConfigured(GameObject root)
    {
        CheckCanConfigure(root);
        EnemyEmergence emergence = root.GetComponent<EnemyEmergence>();
        if (emergence == null || !emergence.enabled) throw new InvalidOperationException("Missing or disabled EnemyEmergence.");
        var serialized = new SerializedObject(emergence);
        if (serialized.FindProperty("_visualPivot").objectReferenceValue != root.transform.Find(PivotName))
            throw new InvalidOperationException("Emergence does not reference its dedicated visual pivot.");
        if (root.transform.Find(PivotName) == null) throw new InvalidOperationException("Missing visual pivot.");
        AnimationCurve curve = serialized.FindProperty("_riseCurve").animationCurveValue;
        if (curve == null || curve.length < 2 || Mathf.Abs(curve.Evaluate(0f)) > 0.0001f || Mathf.Abs(curve.Evaluate(1f) - 1f) > 0.0001f)
            throw new InvalidOperationException("Rise curve must travel from 0 to 1.");
        EnemyHitFeedback feedback = root.GetComponent<EnemyHitFeedback>();
        if (feedback != null && new SerializedObject(feedback).FindProperty("_visualRoot").objectReferenceValue == root.transform.Find(PivotName))
            throw new InvalidOperationException("Hit feedback must not own the emergence pivot.");
    }

    private static void CheckDuration(EnemyEmergence emergence)
    {
        var serialized = new SerializedObject(emergence);
        float min = serialized.FindProperty("_minimumDuration").floatValue;
        float max = serialized.FindProperty("_maximumDuration").floatValue;
        float clearance = serialized.FindProperty("_burialClearance").floatValue;
        if (!Finite(min) || !Finite(max) || !Finite(clearance) || min < 0f || max < min || clearance < 0f)
            throw new InvalidOperationException("Invalid duration range or burial clearance on " + emergence.name);
    }

    private static void ValidateOne(string label, GameObject root, StringBuilder report, ref int failures)
    {
        try
        {
            CheckConfigured(root);
            var serialized = new SerializedObject(root.GetComponent<EnemyEmergence>());
            report.AppendLine($"PASS {label}: {serialized.FindProperty("_minimumDuration").floatValue:0.###}–{serialized.FindProperty("_maximumDuration").floatValue:0.###}s; identity pivot; no physics below pivot.");
        }
        catch (Exception error) { failures++; report.AppendLine("FAIL " + label + ": " + error.Message); }
    }

    private static void CheckBoss(string path)
    {
        GameObject boss = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (boss == null || boss.GetComponentsInChildren<EnemyEmergence>(true).Length != 0)
            throw new InvalidOperationException("Missing excluded boss or unexpected emergence component: " + path);
    }

    private static TutorialEnemy[] GetTutorialActors(Scene scene)
    {
        TutorialEnemy[] actors = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<TutorialEnemy>(true)).ToArray();
        if (actors.Length != TutorialActorNames.Length || !actors.Select(actor => actor.name).OrderBy(name => name, StringComparer.Ordinal)
                .SequenceEqual(TutorialActorNames.OrderBy(name => name, StringComparer.Ordinal)))
            throw new InvalidOperationException("Expected the eight existing named tutorial actors; refusing broader scene changes.");
        return actors;
    }

    private static void RequireSafeEditor()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            throw new InvalidOperationException("Wait for the Editor to finish importing/compiling and leave Play Mode.");
        if (PrefabStageUtility.GetCurrentPrefabStage() != null)
            throw new InvalidOperationException("Close Prefab Mode before emergence authoring/validation.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (scene.isDirty)
                throw new InvalidOperationException("Save or discard the existing dirty scene first: " + scene.name);
        }
    }

    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool IsIdentity(Transform transform) => transform.localPosition == Vector3.zero &&
        Quaternion.Angle(transform.localRotation, Quaternion.identity) < 0.001f && transform.localScale == Vector3.one;
    private static void RecordOverride(Object target)
    {
        if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }

    private sealed class GameplaySceneScope : IDisposable
    {
        private readonly SceneSetup[] _setup = EditorSceneManager.GetSceneManagerSetup();
        private readonly Scene _active = SceneManager.GetActiveScene();
        private readonly bool _opened;
        private readonly bool _alreadyListed;
        public Scene Scene { get; }

        public GameplaySceneScope()
        {
            Scene scene = SceneManager.GetSceneByPath(GameplayScenePath);
            _alreadyListed = _setup.Any(entry => entry.path == GameplayScenePath);
            _opened = !scene.IsValid() || !scene.isLoaded;
            Scene = _opened ? EditorSceneManager.OpenScene(GameplayScenePath, OpenSceneMode.Additive) : scene;
        }

        public void Dispose()
        {
            if (_opened && Scene.IsValid()) EditorSceneManager.CloseScene(Scene, !_alreadyListed);
            if (_active.IsValid() && _active.isLoaded) SceneManager.SetActiveScene(_active);
            SceneSetup[] after = EditorSceneManager.GetSceneManagerSetup();
            if (_setup.Length != after.Length || _setup.Where((entry, i) => entry.path != after[i].path ||
                    entry.isLoaded != after[i].isLoaded || entry.isActive != after[i].isActive).Any())
                throw new InvalidOperationException("Scene setup restoration did not match the original setup.");
        }
    }

    private sealed class PreservationSnapshot
    {
        private readonly Transform _visual;
        private readonly TransformState[] _transforms;
        private readonly KeyValuePair<Component, string>[] _components;

        public PreservationSnapshot(GameObject root, Transform visual)
        {
            _visual = visual;
            _transforms = root.GetComponentsInChildren<Transform>(true).Select(t => new TransformState(t)).ToArray();
            _components = root.GetComponentsInChildren<Component>(true)
                .Where(c => c != null && !(c is Transform) && !(c is EnemyEmergence))
                .Select(c => new KeyValuePair<Component, string>(c, EditorJsonUtility.ToJson(c))).ToArray();
        }

        public void Verify(Transform pivot)
        {
            foreach (TransformState state in _transforms) state.Verify(state.Transform == _visual ? pivot : state.Parent);
            foreach (var entry in _components)
                if (entry.Key == null || EditorJsonUtility.ToJson(entry.Key) != entry.Value)
                    throw new InvalidOperationException("Migration changed an existing component or its references: " + entry.Key);
        }
    }

    private sealed class TransformState
    {
        public readonly Transform Transform;
        public readonly Transform Parent;
        private readonly Vector3 _position, _scale;
        private readonly Quaternion _rotation;
        private readonly bool _active;
        private readonly int _layer;
        private readonly string _tag;

        public TransformState(Transform transform)
        {
            Transform = transform; Parent = transform.parent;
            _position = transform.localPosition; _rotation = transform.localRotation; _scale = transform.localScale;
            _active = transform.gameObject.activeSelf; _layer = transform.gameObject.layer; _tag = transform.gameObject.tag;
        }

        public void Verify(Transform expectedParent)
        {
            if (Transform == null || Transform.parent != expectedParent || Transform.localPosition != _position ||
                Quaternion.Angle(Transform.localRotation, _rotation) > 0.001f || Transform.localScale != _scale ||
                Transform.gameObject.activeSelf != _active || Transform.gameObject.layer != _layer || Transform.gameObject.tag != _tag)
                throw new InvalidOperationException("Migration changed an authored transform or object state: " + Transform);
        }
    }
}
#endif
