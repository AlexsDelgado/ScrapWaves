using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class EnemyStatRandomizerMovementTests
{
    private const float SpawnSpeed = 1.5f;
    private const float DifficultySpeed = 1.4f;
    private const float SlowSpeed = 0.5f;
    private const float DebugSpeed = 1.25f;
    private const float FixedStep = 0.1f;

    [UnityTest]
    public IEnumerator ActualMovement_ComposesSpawnSpeedWithoutScalingKnockbackOrAttackDash()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        // Acquire runtime references after the runner's domain reload. Update-based movers
        // need a real, nonzero frame delta; Rigidbody movement is stepped explicitly.
        yield return ExerciseMovement();
        yield return new ExitPlayMode();
    }

    private static IEnumerator ExerciseMovement()
    {
        var created = new List<GameObject>();
        float previousCaptureDelta = Time.captureDeltaTime;
        float previousTimeScale = Time.timeScale;
        SimulationMode previousSimulation = Physics.simulationMode;
        Random.State previousRandom = Random.state;
        DebugSpeedTool previousDebug = DebugSpeedTool.Active;
        bool previousIntensity = OverheatSwarmBoost.IsIntensityActive;
        float previousExitPressure = (float)typeof(OverheatSwarmBoost)
            .GetField("s_exitPressureMultiplier", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        try
        {
            Time.timeScale = 1f;
            Time.captureDeltaTime = 0.02f;
            Physics.simulationMode = SimulationMode.Script;
            OverheatSwarmBoost.ClearExitPressure();
            OverheatSwarmBoost.SetIntensity(true);
            yield return null;
            Assert.That(Time.deltaTime, Is.GreaterThan(0f), "Displacement assertions need a real runtime frame.");

            DebugSpeedTool debug = Track(created, "Randomizer movement debug").AddComponent<DebugSpeedTool>();
            debug.EnemySpeedScale = DebugSpeed;
            debug.SetApplying(true);
            Assert.That(DebugSpeedTool.EnemyScale, Is.EqualTo(DebugSpeed));

            CheckCharacterFollower(created);
            CheckRigidbodyFollower(created);
            CheckFlyingMovement(created);
            CheckBomberMovementAndDash(created);
        }
        finally
        {
            for (int i = created.Count - 1; i >= 0; i--)
                if (created[i] != null) Object.DestroyImmediate(created[i]);
            typeof(DebugSpeedTool).GetField("<Active>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, previousDebug);
            OverheatSwarmBoost.SetIntensity(previousIntensity);
            OverheatSwarmBoost.SetExitPressureMultiplier(previousExitPressure);
            Random.state = previousRandom;
            Physics.simulationMode = previousSimulation;
            Time.captureDeltaTime = previousCaptureDelta;
            Time.timeScale = previousTimeScale;
        }
    }

    private static void CheckCharacterFollower(List<GameObject> created)
    {
        EnemyFollow mover = CreateMover<EnemyFollow>(created, Vector3.up * 5f, out _);
        CharacterController controller = mover.GetComponent<CharacterController>();
        controller.minMoveDistance = 0f;
        SetField(mover, "_gravity", 0f);
        SetField(mover, "_climbSpeed", 0f);
        mover.ConfigureMovementProfile(default);
        mover.ConfigureDifficultyForSpawn(DifficultySpeed);
        mover.SetTarget(CreateTarget(created, mover.transform));
        AddSlow(mover.gameObject);
        Physics.SyncTransforms();

        Vector3 origin = mover.transform.position;
        Invoke(mover, "Update");
        float expected = mover.AuthoredMoveSpeed * DifficultySpeed * SpawnSpeed * SlowSpeed
            * DebugSpeed * OverheatSwarmBoost.SpeedMultiplier * Time.deltaTime;
        AssertPlanarDisplacement(mover.transform.position - origin, expected, "CharacterController follower");

        // No target leaves only the existing knockback branch. It must not inherit any
        // of the non-neutral movement, difficulty, slow, debug or Overheat multipliers.
        mover.SetTarget(null);
        EnemyKnockbackReceiver knockback = mover.GetComponent<EnemyKnockbackReceiver>();
        origin = mover.transform.position;
        knockback.ApplyKnockback(origin - Vector3.right, 4f);
        Invoke(mover, "Update");
        AssertPlanarDisplacement(mover.transform.position - origin, 4f * Time.deltaTime, "CharacterController knockback");
    }

    private static void CheckRigidbodyFollower(List<GameObject> created)
    {
        SimpleFollow mover = CreateMover<SimpleFollow>(created, new Vector3(20f, 5f, 0f), out EnemyStatRandomizer randomizer);
        Rigidbody body = mover.GetComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = false;
        body.linearDamping = 0f;
        body.angularDamping = 0f;
        body.constraints = RigidbodyConstraints.FreezeRotation;
        mover.ConfigureMovementProfile(default);
        mover.ConfigureDifficultyForSpawn(DifficultySpeed);
        mover.SetTarget(CreateTarget(created, mover.transform));
        AddSlow(mover.gameObject);

        float expected = mover.AuthoredMoveSpeed * DifficultySpeed * SpawnSpeed * SlowSpeed * DebugSpeed * FixedStep;
        AssertPlanarDisplacement(StepRigidbody(mover), expected, "Rigidbody follower");

        // A new lease must be read through the cached component, not a cached old roll.
        randomizer.EndSpawn();
        SetField(randomizer, "_movementSpeedMultiplierRange", Vector2.one * 0.75f);
        randomizer.BeginSpawn();
        expected = mover.AuthoredMoveSpeed * DifficultySpeed * 0.75f * SlowSpeed * DebugSpeed * FixedStep;
        AssertPlanarDisplacement(StepRigidbody(mover), expected, "Rigidbody follower after a new spawn sample");

        mover.SetTarget(null);
        mover.GetComponent<EnemyKnockbackReceiver>().ApplyKnockback(body.position - Vector3.right, 4f);
        AssertPlanarDisplacement(StepRigidbody(mover), 4f * FixedStep, "Rigidbody knockback");
    }

    private static void CheckFlyingMovement(List<GameObject> created)
    {
        FlyingRangedBehavior mover = CreateMover<FlyingRangedBehavior>(created, new Vector3(40f, 5f, 0f), out _);
        Vector3 origin = mover.transform.position;
        const float phaseSpeed = 0.35f;
        Invoke(mover, "MovePlanar", Vector3.right, phaseSpeed);
        float expected = mover.AuthoredMoveSpeed * phaseSpeed * SpawnSpeed * DebugSpeed * Time.deltaTime;
        AssertPlanarDisplacement(mover.transform.position - origin, expected, "Flying ranged locomotion with phase speed");
    }

    private static void CheckBomberMovementAndDash(List<GameObject> created)
    {
        BomberDroneBehavior mover = CreateMover<BomberDroneBehavior>(created, new Vector3(60f, 5f, 0f), out _);
        Vector3 origin = mover.transform.position;
        Invoke(mover, "MovePlanar", Vector3.right);
        float expected = mover.AuthoredMoveSpeed * SpawnSpeed * DebugSpeed * Time.deltaTime;
        AssertPlanarDisplacement(mover.transform.position - origin, expected, "Bomber locomotion");

        // Keep the attack away from its end and bomb-drop thresholds. Its authored dash
        // ignores both ordinary spawn speed and DebugSpeedTool, as it did before task 206.
        SetField(mover, "_dashDir", Vector3.right);
        SetField(mover, "_dashSpeed", 18f);
        SetField(mover, "_dashDistance", 1000f);
        SetField(mover, "_dashTravelled", 0f);
        SetField(mover, "_bombsPerDash", 1);
        origin = mover.transform.position;
        Invoke(mover, "TickDash");
        AssertPlanarDisplacement(mover.transform.position - origin, 18f * Time.deltaTime, "Bomber attack dash");
    }

    private static T CreateMover<T>(List<GameObject> created, Vector3 position, out EnemyStatRandomizer randomizer)
        where T : MonoBehaviour
    {
        GameObject root = Track(created, typeof(T).Name + " randomizer test");
        root.SetActive(false);
        root.transform.position = position;
        randomizer = root.AddComponent<EnemyStatRandomizer>();
        SetField(randomizer, "_movementSpeedMultiplierRange", Vector2.one * SpawnSpeed);
        root.AddComponent<EnemyKnockbackReceiver>();
        T mover = root.AddComponent<T>();
        // Run actual mover methods under controlled ticks; automatic movement must not
        // add a second displacement while the test measures one call.
        mover.enabled = false;
        root.SetActive(true);
        Assert.That(mover.didAwake, Is.True, typeof(T).Name + " must cache the component through its real Awake.");
        randomizer.BeginSpawn();
        Assert.That(randomizer.MovementSpeedMultiplier, Is.EqualTo(SpawnSpeed));
        return mover;
    }

    private static Transform CreateTarget(List<GameObject> created, Transform enemy)
    {
        Transform target = Track(created, "Movement target").transform;
        target.position = enemy.position + Vector3.right * 100f;
        return target;
    }

    private static void AddSlow(GameObject enemy)
    {
        // Configure the existing status directly to isolate its movement contract from
        // unrelated visual effect creation. The mover still calls its public resolver.
        WeaponMovementSlowStatus slow = enemy.AddComponent<WeaponMovementSlowStatus>();
        SetField(slow, "_speedMultiplier", SlowSpeed);
        SetField(slow, "_remainingDuration", 10f);
    }

    private static Vector3 StepRigidbody(SimpleFollow mover)
    {
        Rigidbody body = mover.GetComponent<Rigidbody>();
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        Physics.SyncTransforms();
        Vector3 origin = body.position;
        mover.BrainFixedUpdate(FixedStep);
        Physics.Simulate(FixedStep);
        return body.position - origin;
    }

    private static void AssertPlanarDisplacement(Vector3 displacement, float expected, string label)
    {
        Assert.That(expected, Is.GreaterThan(0f), label);
        Assert.That(displacement.x, Is.EqualTo(expected).Within(0.003f), label);
        Assert.That(Mathf.Abs(displacement.y), Is.LessThan(0.003f), label);
        Assert.That(Mathf.Abs(displacement.z), Is.LessThan(0.003f), label);
    }

    private static GameObject Track(List<GameObject> created, string name)
    {
        GameObject root = new(name);
        created.Add(root);
        return root;
    }

    private static void SetField(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, name);
        field.SetValue(target, value);
    }

    private static void Invoke(object target, string name, params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, name);
        method.Invoke(target, arguments);
    }

    [UnityTearDown]
    public IEnumerator RestoreMode()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
    }
}
