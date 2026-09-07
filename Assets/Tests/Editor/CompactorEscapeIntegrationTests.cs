using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

public sealed class CompactorEscapeIntegrationTests
{
    private const string ModelPath = CompactorEscapeSetupEditor.ModelPath;
    private const string PrefabPath = "Assets/Prefabs/Level/Compactor/CompactorExitDoor.prefab";
    private readonly List<GameObject> _objects = new();
    private GameManager _previousGameManager;
    private GameManager _gameManager;
    private float _previousTimeScale;

    [Test]
    public void GameplayScene_UsesNewModelsAndRetainsGameplayBindings()
    {
        var scene = EditorSceneManager.OpenPreviewScene(GameplayModelsSetupEditor.ScenePath);
        try
        {
            var roots = scene.GetRootGameObjects();
            var door = roots.SelectMany(r => r.GetComponentsInChildren<ExitDoor>(true)).Single();
            var presentation = door.GetComponent<CompactorDoorPresentation>();
            Assert.That(presentation, Is.Not.Null);
            Assert.That(GetField<ExitDoor>(presentation, "_exitDoor"), Is.SameAs(door));
            Assert.That(GetField<AnimationClip>(presentation, "_openingClip"), Is.SameAs(LoadClip()));
            Assert.That(GetField<Animator>(presentation, "_animator"), Is.Not.Null);
            Assert.That(GetField<Collider>(presentation, "_closedDoorCollider"), Is.Not.Null);
            Assert.That(GetField<Renderer>(presentation, "_suctionRenderer").sharedMaterial.shader.name,
                Is.EqualTo("ScrapWaves/Level/Compactor Suction"));
            Assert.That(GetField<LevelExitObjective>(door, "_exitObjective"), Is.Not.Null);
            Assert.That(GetField<float>(door, "_chargeDurationSeconds"), Is.EqualTo(20f));
            Assert.That(GetField<float>(door, "_interactionRadius"), Is.EqualTo(20f));
            Assert.That(GetField<Transform>(door, "_interactionPoint").IsChildOf(door.transform), Is.True);
            Assert.That(roots.SelectMany(r => r.GetComponentsInChildren<CompactorEscapeTestController>(true)), Is.Empty);
            var station = roots.SelectMany(r => r.GetComponentsInChildren<CraftingStation>(true)).Single();
            Assert.That(GetField<float>(station, "_interactionRadius"), Is.EqualTo(10f));
            Assert.That(GetField<Transform>(station, "_interactionPoint"), Is.SameAs(station.transform));
            var model = station.transform.Find("Workbench");
            Assert.That(model, Is.Not.Null);
            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(model.gameObject),
                Is.EqualTo(GameplayModelsSetupEditor.WorkbenchPath));
            Assert.That(station.GetComponentInChildren<Collider>(), Is.Not.Null);
            Assert.That(station.transform.Find("Cube"), Is.Null);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    [SetUp]
    public void SetUp()
    {
        _previousTimeScale = Time.timeScale;
        Time.timeScale = 1f;
        _previousGameManager = GameManager.Instance;
        _gameManager = CreateObject("Escape integration game state").AddComponent<GameManager>();
        SetGameManagerInstance(_gameManager);
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = _objects.Count - 1; i >= 0; i--)
            if (_objects[i] != null)
            {
                foreach (CompactorDoorPresentation presentation in _objects[i].GetComponentsInChildren<CompactorDoorPresentation>(true))
                    presentation.Dispose();
                Object.DestroyImmediate(_objects[i]);
            }
        _objects.Clear();
        SetGameManagerInstance(_previousGameManager);
        Time.timeScale = _previousTimeScale;
    }

