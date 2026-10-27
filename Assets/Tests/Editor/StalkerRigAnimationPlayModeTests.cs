using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Runs the saved boss through its actual Update/Animator/LateUpdate and death callbacks.</summary>
public sealed class StalkerRigAnimationPlayModeTests
{
    private const string SetupKey = "StalkerRigAnimationTests.SceneSetup";
    private const string PrefabPath = "Assets/Prefabs/Stalker.prefab";
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
    [Serializable] private sealed class SavedSetup { public SavedScene[] Scenes; }
    [Serializable] private sealed class SavedScene { public string Path; public bool Loaded; public bool Active; }

    [UnityTest]
    public IEnumerator ActualBoss_RepeatedJumpsAtDifferentSpeedsKeepAnimationAndGameplayInSync()
    {
        SaveAndClearScene();
        yield return new EnterPlayMode();
        // Allocate all scene references after the runner's domain reload.
        yield return RunRepeatedJumps();
    }

    [UnityTest]
    public IEnumerator ActualBoss_DeathInEveryPhaseRewardsImmediatelyAndCleansDetachedVisual()
    {
        SaveAndClearScene();
        yield return new EnterPlayMode();
        yield return RunDeaths();
    }

    [UnityTest]
    public IEnumerator ActualBoss_DeathDuringDeepReburrowKeepsKeyOnSurfaceAndRewardsOnce()
    {
        SaveAndClearScene();
        yield return new EnterPlayMode();
        yield return RunDeepReburrowDeath();
    }

    [UnityTest]
    public IEnumerator ActualBoss_OverheatSpawnStaysAliveThroughEmergenceAndEndsOnlyOnDeath()
    {
        SaveAndClearScene();
        yield return new EnterPlayMode();
        yield return RunOverheatSpawn();
    }

