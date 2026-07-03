using NUnit.Framework;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public class WeaponUpgradeEffectTests
{
    [Test]
    public void DamageAmplifierStatus_IncreasesDamageAppliedThroughWeaponDamageApplier()
    {
        GameObject target = new("Target");
        var damageable = target.AddComponent<TestDamageable>();
        target.AddComponent<WeaponDamageAmplifierStatus>().Refresh(1.5f, 3f);

        bool applied = WeaponDamageApplier.TryApplyDamage(damageable, 10);

        Assert.That(applied, Is.True);
        Assert.That(damageable.LastDamage, Is.EqualTo(15));
        Object.DestroyImmediate(target);
    }

    [Test]
    public void RadialDamage_DamagesEachDamageableOnlyOnce()
    {
        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        target.transform.position = Vector3.zero;
        var damageable = target.AddComponent<TestDamageable>();

        int hits = WeaponRadialDamage.Apply(
            Vector3.zero,
            2f,
            12,
            falloff: 0f,
            knockback: 0f,
            maxTargets: 32);

        Assert.That(hits, Is.EqualTo(1));
        Assert.That(damageable.TotalDamage, Is.EqualTo(12));
        Object.DestroyImmediate(target);
    }

    [Test]
    public void RadialDamage_CanSuppressGenericExplosionVisualsForAreaEffects()
    {
        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        target.transform.position = Vector3.zero;
        var damageable = target.AddComponent<TestDamageable>();

        int hits = WeaponRadialDamage.Apply(
            Vector3.zero,
            2f,
            12,
            falloff: 0f,
            knockback: 0f,
            maxTargets: 32,
            showVfx: false);

        Assert.That(hits, Is.EqualTo(1));
        Assert.That(damageable.TotalDamage, Is.EqualTo(12));
        Object.DestroyImmediate(target);
        DestroyGeneratedVfx();
    }

    [Test]
    public void FlamethrowerBurnStatus_AppliesDamageThroughWeaponDamageApplier()
    {
        GameObject target = new("Burn Target");
        var damageable = target.AddComponent<TestDamageable>();
        target.AddComponent<WeaponDamageAmplifierStatus>().Refresh(1.5f, 3f);
        FlamethrowerBurnStatus burn = target.AddComponent<FlamethrowerBurnStatus>();
        burn.Refresh(damageable, damagePerTick: 10, duration: 3f, tickInterval: 0.5f);
        SetPrivateField(burn, "_tickTimer", 0f);

        InvokePrivate(burn, "Update");

        Assert.That(damageable.LastDamage, Is.EqualTo(15));
        Object.DestroyImmediate(target);
    }

    [Test]
    public void FlamethrowerManualHose_DamagesVisibleEnemyBodyAboveRegisteredRoot()
    {
        GameObject owner = new("Flamethrower Owner");
        GameObject spawn = new("Flamethrower Spawn");
        GameObject target = new("Sandbox Dummy Shape");
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        List<StatDefinition> statDefinitions = CreateDefaultStatDefinitions();

        try
        {
            spawn.transform.position = Vector3.up;
            spawn.transform.SetParent(owner.transform);
            target.transform.position = Vector3.forward * 2f;
            CapsuleCollider targetCollider = target.AddComponent<CapsuleCollider>();
            targetCollider.center = Vector3.up;
            targetCollider.height = 2f;
            targetCollider.radius = 0.5f;
            var damageable = target.AddComponent<TestDamageable>();
            EnemyRegistry.Register(target.transform);
            Physics.SyncTransforms();

            PlayerStats stats = owner.AddComponent<PlayerStats>();
            SetPrivateField(stats, "_statDefinitions", statDefinitions);
            InvokePrivate(stats, "Awake");

            data.WeaponId = "TestFlamethrower";
            data.DisplayName = "Test Flamethrower";
            data.WeaponType = WeaponType.Flamethrower;
            data.BaseDamage = 5f;
            data.BaseRange = 7f;
            data.BaseManualAmmo = 100f;
            data.EnsureSpecificTuningForCurrentType();
            data.Flamethrower.FlameHoseRadius = 0.75f;
            data.Flamethrower.FlameHoseSegmentCount = 4;
            data.Flamethrower.FlameHoseTurbulence = 0f;
            data.Flamethrower.FlameManualAmmoPerSecond = 0f;
            data.Flamethrower.FlameManualTickInterval = 0.01f;
            data.LevelData = new List<WeaponLevelData>
            {
                new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
            };

            WeaponInstance instance = new()
            {
                Data = data,
                Level = 1,
                SelectedPath = WeaponUpgradePath.None,
                State = WeaponState.Manual,
                CurrentAmmo = 100f
            };

            FlamethrowerWeapon weapon = new(null, null, spawn.transform, null);
            weapon.Setup(instance, owner.transform, stats, null);

            weapon.TickManual(0.02f, Vector3.forward, isFiring: true);

            Assert.That(damageable.TotalDamage, Is.GreaterThan(0));
        }
        finally
        {
            EnemyRegistry.Unregister(target.transform);
            DestroyGeneratedVfx();
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(data);
            for (int i = 0; i < statDefinitions.Count; i++)
                Object.DestroyImmediate(statDefinitions[i]);
        }
    }

    [Test]
    public void ProjectileExplosionDamageAmplifier_AffectsExplosionHitDamage()
    {
        GameObject projectileGo = new("Projectile");
        Projectile projectile = projectileGo.AddComponent<Projectile>();
        projectile.ConfigurePooled(3f, 10, 0f);
        projectile.ConfigureExplosion(2f, 0f);
        InvokePrivate(projectile, "ConfigureDamageAmplifierOnExplosion", 1.5f, 3f);

        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        target.transform.position = Vector3.forward;
        var damageable = target.AddComponent<TestDamageable>();
        Physics.SyncTransforms();

        InvokePrivate(projectile, "ApplyExplosionDamage");

        Assert.That(damageable.LastDamage, Is.EqualTo(15));
        Assert.That(target.GetComponent<WeaponDamageAmplifierStatus>(), Is.Not.Null);
        Object.DestroyImmediate(projectileGo);
        Object.DestroyImmediate(target);
        DestroyGeneratedVfx();
    }

    [Test]
    public void ProjectileFragmentCone_DamagesTargetsOnlyInsideForwardCone()
    {
        GameObject projectileGo = new("Projectile");
        Projectile projectile = projectileGo.AddComponent<Projectile>();
        projectile.ConfigurePooled(3f, 10, 0f);
        projectile.ConfigureExplosion(0.1f, 0f);
        InvokePrivate(projectile, "ConfigureFragmentCone", 45f, 3f, 0.5f);

        GameObject forwardTarget = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        forwardTarget.transform.position = Vector3.forward * 2f;
        var forwardDamageable = forwardTarget.AddComponent<TestDamageable>();

        GameObject sideTarget = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        sideTarget.transform.position = Vector3.right * 2f;
        var sideDamageable = sideTarget.AddComponent<TestDamageable>();
        Physics.SyncTransforms();

        InvokePrivate(projectile, "ApplyExplosionDamage");

        Assert.That(forwardDamageable.TotalDamage, Is.EqualTo(5));
        Assert.That(sideDamageable.TotalDamage, Is.Zero);
        Object.DestroyImmediate(projectileGo);
        Object.DestroyImmediate(forwardTarget);
        Object.DestroyImmediate(sideTarget);
        DestroyGeneratedVfx();
    }

    [Test]
    public void FlamethrowerFuelPuddle_TicksRadialDamage()
    {
        System.Type puddleType = typeof(Projectile).Assembly.GetType("FlamethrowerFuelPuddle");
        Assert.That(puddleType, Is.Not.Null, "Missing FlamethrowerFuelPuddle type.");

        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        target.transform.position = Vector3.zero;
        var damageable = target.AddComponent<TestDamageable>();
        Physics.SyncTransforms();

        MethodInfo spawn = puddleType.GetMethod("Spawn", BindingFlags.Static | BindingFlags.Public);
        Assert.That(spawn, Is.Not.Null, "Missing FlamethrowerFuelPuddle.Spawn.");
        object puddle = spawn.Invoke(null, new object[] { Vector3.zero, 2f, 7, 1f, 0.1f });

        InvokePrivate(puddle, "Update");

        Assert.That(damageable.TotalDamage, Is.EqualTo(7));
        if (puddle is Component component)
            Object.DestroyImmediate(component.gameObject);
        Object.DestroyImmediate(target);
        DestroyGeneratedVfx();
    }

    [Test]
    public void FlamethrowerFuelPuddle_SpawnUnderPlayerSitsAboveGround()
    {
        System.Type puddleType = typeof(Projectile).Assembly.GetType("FlamethrowerFuelPuddle");
        Assert.That(puddleType, Is.Not.Null, "Missing FlamethrowerFuelPuddle type.");

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.transform.position = Vector3.zero;
        floor.transform.localScale = new Vector3(10f, 0.12f, 10f);

        GameObject player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        player.transform.position = Vector3.up;
        player.AddComponent<PlayerStats>();
        Physics.SyncTransforms();

        MethodInfo spawn = puddleType.GetMethod("Spawn", BindingFlags.Static | BindingFlags.Public);
        Assert.That(spawn, Is.Not.Null, "Missing FlamethrowerFuelPuddle.Spawn.");
        object puddle = spawn.Invoke(null, new object[] { player.transform.position, 4f, 7, 1f, 0.1f });

        var component = puddle as Component;
        Assert.That(component, Is.Not.Null);
        Assert.That(component.transform.position.y, Is.GreaterThan(0.08f));

        Object.DestroyImmediate(component.gameObject);
        Object.DestroyImmediate(player);
        Object.DestroyImmediate(floor);
        DestroyGeneratedVfx();
    }

    [Test]
    public void WeaponTestingSandbox_TickWeapons_DecrementsManualAbilityCooldown()
    {
        GameObject sandboxGo = new("Sandbox Cooldown Test");
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        WeaponTestingSandboxManager sandbox = sandboxGo.AddComponent<WeaponTestingSandboxManager>();
        data.DisplayName = "Cooldown Test Weapon";

        try
        {
            WeaponInstance instance = new()
            {
                Data = data,
                State = WeaponState.Manual,
                CurrentAmmo = 10f,
                AbilityCooldownTimer = 1f
            };

            WeaponInstance[] instances = ReadField<WeaponInstance[]>(sandbox, "_instances");
            instances[0] = instance;
            SetPrivateField(sandbox, "_manualSlot", 0);

            InvokePrivate(sandbox, "TickWeapons", 0.25f);

            Assert.That(instance.AbilityCooldownTimer, Is.EqualTo(0.75f).Within(0.0001f));
        }
        finally
        {
            Object.DestroyImmediate(sandboxGo);
            Object.DestroyImmediate(data);
        }
    }

    [Test]
    public void FlamethrowerLiquidNitrogenActiveBurn_AppliesMovementFreezeStatus()
    {
        FlamethrowerWeapon weapon = CreateFlamethrowerWeapon(WeaponUpgradePath.PathB, out WeaponData data);
        GameObject target = new("Freeze Target");
        target.AddComponent<TestDamageable>();

        InvokePrivate(
            weapon,
            "ApplyBurnToTarget",
            target.transform,
            4,
            data.Flamethrower,
            true);

        Assert.That(target.GetComponent<WeaponMovementFreezeStatus>(), Is.Not.Null);
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(data);
    }

    [Test]
    public void FlamethrowerJellifiedFuel_DoublesBurnDuration()
    {
        FlamethrowerWeapon weapon = CreateFlamethrowerWeapon(WeaponUpgradePath.PathA, out WeaponData data);
        data.Flamethrower.FlameBurnDuration = 3f;

        float duration = InvokePrivate<float>(weapon, "GetPathAdjustedBurnDuration", data.Flamethrower);

        Assert.That(duration, Is.EqualTo(6f).Within(0.0001f));
        Object.DestroyImmediate(data);
    }

    [Test]
    public void FlamethrowerJellifiedFuelActivePuddle_UsesSpecRadiusAndDuration()
    {
        FlamethrowerWeapon weapon = CreateFlamethrowerWeapon(WeaponUpgradePath.PathA, out WeaponData data);
        data.Flamethrower.FlameBurnDuration = 3f;

        Vector2 settings = InvokePrivate<Vector2>(weapon, "GetJellifiedActivePuddleSettings", data.Flamethrower, 8f);

        Assert.That(settings.x, Is.EqualTo(4f).Within(0.0001f));
        Assert.That(settings.y, Is.EqualTo(6f).Within(0.0001f));
        Object.DestroyImmediate(data);
    }

    [Test]
    public void FlamethrowerAutomaticCone_LiquidNitrogenAppliesSlow()
    {
        GameObject owner = new("Auto Flamethrower Owner");
        GameObject spawn = new("Auto Flamethrower Spawn");
        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        List<StatDefinition> statDefinitions = CreateDefaultStatDefinitions();

        try
        {
            spawn.transform.SetParent(owner.transform);
            target.transform.position = Vector3.forward * 2f;
            var damageable = target.AddComponent<TestDamageable>();
            EnemyRegistry.Register(target.transform);
            Physics.SyncTransforms();

            PlayerStats stats = owner.AddComponent<PlayerStats>();
            SetPrivateField(stats, "_statDefinitions", statDefinitions);
            InvokePrivate(stats, "Awake");

            data.WeaponId = "TestFlamethrower";
            data.DisplayName = "Test Flamethrower";
            data.WeaponType = WeaponType.Flamethrower;
            data.BaseDamage = 5f;
            data.BaseRange = 6f;
            data.EnsureSpecificTuningForCurrentType();
            data.Flamethrower.FlameAutoConeAngle = 90f;
            data.Flamethrower.FlameAutoTickInterval = 0.01f;
            data.Flamethrower.FlameVisualDuration = 0.01f;
            data.LevelData = new List<WeaponLevelData>
            {
                new() { Level = 6, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
            };

            WeaponInstance instance = new()
            {
                Data = data,
                Level = 6,
                SelectedPath = WeaponUpgradePath.PathB,
                State = WeaponState.Automatic
            };

            FlamethrowerWeapon weapon = new(null, null, spawn.transform, null);
            weapon.Setup(instance, owner.transform, stats, null);

            weapon.TickAutomatic(0.02f, Vector3.forward);

            Assert.That(damageable.TotalDamage, Is.GreaterThan(0));
            Assert.That(target.GetComponent<WeaponMovementSlowStatus>(), Is.Not.Null);
        }
        finally
        {
            EnemyRegistry.Unregister(target.transform);
            DestroyGeneratedVfx();
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(data);
            for (int i = 0; i < statDefinitions.Count; i++)
                Object.DestroyImmediate(statDefinitions[i]);
        }
    }

    [Test]
    public void WeaponMovementSlowStatus_AppliesAndRefreshesStrongestSlow()
    {
        GameObject target = new("Slow Target");
        target.AddComponent<TestDamageable>();

        WeaponMovementSlowStatus.Apply(target.transform, 0.5f, 3f, "Liquid Nitrogen");
        WeaponMovementSlowStatus.Apply(target.transform, 0.1f, 1f, "Deep Freeze");

        WeaponMovementSlowStatus status = target.GetComponent<WeaponMovementSlowStatus>();
        Assert.That(status, Is.Not.Null);
        Assert.That(status.SpeedMultiplier, Is.EqualTo(0.1f).Within(0.0001f));
        Object.DestroyImmediate(target);
    }

    [Test]
    public void FlamethrowerLiquidNitrogenBurn_RampsSlowToNinetyPercent()
    {
        FlamethrowerWeapon weapon = CreateFlamethrowerWeapon(WeaponUpgradePath.PathB, out WeaponData data);
        GameObject target = new("Nitrogen Ramp Target");
        target.AddComponent<TestDamageable>();

        for (int i = 0; i < 6; i++)
        {
            InvokePrivate(
                weapon,
                "ApplyBurnToTarget",
                target.transform,
                4,
                data.Flamethrower,
                false);
        }

        WeaponMovementSlowStatus slow = target.GetComponent<WeaponMovementSlowStatus>();
        Assert.That(slow, Is.Not.Null);
        Assert.That(slow.SpeedMultiplier, Is.EqualTo(0.1f).Within(0.0001f));
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(data);
    }

    [Test]
    public void FlamethrowerLiquidNitrogenActiveBurn_AppliesPostFreezeSlow()
    {
        FlamethrowerWeapon weapon = CreateFlamethrowerWeapon(WeaponUpgradePath.PathB, out WeaponData data);
        GameObject target = new("Freeze Slow Target");
        target.AddComponent<TestDamageable>();

        InvokePrivate(
            weapon,
            "ApplyBurnToTarget",
            target.transform,
            4,
            data.Flamethrower,
            true);

        WeaponMovementSlowStatus slow = target.GetComponent<WeaponMovementSlowStatus>();
        Assert.That(slow, Is.Not.Null);
        Assert.That(slow.SpeedMultiplier, Is.EqualTo(0.1f).Within(0.0001f));
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(data);
    }

    [Test]
    public void MortarUpgradePayload_ReflectsSelectedPath()
    {
        MortarWeapon grapeshotWeapon = CreateMortarWeapon(WeaponUpgradePath.PathA, out WeaponData grapeshotData);
        object grapeshotPayload = InvokePrivate<object>(grapeshotWeapon, "GetUpgradePayload", true);

        Assert.That(ReadField<bool>(grapeshotPayload, "UseGrapeshot"), Is.True);
        Assert.That(ReadField<int>(grapeshotPayload, "GrapeshotCount"), Is.EqualTo(15));
        Assert.That(ReadField<float>(grapeshotPayload, "GrapeshotDamageScale"), Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(ReadField<int>(grapeshotPayload, "RepeatExplosionCount"), Is.EqualTo(1));
        Assert.That(InvokePrivate<int>(grapeshotWeapon, "GetGrapeshotRainShellCount", grapeshotData.Mortar), Is.EqualTo(50));

        MortarWeapon repeatWeapon = CreateMortarWeapon(WeaponUpgradePath.PathB, out WeaponData repeatData);
        object repeatPayload = InvokePrivate<object>(repeatWeapon, "GetUpgradePayload", false);

        Assert.That(ReadField<bool>(repeatPayload, "UseGrapeshot"), Is.False);
        Assert.That(ReadField<int>(repeatPayload, "RepeatExplosionCount"), Is.EqualTo(3));
        Assert.That(ReadField<float>(repeatPayload, "RepeatExplosionDelay"), Is.EqualTo(2f).Within(0.0001f));
        Assert.That(
            InvokePrivate<float>(repeatWeapon, "GetActiveShellTravelTime", repeatData.Mortar, 0),
            Is.EqualTo(InvokePrivate<float>(repeatWeapon, "GetActiveShellTravelTime", repeatData.Mortar, 4)).Within(0.0001f));

        Object.DestroyImmediate(grapeshotData);
        Object.DestroyImmediate(repeatData);
    }

    [Test]
    public void MortarPayload_RepeatsExplosionDamage()
    {
        System.Type payloadType = typeof(MortarShellImpact).Assembly.GetType("MortarUpgradePayload");
        Assert.That(payloadType, Is.Not.Null, "Missing MortarUpgradePayload type.");
        object payload = System.Activator.CreateInstance(
            payloadType,
            false,
            0,
            0f,
            0f,
            2,
            0.01f);

        MethodInfo launch = typeof(MortarShellImpact).GetMethod(
            "Launch",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new[]
            {
                typeof(Vector3),
                typeof(Vector3),
                typeof(float),
                typeof(float),
                typeof(int),
                typeof(float),
                typeof(float),
                typeof(float),
                typeof(float),
                typeof(Transform),
                payloadType
            },
            null);
        Assert.That(launch, Is.Not.Null, "Missing MortarShellImpact.Launch overload with payload.");

        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        target.transform.position = Vector3.zero;
        var damageable = target.AddComponent<TestDamageable>();
        Physics.SyncTransforms();

        object shell = launch.Invoke(
            null,
            new object[] { Vector3.back, Vector3.zero, 1f, 0f, 10, 2f, 0f, 0f, 0.1f, null, payload });

        InvokePrivateWithSignature(shell, "Detonate", new[] { typeof(Vector3) }, Vector3.zero);
        Assert.That(damageable.TotalDamage, Is.EqualTo(10));

        SetPrivateField(shell, "_repeatExplosionTimer", 0f);
        InvokePrivate(shell, "TickRepeatExplosions");

        Assert.That(damageable.TotalDamage, Is.EqualTo(20));
        if (shell is Component component)
            Object.DestroyImmediate(component.gameObject);
        Object.DestroyImmediate(target);
        DestroyGeneratedVfx();
    }

    [Test]
    public void WeaponUpgradeVfx_SpawnsRingBeamAndConeEffects()
    {
        System.Type vfxType = typeof(Projectile).Assembly.GetType("WeaponUpgradeVfx");
        Assert.That(vfxType, Is.Not.Null, "Missing WeaponUpgradeVfx type.");

        MethodInfo spawnRing = vfxType.GetMethod(
            "SpawnRing",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new[] { typeof(Vector3), typeof(float), typeof(Color), typeof(float), typeof(float), typeof(string) },
            null);
        MethodInfo spawnBeam = vfxType.GetMethod(
            "SpawnBeam",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new[] { typeof(Vector3), typeof(Vector3), typeof(Color), typeof(float), typeof(float), typeof(string) },
            null);
        MethodInfo spawnCone = vfxType.GetMethod(
            "SpawnCone",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new[] { typeof(Vector3), typeof(Vector3), typeof(float), typeof(float), typeof(Color), typeof(float), typeof(int), typeof(string) },
            null);

        Assert.That(spawnRing, Is.Not.Null, "Missing WeaponUpgradeVfx.SpawnRing.");
        Assert.That(spawnBeam, Is.Not.Null, "Missing WeaponUpgradeVfx.SpawnBeam.");
        Assert.That(spawnCone, Is.Not.Null, "Missing WeaponUpgradeVfx.SpawnCone.");

        object ring = spawnRing.Invoke(null, new object[] { Vector3.zero, 2f, Color.cyan, 0.2f, 1.25f, "RING" });
        object beam = spawnBeam.Invoke(null, new object[] { Vector3.zero, Vector3.forward * 3f, Color.yellow, 0.2f, 0.1f, "BEAM" });
        object cone = spawnCone.Invoke(null, new object[] { Vector3.zero, Vector3.forward, 4f, 45f, Color.magenta, 0.2f, 5, "CONE" });

        Assert.That(ring, Is.TypeOf(vfxType));
        Assert.That(beam, Is.TypeOf(vfxType));
        Assert.That(cone, Is.TypeOf(vfxType));
        if (ring is Component ringComponent)
            Assert.That(ringComponent.transform.position.y, Is.GreaterThan(0.05f));

        DestroyIfComponent(ring);
        DestroyIfComponent(beam);
        DestroyIfComponent(cone);
    }

    [Test]
    public void SandboxPathSelection_AutoPromotesAdvancedPathToLevelSix()
    {
        GameObject go = new("Sandbox Manager");
        WeaponTestingSandboxManager sandbox = go.AddComponent<WeaponTestingSandboxManager>();
        FieldInfo instancesField = typeof(WeaponTestingSandboxManager).GetField("_instances", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(instancesField, Is.Not.Null, "Missing WeaponTestingSandboxManager._instances.");

        WeaponInstance[] instances = (WeaponInstance[])instancesField.GetValue(sandbox);
        instances[0] = new WeaponInstance
        {
            Level = 1,
            SelectedPath = WeaponUpgradePath.None,
            State = WeaponState.Automatic
        };

        sandbox.ApplyWeaponLevelAndPath(0, 1, WeaponUpgradePath.PathA);

        Assert.That(instances[0].Level, Is.EqualTo(6));
        Assert.That(instances[0].SelectedPath, Is.EqualTo(WeaponUpgradePath.PathA));
        Object.DestroyImmediate(go);
        DestroyGeneratedVfx();
    }

    [Test]
    public void WeaponUpgradeVfx_TargetPulseFollowsTarget()
    {
        System.Type vfxType = typeof(Projectile).Assembly.GetType("WeaponUpgradeVfx");
        Assert.That(vfxType, Is.Not.Null, "Missing WeaponUpgradeVfx type.");

        MethodInfo spawnPulse = vfxType.GetMethod(
            "SpawnTargetPulse",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new[] { typeof(Transform), typeof(Color), typeof(float), typeof(string) },
            null);
        Assert.That(spawnPulse, Is.Not.Null, "Missing WeaponUpgradeVfx.SpawnTargetPulse.");

        GameObject target = new("Pulse Target");
        target.transform.position = new Vector3(2f, 0f, 3f);
        object pulse = spawnPulse.Invoke(null, new object[] { target.transform, Color.white, 0.2f, "PULSE" });

        Assert.That(pulse, Is.TypeOf(vfxType));
        if (pulse is Component component)
            Assert.That(component.transform.position.x, Is.EqualTo(target.transform.position.x).Within(0.001f));

        DestroyIfComponent(pulse);
        Object.DestroyImmediate(target);
    }

    private sealed class TestDamageable : MonoBehaviour, IDamageable
    {
        public int LastDamage { get; private set; }
        public int TotalDamage { get; private set; }

        public bool ApplyDamage(int amount)
        {
            LastDamage = amount;
            TotalDamage += amount;
            return true;
        }
    }

    private static List<StatDefinition> CreateDefaultStatDefinitions()
    {
        return new List<StatDefinition>
        {
            CreateDefinition(StatType.DamageMultiplier, 1f),
            CreateDefinition(StatType.EliteDamageMultiplier, 1f),
            CreateDefinition(StatType.CriticalChance, 0f),
            CreateDefinition(StatType.CriticalDamage, 2f),
            CreateDefinition(StatType.AttackSpeedMultiplier, 1f),
            CreateDefinition(StatType.AmmoMultiplier, 1f),
            CreateDefinition(StatType.Knockback, 1f),
            CreateDefinition(StatType.ProjectileAreaSize, 1f),
            CreateDefinition(StatType.AbilityDamageMultiplier, 1f),
            CreateDefinition(StatType.AbilityCooldownReduction, 0f)
        };
    }

    private static StatDefinition CreateDefinition(StatType type, float baseValue)
    {
        StatDefinition definition = ScriptableObject.CreateInstance<StatDefinition>();
        SetPrivateField(definition, "<StatType>k__BackingField", type);
        SetPrivateField(definition, "<Category>k__BackingField", StatCategory.Offensive);
        SetPrivateField(definition, "<BaseValue>k__BackingField", baseValue);
        SetPrivateField(definition, "<UpgradeableByLevel>k__BackingField", false);
        SetPrivateField(definition, "<UpgradeableByItems>k__BackingField", false);
        SetPrivateField(definition, "<LevelUpgradeBaseAmount>k__BackingField", 0f);
        SetPrivateField(definition, "<IsPercentage>k__BackingField", false);
        SetPrivateField(definition, "<IsInteger>k__BackingField", false);
        return definition;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing field {fieldName} on {target.GetType().Name}");
        field.SetValue(target, value);
    }

    private static void InvokePrivate(object target, string methodName)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, $"Missing method {methodName} on {target.GetType().Name}");
        method.Invoke(target, null);
    }

    private static void InvokePrivate(object target, string methodName, params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.That(method, Is.Not.Null, $"Missing method {methodName} on {target.GetType().Name}");
        method.Invoke(target, arguments);
    }

    private static T InvokePrivate<T>(object target, string methodName, params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.That(method, Is.Not.Null, $"Missing method {methodName} on {target.GetType().Name}");
        return (T)method.Invoke(target, arguments);
    }

    private static void InvokePrivateWithSignature(object target, string methodName, System.Type[] parameterTypes, params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            null,
            parameterTypes,
            null);
        Assert.That(method, Is.Not.Null, $"Missing method {methodName} on {target.GetType().Name}");
        method.Invoke(target, arguments);
    }

    private static T ReadField<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing field {fieldName} on {target.GetType().Name}");
        return (T)field.GetValue(target);
    }

    private static void DestroyGeneratedVfx()
    {
        foreach (ExplosionRadiusVfx vfx in Object.FindObjectsByType<ExplosionRadiusVfx>(FindObjectsSortMode.None))
            Object.DestroyImmediate(vfx.gameObject);

        System.Type upgradeVfxType = typeof(Projectile).Assembly.GetType("WeaponUpgradeVfx");
        if (upgradeVfxType == null)
            return;

        foreach (Object vfx in Object.FindObjectsByType(upgradeVfxType, FindObjectsSortMode.None))
        {
            if (vfx is Component component)
                Object.DestroyImmediate(component.gameObject);
        }

        foreach (FlamethrowerStreamVfx vfx in Object.FindObjectsByType<FlamethrowerStreamVfx>(FindObjectsSortMode.None))
            Object.DestroyImmediate(vfx.gameObject);

        foreach (WeaponStatusShardVfx vfx in Object.FindObjectsByType<WeaponStatusShardVfx>(FindObjectsSortMode.None))
            Object.DestroyImmediate(vfx.gameObject);

        foreach (FlamethrowerFuelPuddle puddle in Object.FindObjectsByType<FlamethrowerFuelPuddle>(FindObjectsSortMode.None))
            Object.DestroyImmediate(puddle.gameObject);
    }

    private static void DestroyIfComponent(object value)
    {
        if (value is Component component && component != null)
            Object.DestroyImmediate(component.gameObject);
    }

    private static FlamethrowerWeapon CreateFlamethrowerWeapon(WeaponUpgradePath path, out WeaponData data)
    {
        data = ScriptableObject.CreateInstance<WeaponData>();
        data.WeaponId = "TestFlamethrower";
        data.DisplayName = "Test Flamethrower";
        data.WeaponType = WeaponType.Flamethrower;
        data.BaseDamage = 10f;
        data.BaseAttackRate = 1f;
        data.BaseManualAmmo = 100f;
        data.EnsureSpecificTuningForCurrentType();
        data.LevelData = new List<WeaponLevelData>
        {
            new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
            new() { Level = 6, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
        };

        WeaponInstance instance = new()
        {
            Data = data,
            Level = 6,
            SelectedPath = path,
            State = WeaponState.Manual
        };

        FlamethrowerWeapon weapon = new(null, null, null, null);
        weapon.Setup(instance, null, null, null);
        return weapon;
    }

    private static MortarWeapon CreateMortarWeapon(WeaponUpgradePath path, out WeaponData data)
    {
        data = ScriptableObject.CreateInstance<WeaponData>();
        data.WeaponId = "TestMortar";
        data.DisplayName = "Test Mortar";
        data.WeaponType = WeaponType.Mortar;
        data.BaseDamage = 10f;
        data.BaseAttackRate = 1f;
        data.BaseManualAmmo = 100f;
        data.EnsureSpecificTuningForCurrentType();
        data.LevelData = new List<WeaponLevelData>
        {
            new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
            new() { Level = 6, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
        };

        WeaponInstance instance = new()
        {
            Data = data,
            Level = 6,
            SelectedPath = path,
            State = WeaponState.Manual
        };

        MortarWeapon weapon = new(null, null, null);
        weapon.Setup(instance, null, null, null);
        return weapon;
    }
}