    [Test]
    public void ImportedPrefab_UsesOneNonLoopingGenericOpeningClipAndPlainModelMaterials()
    {
        GameObject prefab = LoadPrefab();
        ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        Assert.That(importer, Is.Not.Null);
        Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Generic));
        Assert.That(importer.importAnimation, Is.True);
        Assert.That(importer.optimizeGameObjects, Is.False, "Animated mechanical parts must remain addressable.");
        Assert.That(importer.clipAnimations, Has.Length.EqualTo(1));
        Assert.That(importer.clipAnimations[0].name, Is.EqualTo("Open"));
        Assert.That(importer.clipAnimations[0].loopTime, Is.False);
        AnimationClip clip = LoadClip();
        Assert.That(clip.legacy, Is.False);
        Assert.That(clip.length, Is.GreaterThan(0f));
        Assert.That(AnimationUtility.GetAnimationClipSettings(clip).loopTime, Is.False);

        CompactorDoorPresentation presentation = prefab.GetComponent<CompactorDoorPresentation>();
        Assert.That(presentation, Is.Not.Null);
        Assert.That(GetField<ExitDoor>(presentation, "_exitDoor"), Is.SameAs(prefab.GetComponent<ExitDoor>()));
        Assert.That(GetField<AnimationClip>(presentation, "_openingClip"), Is.SameAs(clip));
        Animator animator = GetField<Animator>(presentation, "_animator");
        Assert.That(animator, Is.Not.Null);
        Assert.That(animator.runtimeAnimatorController, Is.Null);
        Assert.That(animator.applyRootMotion, Is.False);
        FindPart(animator.transform, "Door_Lift");
        FindPart(animator.transform, "Piston_1");
        FindPart(animator.transform, "Piston_2");

        Renderer[] modelRenderers = animator.GetComponentsInChildren<Renderer>(true);
        Assert.That(modelRenderers, Is.Not.Empty);
        foreach (Material material in modelRenderers.SelectMany(r => r.sharedMaterials).Distinct())
        {
            Assert.That(material, Is.Not.Null);
            Assert.That(material.shader.name, Is.EqualTo("Universal Render Pipeline/Lit"), material.name);
            Assert.That(material.GetTexture("_BaseMap"), Is.Null, material.name + " must use plain colors.");
        }

        Renderer suction = GetField<Renderer>(presentation, "_suctionRenderer");
        Assert.That(suction, Is.Not.Null);
        Assert.That(suction.sharedMaterial.shader.name, Is.EqualTo("ScrapWaves/Level/Compactor Suction"));
        Assert.That(suction.GetComponent<Collider>(), Is.Null, "The effect must not introduce invisible collision.");
        Assert.That(GetField<Collider>(presentation, "_closedDoorCollider"), Is.Not.Null);
    }

    [Test]
    public void ActualImportedRig_FollowsEscapeProgressThroughOpenHoldAndReset()
    {
        GameObject instance = Track(Object.Instantiate(LoadPrefab()));
        ExitDoor door = instance.GetComponent<ExitDoor>();
        CompactorDoorPresentation presentation = instance.GetComponent<CompactorDoorPresentation>();
        Animator animator = GetField<Animator>(presentation, "_animator");
        AnimationClip clip = LoadClip();
        Renderer suction = GetField<Renderer>(presentation, "_suctionRenderer");
        Collider blocker = GetField<Collider>(presentation, "_closedDoorCollider");
        LevelExitObjective objective = CreateObject("Required escape keys").AddComponent<LevelExitObjective>();
        SetField(door, "_exitObjective", objective);
        // Deliberately differs from the FBX take duration: gameplay owns timing.
        SetField(door, "_chargeDurationSeconds", 11f);
        door.ResetCharge();
        presentation.Configure(door, animator, clip, suction, blocker);

        Transform lift = FindPart(animator.transform, "Door_Lift");
        Transform[] pistons = { FindPart(animator.transform, "Piston_1"), FindPart(animator.transform, "Piston_2") };
        Vector3 closedDoorPosition = instance.transform.InverseTransformPoint(lift.position);
        PartPose[] closedPistons = pistons.Select(p => new PartPose(p)).ToArray();
        Assert.That(blocker.enabled, Is.True);

        // A separate imported instance provides the authored mechanical pose for each clip time.
        GameObject reference = Track(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath)));
        CompactorEscapeTestController controller = instance.AddComponent<CompactorEscapeTestController>();
        controller.Configure(door, objective);
        controller.StartSequence();
        Assert.That(door.State, Is.EqualTo(ExitDoorState.Charging));
        Assert.That(door.ChargeNormalized, Is.Zero);
        AssertRigMatchesClip(animator.transform, reference, clip, 0f);

        Tick(door, 5.5f);
        Assert.That(door.ChargeNormalized, Is.EqualTo(0.5f).Within(0.0001f));
        AssertRigMatchesClip(animator.transform, reference, clip, 0.5f);
        Vector3 middleDoorPosition = instance.transform.InverseTransformPoint(lift.position);
        Assert.That(middleDoorPosition.y, Is.GreaterThan(closedDoorPosition.y + 0.25f), "The door must move upwards.");
        Assert.That(blocker.enabled, Is.True, "The escape stays blocked while its charge is incomplete.");
        for (int i = 0; i < pistons.Length; i++)
            Assert.That(closedPistons[i].Difference(pistons[i]), Is.GreaterThan(0.01f), pistons[i].name + " must follow the lift.");

        Tick(door, 5.5f);
        Assert.That(door.State, Is.EqualTo(ExitDoorState.Ready));
        Assert.That(door.ChargeNormalized, Is.EqualTo(1f));
        AssertRigMatchesClip(animator.transform, reference, clip, 1f);
        Assert.That(instance.transform.InverseTransformPoint(lift.position).y, Is.GreaterThan(middleDoorPosition.y + 0.25f));
        Assert.That(blocker.enabled, Is.False);
        PartPose openedDoor = new(lift);
        PartPose[] openedPistons = pistons.Select(p => new PartPose(p)).ToArray();

        GetField<PlayableGraph>(presentation, "_graph").Evaluate(30f);
        Tick(door, 30f);
        openedDoor.AssertMatches(lift);
        for (int i = 0; i < pistons.Length; i++)
            openedPistons[i].AssertMatches(pistons[i]);
        Assert.That(_gameManager.State, Is.EqualTo(GameManager.GameState.Playing));

        controller.ResetSequence();
        Assert.That(door.State, Is.EqualTo(ExitDoorState.AwaitingActivation));
        Assert.That(door.ChargeNormalized, Is.Zero);
        AssertRigMatchesClip(animator.transform, reference, clip, 0f);
        Assert.That(blocker.enabled, Is.True);
        MaterialPropertyBlock properties = new();
        suction.GetPropertyBlock(properties);
        Assert.That(properties.GetFloat("_Activity"), Is.Zero);
    }

    [Test]
    public void SandboxStart_GrantsKeysStartsOnceCompletesOnceAndDoesNotEndTheGame()
    {
        GameObject root = CreateObject("Escape sequence rules");
        ExitDoor door = root.AddComponent<ExitDoor>();
        LevelExitObjective objective = root.AddComponent<LevelExitObjective>();
        CompactorEscapeTestController controller = root.AddComponent<CompactorEscapeTestController>();
        SetField(door, "_exitObjective", objective);
        SetField(door, "_chargeDurationSeconds", 8f);
        door.ResetCharge();
        controller.Configure(door, objective);
        int starts = 0;
        int completions = 0;
        door.OnChargeStarted += () => starts++;
        door.OnDoorReady += () => completions++;

        Assert.That(door.TryStartCharging(), Is.False, "Gameplay activation must require its keys.");
        Assert.That(objective.KeysCollected, Is.Zero);
        controller.StartSequence();
        Assert.That(objective.KeysCollected, Is.EqualTo(objective.KeysRequired));
        Assert.That(controller.IsCharging, Is.True);
        Assert.That(starts, Is.EqualTo(1));

        Tick(door, 3f);
        float progressBeforeSecondClick = door.ChargeNormalized;
        controller.StartSequence();
        Assert.That(door.TryStartCharging(), Is.False);
        Assert.That(door.ChargeNormalized, Is.EqualTo(progressBeforeSecondClick));
        Assert.That(starts, Is.EqualTo(1));
        Tick(door, 5f);
        Tick(door, 100f);
        Assert.That(completions, Is.EqualTo(1));
        Assert.That(door.State, Is.EqualTo(ExitDoorState.Ready));
        Assert.That(controller.Status, Does.Contain("100%"));
        Assert.That(_gameManager.State, Is.EqualTo(GameManager.GameState.Playing), "Completion of the test must not trigger victory.");
        Assert.That(Time.timeScale, Is.EqualTo(1f));
        Assert.That(door.TryStartCharging(), Is.False, "Ready must hold until reset or a separate escape interaction.");

        controller.ResetSequence();
        Assert.That(objective.AllKeysCollected, Is.True);
        Assert.That(door.State, Is.EqualTo(ExitDoorState.AwaitingActivation));
        Assert.That(door.ChargeNormalized, Is.Zero);
        Assert.That(door.TryStartCharging(), Is.True, "A reset permits a deliberate replay.");
        Assert.That(starts, Is.EqualTo(2));
    }

    [TestCase("Assets/Scenes/Testing/WeaponTestingSandbox.unity")]
    [TestCase("Assets/Scenes/Testing/WeaponTestingSandbox_GameFeel.unity")]
    public void ArenaScene_WiresOneCompactorToSharedObjectiveAndDebugControls(string scenePath)
    {
        var scene = EditorSceneManager.OpenPreviewScene(scenePath);
        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            CompactorEscapeTestController[] controls = roots.SelectMany(r => r.GetComponentsInChildren<CompactorEscapeTestController>(true)).ToArray();
            Assert.That(controls, Has.Length.EqualTo(1), scenePath);
            CompactorEscapeTestController controller = controls[0];
            Assert.That(controller.gameObject.activeInHierarchy, Is.True);
            ExitDoor door = controller.Door;
            Assert.That(door, Is.Not.Null);
            LevelExitObjective objective = GetField<LevelExitObjective>(controller, "_objective");
            Assert.That(objective, Is.Not.Null);
            Assert.That(GetField<LevelExitObjective>(door, "_exitObjective"), Is.SameAs(objective));
            CompactorDoorPresentation presentation = controller.GetComponent<CompactorDoorPresentation>();
            Assert.That(presentation, Is.Not.Null);
            Assert.That(GetField<ExitDoor>(presentation, "_exitDoor"), Is.SameAs(door));
            Assert.That(GetField<AnimationClip>(presentation, "_openingClip"), Is.SameAs(LoadClip()));
            WeaponSandboxDebugUI[] debugUis = roots.SelectMany(r => r.GetComponentsInChildren<WeaponSandboxDebugUI>(true)).ToArray();
            Assert.That(debugUis, Is.Not.Empty);
            foreach (WeaponSandboxDebugUI ui in debugUis)
                Assert.That(GetField<CompactorEscapeTestController>(ui, "_escapeTest"), Is.SameAs(controller));
        }
        finally
        {
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static GameObject LoadPrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(prefab, Is.Not.Null, "Run the compactor setup to author the required prefab.");
        return prefab;
    }

    private static AnimationClip LoadClip()
    {
        AnimationClip[] clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<AnimationClip>()
            .Where(c => !c.name.StartsWith("__preview__")).ToArray();
        Assert.That(clips, Has.Length.EqualTo(1));
        Assert.That(clips[0].name, Is.EqualTo("Open"));
        return clips[0];
    }

    private static Transform FindPart(Transform root, string name)
    {
        Transform part = root.GetComponentsInChildren<Transform>(true).SingleOrDefault(t => t.name == name);
        Assert.That(part, Is.Not.Null, name + " is required in the imported hierarchy.");
        return part;
    }

    private static void AssertRigMatchesClip(Transform actual, GameObject reference, AnimationClip clip, float progress)
    {
        clip.SampleAnimation(reference, clip.length * progress);
        foreach (string name in new[] { "Door_Lift", "Piston_1", "Piston_2" })
            new PartPose(FindPart(reference.transform, name)).AssertMatches(FindPart(actual, name));
    }

    private readonly struct PartPose
    {
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;
        private readonly Vector3 _scale;

        public PartPose(Transform part)
        {
            _position = part.localPosition;
            _rotation = part.localRotation;
            _scale = part.localScale;
        }

        public float Difference(Transform part) => Vector3.Distance(_position, part.localPosition)
            + Quaternion.Angle(_rotation, part.localRotation) / 180f + Vector3.Distance(_scale, part.localScale);

        public void AssertMatches(Transform part)
        {
            Assert.That(Vector3.Distance(part.localPosition, _position), Is.LessThan(0.002f), part.name + " position");
            Assert.That(Quaternion.Angle(part.localRotation, _rotation), Is.LessThan(0.1f), part.name + " rotation");
            Assert.That(Vector3.Distance(part.localScale, _scale), Is.LessThan(0.002f), part.name + " scale");
        }
    }

    private GameObject CreateObject(string name) => Track(new GameObject(name));

    private GameObject Track(GameObject value)
    {
        _objects.Add(value);
        return value;
    }

    private static void Tick(ExitDoor door, float seconds)
    {
        MethodInfo method = typeof(ExitDoor).GetMethod("TickCharge", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        method.Invoke(door, new object[] { seconds });
    }

    private static void SetGameManagerInstance(GameManager value)
    {
        typeof(GameManager).GetField("<Instance>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, value);
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, name);
        field.SetValue(target, value);
    }

    private static T GetField<T>(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, name);
        return (T)field.GetValue(target);
    }
}
