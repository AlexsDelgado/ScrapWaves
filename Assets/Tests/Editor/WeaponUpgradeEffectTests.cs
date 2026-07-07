using NUnit.Framework;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

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
    public void ExplosionRadiusVfx_CanSpawnWithCustomColor()
    {
        MethodInfo spawn = typeof(ExplosionRadiusVfx).GetMethod(
            "Spawn",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new[] { typeof(Vector3), typeof(float), typeof(Color) },
            null);
        Assert.That(spawn, Is.Not.Null, "Missing ExplosionRadiusVfx.Spawn overload with Color.");

        Color pink = new(1f, 0.1f, 0.72f, 0.95f);
        spawn.Invoke(null, new object[] { Vector3.zero, 2f, pink });

        ExplosionRadiusVfx vfx = Object.FindAnyObjectByType<ExplosionRadiusVfx>();
        Assert.That(vfx, Is.Not.Null);

        LineRenderer line = vfx.GetComponentInChildren<LineRenderer>();
        Assert.That(line, Is.Not.Null);
        Assert.That(line.startColor.r, Is.EqualTo(pink.r).Within(0.001f));
        Assert.That(line.startColor.g, Is.EqualTo(pink.g).Within(0.001f));
        Assert.That(line.startColor.b, Is.EqualTo(pink.b).Within(0.001f));
        DestroyGeneratedVfx();
    }

    [Test]
    public void RotatingBladeVfx_ShowOrbit_DrawsBladeAlongRadius()
    {
        RotatingBladeVfx vfx = RotatingBladeVfx.Create();
        try
        {
            vfx.ShowOrbit(Vector3.zero, Vector3.forward * 3f, 0.5f, 1f);

            List<LineRenderer> bladeLines = GetEnabledRotatingBladeContactLines(vfx);
            Assert.That(bladeLines.Count, Is.EqualTo(1));

            Vector3 bladeDelta = bladeLines[0].GetPosition(1) - bladeLines[0].GetPosition(0);
            bladeDelta.y = 0f;

            Assert.That(Mathf.Abs(bladeDelta.z), Is.GreaterThan(Mathf.Abs(bladeDelta.x) * 4f));
        }
        finally
        {
            Object.DestroyImmediate(vfx.gameObject);
        }
    }

    [Test]
    public void RotatingBladeVfx_ShowOrbit_KeepsMultipleBladeLinesInSameFrame()
    {
        RotatingBladeVfx vfx = RotatingBladeVfx.Create();
        try
        {
            vfx.ShowOrbit(Vector3.zero, Vector3.forward * 3f, 0.5f, 1f);
            vfx.ShowOrbit(Vector3.zero, Vector3.back * 3f, 0.5f, 1f);

            List<LineRenderer> bladeLines = GetEnabledRotatingBladeContactLines(vfx);

            Assert.That(bladeLines.Count, Is.EqualTo(2));
        }
        finally
        {
            Object.DestroyImmediate(vfx.gameObject);
        }
    }

    [Test]
    public void RotatingBlade_MultiBladeVfxColor_IsPeach()
    {
        Color color = ReadStaticField<Color>(typeof(RotatingBladeWeapon), "MultiBladeVfxColor");

        AssertPeach(color);
    }

    [Test]
    public void RotatingBlade_AtomicSharpnessVfxColor_IsDarkPurple()
    {
        Color color = ReadStaticField<Color>(typeof(RotatingBladeWeapon), "AtomicSharpnessVfxColor");

        AssertDarkPurple(color);
    }

    [Test]
    public void Projectile_FragmentationVfxColors_AreDarkRed()
    {
        Color fragmentColor = ReadStaticField<Color>(typeof(Projectile), "FragmentVfxColor");
        Color clusterColor = ReadStaticField<Color>(typeof(Projectile), "ClusterVfxColor");

        AssertDarkRed(fragmentColor);
        AssertDarkRed(clusterColor);
    }

    [Test]
    public void WeaponDamageAmplifierStatus_SpawnsVulnerableAura()
    {
        System.Type auraType = typeof(Projectile).Assembly.GetType("WeaponStatusAuraVfx");
        Assert.That(auraType, Is.Not.Null, "Missing WeaponStatusAuraVfx type.");

        GameObject target = new("Vulnerable Target");
        var damageable = target.AddComponent<TestDamageable>();

        WeaponDamageAmplifierStatus.Apply(damageable, 1.5f, 3f);

        Object aura = Object.FindAnyObjectByType(auraType);
        Assert.That(aura, Is.Not.Null);
        if (aura is Component component)
            Assert.That(component.transform.position, Is.EqualTo(target.transform.position));

        Object.DestroyImmediate(target);
        DestroyGeneratedVfx();
    }

    [Test]
    public void WeaponDamageAmplifierStatus_DismissesVulnerableAuraWhenTargetDisabled()
    {
        System.Type auraType = typeof(Projectile).Assembly.GetType("WeaponStatusAuraVfx");
        Assert.That(auraType, Is.Not.Null, "Missing WeaponStatusAuraVfx type.");

        GameObject target = new("Disabled Vulnerable Target");
        var damageable = target.AddComponent<TestDamageable>();

        WeaponDamageAmplifierStatus.Apply(damageable, 1.5f, 3f);
        Assert.That(Object.FindAnyObjectByType(auraType), Is.Not.Null);

        target.SetActive(false);

        Assert.That(Object.FindAnyObjectByType(auraType), Is.Null);

        Object.DestroyImmediate(target);
        DestroyGeneratedVfx();
    }

    [Test]
    public void PlayerInvulnerability_IgnoresEnemyCollisionUntilIFramesExpire()
    {
        GameObject player = new("Invulnerable Player");
        GameObject enemy = new("Collision Enemy");

        try
        {
            CapsuleCollider playerCollider = player.AddComponent<CapsuleCollider>();
            PlayerHealth health = player.AddComponent<PlayerHealth>();
            CapsuleCollider enemyCollider = enemy.AddComponent<CapsuleCollider>();
            EnemyRegistry.Register(enemy.transform);
            Physics.SyncTransforms();

            health.GrantInvulnerability(1f);
            InvokePrivate(health, "Update");

            Assert.That(Physics.GetIgnoreCollision(playerCollider, enemyCollider), Is.True);

            SetPrivateField(health, "_invulnerableUntil", Time.time - 0.1f);
            InvokePrivate(health, "Update");

            Assert.That(Physics.GetIgnoreCollision(playerCollider, enemyCollider), Is.False);
        }
        finally
        {
            EnemyRegistry.Unregister(enemy.transform);
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(enemy);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void ExplosiveProjectile_DetonatesWhenFastMovementSweepsThroughGround()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject rocket = new("Fast Rocket");

        try
        {
            floor.transform.position = Vector3.zero;
            floor.transform.localScale = new Vector3(10f, 0.12f, 10f);

            rocket.transform.position = Vector3.up * 2f;
            rocket.AddComponent<Rigidbody>();
            SphereCollider sphere = rocket.AddComponent<SphereCollider>();
            sphere.radius = 0.08f;
            Projectile projectile = rocket.AddComponent<Projectile>();
            InvokePrivate(projectile, "Awake");

            projectile.ConfigurePooled(5f, 10, 0f);
            projectile.Launch(Vector3.down);
            projectile.ConfigureExplosion(1.5f, 0f);
            projectile.ConfigureSpeedMultiplier(10f);
            Physics.SyncTransforms();

            InvokePrivate(projectile, "FixedUpdate");

            Assert.That(ReadField<bool>(projectile, "_consumed"), Is.True);
            Assert.That(Object.FindObjectsByType<ExplosionRadiusVfx>(FindObjectsSortMode.None), Has.Length.EqualTo(1));
        }
        finally
        {
            Object.DestroyImmediate(floor);
            Object.DestroyImmediate(rocket);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void Projectile_SweptCollisionAgainstDamageable_AppliesDamage()
    {
        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        GameObject bullet = new("Fast Cannon Bullet");

        try
        {
            target.transform.position = Vector3.forward;
            var damageable = target.AddComponent<TestDamageable>();

            bullet.transform.position = Vector3.zero;
            bullet.AddComponent<Rigidbody>();
            SphereCollider sphere = bullet.AddComponent<SphereCollider>();
            sphere.radius = 0.08f;
            Projectile projectile = bullet.AddComponent<Projectile>();
            InvokePrivate(projectile, "Awake");

            projectile.ConfigurePooled(5f, 10, 0f);
            projectile.Launch(Vector3.forward);
            Physics.SyncTransforms();

            bool consumed = InvokePrivate<bool>(projectile, "TryConsumeSweptWorldCollision", Vector3.zero, Vector3.forward * 2f);

            Assert.That(consumed, Is.True);
            Assert.That(ReadField<bool>(projectile, "_consumed"), Is.True);
            Assert.That(damageable.TotalDamage, Is.EqualTo(10));
        }
        finally
        {
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(bullet);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void EnemyRegistryAimPoint_UsesBodyColliderCenterInsteadOfGroundRoot()
    {
        GameObject target = new("Tall Target");

        try
        {
            target.transform.position = Vector3.zero;
            CapsuleCollider body = target.AddComponent<CapsuleCollider>();
            body.center = Vector3.up;
            body.height = 2f;
            body.radius = 0.5f;

            Vector3 aimPoint = EnemyRegistry.GetAimPoint(target.transform);

            Assert.That(aimPoint.y, Is.EqualTo(body.bounds.center.y).Within(0.0001f));
            Assert.That(aimPoint.y, Is.GreaterThan(target.transform.position.y + 0.5f));
        }
        finally
        {
            Object.DestroyImmediate(target);
        }
    }

    [Test]
    public void AutomaticCannonContinuousFire_DoesNotSpawnOrangePathBeam()
    {
        GameObject spawn = new("Cannon Spawn");
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();

        try
        {
            data.WeaponId = "TestAutomaticCannon";
            data.DisplayName = "Test Automatic Cannon";
            data.WeaponType = WeaponType.AutomaticCannon;
            data.BaseDamage = 10f;
            data.BaseAttackRate = 1f;
            data.BaseRange = 12f;
            data.BaseManualAmmo = 400f;
            data.EnsureSpecificTuningForCurrentType();
            data.LevelData = new List<WeaponLevelData>
            {
                new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
                new() { Level = 6, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
            };
            data.PathA = new WeaponUpgradePathData { PathName = "Continuous Fire", DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoOverride = 400f };
            data.PathB = new WeaponUpgradePathData { PathName = "Head Hunter", DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoOverride = 40f };

            WeaponInstance instance = new()
            {
                Data = data,
                Level = 6,
                SelectedPath = WeaponUpgradePath.PathA,
                State = WeaponState.Manual,
                CurrentAmmo = 400f
            };

            AutomaticCannonWeapon weapon = new(null, null, spawn.transform);
            weapon.Setup(instance, null, null, null);

            InvokePrivate(
                weapon,
                "FireLineBurst",
                Vector3.forward,
                1,
                1f,
                0f,
                0f,
                0f,
                false);

            System.Type upgradeVfxType = typeof(Projectile).Assembly.GetType("WeaponUpgradeVfx");
            Assert.That(upgradeVfxType, Is.Not.Null, "Missing WeaponUpgradeVfx type.");
            Assert.That(Object.FindObjectsByType(upgradeVfxType, FindObjectsSortMode.None), Has.Length.EqualTo(0));
        }
        finally
        {
            Object.DestroyImmediate(spawn);
            Object.DestroyImmediate(data);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void AutomaticCannonHeadHunter_SpawnsProjectileVisualWithoutBlueBeam()
    {
        GameObject spawn = new("Head Hunter Spawn");
        GameObject poolGo = new("Head Hunter Pool");
        GameObject poolContainer = new("Head Hunter Pool Container");
        GameObject prefab = new("Head Hunter Projectile Prefab");
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();

        try
        {
            prefab.AddComponent<Rigidbody>();
            prefab.AddComponent<SphereCollider>();
            prefab.AddComponent<Projectile>();
            prefab.SetActive(false);

            poolGo.SetActive(false);
            ProjectilePool pool = poolGo.AddComponent<ProjectilePool>();
            SetPrivateField(pool, "_projectilePrefab", prefab);
            SetPrivateField(pool, "_container", poolContainer.transform);
            SetPrivateField(pool, "_initialPoolSize", 1);
            SetPrivateField(pool, "_maxPoolSize", 2);
            SetPrivateField(pool, "_allowPoolGrowth", true);
            poolGo.SetActive(true);

            data.WeaponId = "TestAutomaticCannon";
            data.DisplayName = "Test Automatic Cannon";
            data.WeaponType = WeaponType.AutomaticCannon;
            data.BaseDamage = 10f;
            data.BaseAttackRate = 1f;
            data.BaseRange = 12f;
            data.BaseManualAmmo = 40f;
            data.EnsureSpecificTuningForCurrentType();
            data.LevelData = new List<WeaponLevelData>
            {
                new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
                new() { Level = 6, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
            };
            data.PathB = new WeaponUpgradePathData { PathName = "Head Hunter", DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoOverride = 40f };

            WeaponInstance instance = new()
            {
                Data = data,
                Level = 6,
                SelectedPath = WeaponUpgradePath.PathB,
                State = WeaponState.Manual,
                CurrentAmmo = 40f
            };

            AutomaticCannonWeapon weapon = new(null, pool, spawn.transform);
            weapon.Setup(instance, null, null, null);

            InvokePrivate(
                weapon,
                "FireHeadHunterPiercingLine",
                Vector3.forward,
                10,
                12f,
                false,
                false,
                null);

            Assert.That(Object.FindObjectsByType<WeaponUpgradeVfx>(FindObjectsSortMode.None), Is.Empty);
            Assert.That(pool.ActiveLeasedCount, Is.EqualTo(1));

            Projectile activeProjectile = null;
            foreach (Projectile candidate in Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None))
            {
                if (candidate.gameObject != prefab && candidate.gameObject.activeSelf)
                {
                    activeProjectile = candidate;
                    break;
                }
            }

            Assert.That(activeProjectile, Is.Not.Null);
            Assert.That(ReadField<bool>(activeProjectile, "_visualOnly"), Is.True);
            Assert.That(ReadField<float>(activeProjectile, "_maxTravelDistance"), Is.Zero);
        }
        finally
        {
            Object.DestroyImmediate(spawn);
            Object.DestroyImmediate(poolGo);
            Object.DestroyImmediate(poolContainer);
            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(data);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void ProjectileVisualOnly_DoesNotApplyCollisionDamage()
    {
        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        GameObject bullet = new("Visual Only Bullet");

        try
        {
            target.transform.position = Vector3.forward;
            var damageable = target.AddComponent<TestDamageable>();

            bullet.transform.position = Vector3.zero;
            bullet.AddComponent<Rigidbody>();
            bullet.AddComponent<SphereCollider>();
            Projectile projectile = bullet.AddComponent<Projectile>();
            InvokePrivate(projectile, "Awake");

            projectile.ConfigureVisualOnly(5f);
            projectile.Launch(Vector3.forward);
            Physics.SyncTransforms();

            bool consumed = InvokePrivate<bool>(projectile, "TryConsumeSweptWorldCollision", Vector3.zero, Vector3.forward * 2f);

            Assert.That(consumed, Is.True);
            Assert.That(damageable.TotalDamage, Is.Zero);
        }
        finally
        {
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(bullet);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void RocketLauncherManualRocket_IgnoresAreaSizeForTravelRangeButScalesExplosion()
    {
        GameObject owner = new("Rocket Owner");
        GameObject spawn = new("Rocket Spawn");
        GameObject poolGo = new("Rocket Pool");
        GameObject poolContainer = new("Rocket Pool Container");
        GameObject prefab = new("Rocket Projectile Prefab");
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        List<StatDefinition> statDefinitions = CreateDefaultStatDefinitions(projectileAreaSize: 2f);

        try
        {
            prefab.AddComponent<Rigidbody>();
            prefab.AddComponent<SphereCollider>();
            prefab.AddComponent<Projectile>();
            prefab.SetActive(false);

            poolGo.SetActive(false);
            ProjectilePool pool = poolGo.AddComponent<ProjectilePool>();
            SetPrivateField(pool, "_projectilePrefab", prefab);
            SetPrivateField(pool, "_container", poolContainer.transform);
            SetPrivateField(pool, "_initialPoolSize", 1);
            SetPrivateField(pool, "_maxPoolSize", 2);
            SetPrivateField(pool, "_allowPoolGrowth", true);
            poolGo.SetActive(true);

            PlayerStats stats = owner.AddComponent<PlayerStats>();
            SetPrivateField(stats, "_statDefinitions", statDefinitions);
            InvokePrivate(stats, "Awake");

            data.WeaponId = "TestRocket";
            data.DisplayName = "Test Rocket";
            data.WeaponType = WeaponType.RocketLauncher;
            data.BaseDamage = 10f;
            data.BaseAttackRate = 1f;
            data.BaseRange = 12f;
            data.BaseManualAmmo = 100f;
            data.EnsureSpecificTuningForCurrentType();
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

            RocketLauncherWeapon weapon = new(null, pool, spawn.transform);
            weapon.Setup(instance, owner.transform, stats, null);
            InvokePrivateWithSignature(
                weapon,
                "FireRocketAt",
                new[] { typeof(Vector3), typeof(float), typeof(float), typeof(float), typeof(float) },
                Vector3.forward * data.BaseRange,
                1f,
                data.RocketLauncher.RocketManualExplosionRadius,
                data.RocketLauncher.RocketManualExplosionFalloff,
                data.RocketLauncher.RocketManualSpeedMultiplier);

            Projectile activeRocket = null;
            foreach (Projectile candidate in Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None))
            {
                if (candidate.gameObject != prefab && candidate.gameObject.activeSelf)
                {
                    activeRocket = candidate;
                    break;
                }
            }

            Assert.That(activeRocket, Is.Not.Null);
            Assert.That(ReadField<float>(activeRocket, "_maxTravelDistance"), Is.EqualTo(12f).Within(0.0001f));
            Assert.That(ReadField<float>(activeRocket, "_explosionRadius"), Is.EqualTo(4.8f).Within(0.0001f));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(spawn);
            Object.DestroyImmediate(poolGo);
            Object.DestroyImmediate(poolContainer);
            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(data);
            for (int i = 0; i < statDefinitions.Count; i++)
                Object.DestroyImmediate(statDefinitions[i]);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void AutomaticCannonHeadHunterManual_DamagesOnlyWhenProjectileReachesFarTarget()
    {
        GameObject owner = new("Cannon Owner");
        GameObject spawn = new("Cannon Spawn");
        GameObject target = new("Far Head Hunter Target");
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        List<StatDefinition> statDefinitions = CreateDefaultStatDefinitions();

        try
        {
            spawn.transform.position = Vector3.zero;
            target.transform.position = Vector3.forward * 18f;
            SphereCollider targetCollider = target.AddComponent<SphereCollider>();
            targetCollider.radius = 0.4f;
            var damageable = target.AddComponent<TestDamageable>();
            EnemyRegistry.Register(target.transform);
            Physics.SyncTransforms();

            PlayerStats stats = owner.AddComponent<PlayerStats>();
            SetPrivateField(stats, "_statDefinitions", statDefinitions);
            InvokePrivate(stats, "Awake");

            data.WeaponId = "TestAutomaticCannon";
            data.DisplayName = "Test Automatic Cannon";
            data.WeaponType = WeaponType.AutomaticCannon;
            data.BaseDamage = 10f;
            data.BaseAttackRate = 1f;
            data.BaseRange = 12f;
            data.BaseManualAmmo = 40f;
            data.EnsureSpecificTuningForCurrentType();
            data.LevelData = new List<WeaponLevelData>
            {
                new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
                new() { Level = 6, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
            };
            data.PathB = new WeaponUpgradePathData { PathName = "Head Hunter", DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoOverride = 40f };

            WeaponInstance instance = new()
            {
                Data = data,
                Level = 6,
                SelectedPath = WeaponUpgradePath.PathB,
                State = WeaponState.Manual,
                CurrentAmmo = 40f
            };

            AutomaticCannonWeapon weapon = new(null, null, spawn.transform);
            weapon.Setup(instance, owner.transform, stats, null);
            weapon.TickManual(0.1f, Vector3.forward, isFiring: true);

            Assert.That(damageable.TotalDamage, Is.Zero);

            weapon.TickManual(0.4f, Vector3.forward, isFiring: false);

            Assert.That(damageable.TotalDamage, Is.GreaterThan(0));
            Assert.That(Object.FindObjectsByType<WeaponUpgradeVfx>(FindObjectsSortMode.None), Is.Empty);
        }
        finally
        {
            EnemyRegistry.Unregister(target.transform);
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(spawn);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(data);
            for (int i = 0; i < statDefinitions.Count; i++)
                Object.DestroyImmediate(statDefinitions[i]);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void AutomaticCannonHeadHunterManual_AppliesKnockbackOnImpact()
    {
        GameObject owner = new("Cannon Owner");
        GameObject spawn = new("Cannon Spawn");
        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        target.name = "Head Hunter Knockback Target";
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        List<StatDefinition> statDefinitions = CreateDefaultStatDefinitions();

        try
        {
            spawn.transform.position = Vector3.zero;
            target.transform.position = Vector3.forward * 6f;
            var damageable = target.AddComponent<TestDamageable>();
            EnemyRegistry.Register(target.transform);
            Physics.SyncTransforms();

            PlayerStats stats = owner.AddComponent<PlayerStats>();
            SetPrivateField(stats, "_statDefinitions", statDefinitions);
            InvokePrivate(stats, "Awake");

            data.WeaponId = "TestAutomaticCannon";
            data.DisplayName = "Test Automatic Cannon";
            data.WeaponType = WeaponType.AutomaticCannon;
            data.BaseDamage = 10f;
            data.BaseKnockback = 1f;
            data.BaseAttackRate = 1f;
            data.BaseRange = 12f;
            data.BaseManualAmmo = 40f;
            data.EnsureSpecificTuningForCurrentType();
            data.LevelData = new List<WeaponLevelData>
            {
                new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
                new() { Level = 6, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
            };
            data.PathB = new WeaponUpgradePathData { PathName = "Head Hunter", DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoOverride = 40f };

            WeaponInstance instance = new()
            {
                Data = data,
                Level = 6,
                SelectedPath = WeaponUpgradePath.PathB,
                State = WeaponState.Manual,
                CurrentAmmo = 40f
            };

            AutomaticCannonWeapon weapon = new(null, null, spawn.transform);
            weapon.Setup(instance, owner.transform, stats, null);
            weapon.TickManual(0.1f, Vector3.forward, isFiring: true);
            weapon.TickManual(0.2f, Vector3.forward, isFiring: false);

            EnemyKnockbackReceiver receiver = target.GetComponent<EnemyKnockbackReceiver>();

            Assert.That(damageable.TotalDamage, Is.GreaterThan(0));
            Assert.That(receiver, Is.Not.Null);
            Assert.That(receiver.ConsumeDisplacement(0.1f).z, Is.GreaterThan(0f));
        }
        finally
        {
            EnemyRegistry.Unregister(target.transform);
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(spawn);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(data);
            for (int i = 0; i < statDefinitions.Count; i++)
                Object.DestroyImmediate(statDefinitions[i]);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void AutomaticCannonHeadHunterManual_FiresOnlyOncePerClick()
    {
        GameObject owner = new("Cannon Owner");
        GameObject spawn = new("Cannon Spawn");
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        List<StatDefinition> statDefinitions = CreateDefaultStatDefinitions();

        try
        {
            spawn.transform.position = Vector3.zero;
            spawn.transform.SetParent(owner.transform);

            PlayerStats stats = owner.AddComponent<PlayerStats>();
            SetPrivateField(stats, "_statDefinitions", statDefinitions);
            InvokePrivate(stats, "Awake");

            data.WeaponId = "TestAutomaticCannon";
            data.DisplayName = "Test Automatic Cannon";
            data.WeaponType = WeaponType.AutomaticCannon;
            data.BaseDamage = 10f;
            data.BaseAttackRate = 1f;
            data.BaseRange = 12f;
            data.BaseManualAmmo = 40f;
            data.EnsureSpecificTuningForCurrentType();
            data.LevelData = new List<WeaponLevelData>
            {
                new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
                new() { Level = 6, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
            };
            data.PathB = new WeaponUpgradePathData { PathName = "Head Hunter", DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoOverride = 40f };

            WeaponInstance instance = new()
            {
                Data = data,
                Level = 6,
                SelectedPath = WeaponUpgradePath.PathB,
                State = WeaponState.Manual,
                CurrentAmmo = 40f
            };

            AutomaticCannonWeapon weapon = new(null, null, spawn.transform);
            weapon.Setup(instance, owner.transform, stats, null);

            weapon.TickManual(0.1f, Vector3.forward, isFiring: true);

            Assert.That(instance.CurrentAmmo, Is.EqualTo(39f));

            weapon.TickManual(5f, Vector3.forward, isFiring: true);

            Assert.That(instance.CurrentAmmo, Is.EqualTo(39f));

            weapon.TickManual(0.1f, Vector3.forward, isFiring: false);
            weapon.TickManual(5f, Vector3.forward, isFiring: true);

            Assert.That(instance.CurrentAmmo, Is.EqualTo(38f));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(data);
            for (int i = 0; i < statDefinitions.Count; i++)
                Object.DestroyImmediate(statDefinitions[i]);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void AutomaticCannonHeadHunterWeakPointHit_NotifiesReticleFeedback()
    {
        GameObject owner = new("Cannon Owner");
        GameObject spawn = new("Cannon Spawn");
        GameObject target = new("Weak Point Target");
        GameObject weakPoint = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        List<StatDefinition> statDefinitions = CreateDefaultStatDefinitions();
        int weakPointNotifications = 0;

        void OnWeakPointHit() => weakPointNotifications++;

        try
        {
            spawn.transform.position = Vector3.zero;
            target.transform.position = Vector3.forward * 8f;
            SphereCollider targetCollider = target.AddComponent<SphereCollider>();
            targetCollider.radius = 0.5f;
            target.AddComponent<TestDamageable>();
            EnemyRegistry.Register(target.transform);

            weakPoint.name = "WeakPoint";
            weakPoint.transform.SetParent(target.transform, false);
            weakPoint.transform.localPosition = Vector3.zero;
            weakPoint.transform.localScale = Vector3.one * 0.3f;
            weakPoint.GetComponent<Collider>().isTrigger = true;
            Physics.SyncTransforms();

            PlayerStats stats = owner.AddComponent<PlayerStats>();
            SetPrivateField(stats, "_statDefinitions", statDefinitions);
            InvokePrivate(stats, "Awake");

            data.WeaponId = "TestAutomaticCannon";
            data.DisplayName = "Test Automatic Cannon";
            data.WeaponType = WeaponType.AutomaticCannon;
            data.BaseDamage = 10f;
            data.BaseAttackRate = 1f;
            data.BaseRange = 12f;
            data.BaseManualAmmo = 40f;
            data.EnsureSpecificTuningForCurrentType();
            data.LevelData = new List<WeaponLevelData>
            {
                new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
                new() { Level = 6, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
            };
            data.PathB = new WeaponUpgradePathData { PathName = "Head Hunter", DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoOverride = 40f };

            WeaponInstance instance = new()
            {
                Data = data,
                Level = 6,
                SelectedPath = WeaponUpgradePath.PathB,
                State = WeaponState.Manual,
                CurrentAmmo = 40f
            };

            WeaponWeakPointFeedback.WeakPointHit += OnWeakPointHit;

            AutomaticCannonWeapon weapon = new(null, null, spawn.transform);
            weapon.Setup(instance, owner.transform, stats, null);
            InvokePrivate(
                weapon,
                "FireHeadHunterPiercingLine",
                Vector3.forward,
                10,
                100f,
                true,
                false,
                null);

            Assert.That(weakPointNotifications, Is.Zero);

            weapon.TickManual(0.2f, Vector3.forward, isFiring: false);

            Assert.That(weakPointNotifications, Is.EqualTo(1));
        }
        finally
        {
            WeaponWeakPointFeedback.WeakPointHit -= OnWeakPointHit;
            EnemyRegistry.Unregister(target.transform);
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(spawn);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(data);
            for (int i = 0; i < statDefinitions.Count; i++)
                Object.DestroyImmediate(statDefinitions[i]);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void ReticleHud_WeakPointFeedbackFlashesMainReticleBrightRed()
    {
        GameObject owner = new("Reticle Owner");

        try
        {
            ReticleHud hud = owner.AddComponent<ReticleHud>();
            if (ReadField<GameObject>(hud, "_canvasRoot") == null)
                InvokePrivate(hud, "Awake");
            InvokePrivate(hud, "OnEnable");

            WeaponWeakPointFeedback.NotifyWeakPointHit();

            Image circle = null;
            Image shadow = null;
            foreach (Image image in owner.GetComponentsInChildren<Image>(true))
            {
                if (image.name == "Circle")
                    circle = image;
                else if (image.name == "CircleShadow")
                    shadow = image;
            }

            Assert.That(circle, Is.Not.Null);
            Assert.That(circle.color.r, Is.EqualTo(1f).Within(0.001f));
            Assert.That(circle.color.g, Is.LessThanOrEqualTo(0.05f));
            Assert.That(circle.color.b, Is.LessThanOrEqualTo(0.05f));
            Assert.That(shadow, Is.Not.Null);
            Assert.That(shadow.color.r, Is.LessThan(0.1f));
        }
        finally
        {
            Object.DestroyImmediate(owner);
        }
    }

    [Test]
    public void PlayerMovement_MomentumPreservingChargeLock_CanCoastWithoutStunFeedback()
    {
        GameObject player = new("Player");

        try
        {
            Rigidbody body = player.AddComponent<Rigidbody>();
            body.linearVelocity = new Vector3(7f, 0f, 2f);
            player.AddComponent<PlayerStats>();
            PlayerMovement movement = player.AddComponent<PlayerMovement>();
            int stunEvents = 0;
            movement.OnStunned += () => stunEvents++;

            movement.ApplyMomentumPreservingStun(1f, triggerStunFeedback: false, freezePlanarVelocity: false);

            Assert.That(stunEvents, Is.Zero);
            Assert.That(movement.IsStunned, Is.False);
            Assert.That(body.linearVelocity.x, Is.EqualTo(7f).Within(0.001f));
            Assert.That(body.linearVelocity.z, Is.EqualTo(2f).Within(0.001f));
            Assert.That(ReadField<float>(movement, "_momentumPreservingStunTimer"), Is.GreaterThan(0f));
            Assert.That(ReadField<bool>(movement, "_hasMomentumPreservingStunVelocity"), Is.False);

            SetPrivateField(movement, "_momentumPreservingStunTimer", 0.01f);
            InvokePrivate(movement, "TickMomentumPreservingStun");

            Assert.That(ReadField<float>(movement, "_momentumPreservingStunTimer"), Is.Zero);
            Assert.That(InvokePrivate<bool>(movement, "get_IsMovementInputLocked"), Is.False);
        }
        finally
        {
            Object.DestroyImmediate(player);
        }
    }

    [Test]
    public void HeadHunterChargeVfx_GrowsFromFirePointWithProgress()
    {
        GameObject firePoint = new("Fire Point");

        try
        {
            firePoint.transform.position = new Vector3(1f, 2f, 3f);
            HeadHunterChargeVfx vfx = HeadHunterChargeVfx.Spawn(firePoint.transform, Vector3.forward, 1f);

            Assert.That(vfx, Is.Not.Null);
            Assert.That(vfx.transform.position, Is.EqualTo(firePoint.transform.position));

            LineRenderer ring = vfx.GetComponentInChildren<LineRenderer>();
            Assert.That(ring, Is.Not.Null);
            float initialRadius = ring.GetPosition(0).magnitude;

            vfx.SetChargeProgress(0.75f, Vector3.forward);
            float grownRadius = ring.GetPosition(0).magnitude;

            Assert.That(grownRadius, Is.GreaterThan(initialRadius));
            Assert.That(ring.startColor.r, Is.EqualTo(1f).Within(0.001f));
            Assert.That(ring.startColor.g, Is.EqualTo(1f).Within(0.001f));
            Assert.That(ring.startColor.b, Is.EqualTo(1f).Within(0.001f));

            firePoint.transform.position += Vector3.right * 3f;
            vfx.SetChargeProgress(0.8f, Vector3.forward);

            Assert.That(vfx.transform.position, Is.EqualTo(firePoint.transform.position));
        }
        finally
        {
            Object.DestroyImmediate(firePoint);
            foreach (HeadHunterChargeVfx vfx in Object.FindObjectsByType<HeadHunterChargeVfx>(FindObjectsSortMode.None))
                Object.DestroyImmediate(vfx.gameObject);
        }
    }

    [Test]
    public void AutomaticCannonHeadHunterActiveCharge_SpawnsAndClearsChargeVfx()
    {
        GameObject owner = new("Cannon Owner");
        GameObject spawn = new("Cannon Spawn");
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        List<StatDefinition> statDefinitions = CreateDefaultStatDefinitions();

        try
        {
            spawn.transform.position = Vector3.up;
            spawn.transform.SetParent(owner.transform);

            PlayerStats stats = owner.AddComponent<PlayerStats>();
            SetPrivateField(stats, "_statDefinitions", statDefinitions);
            InvokePrivate(stats, "Awake");

            data.WeaponId = "TestAutomaticCannon";
            data.DisplayName = "Test Automatic Cannon";
            data.WeaponType = WeaponType.AutomaticCannon;
            data.BaseDamage = 10f;
            data.BaseAttackRate = 1f;
            data.BaseRange = 12f;
            data.BaseManualAmmo = 40f;
            data.EnsureSpecificTuningForCurrentType();
            data.LevelData = new List<WeaponLevelData>
            {
                new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
                new() { Level = 6, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
            };
            data.PathB = new WeaponUpgradePathData { PathName = "Head Hunter", DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoOverride = 40f };

            WeaponInstance instance = new()
            {
                Data = data,
                Level = 6,
                SelectedPath = WeaponUpgradePath.PathB,
                State = WeaponState.Manual,
                CurrentAmmo = 40f
            };

            AutomaticCannonWeapon weapon = new(null, null, spawn.transform);
            weapon.Setup(instance, owner.transform, stats, null);

            weapon.UseActiveAbility(Vector3.forward);

            Assert.That(Object.FindAnyObjectByType<HeadHunterChargeVfx>(), Is.Not.Null);

            weapon.TickManual(1.1f, Vector3.forward, isFiring: false);

            Assert.That(Object.FindAnyObjectByType<HeadHunterChargeVfx>(), Is.Null);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(data);
            for (int i = 0; i < statDefinitions.Count; i++)
                Object.DestroyImmediate(statDefinitions[i]);
            foreach (HeadHunterChargeVfx vfx in Object.FindObjectsByType<HeadHunterChargeVfx>(FindObjectsSortMode.None))
                Object.DestroyImmediate(vfx.gameObject);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void AutomaticCannonHeadHunterActiveCharge_FollowsReticleDirectionUntilRelease()
    {
        GameObject owner = new("Cannon Owner");
        GameObject spawn = new("Cannon Spawn");
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        List<StatDefinition> statDefinitions = CreateDefaultStatDefinitions();

        try
        {
            spawn.transform.position = Vector3.zero;
            spawn.transform.SetParent(owner.transform);

            PlayerStats stats = owner.AddComponent<PlayerStats>();
            SetPrivateField(stats, "_statDefinitions", statDefinitions);
            InvokePrivate(stats, "Awake");

            data.WeaponId = "TestAutomaticCannon";
            data.DisplayName = "Test Automatic Cannon";
            data.WeaponType = WeaponType.AutomaticCannon;
            data.BaseDamage = 10f;
            data.BaseAttackRate = 1f;
            data.BaseRange = 12f;
            data.BaseManualAmmo = 40f;
            data.EnsureSpecificTuningForCurrentType();
            data.LevelData = new List<WeaponLevelData>
            {
                new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
                new() { Level = 6, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
            };
            data.PathB = new WeaponUpgradePathData { PathName = "Head Hunter", DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoOverride = 40f };

            WeaponInstance instance = new()
            {
                Data = data,
                Level = 6,
                SelectedPath = WeaponUpgradePath.PathB,
                State = WeaponState.Manual,
                CurrentAmmo = 40f
            };

            AutomaticCannonWeapon weapon = new(null, null, spawn.transform);
            weapon.Setup(instance, owner.transform, stats, null);
            weapon.UseActiveAbility(Vector3.forward);

            weapon.TickManual(0.25f, Vector3.right, isFiring: false);

            HeadHunterChargeVfx vfx = Object.FindAnyObjectByType<HeadHunterChargeVfx>();
            Assert.That(vfx, Is.Not.Null);
            Assert.That(Vector3.Dot(vfx.transform.forward, Vector3.right), Is.GreaterThan(0.99f));

            weapon.TickManual(1f, Vector3.right, isFiring: false);

            Vector3[] line = ReadField<Vector3[]>(weapon, "_piercingLine");
            Vector3 firedDirection = (line[1] - line[0]).normalized;

            Assert.That(firedDirection.x, Is.GreaterThan(0.99f));
            Assert.That(Mathf.Abs(firedDirection.z), Is.LessThan(0.01f));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(data);
            for (int i = 0; i < statDefinitions.Count; i++)
                Object.DestroyImmediate(statDefinitions[i]);
            foreach (HeadHunterChargeVfx vfx in Object.FindObjectsByType<HeadHunterChargeVfx>(FindObjectsSortMode.None))
                Object.DestroyImmediate(vfx.gameObject);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void RotatingBladeAutomatic_DamagesEnemySweptByOrbitingBlade()
    {
        GameObject owner = new("Blade Owner");
        GameObject target = new("Swept Blade Target");
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        List<StatDefinition> statDefinitions = CreateDefaultStatDefinitions();

        try
        {
            owner.transform.position = Vector3.zero;
            owner.transform.rotation = Quaternion.LookRotation(Vector3.right, Vector3.up);

            PlayerStats stats = owner.AddComponent<PlayerStats>();
            SetPrivateField(stats, "_statDefinitions", statDefinitions);
            InvokePrivate(stats, "Awake");

            data.WeaponId = "TestRotatingBlade";
            data.DisplayName = "Test Rotating Blade";
            data.WeaponType = WeaponType.RotatingBlade;
            data.BaseDamage = 10f;
            data.BaseAttackRate = 1f;
            data.BaseRange = 12f;
            data.EnsureSpecificTuningForCurrentType();
            data.RotatingBlade.BladeOrbitRadius = 2.2f;
            data.RotatingBlade.BladeHitRadius = 0.35f;
            data.RotatingBlade.BladeAutoDamageInterval = 0.25f;
            data.RotatingBlade.BladeBaseSpinDegreesPerSecond = 240f;
            data.RotatingBlade.BladeVisualDuration = 0.01f;
            data.LevelData = new List<WeaponLevelData>
            {
                new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
            };

            WeaponInstance instance = new()
            {
                Data = data,
                Level = 1,
                SelectedPath = WeaponUpgradePath.None,
                State = WeaponState.Automatic
            };

            Vector3 sweptArcPoint = Quaternion.AngleAxis(30f, Vector3.up) * Vector3.forward * data.RotatingBlade.BladeOrbitRadius;
            target.transform.position = sweptArcPoint;
            TestDamageable damageable = target.AddComponent<TestDamageable>();
            EnemyRegistry.Register(target.transform);
            Physics.SyncTransforms();

            RotatingBladeWeapon weapon = new(null, null, null);
            weapon.Setup(instance, owner.transform, stats, null);
            SetPrivateField(weapon, "_spinAngle", 0f);

            weapon.TickAutomatic(0.25f, Vector3.forward);

            Assert.That(damageable.TotalDamage, Is.GreaterThan(0));
        }
        finally
        {
            EnemyRegistry.Unregister(target.transform);
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(data);
            for (int i = 0; i < statDefinitions.Count; i++)
                Object.DestroyImmediate(statDefinitions[i]);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void RotatingBladeAutomatic_AreaSizeHitWidthDamagesEnemyInsideExpandedOrbit()
    {
        GameObject owner = new("Blade Area Owner");
        GameObject target = new("Blade Inner Orbit Target");
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        List<StatDefinition> statDefinitions = CreateDefaultStatDefinitions(projectileAreaSize: 2f);

        try
        {
            owner.transform.position = Vector3.zero;

            PlayerStats stats = owner.AddComponent<PlayerStats>();
            SetPrivateField(stats, "_statDefinitions", statDefinitions);
            InvokePrivate(stats, "Awake");

            data.WeaponId = "TestRotatingBladeArea";
            data.DisplayName = "Test Rotating Blade Area";
            data.WeaponType = WeaponType.RotatingBlade;
            data.BaseDamage = 10f;
            data.BaseAttackRate = 1f;
            data.BaseRange = 12f;
            data.EnsureSpecificTuningForCurrentType();
            data.RotatingBlade.BladeOrbitRadius = 2f;
            data.RotatingBlade.BladeHitRadius = 0.25f;
            data.RotatingBlade.BladeAutoDamageInterval = 0.25f;
            data.RotatingBlade.BladeBaseSpinDegreesPerSecond = 240f;
            data.RotatingBlade.BladeVisualDuration = 0.01f;
            data.LevelData = new List<WeaponLevelData>
            {
                new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
            };

            WeaponInstance instance = new()
            {
                Data = data,
                Level = 1,
                SelectedPath = WeaponUpgradePath.None,
                State = WeaponState.Automatic
            };

            target.transform.position = Vector3.forward * 2f;
            TestDamageable damageable = target.AddComponent<TestDamageable>();
            EnemyRegistry.Register(target.transform);
            Physics.SyncTransforms();

            RotatingBladeWeapon weapon = new(null, null, null);
            weapon.Setup(instance, owner.transform, stats, null);
            SetPrivateField(weapon, "_spinAngle", 0f);

            weapon.TickAutomatic(0.25f, Vector3.forward);

            Assert.That(damageable.TotalDamage, Is.GreaterThan(0));
        }
        finally
        {
            EnemyRegistry.Unregister(target.transform);
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(data);
            for (int i = 0; i < statDefinitions.Count; i++)
                Object.DestroyImmediate(statDefinitions[i]);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void RotatingBladeMultiBladeAutomatic_DoesNotSpawnUpgradeRingVfx()
    {
        GameObject owner = new("Multi Blade Auto Owner");
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();

        try
        {
            data.WeaponId = "TestRotatingBladePathA";
            data.DisplayName = "Test Rotating Blade Path A";
            data.WeaponType = WeaponType.RotatingBlade;
            data.BaseDamage = 10f;
            data.BaseAttackRate = 1f;
            data.BaseRange = 12f;
            data.EnsureSpecificTuningForCurrentType();
            data.RotatingBlade.BladeOrbitRadius = 2f;
            data.RotatingBlade.BladeHitRadius = 0.25f;
            data.RotatingBlade.BladeAutoDamageInterval = 0.25f;
            data.RotatingBlade.BladeBaseSpinDegreesPerSecond = 240f;
            data.RotatingBlade.BladeVisualDuration = 0.01f;
            data.LevelData = new List<WeaponLevelData>
            {
                new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
                new() { Level = 6, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
            };

            WeaponInstance instance = new()
            {
                Data = data,
                Level = 6,
                SelectedPath = WeaponUpgradePath.PathA,
                State = WeaponState.Automatic
            };

            RotatingBladeWeapon weapon = new(null, null, null);
            weapon.Setup(instance, owner.transform, null, null);
            weapon.TickAutomatic(0.25f, Vector3.forward);

            Assert.That(Object.FindObjectsByType<WeaponUpgradeVfx>(FindObjectsSortMode.None), Is.Empty);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(data);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void RotatingBladeAtomicSharpnessAutomatic_DoesNotSpawnUpgradeRingVfx()
    {
        GameObject owner = new("Atomic Sharpness Auto Owner");
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();

        try
        {
            data.WeaponId = "TestRotatingBladePathB";
            data.DisplayName = "Test Rotating Blade Path B";
            data.WeaponType = WeaponType.RotatingBlade;
            data.BaseDamage = 10f;
            data.BaseAttackRate = 1f;
            data.BaseRange = 12f;
            data.EnsureSpecificTuningForCurrentType();
            data.RotatingBlade.BladeOrbitRadius = 2f;
            data.RotatingBlade.BladeHitRadius = 0.25f;
            data.RotatingBlade.BladeAutoDamageInterval = 0.25f;
            data.RotatingBlade.BladeBaseSpinDegreesPerSecond = 240f;
            data.RotatingBlade.BladeVisualDuration = 0.01f;
            data.LevelData = new List<WeaponLevelData>
            {
                new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
                new() { Level = 6, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
            };

            WeaponInstance instance = new()
            {
                Data = data,
                Level = 6,
                SelectedPath = WeaponUpgradePath.PathB,
                State = WeaponState.Automatic
            };

            RotatingBladeWeapon weapon = new(null, null, null);
            weapon.Setup(instance, owner.transform, null, null);
            weapon.TickAutomatic(0.25f, Vector3.forward);

            Assert.That(Object.FindObjectsByType<WeaponUpgradeVfx>(FindObjectsSortMode.None), Is.Empty);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(data);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void RotatingBladeMultiBladeManual_ShowsOnePeachSwishPerSwordWithoutVerticalSwordOrUpgradeStreaks()
    {
        GameObject owner = new("Multi Blade Manual Owner");
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();

        try
        {
            data.WeaponId = "TestRotatingBladePathAManual";
            data.DisplayName = "Test Rotating Blade Path A Manual";
            data.WeaponType = WeaponType.RotatingBlade;
            data.BaseDamage = 10f;
            data.BaseAttackRate = 1f;
            data.BaseRange = 12f;
            data.BaseManualAmmo = 100f;
            data.EnsureSpecificTuningForCurrentType();
            data.RotatingBlade.BladeHitRadius = 0.35f;
            data.RotatingBlade.BladeManualRange = 3f;
            data.RotatingBlade.BladeManualCooldown = 0.1f;
            data.RotatingBlade.BladeVisualDuration = 1f;
            data.LevelData = new List<WeaponLevelData>
            {
                new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
                new() { Level = 7, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
            };

            WeaponInstance instance = new()
            {
                Data = data,
                Level = 7,
                SelectedPath = WeaponUpgradePath.PathA,
                State = WeaponState.Manual,
                CurrentAmmo = 100f
            };

            RotatingBladeWeapon weapon = new(null, null, null);
            weapon.Setup(instance, owner.transform, null, null);

            weapon.TickManual(0.1f, Vector3.forward, true);
            weapon.TickManual(0.1f, Vector3.forward, true);

            Assert.That(Object.FindObjectsByType<WeaponUpgradeVfx>(FindObjectsSortMode.None), Is.Empty);

            RotatingBladeVfx vfx = Object.FindAnyObjectByType<RotatingBladeVfx>();
            Assert.That(vfx, Is.Not.Null);

            List<LineRenderer> slashLines = GetEnabledLineRenderersStartingWith(vfx, "Blade Slash");
            Assert.That(slashLines.Count, Is.EqualTo(2));
            for (int i = 0; i < slashLines.Count; i++)
                AssertPeach(slashLines[i].startColor);

            List<LineRenderer> swordLines = GetEnabledLineRenderersStartingWith(vfx, "Blade Manual Sword");
            Assert.That(swordLines, Is.Empty);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(data);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void RotatingBladeAtomicSharpnessManual_ShowsDarkPurpleRadialSlashWithoutUpgradeStreaks()
    {
        GameObject owner = new("Atomic Sharpness Manual Owner");
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();

        try
        {
            data.WeaponId = "TestRotatingBladePathBManual";
            data.DisplayName = "Test Rotating Blade Path B Manual";
            data.WeaponType = WeaponType.RotatingBlade;
            data.BaseDamage = 10f;
            data.BaseAttackRate = 1f;
            data.BaseRange = 12f;
            data.BaseManualAmmo = 100f;
            data.EnsureSpecificTuningForCurrentType();
            data.RotatingBlade.BladeManualRange = 3f;
            data.RotatingBlade.BladeManualCooldown = 0.1f;
            data.RotatingBlade.BladeVisualDuration = 1f;
            data.LevelData = new List<WeaponLevelData>
            {
                new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
                new() { Level = 6, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
            };

            WeaponInstance instance = new()
            {
                Data = data,
                Level = 6,
                SelectedPath = WeaponUpgradePath.PathB,
                State = WeaponState.Manual,
                CurrentAmmo = 100f
            };

            RotatingBladeWeapon weapon = new(null, null, null);
            weapon.Setup(instance, owner.transform, null, null);

            weapon.TickManual(0.1f, Vector3.forward, true);

            Assert.That(Object.FindObjectsByType<WeaponUpgradeVfx>(FindObjectsSortMode.None), Is.Empty);

            RotatingBladeVfx vfx = Object.FindAnyObjectByType<RotatingBladeVfx>();
            Assert.That(vfx, Is.Not.Null);

            List<LineRenderer> slashLines = GetEnabledLineRenderersStartingWith(vfx, "Blade Slash");
            Assert.That(slashLines.Count, Is.EqualTo(1));
            AssertDarkPurple(slashLines[0].startColor);
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(data);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void RotatingBladeMultiBladeManual_UsesPlayerOriginConeHitbox()
    {
        GameObject owner = new("Multi Blade Hitbox Owner");
        GameObject forwardTarget = new("Forward Target");
        GameObject sideTarget = new("Side Target");
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();

        try
        {
            data.WeaponId = "TestRotatingBladePathAHitbox";
            data.DisplayName = "Test Rotating Blade Path A Hitbox";
            data.WeaponType = WeaponType.RotatingBlade;
            data.BaseDamage = 10f;
            data.BaseAttackRate = 1f;
            data.BaseRange = 12f;
            data.BaseManualAmmo = 100f;
            data.EnsureSpecificTuningForCurrentType();
            data.RotatingBlade.BladeManualRange = 4f;
            data.RotatingBlade.BladeManualConeAngle = 45f;
            data.RotatingBlade.BladeManualCooldown = 0.1f;
            data.RotatingBlade.BladeVisualDuration = 0.01f;
            data.LevelData = new List<WeaponLevelData>
            {
                new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
                new() { Level = 6, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
            };

            WeaponInstance instance = new()
            {
                Data = data,
                Level = 6,
                SelectedPath = WeaponUpgradePath.PathA,
                State = WeaponState.Manual,
                CurrentAmmo = 100f
            };

            forwardTarget.transform.position = Vector3.forward * 3f;
            sideTarget.transform.position = Vector3.right * 2f + Vector3.forward * 3f;
            TestDamageable forwardDamageable = forwardTarget.AddComponent<TestDamageable>();
            TestDamageable sideDamageable = sideTarget.AddComponent<TestDamageable>();
            EnemyRegistry.Register(forwardTarget.transform);
            EnemyRegistry.Register(sideTarget.transform);
            Physics.SyncTransforms();

            RotatingBladeWeapon weapon = new(null, null, null);
            weapon.Setup(instance, owner.transform, null, null);

            weapon.TickManual(0.1f, Vector3.forward, true);

            Assert.That(forwardDamageable.TotalDamage, Is.GreaterThan(0));
            Assert.That(sideDamageable.TotalDamage, Is.Zero);
        }
        finally
        {
            EnemyRegistry.Unregister(forwardTarget.transform);
            EnemyRegistry.Unregister(sideTarget.transform);
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(forwardTarget);
            Object.DestroyImmediate(sideTarget);
            Object.DestroyImmediate(data);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void RotatingBladeMultiBladeManual_DelayedSwingsFollowOwnerPosition()
    {
        GameObject owner = new("Multi Blade Moving Manual Owner");
        GameObject movedTarget = new("Moved Manual Target");
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();

        try
        {
            data.WeaponId = "TestRotatingBladePathAMovingManual";
            data.DisplayName = "Test Rotating Blade Path A Moving Manual";
            data.WeaponType = WeaponType.RotatingBlade;
            data.BaseDamage = 10f;
            data.BaseAttackRate = 1f;
            data.BaseKnockback = 1f;
            data.BaseRange = 12f;
            data.BaseManualAmmo = 100f;
            data.EnsureSpecificTuningForCurrentType();
            data.RotatingBlade.BladeManualRange = 4f;
            data.RotatingBlade.BladeManualConeAngle = 35f;
            data.RotatingBlade.BladeManualCooldown = 0.1f;
            data.RotatingBlade.BladeVisualDuration = 0.01f;
            data.LevelData = new List<WeaponLevelData>
            {
                new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
                new() { Level = 7, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
            };

            WeaponInstance instance = new()
            {
                Data = data,
                Level = 7,
                SelectedPath = WeaponUpgradePath.PathA,
                State = WeaponState.Manual,
                CurrentAmmo = 100f
            };

            movedTarget.transform.position = Vector3.right * 10f + Vector3.forward * 3f;
            TestDamageable movedDamageable = movedTarget.AddComponent<TestDamageable>();
            EnemyRegistry.Register(movedTarget.transform);
            Physics.SyncTransforms();

            RotatingBladeWeapon weapon = new(null, null, null);
            weapon.Setup(instance, owner.transform, null, null);

            weapon.TickManual(0.1f, Vector3.forward, true);
            Assert.That(movedDamageable.TotalDamage, Is.Zero);

            owner.transform.position = Vector3.right * 10f;
            Physics.SyncTransforms();

            weapon.TickManual(0.1f, Vector3.forward, true);

            Assert.That(movedDamageable.TotalDamage, Is.GreaterThan(0));
        }
        finally
        {
            EnemyRegistry.Unregister(movedTarget.transform);
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(movedTarget);
            Object.DestroyImmediate(data);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void RotatingBladeMultiBladeActive_FollowsOwnerAndOnlyFinalThrustKnocksBack()
    {
        GameObject owner = new("Multi Blade Moving Active Owner");
        GameObject firstTarget = new("First Active Target");
        GameObject finalTarget = new("Final Active Target");
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();

        try
        {
            data.WeaponId = "TestRotatingBladePathAMovingActive";
            data.DisplayName = "Test Rotating Blade Path A Moving Active";
            data.WeaponType = WeaponType.RotatingBlade;
            data.BaseDamage = 10f;
            data.BaseAttackRate = 1f;
            data.BaseKnockback = 1f;
            data.BaseRange = 12f;
            data.BaseManualAmmo = 100f;
            data.ActiveAbilityAmmoCost = 0f;
            data.SkillCooldown = 0f;
            data.EnsureSpecificTuningForCurrentType();
            data.RotatingBlade.BladeManualRange = 4f;
            data.RotatingBlade.BladeActiveBaseRangeMultiplier = 1f;
            data.RotatingBlade.BladeActiveMaxRangeMultiplier = 1f;
            data.RotatingBlade.BladeActiveLineWidth = 1f;
            data.RotatingBlade.BladeActiveKnockbackScale = 1.25f;
            data.RotatingBlade.BladeVisualDuration = 0.01f;
            data.LevelData = new List<WeaponLevelData>
            {
                new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
                new() { Level = 7, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
            };

            WeaponInstance instance = new()
            {
                Data = data,
                Level = 7,
                SelectedPath = WeaponUpgradePath.PathA,
                State = WeaponState.Manual,
                CurrentAmmo = 100f
            };

            firstTarget.transform.position = Vector3.forward * 2f;
            finalTarget.transform.position = Vector3.right * 10f + Vector3.forward * 2f;
            TestDamageable firstDamageable = firstTarget.AddComponent<TestDamageable>();
            TestDamageable finalDamageable = finalTarget.AddComponent<TestDamageable>();
            EnemyRegistry.Register(firstTarget.transform);
            EnemyRegistry.Register(finalTarget.transform);
            Physics.SyncTransforms();

            RotatingBladeWeapon weapon = new(null, null, null);
            weapon.Setup(instance, owner.transform, null, null);

            weapon.UseActiveAbility(Vector3.forward);

            Assert.That(firstDamageable.TotalDamage, Is.GreaterThan(0));
            Assert.That(firstTarget.GetComponent<EnemyKnockbackReceiver>(), Is.Null);
            Assert.That(finalDamageable.TotalDamage, Is.Zero);

            owner.transform.position = Vector3.right * 10f;
            Physics.SyncTransforms();

            weapon.TickManual(0.1f, Vector3.forward, false);

            Assert.That(finalDamageable.TotalDamage, Is.GreaterThan(0));
            EnemyKnockbackReceiver finalReceiver = finalTarget.GetComponent<EnemyKnockbackReceiver>();
            Assert.That(finalReceiver, Is.Not.Null);
            Assert.That(finalReceiver.ConsumeDisplacement(0.1f).sqrMagnitude, Is.GreaterThan(0f));
        }
        finally
        {
            EnemyRegistry.Unregister(firstTarget.transform);
            EnemyRegistry.Unregister(finalTarget.transform);
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(firstTarget);
            Object.DestroyImmediate(finalTarget);
            Object.DestroyImmediate(data);
            DestroyGeneratedVfx();
        }
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
    public void WeaponRadialDamage_ContextAppliesEliteMultiplierPerTarget()
    {
        MethodInfo contextApply = typeof(WeaponRadialDamage).GetMethod(
            "Apply",
            BindingFlags.Static | BindingFlags.Public,
            null,
            new[] { typeof(Vector3), typeof(float), typeof(WeaponDamageContext), typeof(float), typeof(int), typeof(bool) },
            null);
        Assert.That(contextApply, Is.Not.Null, "Jellified Fuel puddles need context-aware radial damage so stats resolve per target.");

        GameObject owner = new("Radial Damage Owner");
        GameObject normalTarget = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        GameObject eliteTarget = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        List<StatDefinition> statDefinitions = CreateDefaultStatDefinitions(eliteDamageMultiplier: 2f);

        try
        {
            Vector3 center = new(1000f, 0f, 1000f);
            normalTarget.name = "Normal Target";
            eliteTarget.name = "Elite Target";
            normalTarget.transform.position = center + Vector3.forward;
            eliteTarget.transform.position = center + Vector3.right;
            TestDamageable normalDamageable = normalTarget.AddComponent<TestDamageable>();
            TestDamageable eliteDamageable = eliteTarget.AddComponent<TestDamageable>();
            Physics.SyncTransforms();

            PlayerStats stats = owner.AddComponent<PlayerStats>();
            SetPrivateField(stats, "_statDefinitions", statDefinitions);
            InvokePrivate(stats, "Awake");

            data.WeaponId = "PuddleDamageTest";
            data.DisplayName = "Puddle Damage Test";
            data.BaseDamage = 10f;
            data.BaseKnockback = 0f;
            data.LevelData = new List<WeaponLevelData>
            {
                new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
            };

            WeaponInstance instance = new()
            {
                Data = data,
                Level = 1,
                State = WeaponState.Manual
            };
            WeaponDamageContext context = new(stats, instance, canCrit: false, critMultiplierOverride: 1f, damageScale: 0.5f, isAbilityDamage: false, knockbackScale: 0f);

            int applied = (int)contextApply.Invoke(null, new object[] { center, 3f, context, 0f, 64, false });

            Assert.That(applied, Is.EqualTo(2));
            Assert.That(normalDamageable.LastDamage, Is.EqualTo(5));
            Assert.That(eliteDamageable.LastDamage, Is.EqualTo(10));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(normalTarget);
            Object.DestroyImmediate(eliteTarget);
            Object.DestroyImmediate(data);
            for (int i = 0; i < statDefinitions.Count; i++)
                Object.DestroyImmediate(statDefinitions[i]);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void RocketLauncherFragmentationActive_MarksOnlyOneLockedTarget()
    {
        GameObject owner = new("Rocket Owner");
        GameObject spawn = new("Rocket Spawn");
        GameObject firstTarget = new("First Lock Target");
        GameObject secondTarget = new("Second Lock Target");
        WeaponData data = CreateRocketTestData();

        try
        {
            spawn.transform.position = Vector3.zero;
            firstTarget.transform.position = Vector3.forward * 3f;
            secondTarget.transform.position = Vector3.forward * 5f;
            EnemyRegistry.Register(firstTarget.transform);
            EnemyRegistry.Register(secondTarget.transform);

            RocketLauncherWeapon weapon = CreateRocketLauncherWeapon(WeaponUpgradePath.PathB, data, owner.transform, spawn.transform);

            weapon.BeginActiveAbility(Vector3.forward);

            Assert.That(weapon.InitialRocketLocks, Is.EqualTo(1));
            Assert.That(weapon.MaximumRocketLocks, Is.EqualTo(1));
            Assert.That(weapon.CurrentRocketLocks, Is.EqualTo(1));
            RocketTargetMarkerVfx[] markers = Object.FindObjectsByType<RocketTargetMarkerVfx>(FindObjectsSortMode.None);
            Assert.That(markers, Has.Length.EqualTo(1));
            Assert.That(markers[0].Target, Is.EqualTo(firstTarget.transform));
        }
        finally
        {
            EnemyRegistry.Unregister(firstTarget.transform);
            EnemyRegistry.Unregister(secondTarget.transform);
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(spawn);
            Object.DestroyImmediate(firstTarget);
            Object.DestroyImmediate(secondTarget);
            Object.DestroyImmediate(data);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void RocketLauncherFragmentationActive_RelocksWhenMarkedTargetLeavesRange()
    {
        GameObject owner = new("Rocket Owner");
        GameObject spawn = new("Rocket Spawn");
        GameObject firstTarget = new("First Lock Target");
        GameObject secondTarget = new("Second Lock Target");
        WeaponData data = CreateRocketTestData();

        try
        {
            spawn.transform.position = Vector3.zero;
            firstTarget.transform.position = Vector3.forward * 3f;
            secondTarget.transform.position = Vector3.forward * 5f;
            EnemyRegistry.Register(firstTarget.transform);
            EnemyRegistry.Register(secondTarget.transform);

            RocketLauncherWeapon weapon = CreateRocketLauncherWeapon(WeaponUpgradePath.PathB, data, owner.transform, spawn.transform);
            weapon.BeginActiveAbility(Vector3.forward);

            firstTarget.transform.position = Vector3.forward * 30f;
            weapon.TickActiveAbility(0.2f, Vector3.forward);

            Assert.That(weapon.CurrentRocketLocks, Is.EqualTo(1));
            RocketTargetMarkerVfx[] markers = Object.FindObjectsByType<RocketTargetMarkerVfx>(FindObjectsSortMode.None);
            Assert.That(markers, Has.Length.EqualTo(1));
            Assert.That(markers[0].Target, Is.EqualTo(secondTarget.transform));
        }
        finally
        {
            EnemyRegistry.Unregister(firstTarget.transform);
            EnemyRegistry.Unregister(secondTarget.transform);
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(spawn);
            Object.DestroyImmediate(firstTarget);
            Object.DestroyImmediate(secondTarget);
            Object.DestroyImmediate(data);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void RocketLauncherNonFragmentationActive_KeepsMultipleLocks()
    {
        GameObject owner = new("Rocket Owner");
        GameObject spawn = new("Rocket Spawn");
        GameObject firstTarget = new("First Lock Target");
        GameObject secondTarget = new("Second Lock Target");
        WeaponData data = CreateRocketTestData();

        try
        {
            spawn.transform.position = Vector3.zero;
            firstTarget.transform.position = Vector3.forward * 3f;
            secondTarget.transform.position = Vector3.forward * 5f;
            EnemyRegistry.Register(firstTarget.transform);
            EnemyRegistry.Register(secondTarget.transform);

            RocketLauncherWeapon weapon = CreateRocketLauncherWeapon(WeaponUpgradePath.PathA, data, owner.transform, spawn.transform);

            weapon.BeginActiveAbility(Vector3.forward);

            Assert.That(weapon.InitialRocketLocks, Is.GreaterThan(1));
            Assert.That(weapon.MaximumRocketLocks, Is.GreaterThan(1));
            Assert.That(weapon.CurrentRocketLocks, Is.EqualTo(2));
            Assert.That(Object.FindObjectsByType<RocketTargetMarkerVfx>(FindObjectsSortMode.None), Has.Length.EqualTo(2));
        }
        finally
        {
            EnemyRegistry.Unregister(firstTarget.transform);
            EnemyRegistry.Unregister(secondTarget.transform);
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(spawn);
            Object.DestroyImmediate(firstTarget);
            Object.DestroyImmediate(secondTarget);
            Object.DestroyImmediate(data);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void RocketLauncherFragmentationActive_ConfiguresSingleClusterRocketWithoutMainCone()
    {
        GameObject owner = new("Rocket Owner");
        GameObject spawn = new("Rocket Spawn");
        GameObject poolGo = new("Rocket Pool");
        GameObject poolContainer = new("Rocket Pool Container");
        GameObject prefab = new("Rocket Projectile Prefab");
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        List<StatDefinition> statDefinitions = CreateDefaultStatDefinitions();

        try
        {
            spawn.transform.position = Vector3.zero;
            prefab.AddComponent<Rigidbody>();
            prefab.AddComponent<SphereCollider>();
            prefab.AddComponent<Projectile>();
            prefab.SetActive(false);

            poolGo.SetActive(false);
            ProjectilePool pool = poolGo.AddComponent<ProjectilePool>();
            SetPrivateField(pool, "_projectilePrefab", prefab);
            SetPrivateField(pool, "_container", poolContainer.transform);
            SetPrivateField(pool, "_initialPoolSize", 1);
            SetPrivateField(pool, "_maxPoolSize", 4);
            SetPrivateField(pool, "_allowPoolGrowth", true);
            poolGo.SetActive(true);

            PlayerStats stats = owner.AddComponent<PlayerStats>();
            SetPrivateField(stats, "_statDefinitions", statDefinitions);
            InvokePrivate(stats, "Awake");

            data.WeaponId = "TestRocket";
            data.DisplayName = "Test Rocket";
            data.WeaponType = WeaponType.RocketLauncher;
            data.BaseDamage = 10f;
            data.BaseRange = 12f;
            data.BaseManualAmmo = 100f;
            data.EnsureSpecificTuningForCurrentType();
            data.LevelData = new List<WeaponLevelData>
            {
                new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
                new() { Level = 6, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
            };
            data.PathB = new WeaponUpgradePathData { PathName = "Fragmentation Cap", DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoOverride = -1f };

            WeaponInstance instance = new()
            {
                Data = data,
                Level = 6,
                SelectedPath = WeaponUpgradePath.PathB,
                State = WeaponState.Manual,
                CurrentAmmo = 100f
            };

            RocketLauncherWeapon weapon = new(null, pool, spawn.transform);
            weapon.Setup(instance, owner.transform, stats, null);
            RocketLauncherTuning tuning = data.RocketLauncher;
            InvokePrivateWithSignature(
                weapon,
                "FireRocketAt",
                new[] { typeof(Vector3), typeof(float), typeof(float), typeof(float), typeof(float), typeof(bool), typeof(bool) },
                Vector3.forward * 6f,
                tuning.RocketActiveDamageScale,
                tuning.RocketActiveExplosionRadius,
                tuning.RocketActiveExplosionFalloff,
                tuning.RocketActiveSpeedMultiplier,
                false,
                true);

            Projectile activeRocket = null;
            foreach (Projectile candidate in Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None))
            {
                if (candidate.gameObject != prefab && candidate.gameObject.activeSelf)
                {
                    activeRocket = candidate;
                    break;
                }
            }

            Assert.That(activeRocket, Is.Not.Null);
            Assert.That(ReadField<bool>(activeRocket, "_useFragmentCone"), Is.False);
            Assert.That(ReadField<bool>(activeRocket, "_useExplosionCluster"), Is.True);
            Assert.That(activeRocket.transform.localScale, Is.EqualTo(Vector3.one * 5f));
            Assert.That(ReadField<int>(activeRocket, "_clusterProjectileCount"), Is.EqualTo(20));
            Assert.That(ReadField<int>(activeRocket, "_clusterDamage"), Is.EqualTo(Mathf.RoundToInt(ReadField<int>(activeRocket, "_damage") * 0.5f)));
            Assert.That(ReadField<float>(activeRocket, "_clusterFragmentConeAngle"), Is.EqualTo(45f).Within(0.0001f));
            Assert.That(ReadField<float>(activeRocket, "_clusterFragmentDamageScale"), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(ReadField<float>(activeRocket, "_clusterFragmentConeRange"), Is.GreaterThan(0f));
        }
        finally
        {
            Object.DestroyImmediate(owner);
            Object.DestroyImmediate(spawn);
            Object.DestroyImmediate(poolGo);
            Object.DestroyImmediate(poolContainer);
            Object.DestroyImmediate(prefab);
            Object.DestroyImmediate(data);
            for (int i = 0; i < statDefinitions.Count; i++)
                Object.DestroyImmediate(statDefinitions[i]);
            DestroyGeneratedVfx();
        }
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
        object grapeshotPayload = InvokePrivate<object>(grapeshotWeapon, "GetUpgradePayload", false);

        Assert.That(ReadField<bool>(grapeshotPayload, "UseGrapeshot"), Is.True);
        Assert.That(ReadField<int>(grapeshotPayload, "GrapeshotCount"), Is.EqualTo(15));
        Assert.That(ReadField<float>(grapeshotPayload, "GrapeshotConeAngle"), Is.EqualTo(70f).Within(0.0001f));
        Assert.That(ReadField<float>(grapeshotPayload, "GrapeshotDamageScale"), Is.EqualTo(0.5f).Within(0.0001f));
        Assert.That(ReadField<int>(grapeshotPayload, "RepeatExplosionCount"), Is.EqualTo(1));
        Assert.That(InvokePrivate<int>(grapeshotWeapon, "GetGrapeshotRainShellCount", grapeshotData.Mortar), Is.EqualTo(50));

        object grapeshotActivePayload = InvokePrivate<object>(grapeshotWeapon, "GetUpgradePayload", true);
        Assert.That(ReadField<bool>(grapeshotActivePayload, "UseGrapeshot"), Is.False);
        Assert.That(ReadField<int>(grapeshotActivePayload, "RepeatExplosionCount"), Is.EqualTo(1));

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
    public void MortarGrapeshotShell_IgnoresCollisionsUntilAirburst()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        MortarShellImpact shell = null;

        try
        {
            floor.transform.position = new Vector3(0f, -0.05f, 0f);
            floor.transform.localScale = new Vector3(5f, 0.1f, 5f);
            target.transform.position = new Vector3(0f, 1f, 0f);
            target.AddComponent<TestDamageable>();
            Physics.SyncTransforms();

            shell = MortarShellImpact.Launch(
                Vector3.up * 3f,
                Vector3.down,
                1f,
                0f,
                10,
                2f,
                0f,
                0f,
                0.1f,
                null,
                new MortarUpgradePayload(true, 15, 70f, 0.5f, 1, 0f));

            bool foundCollision = TryGetMortarCollision(shell, Vector3.up * 3f, Vector3.down, out _);

            Assert.That(foundCollision, Is.False);
        }
        finally
        {
            if (shell != null)
                Object.DestroyImmediate(shell.gameObject);
            Object.DestroyImmediate(floor);
            Object.DestroyImmediate(target);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void MortarMultiChargedShell_IgnoresEnemiesAndCollidesWithGround()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        GameObject target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        MortarShellImpact shell = null;

        try
        {
            floor.transform.position = new Vector3(0f, -0.05f, 0f);
            floor.transform.localScale = new Vector3(5f, 0.1f, 5f);
            target.transform.position = new Vector3(0f, 1f, 0f);
            target.AddComponent<TestDamageable>();
            Physics.SyncTransforms();

            shell = MortarShellImpact.Launch(
                Vector3.up * 3f,
                Vector3.down,
                1f,
                0f,
                10,
                2f,
                0f,
                0f,
                0.1f,
                null,
                new MortarUpgradePayload(false, 0, 0f, 0f, 3, 2f));

            bool foundCollision = TryGetMortarCollision(shell, Vector3.up * 3f, Vector3.down, out Vector3 collisionPoint);

            Assert.That(foundCollision, Is.True);
            Assert.That(collisionPoint.y, Is.LessThan(0.25f));
        }
        finally
        {
            if (shell != null)
                Object.DestroyImmediate(shell.gameObject);
            Object.DestroyImmediate(floor);
            Object.DestroyImmediate(target);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void MortarGrapeshotAirburst_SpawnsFallingSubShells()
    {
        MortarShellImpact shell = null;

        try
        {
            shell = MortarShellImpact.Launch(
                Vector3.zero,
                Vector3.forward * 4f,
                1f,
                0f,
                20,
                2f,
                0f,
                0f,
                0.2f,
                null,
                new MortarUpgradePayload(true, 15, 70f, 0.5f, 1, 0f));

            InvokePrivateWithSignature(shell, "Detonate", new[] { typeof(Vector3) }, Vector3.up * 2f);

            MortarShellImpact[] subShells = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None);
            Assert.That(subShells, Has.Length.EqualTo(15));
            for (int i = 0; i < subShells.Length; i++)
            {
                Assert.That(ReadField<int>(subShells[i], "_damage"), Is.EqualTo(10));
                Assert.That(ReadField<bool>(ReadField<MortarUpgradePayload>(subShells[i], "_payload"), "UseGrapeshot"), Is.False);
                Assert.That(ReadField<Vector3>(subShells[i], "_target").y, Is.LessThan(ReadField<Vector3>(subShells[i], "_start").y));
                Assert.That(ReadField<bool>(subShells[i], "_useGrapeshotVfx"), Is.True);
            }
        }
        finally
        {
            foreach (MortarShellImpact impact in Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None))
                Object.DestroyImmediate(impact.gameObject);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void MortarGrapeshotVfx_UsesYellowShellVisuals()
    {
        MortarShellImpact shell = null;

        try
        {
            shell = MortarShellImpact.Launch(
                Vector3.zero,
                Vector3.forward * 4f,
                1f,
                0f,
                20,
                2f,
                0f,
                0f,
                0.2f,
                null,
                new MortarUpgradePayload(true, 15, 70f, 0.5f, 1, 0f));

            LineRenderer line = shell.GetComponent<LineRenderer>();
            Assert.That(line, Is.Not.Null);
            AssertYellow(line.startColor);

            Renderer visualRenderer = shell.GetComponentInChildren<Renderer>();
            Assert.That(visualRenderer, Is.Not.Null);
            AssertYellow(visualRenderer.sharedMaterial.color);
        }
        finally
        {
            if (shell != null)
                Object.DestroyImmediate(shell.gameObject);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void MortarGrapeshotShell_DetonatesHalfwayThroughDownwardTrajectory()
    {
        MortarShellImpact shell = null;

        try
        {
            shell = MortarShellImpact.Launch(
                Vector3.zero,
                Vector3.forward * 4f,
                1f,
                4f,
                20,
                2f,
                0f,
                0f,
                0.2f,
                null,
                new MortarUpgradePayload(true, 15, 70f, 0.5f, 1, 0f));

            SetPrivateField(shell, "_elapsed", 0.74f);
            InvokePrivate(shell, "Update");
            Assert.That(Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None), Has.Length.EqualTo(1));

            SetPrivateField(shell, "_elapsed", 0.75f);
            InvokePrivate(shell, "Update");
            MortarShellImpact[] subShells = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None);
            Assert.That(subShells, Has.Length.EqualTo(15));
            for (int i = 0; i < subShells.Length; i++)
                Assert.That(ReadField<bool>(ReadField<MortarUpgradePayload>(subShells[i], "_payload"), "UseGrapeshot"), Is.False);
        }
        finally
        {
            foreach (MortarShellImpact impact in Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None))
                Object.DestroyImmediate(impact.gameObject);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void MortarGrapeshotShell_AirburstUsesExactMidDownwardPointWhenFrameOvershoots()
    {
        MortarShellImpact shell = null;

        try
        {
            shell = MortarShellImpact.Launch(
                Vector3.zero,
                Vector3.forward * 4f,
                1f,
                4f,
                20,
                2f,
                0f,
                0f,
                0.2f,
                null,
                new MortarUpgradePayload(true, 15, 70f, 0.5f, 1, 0f));

            SetPrivateField(shell, "_elapsed", 0.9f);
            InvokePrivate(shell, "Update");

            MortarShellImpact[] subShells = Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None);
            Assert.That(subShells, Has.Length.EqualTo(15));
            for (int i = 0; i < subShells.Length; i++)
                Assert.That(ReadField<Vector3>(subShells[i], "_start").y, Is.EqualTo(3f).Within(0.001f));
        }
        finally
        {
            foreach (MortarShellImpact impact in Object.FindObjectsByType<MortarShellImpact>(FindObjectsSortMode.None))
                Object.DestroyImmediate(impact.gameObject);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void MortarGrapeshotShell_AirburstUsesGroundImpactBeforeBelowGroundTarget()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        MortarShellImpact shell = null;

        try
        {
            floor.transform.position = new Vector3(0f, -0.05f, 0f);
            floor.transform.localScale = new Vector3(10f, 0.1f, 10f);
            Physics.SyncTransforms();

            shell = MortarShellImpact.Launch(
                Vector3.up * 2f,
                new Vector3(0f, -8f, 4f),
                1f,
                2f,
                20,
                2f,
                0f,
                0f,
                0.2f,
                null,
                new MortarUpgradePayload(true, 15, 70f, 0.5f, 1, 0f));

            float airburstTime = InvokePrivate<float>(shell, "GetGrapeshotAirburstNormalizedTime");

            Assert.That(airburstTime, Is.LessThan(0.4f));
            Assert.That(ReadField<Vector3>(shell, "_target").y, Is.LessThan(0f));
        }
        finally
        {
            if (shell != null)
                Object.DestroyImmediate(shell.gameObject);
            Object.DestroyImmediate(floor);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void MortarMultiChargedVfx_UsesVioletShellTrajectoryAndExplosion()
    {
        MortarShellImpact shell = null;

        try
        {
            shell = MortarShellImpact.Launch(
                Vector3.up * 2f,
                Vector3.zero,
                1f,
                2f,
                20,
                2f,
                0f,
                0f,
                0.2f,
                null,
                new MortarUpgradePayload(false, 0, 0f, 0f, 3, 2f));

            LineRenderer line = shell.GetComponent<LineRenderer>();
            Assert.That(line, Is.Not.Null);
            AssertViolet(line.startColor);

            Renderer visualRenderer = GetShellVisualRenderer(shell);
            AssertViolet(visualRenderer.sharedMaterial.color);

            InvokePrivateWithSignature(shell, "Detonate", new[] { typeof(Vector3) }, Vector3.zero);

            ExplosionRadiusVfx explosion = Object.FindAnyObjectByType<ExplosionRadiusVfx>();
            Assert.That(explosion, Is.Not.Null);
            LineRenderer explosionLine = explosion.GetComponentInChildren<LineRenderer>();
            Assert.That(explosionLine, Is.Not.Null);
            AssertViolet(explosionLine.startColor);
        }
        finally
        {
            if (shell != null)
                Object.DestroyImmediate(shell.gameObject);
            DestroyGeneratedVfx();
        }
    }

    [Test]
    public void MortarShellImpact_HidesTrajectoryVfxAfterDetonation()
    {
        MortarShellImpact shell = null;

        try
        {
            shell = MortarShellImpact.Launch(
                Vector3.up * 2f,
                Vector3.zero,
                1f,
                2f,
                20,
                2f,
                0f,
                0f,
                0.2f,
                null,
                new MortarUpgradePayload(false, 0, 0f, 0f, 3, 2f));

            LineRenderer line = shell.GetComponent<LineRenderer>();
            Assert.That(line, Is.Not.Null);
            Assert.That(line.enabled, Is.True);

            InvokePrivateWithSignature(shell, "Detonate", new[] { typeof(Vector3) }, Vector3.zero);

            Assert.That(line.enabled, Is.False);
            Assert.That(GetShellVisualRenderer(shell).gameObject.activeSelf, Is.False);
        }
        finally
        {
            if (shell != null)
                Object.DestroyImmediate(shell.gameObject);
            DestroyGeneratedVfx();
        }
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

    private static List<LineRenderer> GetEnabledRotatingBladeContactLines(RotatingBladeVfx vfx)
    {
        return GetEnabledLineRenderersStartingWith(vfx, "Blade Contact");
    }

    private static List<LineRenderer> GetEnabledLineRenderersStartingWith(RotatingBladeVfx vfx, string namePrefix)
    {
        List<LineRenderer> lines = new();
        LineRenderer[] renderers = vfx.GetComponentsInChildren<LineRenderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            LineRenderer renderer = renderers[i];
            if (!renderer.enabled || !renderer.name.StartsWith(namePrefix, System.StringComparison.Ordinal))
                continue;

            lines.Add(renderer);
        }

        return lines;
    }

    private static List<StatDefinition> CreateDefaultStatDefinitions(float projectileAreaSize = 1f, float eliteDamageMultiplier = 1f)
    {
        return new List<StatDefinition>
        {
            CreateDefinition(StatType.DamageMultiplier, 1f),
            CreateDefinition(StatType.EliteDamageMultiplier, eliteDamageMultiplier),
            CreateDefinition(StatType.CriticalChance, 0f),
            CreateDefinition(StatType.CriticalDamage, 2f),
            CreateDefinition(StatType.AttackSpeedMultiplier, 1f),
            CreateDefinition(StatType.AmmoMultiplier, 1f),
            CreateDefinition(StatType.Knockback, 1f),
            CreateDefinition(StatType.ProjectileAreaSize, projectileAreaSize),
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

    private static bool TryGetMortarCollision(MortarShellImpact shell, Vector3 start, Vector3 end, out Vector3 collisionPoint)
    {
        MethodInfo method = typeof(MortarShellImpact).GetMethod(
            "TryGetCollision",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null, "Missing MortarShellImpact.TryGetCollision.");
        object[] arguments = { start, end, Vector3.zero };
        bool found = (bool)method.Invoke(shell, arguments);
        collisionPoint = (Vector3)arguments[2];
        return found;
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

    private static T ReadStaticField<T>(System.Type type, string fieldName)
    {
        FieldInfo field = type.GetField(fieldName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing static field {fieldName} on {type.Name}");
        return (T)field.GetValue(null);
    }

    private static void AssertDarkRed(Color color)
    {
        Assert.That(color.r, Is.GreaterThanOrEqualTo(0.35f));
        Assert.That(color.g, Is.LessThanOrEqualTo(0.16f));
        Assert.That(color.b, Is.LessThanOrEqualTo(0.16f));
        Assert.That(color.r, Is.GreaterThan(color.g * 2f));
        Assert.That(color.r, Is.GreaterThan(color.b * 2f));
    }

    private static void AssertYellow(Color color)
    {
        Assert.That(color.r, Is.GreaterThanOrEqualTo(0.85f));
        Assert.That(color.g, Is.GreaterThanOrEqualTo(0.7f));
        Assert.That(color.b, Is.LessThanOrEqualTo(0.35f));
        Assert.That(color.g, Is.GreaterThan(color.b * 2f));
    }

    private static void AssertViolet(Color color)
    {
        Assert.That(color.r, Is.GreaterThanOrEqualTo(0.45f));
        Assert.That(color.b, Is.GreaterThanOrEqualTo(0.75f));
        Assert.That(color.g, Is.LessThanOrEqualTo(0.55f));
        Assert.That(color.b, Is.GreaterThan(color.g * 1.5f));
    }

    private static void AssertDarkPurple(Color color)
    {
        Assert.That(color.r, Is.GreaterThanOrEqualTo(0.25f));
        Assert.That(color.r, Is.LessThanOrEqualTo(0.5f));
        Assert.That(color.g, Is.LessThanOrEqualTo(0.15f));
        Assert.That(color.b, Is.GreaterThanOrEqualTo(0.35f));
        Assert.That(color.b, Is.LessThanOrEqualTo(0.7f));
        Assert.That(color.b, Is.GreaterThan(color.g * 3f));
    }

    private static void AssertPeach(Color color)
    {
        Assert.That(color.r, Is.EqualTo(1f).Within(0.001f));
        Assert.That(color.g, Is.EqualTo(0.68f).Within(0.001f));
        Assert.That(color.b, Is.EqualTo(0.48f).Within(0.001f));
    }

    private static Renderer GetShellVisualRenderer(Component root)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is LineRenderer)
                continue;

            return renderer;
        }

        Assert.Fail("Missing mortar shell visual renderer.");
        return null;
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

        foreach (RocketTargetMarkerVfx marker in Object.FindObjectsByType<RocketTargetMarkerVfx>(FindObjectsSortMode.None))
            Object.DestroyImmediate(marker.gameObject);

        foreach (HeadHunterChargeVfx vfx in Object.FindObjectsByType<HeadHunterChargeVfx>(FindObjectsSortMode.None))
            Object.DestroyImmediate(vfx.gameObject);

        foreach (RotatingBladeVfx vfx in Object.FindObjectsByType<RotatingBladeVfx>(FindObjectsSortMode.None))
            Object.DestroyImmediate(vfx.gameObject);

        System.Type auraVfxType = typeof(Projectile).Assembly.GetType("WeaponStatusAuraVfx");
        if (auraVfxType != null)
        {
            foreach (Object vfx in Object.FindObjectsByType(auraVfxType, FindObjectsSortMode.None))
            {
                if (vfx is Component component)
                    Object.DestroyImmediate(component.gameObject);
            }
        }

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

    private static WeaponData CreateRocketTestData()
    {
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        data.WeaponId = "TestRocket";
        data.DisplayName = "Test Rocket";
        data.WeaponType = WeaponType.RocketLauncher;
        data.BaseDamage = 10f;
        data.BaseAttackRate = 1f;
        data.BaseRange = 12f;
        data.BaseManualAmmo = 100f;
        data.ActiveAbilityAmmoCost = 20f;
        data.EnsureSpecificTuningForCurrentType();
        data.LevelData = new List<WeaponLevelData>
        {
            new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
            new() { Level = 6, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
        };
        data.PathA = new WeaponUpgradePathData { PathName = "Kinetic Explosion", DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoOverride = -1f };
        data.PathB = new WeaponUpgradePathData { PathName = "Fragmentation Cap", DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoOverride = -1f };
        return data;
    }

    private static RocketLauncherWeapon CreateRocketLauncherWeapon(WeaponUpgradePath path, WeaponData data, Transform owner, Transform spawn)
    {
        WeaponInstance instance = new()
        {
            Data = data,
            Level = 6,
            SelectedPath = path,
            State = WeaponState.Manual,
            CurrentAmmo = 100f
        };

        RocketLauncherWeapon weapon = new(null, null, spawn);
        weapon.Setup(instance, owner, null, null);
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