    private IEnumerator RunOverheatSpawn()
    {
        Transform target = CreateScene();
        GameObject ground = GameObject.Find("Stalker rig test ground");
        GameObject region = new GameObject("Upper region");
        region.transform.position = Vector3.up * 50f;
        BoxCollider volume = region.AddComponent<BoxCollider>();
        volume.isTrigger = true;
        volume.size = new Vector3(400f, 10f, 400f);
        UpperLevelVolume upper = region.AddComponent<UpperLevelVolume>();
        typeof(UpperLevelVolume).GetField("_baseSurfaceRoots", PrivateInstance)
            .SetValue(upper, new[] { ground.transform });

        target.gameObject.SetActive(false);
        target.gameObject.layer = LayerMask.NameToLayer("Player");
        target.position = new Vector3(30f, 1.02f, 0f);
        CapsuleCollider capsule = target.gameObject.AddComponent<CapsuleCollider>();
        capsule.height = 2f;
        capsule.radius = 0.5f;
        target.gameObject.AddComponent<Rigidbody>().useGravity = false;
        target.gameObject.AddComponent<PlayerMovement>();
        target.gameObject.AddComponent<PlayerHealth>();
        PlayerPlatformState platform = target.gameObject.AddComponent<PlayerPlatformState>();
        typeof(PlayerPlatformState).GetField("_upperLevelVolume", PrivateInstance).SetValue(platform, upper);
        target.gameObject.SetActive(true);
        Physics.SyncTransforms();
        yield return null;
        Assert.That(platform.CanSpawnObjectives, Is.True);

        OverheatManager overheat = new GameObject("Overheat").AddComponent<OverheatManager>();
        GameObject managerRoot = new GameObject("Boss manager");
        managerRoot.SetActive(false);
        BossManager manager = managerRoot.AddComponent<BossManager>();
        typeof(BossManager).GetField("_overheatManager", PrivateInstance).SetValue(manager, overheat);
        typeof(BossManager).GetField("_bossPrefab", PrivateInstance)
            .SetValue(manager, AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        // Match GameplayScene's first boss appearance, HP and spawn distance.
        typeof(BossManager).GetField("_overheatCycleIndex", PrivateInstance).SetValue(manager, 1);
        typeof(BossManager).GetField("_bossMaxHealth", PrivateInstance).SetValue(manager, 1500);
        typeof(BossManager).GetField("_spawnDistance", PrivateInstance).SetValue(manager, 35f);
        managerRoot.SetActive(true);
        int defeats = 0;
        manager.OnBossDefeated += () => defeats++;
        typeof(OverheatManager).GetMethod("OnMaxHeatReached", PrivateInstance).Invoke(overheat, null);

        Assert.That(manager.CurrentOverheatCycle, Is.EqualTo(2));
        Assert.That(manager.ActiveBosses.Count, Is.EqualTo(1),
            "The actual burrowing prefab must pass placement instead of clearing an empty Overheat.");
        EnemyHealth health = manager.PrimaryBoss;
        Assert.That(health, Is.Not.Null);
        Assert.That(health.CurrentHealth, Is.EqualTo(1500));
        Assert.That(health.MaxHealth, Is.EqualTo(1500));
        StalkerRigAnimation rig = health.GetComponent<StalkerRigAnimation>();
        yield return null;
        Assert.That(overheat.IsOverheating, Is.True);
        Assert.That(rig.CurrentPhase, Is.EqualTo(StalkerRigAnimation.Phase.Hidden));
        Assert.That(health.GetComponent<CharacterController>().enabled, Is.False);
        Assert.That(health.GetComponentsInChildren<Collider>().All(c => !c.enabled), Is.True);
        Assert.That(health.IsInvincible, Is.True);

        for (int frame = 0; frame < 360 && rig.CurrentPhase == StalkerRigAnimation.Phase.Hidden; frame++)
            yield return null;
        Assert.That(rig.CurrentPhase, Is.EqualTo(StalkerRigAnimation.Phase.Emergence));
        Assert.That(health.CurrentHealth, Is.EqualTo(1500));
        Assert.That(health.GetComponentInChildren<SkinnedMeshRenderer>().enabled, Is.True);
        Assert.That(overheat.IsOverheating, Is.True);
        Assert.That(defeats, Is.Zero);

        // The authored body takes only 20% damage; this hit must still be lethal.
        Assert.That(health.ApplyDamage(health.MaxHealth * 10), Is.True);
        Assert.That(defeats, Is.EqualTo(1));
        Assert.That(manager.HasActiveBosses, Is.False);
        Assert.That(overheat.IsOverheating, Is.False);
    }

    private IEnumerator RunRepeatedJumps()
    {
        Transform target = CreateScene();
        Assert.That(target.GetComponent<PlayerHealth>(), Is.Null,
            "The entry burst must also work when no player-health component is available.");
        DebugSpeedTool speedTool = new GameObject("Stalker test speed").AddComponent<DebugSpeedTool>();
        speedTool.SetApplying(true);
        GameObject boss = Spawn(target);
        GigaWormBehavior behavior = boss.GetComponent<GigaWormBehavior>();
        StalkerRigAnimation rig = boss.GetComponent<StalkerRigAnimation>();
        EnemyHealth health = boss.GetComponent<EnemyHealth>();
        SkinnedMeshRenderer skin = boss.GetComponentInChildren<SkinnedMeshRenderer>(true);
        Transform[] bones = skin.bones;
        Transform[] proxies = ReadTransforms(rig, "_collisionSegments");
        Transform headZone = boss.transform.Find("Head");
        Vector3 headZonePosition = headZone.localPosition;
        yield return null;
        Pose[] bind = bones.Select(b => new Pose(b.localPosition, b.localRotation)).ToArray();
        Assert.That(rig.CurrentPhase.ToString(), Is.EqualTo("Hidden"));
        AssertVisibility(skin, proxies, false);
        Mesh baked = new();
        try
        {
            // Every rate must complete the entire arch and bury the whole body, including 2x.
            foreach (float speed in new[] { 0.5f, 1f, 2f })
            {
                speedTool.EnemySpeedScale = speed;
                Assert.That(DebugSpeedTool.EnemyScale, Is.EqualTo(speed));
                // Exercise different real launch/landing planes, not only a pre-launch
                // teleport that BeginJump replaces with the same origin and route.
                target.position = speed < 1f ? new Vector3(30f, 1f, 0f)
                    : speed < 2f ? new Vector3(24f, 1f, 18f) : new Vector3(0f, 1f, -30f);
                boss.SetActive(false);
                boss.transform.SetPositionAndRotation(new Vector3(-20f * speed, -2.5f, 15f), Quaternion.Euler(0f, 45f, 0f));
                boss.SetActive(true);
                SetPlayer(behavior, target);
                yield return null;
                Assert.That(rig.CurrentPhase.ToString(), Is.EqualTo("Hidden"));
                AssertBind(bones, bind);
                AssertVisibility(skin, proxies, false);
                StalkerScrapBurstVfx scrap = Object.FindAnyObjectByType<StalkerScrapBurstVfx>();
                int entryBurstsBefore = scrap != null ? scrap.EntryBurstCount : 0;
                bool checkedEntryBurst = false;
                int trailsAtEntry = -1;
                BeginJump(behavior);
                Assert.That(rig.CurrentPhase.ToString(), Is.EqualTo("Emergence"));
                Assert.That(health.IsInvincible, Is.False);
                AssertVisibility(skin, proxies, true);
                StalkerFlightPath flightPath = Read<StalkerFlightPath>(behavior, "_flightPath");
                float directAscentDistance = Vector3.Distance(Read<Vector3>(behavior, "_emergeOriginGround"),
                    Read<Vector3>(behavior, "_apexPoint"));
                float ascentPathRatio = flightPath.ApexDistance / directAscentDistance;
                float nominalAscentDuration = directAscentDistance / (Read<float>(behavior, "_jumpRiseSpeed") * speed);

                HashSet<string> observed = new() { "Emergence" };
                List<FlightSample> samples = new();
                float elapsed = 0f;
                float emergenceEndedAt = -1f;
                float apexBeganAt = -1f;
                float diveBeganAt = -1f;
                float fallBeganAt = -1f;
                float returnBeganAt = -1f;
                float firstReturnTop = float.NaN;
                float lastVisibleTop = float.PositiveInfinity;
                float lastVisibleTravelBudget = 0f;
                float maximumBoneRotation = 0f;
                int travelSamples = 0;
                for (int frame = 0; frame < 1800; frame++)
                {
                    string beforeState = State(behavior);
                    string beforePhase = rig.CurrentPhase.ToString();
                    Vector3 beforePosition = boss.transform.position;
                    Quaternion beforeRotation = boss.transform.rotation;
                    float beforeDistance = Read<float>(behavior, "_flightDistance");
                    yield return null;
                    elapsed += Time.deltaTime;
                    string state = State(behavior);
                    string phase = rig.CurrentPhase.ToString();
                    observed.Add(phase);
                    AssertVector(headZone.localPosition, headZonePosition, "The animation changed the head damage zone.");
                    Assert.That(Quaternion.Angle(bones[0].rotation, boss.transform.rotation), Is.LessThan(0.1f),
                        "The rig head must stay aligned with the authoritative root/head hit zone.");
                    Assert.That(rig.Animator.applyRootMotion, Is.False);
                    if (phase == "Ascent" && emergenceEndedAt < 0f) emergenceEndedAt = elapsed;
                    if (phase == "ApexDive" && apexBeganAt < 0f) apexBeganAt = elapsed;
                    if (phase == "Dive" && diveBeganAt < 0f) diveBeganAt = elapsed;
                    if (state == "Fall" && fallBeganAt < 0f) fallBeganAt = elapsed;
                    if (state == "ReturnUnderground" && returnBeganAt < 0f) returnBeganAt = elapsed;
                    if (state == "Jump" || state == "Fall")
                    {
                        scrap = Object.FindAnyObjectByType<StalkerScrapBurstVfx>();
                        Assert.That(scrap != null ? scrap.EntryBurstCount : 0, Is.EqualTo(entryBurstsBefore),
                            "The entry burst fired before the head reached the landing surface.");
                        AssertVisibility(skin, proxies, true);
                        Assert.That(health.IsInvincible, Is.False);
                        Assert.That(phase, state == "Jump"
                            ? Is.EqualTo("Emergence").Or.EqualTo("Ascent")
                            : Is.EqualTo("ApexDive").Or.EqualTo("Dive"));
                        // A new crossfade is observable after this frame's Animator evaluation.
                        if (phase == beforePhase) AssertAnimatorState(rig, phase);
                        AssertProxyAlignment(bones, proxies);
                        samples.Add(new FlightSample(boss.transform, bones[0], bones[7], elapsed));
                        for (int i = 1; i < 8; i++)
                        {
                            maximumBoneRotation = Mathf.Max(maximumBoneRotation, Quaternion.Angle(bind[i].rotation, bones[i].localRotation));
                            Assert.That(Quaternion.Angle(bind[i].rotation, bones[i].localRotation),
                                Is.LessThanOrEqualTo(rig.MaximumJointBend + 0.1f), "Mechanical joint " + i);
                            Assert.That(bones[i].localPosition.magnitude / bind[i].position.magnitude,
                                Is.InRange(0.9999f, 1.0001f), "The physical chain changed a fixed link length: " + i);
                        }
                    }
                    if ((beforeState == "Jump" || beforeState == "Fall") && (state == "Jump" || state == "Fall"))
                    {
                        float authoredSpeed = Read<float>(behavior, beforeState == "Jump" ? "_jumpRiseSpeed" : "_fallSpeed");
                        float travelBudget = authoredSpeed * speed * Time.deltaTime * (beforeState == "Jump" ? ascentPathRatio : 1f);
                        float progress = Read<float>(behavior, "_flightDistance") - beforeDistance;
                        Assert.That(progress, Is.GreaterThan(0f), "The head stopped advancing along the arch.");
                        Assert.That(progress, Is.LessThanOrEqualTo(travelBudget + 0.001f), "The path clock exceeded its scripted rate.");
                        // Turn limiting and the partial step that reaches a phase boundary may
                        // reduce progress. Compare the world chord to that actual arc advance,
                        // allowing 2mm for the distance lookup's interpolation and float error.
                        Assert.That(Vector3.Distance(beforePosition, boss.transform.position),
                            Is.LessThanOrEqualTo(progress + 0.002f), "The root moved farther than its path advance.");
                        AssertVector(boss.transform.position, flightPath.GetPosition(Read<float>(behavior, "_flightDistance")),
                            "Animation took authority over the gameplay root.", 0.001f);
                        float angularBudget = Read<float>(behavior, "_airTiltSpeed") * speed * Time.deltaTime;
                        Assert.That(Quaternion.Angle(beforeRotation, boss.transform.rotation), Is.LessThanOrEqualTo(angularBudget + 0.1f),
                            "The head orientation snapped instead of following the smooth arch.");
                        travelSamples++;
                    }
                    if (skin.enabled)
                    {
                        skin.BakeMesh(baked, true);
                        lastVisibleTop = baked.vertices.Max(vertex => skin.transform.TransformPoint(vertex).y);
                        lastVisibleTravelBudget = Read<float>(behavior, "_fallSpeed") * speed * Time.deltaTime;
                        Assert.That(rig.GetHighestBodyPoint(), Is.GreaterThanOrEqualTo(lastVisibleTop - 0.02f),
                            "The hide gate underestimated the actual posed mesh height.");
                    }
                    if (state == "ReturnUnderground")
                    {
                        Assert.That(phase, Is.EqualTo("Reburrow"));
                        Assert.That(Read<bool>(behavior, "_landingImpactApplied"), Is.True);
                        if (phase == beforePhase) AssertAnimatorState(rig, "ImpactReburrow");
                        AssertVisibility(skin, proxies, true);
                        AssertProxyAlignment(bones, proxies);
                        if (float.IsNaN(firstReturnTop)) firstReturnTop = lastVisibleTop;
                        scrap = Object.FindAnyObjectByType<StalkerScrapBurstVfx>();
                        Assert.That(scrap, Is.Not.Null, "The head impact must emit scrap before the missing-health early return.");
                        if (!checkedEntryBurst)
                        {
                            Assert.That(scrap.EntryBurstCount, Is.EqualTo(entryBurstsBefore + 1));
                            AssertVector(scrap.LastEntryPosition, Read<Vector3>(behavior, "_landingPoint"),
                                "Scrap entry burst did not use the resolved landing surface.", 0.001f);
                            Assert.That(scrap.ActiveParticleCount, Is.GreaterThan(0));
                            int particlesAtEntry = scrap.ActiveParticleCount;
                            // Duplicate impact callbacks must preserve the single guarded burst.
                            MethodInfo impact = typeof(GigaWormBehavior).GetMethod("ApplyLandingImpact", PrivateInstance);
                            impact.Invoke(behavior, null);
                            impact.Invoke(behavior, null);
                            Assert.That(scrap.EntryBurstCount, Is.EqualTo(entryBurstsBefore + 1));
                            Assert.That(scrap.ActiveParticleCount, Is.EqualTo(particlesAtEntry));
                            trailsAtEntry = scrap.TrailBurstCount;
                            checkedEntryBurst = true;
                        }
                        Assert.That(scrap.EntryBurstCount, Is.EqualTo(entryBurstsBefore + 1),
                            "Continuing to bury the tail emitted another entry burst.");
                        Assert.That(scrap.TrailBurstCount, Is.EqualTo(trailsAtEntry),
                            "The underground-follow trail must not emit during the same entry animation.");
                    }
                    if (state == "HuntUnderground") break;
                }
                Assert.That(State(behavior), Is.EqualTo("HuntUnderground"), "Flight did not finish at speed " + speed);
                Assert.That(checkedEntryBurst, Is.True, "Every repeated dive must reset the single-entry burst guard.");
                Assert.That(scrap.EntryBurstCount, Is.EqualTo(entryBurstsBefore + 1));
                Assert.That(observed, Is.SupersetOf(new[] { "Emergence", "Ascent", "ApexDive", "Dive", "Reburrow", "Hidden" }));
                Assert.That(fallBeganAt, Is.EqualTo(nominalAscentDuration).Within(3f / 60f),
                    "Rounding the path must preserve the nominal ascent/spit window.");
                Assert.That(emergenceEndedAt, Is.EqualTo(0.3f / speed).Within(3f / 60f));
                Assert.That(diveBeganAt - apexBeganAt, Is.EqualTo(0.6f / speed).Within(3f / 60f));
                Assert.That(firstReturnTop, Is.GreaterThan(2f), "The fixture must expose a tail still above ground at head impact.");
                Assert.That(elapsed - returnBeganAt, Is.GreaterThan(0.35f + 1f / 60f),
                    "The long body cannot disappear when the short impact clip finishes.");
                // Hide/reset happens in Update. The previous visible sample may be one travel step
                // above the surface, but cannot still contain metres of exposed body.
                Assert.That(lastVisibleTop, Is.LessThanOrEqualTo(lastVisibleTravelBudget + 0.05f),
                    "The body was hidden before the trailing geometry crossed the ground plane.");
                Assert.That(travelSamples, Is.GreaterThan(30));
                Assert.That(maximumBoneRotation, Is.GreaterThan(0.5f), "The actual rig never articulated.");
                AssertArch(samples, speed);
                TestContext.WriteLine("Flight " + speed + "x: reburrow " + (elapsed - returnBeganAt).ToString("F3") +
                    " s, last visible mesh top " + lastVisibleTop.ToString("F3") + " m.");
                Assert.That(health.IsInvincible, Is.True);
                AssertVisibility(skin, proxies, false);
                AssertBind(bones, bind);
                AssertProxyAlignment(bones, proxies);
            }
        }
        finally { Object.Destroy(baked); }
    }

    private static void AssertArch(List<FlightSample> samples, float speed)
    {
        int apex = 0;
        for (int i = 1; i < samples.Count; i++)
            if (samples[i].Position.y > samples[apex].Position.y) apex = i;
        Assert.That(apex, Is.InRange(2, samples.Count - 3));
        FlightSample top = samples[apex];
        Assert.That(Mathf.Abs(top.HeadUp.y), Is.LessThan(0.2f), "The head must cross the crest facing along a horizontal tangent.");
        Assert.That(top.HeadPosition.y - top.TailPosition.y, Is.GreaterThan(2f),
            "The tail must remain on the rising side of the arch instead of swinging rigidly around the head.");
        Assert.That(top.TailUp.y, Is.GreaterThan(0.2f), "The trailing body must still follow the rising section at the crest.");
        Assert.That(Vector3.Dot(top.HeadPosition - top.TailPosition, top.HeadUp), Is.GreaterThan(2f),
            "The tail must trail the head through the crest.");
        float headTravel = 0f;
        float tailTravel = 0f;
        for (int i = apex + 1; i < samples.Count && samples[i].Time <= top.Time + 0.6f / speed; i++)
        {
            headTravel += Vector3.Distance(samples[i - 1].HeadPosition, samples[i].HeadPosition);
            tailTravel += Vector3.Distance(samples[i - 1].TailPosition, samples[i].TailPosition);
        }
        Assert.That(headTravel, Is.GreaterThan(2f), "The apex window must include real forward travel.");
        Assert.That(tailTravel / headTravel, Is.LessThan(3f),
            "The tail is whipping around a head pivot instead of following the arch. Baseline was 6.166x at 1x speed.");
        TestContext.WriteLine("Apex " + speed + "x: tail/head travel ratio " + (tailTravel / headTravel).ToString("F3"));
        for (int i = 1; i < samples.Count - 1; i++)
        {
            Vector3 travel = samples[i + 1].Position - samples[i - 1].Position;
            if (travel.sqrMagnitude < 0.00001f) continue;
            Assert.That(Vector3.Angle(travel, samples[i].HeadUp), Is.LessThan(8f),
                "The head direction diverged from the actual flight tangent at sample " + i + ", speed " + speed);
        }
    }

    private readonly struct FlightSample
    {
        public readonly Vector3 Position;
        public readonly Vector3 HeadPosition;
        public readonly Vector3 HeadUp;
        public readonly Vector3 TailPosition;
        public readonly Vector3 TailUp;
        public readonly float Time;
        public FlightSample(Transform root, Transform head, Transform tail, float time)
        {
            Position = root.position; HeadPosition = head.position; HeadUp = head.up;
            TailPosition = tail.position; TailUp = tail.up; Time = time;
        }
    }
    private IEnumerator RunDeaths()
    {
        Transform target = CreateScene();
        EnemyReactionRuntime.Apply(new GameFeelRuntimeOptions { EnemyReactionEnabled = true });
        EnemyReactionRuntime.ApplyUserPreferences(false, true);
        BossManager objective = new GameObject("Stalker test objective").AddComponent<BossManager>();
        int objectiveDefeats = 0;
        objective.OnBossDefeated += () => objectiveDefeats++;
        foreach (string phase in new[] { "Emergence", "Ascent", "ApexDive", "Dive", "Reburrow", "Hidden" })
        {
            GameObject boss = Spawn(target);
            GigaWormBehavior behavior = boss.GetComponent<GigaWormBehavior>();
            StalkerRigAnimation rig = boss.GetComponent<StalkerRigAnimation>();
            EnemyHealth health = boss.GetComponent<EnemyHealth>();
            SkinnedMeshRenderer skin = boss.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Transform[] proxies = ReadTransforms(rig, "_collisionSegments");
            Transform animationRoot = rig.Animator.transform;
            yield return null;
            RegisterBossObjective(objective, health);
            Assert.That(objective.HasActiveBosses, Is.True);
            Assert.That(boss.GetComponentsInChildren<EnemyDeathFeedback>(true), Is.Empty);
            if (phase != "Hidden")
            {
                BeginJump(behavior);
                for (int frame = 0; frame < 900 && rig.CurrentPhase.ToString() != phase; frame++) yield return null;
            }
            Assert.That(rig.CurrentPhase.ToString(), Is.EqualTo(phase), "Could not reach requested death phase.");
            AssertVisibility(skin, proxies, phase != "Hidden");
            Vector3 deathPosition = animationRoot.position;
            Quaternion deathRotation = animationRoot.rotation;
            Vector3 deathScale = animationRoot.lossyScale;
            int bossId = boss.GetInstanceID();
            int keyCount = Object.FindObjectsByType<KeyPickup>(FindObjectsSortMode.None).Length;
            int eliminations = RunCombatStats.EnemiesEliminated;
            int objectiveDefeatsBefore = objectiveDefeats;
            int deaths = 0;
            health.OnDied += () => deaths++;
            Assert.That(Object.FindObjectsByType<StalkerRigDeathVisual>(FindObjectsSortMode.None), Is.Empty);

            if (phase == "Hidden") Assert.That(health.ApplyDotDamage(100000), Is.True, "Buried DoT deaths must still work.");
            else health.ForceKill();
            health.ForceKill();
            Assert.That(deaths, Is.EqualTo(1), phase + " produced duplicate OnDied/reward callbacks.");
            Assert.That(objective.HasActiveBosses, Is.False, "A death animation must not keep the gameplay objective occupied.");
            Assert.That(objectiveDefeats, Is.EqualTo(objectiveDefeatsBefore + 1));
            Assert.That(RunCombatStats.EnemiesEliminated, Is.EqualTo(eliminations + 1));
            Assert.That(Object.FindObjectsByType<KeyPickup>(FindObjectsSortMode.None).Length, Is.EqualTo(keyCount + 1),
                phase + ": the key must spawn in OnDied, before the death animation finishes.");
            Assert.That(GenericDeathPending(bossId), Is.False, "A generic baked snapshot would duplicate the authored death.");
            StalkerRigDeathVisual[] detached = Object.FindObjectsByType<StalkerRigDeathVisual>(FindObjectsSortMode.None);
            Assert.That(detached.Length, Is.EqualTo(phase == "Hidden" ? 0 : 1), phase);
            if (phase != "Hidden")
            {
                Assert.That(detached[0].transform, Is.SameAs(animationRoot), "Detach the original animated rig, without cloning the health owner.");
                Assert.That(animationRoot.parent, Is.Null);
                AssertVector(animationRoot.position, deathPosition);
                Assert.That(Quaternion.Angle(animationRoot.rotation, deathRotation), Is.LessThan(0.05f));
                AssertVector(animationRoot.lossyScale, deathScale);
                Assert.That(animationRoot.GetComponentsInChildren<Collider>(true), Is.Empty);
                Assert.That(animationRoot.GetComponentsInChildren<EnemyHealth>(true), Is.Empty);
                Assert.That(animationRoot.GetComponentsInChildren<GigaWormBehavior>(true), Is.Empty);
                Assert.That(skin.enabled, Is.True);
            }
            yield return null;
            Assert.That(boss == null, Is.True, phase + ": presentation must not defer normal gameplay destruction.");
            if (phase != "Hidden")
            {
                Assert.That(detached[0] != null, Is.True, "Death must remain visible after the gameplay owner is destroyed.");
                Animator deathAnimator = detached[0].GetComponent<Animator>();
                Assert.That(deathAnimator.GetCurrentAnimatorStateInfo(0).IsName("Death") ||
                    (deathAnimator.IsInTransition(0) && deathAnimator.GetNextAnimatorStateInfo(0).IsName("Death")), Is.True);
                for (int frame = 0; frame < 120 && detached[0] != null; frame++) yield return null;
                Assert.That(detached[0] == null, Is.True, "Detached death geometry leaked beyond its presentation lifetime.");
            }
            Assert.That(Object.FindObjectsByType<StalkerRigDeathVisual>(FindObjectsSortMode.None), Is.Empty);
            Assert.That(Object.FindObjectsByType<KeyPickup>(FindObjectsSortMode.None).Length, Is.EqualTo(keyCount + 1));
            Assert.That(deaths, Is.EqualTo(1));
            Assert.That(objectiveDefeats, Is.EqualTo(objectiveDefeatsBefore + 1));
            Assert.That(RunCombatStats.EnemiesEliminated, Is.EqualTo(eliminations + 1));
        }
    }

    private IEnumerator RunDeepReburrowDeath()
    {
        Transform target = CreateScene();
        EnemyReactionRuntime.Apply(new GameFeelRuntimeOptions { EnemyReactionEnabled = true });
        EnemyReactionRuntime.ApplyUserPreferences(false, true);
        BossManager objective = new GameObject("Stalker deep reburrow objective").AddComponent<BossManager>();
        int objectiveDefeats = 0;
        objective.OnBossDefeated += () => objectiveDefeats++;
        GameObject boss = Spawn(target);
        GigaWormBehavior behavior = boss.GetComponent<GigaWormBehavior>();
        StalkerRigAnimation rig = boss.GetComponent<StalkerRigAnimation>();
        EnemyHealth health = boss.GetComponent<EnemyHealth>();
        SkinnedMeshRenderer skin = boss.GetComponentInChildren<SkinnedMeshRenderer>(true);
        Transform[] proxies = ReadTransforms(rig, "_collisionSegments");
        Transform animationRoot = rig.Animator.transform;
        BoneWeight[] weights = skin.sharedMesh.boneWeights;
        int[] tailVertices = Enumerable.Range(0, weights.Length)
            .Where(i => weights[i].boneIndex0 == 7 && weights[i].weight0 > 0.999f).ToArray();
        Assert.That(tailVertices, Is.Not.Empty, "The fixture must measure actual tail geometry.");
        yield return null;
        RegisterBossObjective(objective, health);
        BeginJump(behavior);
        Vector3 landing = Read<Vector3>(behavior, "_landingPoint");
        float bodyTop = float.PositiveInfinity;
        float tailTop = float.NegativeInfinity;
        Mesh baked = new();
        try
        {
            // Let the real AI, Animator and LateUpdate reach late burial. Killing at
            // Reburrow entry misses the regression because the head is still at ground level.
            bool reachedLateBurial = false;
            for (int frame = 0; frame < 1800; frame++)
            {
                yield return null;
                if (State(behavior) != "ReturnUnderground" || !skin.enabled) continue;
                skin.BakeMesh(baked, true);
                Vector3[] vertices = baked.vertices;
                bodyTop = vertices.Max(vertex => skin.transform.TransformPoint(vertex).y);
                tailTop = tailVertices.Max(i => skin.transform.TransformPoint(vertices[i]).y);
                if (skin.bones[0].position.y < landing.y - 10f &&
                    bodyTop < landing.y + 8f && tailTop > landing.y + 0.5f)
                {
                    reachedLateBurial = true;
                    break;
                }
            }
            Assert.That(reachedLateBurial, Is.True,
                "The live flight must reach a deeply buried head while the tail remains exposed.");
            Assert.That(State(behavior), Is.EqualTo("ReturnUnderground"));
            Assert.That(rig.CurrentPhase.ToString(), Is.EqualTo("Reburrow"));
            Assert.That(skin.bones[0].position.y, Is.LessThan(landing.y - 10f));
            Assert.That(tailTop, Is.GreaterThan(landing.y + 0.5f));
            Assert.That(health.IsInvincible, Is.False);
            AssertVisibility(skin, proxies, true);
            AssertProxyAlignment(skin.bones, proxies);
            Assert.That(objective.HasActiveBosses, Is.True);
            Assert.That(boss.GetComponentsInChildren<EnemyDeathFeedback>(true), Is.Empty);
            Assert.That(Object.FindObjectsByType<StalkerRigDeathVisual>(FindObjectsSortMode.None), Is.Empty);

            Vector3 deathPosition = animationRoot.position;
            Quaternion deathRotation = animationRoot.rotation;
            Vector3 deathScale = animationRoot.lossyScale;
            Vector3[] deathBonePositions = skin.bones.Select(bone => bone.position).ToArray();
            int bossId = boss.GetInstanceID();
            HashSet<int> existingKeys = new(Object.FindObjectsByType<KeyPickup>(FindObjectsSortMode.None)
                .Select(key => key.GetInstanceID()));
            int eliminations = RunCombatStats.EnemiesEliminated;
            int deaths = 0;
            health.OnDied += () => deaths++;

            health.ForceKill();
            health.ForceKill();

            KeyPickup[] spawnedKeys = Object.FindObjectsByType<KeyPickup>(FindObjectsSortMode.None)
                .Where(key => !existingKeys.Contains(key.GetInstanceID())).ToArray();
            Assert.That(spawnedKeys.Length, Is.EqualTo(1), "A repeated lethal callback must create exactly one key immediately.");
            Assert.That(spawnedKeys[0].transform.position.y, Is.GreaterThanOrEqualTo(landing.y),
                "The reward must remain accessible when the boss head is deep underground.");
            AssertVector(spawnedKeys[0].transform.position, landing + Vector3.up * 0.75f,
                "Reburrow must use the impact surface plus the existing pickup lift, not the submerged root.");
            Assert.That(deaths, Is.EqualTo(1));
            Assert.That(objective.HasActiveBosses, Is.False, "Visual death must not delay completing the boss objective.");
            Assert.That(objectiveDefeats, Is.EqualTo(1));
            Assert.That(RunCombatStats.EnemiesEliminated, Is.EqualTo(eliminations + 1));
            Assert.That(GenericDeathPending(bossId), Is.False);
            StalkerRigDeathVisual[] detached = Object.FindObjectsByType<StalkerRigDeathVisual>(FindObjectsSortMode.None);
            Assert.That(detached.Length, Is.EqualTo(1));
            Assert.That(detached[0].transform, Is.SameAs(animationRoot), "Keep the original visible rig, without a second health owner.");
            Assert.That(animationRoot.parent, Is.Null);
            AssertVector(animationRoot.position, deathPosition);
            Assert.That(Quaternion.Angle(animationRoot.rotation, deathRotation), Is.LessThan(0.05f));
            AssertVector(animationRoot.lossyScale, deathScale);
            for (int i = 0; i < skin.bones.Length; i++)
                AssertVector(skin.bones[i].position, deathBonePositions[i], "Death detached with a changed bone pose: " + i, 0.001f);
            Assert.That(animationRoot.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(animationRoot.GetComponentsInChildren<EnemyHealth>(true), Is.Empty);
            Assert.That(animationRoot.GetComponentsInChildren<GigaWormBehavior>(true), Is.Empty);
            Assert.That(skin.enabled, Is.True);
            TestContext.WriteLine("Late reburrow death: head " + deathBonePositions[0].y.ToString("F3") +
                " m, body top " + bodyTop.ToString("F3") + " m, tail top " + tailTop.ToString("F3") +
                " m; key " + spawnedKeys[0].transform.position + ".");

            yield return null;
            Assert.That(boss == null, Is.True, "Presentation must not defer normal gameplay destruction.");
            Assert.That(detached[0] != null, Is.True, "The exposed tail must retain its death presentation after the owner is destroyed.");
            for (int frame = 0; frame < 120 && detached[0] != null; frame++) yield return null;
            Assert.That(detached[0] == null, Is.True, "Detached death geometry leaked beyond its presentation lifetime.");
            Assert.That(Object.FindObjectsByType<StalkerRigDeathVisual>(FindObjectsSortMode.None), Is.Empty);
            Assert.That(Object.FindObjectsByType<KeyPickup>(FindObjectsSortMode.None).Length, Is.EqualTo(existingKeys.Count + 1));
            Assert.That(deaths, Is.EqualTo(1));
            Assert.That(objectiveDefeats, Is.EqualTo(1));
            Assert.That(RunCombatStats.EnemiesEliminated, Is.EqualTo(eliminations + 1));
        }
        finally { Object.Destroy(baked); }
    }

    private static Transform CreateScene()
    {
        Assert.That(Application.isPlaying, Is.True);
        Time.timeScale = 1f;
        Time.captureDeltaTime = 1f / 60f;
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Stalker rig test ground";
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(400f, 1f, 400f);
        Transform target = new GameObject("Stalker rig test target").transform;
        target.position = new Vector3(30f, 1f, 0f);
        Physics.SyncTransforms();
        return target;
    }

    private static GameObject Spawn(Transform target)
    {
        GameObject boss = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        SetPlayer(boss.GetComponent<GigaWormBehavior>(), target);
        return boss;
    }

    private static void BeginJump(GigaWormBehavior behavior)
    {
        typeof(GigaWormBehavior).GetField("_emergeOriginGround", PrivateInstance).SetValue(behavior, Vector3.zero);
        typeof(GigaWormBehavior).GetMethod("BeginJump", PrivateInstance).Invoke(behavior, null);
    }

    private static void RegisterBossObjective(BossManager manager, EnemyHealth health)
    {
        // Exercise the existing real death handler without starting a full Overheat/spawn-placement scenario.
        var active = (List<EnemyHealth>)typeof(BossManager).GetField("_activeBosses", PrivateInstance).GetValue(manager);
        var handlers = (Dictionary<EnemyHealth, Action>)typeof(BossManager)
            .GetField("_onBossDiedHandlers", PrivateInstance).GetValue(manager);
        MethodInfo onDied = typeof(BossManager).GetMethod("OnBossInstanceDied", PrivateInstance);
        Action handler = () => onDied.Invoke(manager, new object[] { health });
        active.Add(health);
        handlers.Add(health, handler);
        health.OnDied += handler;
    }

    private static void SetPlayer(GigaWormBehavior behavior, Transform player) =>
        typeof(EnemyBehaviorBase).GetField("_player", PrivateInstance).SetValue(behavior, player);
    private static string State(GigaWormBehavior behavior) => Read<object>(behavior, "_state").ToString();
    private static T Read<T>(GigaWormBehavior behavior, string name) =>
        (T)typeof(GigaWormBehavior).GetField(name, PrivateInstance).GetValue(behavior);
    private static Transform[] ReadTransforms(StalkerRigAnimation rig, string name) =>
        (Transform[])typeof(StalkerRigAnimation).GetField(name, PrivateInstance).GetValue(rig);
    private static bool GenericDeathPending(int id)
    {
        IDictionary pending = (IDictionary)typeof(EnemyDeathReactionVfx)
            .GetField("s_pending", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        return pending.Contains(id);
    }

    private static void AssertAnimatorState(StalkerRigAnimation rig, string name)
    {
        Animator animator = rig.Animator;
        Assert.That(animator.GetCurrentAnimatorStateInfo(0).IsName(name) ||
            (animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).IsName(name)), Is.True,
            "Runtime phase and Animator state disagree: " + name);
    }
    private static void AssertVisibility(SkinnedMeshRenderer skin, Transform[] proxies, bool visible)
    {
        Assert.That(skin.enabled, Is.EqualTo(visible));
        for (int i = 1; i < 8; i++) Assert.That(proxies[i].GetComponent<Collider>().enabled, Is.EqualTo(visible), "Body collider " + i);
    }
    private static void AssertProxyAlignment(Transform[] bones, Transform[] proxies)
    {
        for (int i = 1; i < 8; i++)
        {
            AssertVector(proxies[i].position, bones[i].position, "Proxy position " + i, 0.003f);
            Assert.That(Quaternion.Angle(proxies[i].rotation, bones[i].rotation), Is.LessThan(0.1f), "Proxy rotation " + i);
        }
    }
    private static void AssertBind(Transform[] bones, Pose[] bind)
    {
        for (int i = 0; i < 8; i++)
        {
            AssertVector(bones[i].localPosition, bind[i].position, "Reset position " + i);
            Assert.That(Quaternion.Angle(bones[i].localRotation, bind[i].rotation), Is.LessThan(0.05f), "Reset rotation " + i);
            AssertVector(bones[i].localScale, Vector3.one);
        }
    }
    private static void AssertVector(Vector3 actual, Vector3 expected, string message = null, float tolerance = 0.0002f) =>
        Assert.That(Vector3.Distance(actual, expected), Is.LessThan(tolerance), message);

    private static void SaveAndClearScene()
    {
        SessionState.SetString(SetupKey, JsonUtility.ToJson(new SavedSetup
        {
            Scenes = EditorSceneManager.GetSceneManagerSetup().Select(s => new SavedScene
                { Path = s.path, Loaded = s.isLoaded, Active = s.isActive }).ToArray()
        }));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    [UnityTearDown]
    public IEnumerator CleanupAndRestoreScene()
    {
        Time.timeScale = 1f;
        Time.captureDeltaTime = 0f;
        if (Application.isPlaying) yield return new ExitPlayMode();
        string json = SessionState.GetString(SetupKey, string.Empty);
        SessionState.EraseString(SetupKey);
        if (string.IsNullOrEmpty(json)) yield break;
        SavedSetup saved = JsonUtility.FromJson<SavedSetup>(json);
        SceneSetup[] setup = saved.Scenes.Where(s => !string.IsNullOrEmpty(s.Path)).Select(s =>
            new SceneSetup { path = s.Path, isLoaded = s.Loaded, isActive = s.Active }).ToArray();
        if (setup.Length > 0 && setup.Any(s => s.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup);
    }
}
