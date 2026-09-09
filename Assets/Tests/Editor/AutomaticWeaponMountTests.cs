using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class AutomaticWeaponMountTests
{
    private readonly List<Object> _cleanup = new();
    private readonly List<Transform> _registeredEnemies = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = 0; i < _registeredEnemies.Count; i++)
            EnemyRegistry.Unregister(_registeredEnemies[i]);
        _registeredEnemies.Clear();

        for (int i = _cleanup.Count - 1; i >= 0; i--)
        {
            if (_cleanup[i] != null)
                Object.DestroyImmediate(_cleanup[i]);
        }
        _cleanup.Clear();
    }

    [Test]
    public void ManualCycle_KeepsEachWeaponOnItsOwnVisibleMount()
    {
        GameObject owner = Track(new GameObject("Player"));
        Transform main = CreateChild(owner.transform, "Main", Vector3.zero);
        PlayerWeaponMountController controller = owner.AddComponent<PlayerWeaponMountController>();
        controller.Initialize(main);

        BasicProjectileWeapon first = CreateWeapon(owner.transform, WeaponType.AutomaticCannon);
        BasicProjectileWeapon second = CreateWeapon(owner.transform, WeaponType.RocketLauncher);
        BasicProjectileWeapon third = CreateWeapon(owner.transform, WeaponType.Mortar);

        controller.AddWeapon(first, true);
        controller.AddWeapon(second, false);
        controller.AddWeapon(third, false);

        AutomaticWeaponMount firstMount = controller.GetEquippedMount(first);
        AutomaticWeaponMount secondMount = controller.GetAssignedMount(second);
        AutomaticWeaponMount thirdMount = controller.GetAssignedMount(third);
        Assert.That(first.FireOrigin.Muzzle, Is.SameAs(main));
        Assert.That(firstMount, Is.Not.Null);
        Assert.That(firstMount.IsAutomatic, Is.False);
        AssertVisible(firstMount);
        Assert.That(secondMount, Is.Not.Null);
        Assert.That(thirdMount, Is.Not.Null.And.Not.SameAs(secondMount));

        first.Runtime.State = WeaponState.Automatic;
        second.Runtime.State = WeaponState.Manual;
        controller.SetManualWeapon(second);

        Assert.That(second.FireOrigin.Muzzle, Is.SameAs(main));
        Assert.That(controller.GetAssignedMount(second), Is.Null);
        Assert.That(controller.GetEquippedMount(second), Is.SameAs(secondMount));
        Assert.That(controller.GetAssignedMount(first), Is.SameAs(firstMount));
        Assert.That(controller.GetAssignedMount(third), Is.SameAs(thirdMount));
        Assert.That(first.FireOrigin.Muzzle, Is.SameAs(firstMount.Muzzle));
        Assert.That(firstMount.IsAutomatic, Is.True);
        Assert.That(secondMount.IsAutomatic, Is.False);
        AssertVisible(secondMount);

        second.Runtime.State = WeaponState.Automatic;
        third.Runtime.State = WeaponState.Manual;
        controller.SetManualWeapon(third);

        Assert.That(third.FireOrigin.Muzzle, Is.SameAs(main));
        Assert.That(controller.GetAssignedMount(first), Is.SameAs(firstMount));
        Assert.That(controller.GetAssignedMount(second), Is.SameAs(secondMount));
        Assert.That(controller.GetEquippedMount(third), Is.SameAs(thirdMount));
        Assert.That(secondMount.IsAutomatic, Is.True);
        Assert.That(thirdMount.IsAutomatic, Is.False);
        AssertVisible(thirdMount);
    }

    [Test]
    public void BladeCharm_RemainsVisibleInAutomaticAndManualMode()
    {
        GameObject owner = Track(new GameObject("Player"));
        Transform main = CreateChild(owner.transform, "Main", Vector3.zero);
        PlayerWeaponMountController controller = owner.AddComponent<PlayerWeaponMountController>();
        controller.Initialize(main);

        BasicProjectileWeapon manual = CreateWeapon(owner.transform, WeaponType.AutomaticCannon);
        BasicProjectileWeapon blade = CreateWeapon(owner.transform, WeaponType.RotatingBlade);
        controller.AddWeapon(manual, true);
        controller.AddWeapon(blade, false);

        AutomaticWeaponMount mount = controller.GetAssignedMount(blade);
        Assert.That(mount, Is.Not.Null);
        Assert.That(mount.IsAutomatic, Is.True);
        AssertVisible(mount);

        manual.Runtime.State = WeaponState.Automatic;
        blade.Runtime.State = WeaponState.Manual;
        controller.SetManualWeapon(blade);

        Assert.That(controller.GetEquippedMount(blade), Is.SameAs(mount));
        Assert.That(controller.GetAssignedMount(blade), Is.Null);
        Assert.That(blade.FireOrigin.Muzzle, Is.SameAs(main));
        Assert.That(mount.IsAutomatic, Is.False);
        AssertVisible(mount);
    }

    [Test]
    public void Equip_ShowsOnlyOwnedWeaponModels()
    {
        GameObject owner = Track(new GameObject("Player"));
        PlayerWeaponMountController controller = owner.AddComponent<PlayerWeaponMountController>();
        controller.Initialize(CreateChild(owner.transform, "Main", Vector3.zero));
        Assert.That(owner.GetComponentsInChildren<Renderer>(false), Is.Empty);

        BasicProjectileWeapon cannon = CreateWeapon(owner.transform, WeaponType.AutomaticCannon);
        BasicProjectileWeapon unequippedRocket = CreateWeapon(owner.transform, WeaponType.RocketLauncher);
        controller.AddWeapon(cannon, true);

        AutomaticWeaponMount mount = controller.GetEquippedMount(cannon);
        Assert.That(mount, Is.Not.Null);
        AssertVisible(mount);
        Assert.That(controller.GetEquippedMount(unequippedRocket), Is.Null);
        foreach (Renderer renderer in owner.GetComponentsInChildren<Renderer>(false))
            Assert.That(renderer.transform.IsChildOf(mount.transform), Is.True, renderer.name);
    }

    [Test]
    public void RemoveAndClear_HideModelsAndDropAssignments_ThenAllowReequip()
    {
        GameObject owner = Track(new GameObject("Player"));
        Transform main = CreateChild(owner.transform, "Main", Vector3.zero);
        PlayerWeaponMountController controller = owner.AddComponent<PlayerWeaponMountController>();
        controller.Initialize(main);
        BasicProjectileWeapon cannon = CreateWeapon(owner.transform, WeaponType.AutomaticCannon);
        BasicProjectileWeapon rocket = CreateWeapon(owner.transform, WeaponType.RocketLauncher);
        controller.AddWeapon(cannon, true);
        controller.AddWeapon(rocket, false);
        AutomaticWeaponMount cannonMount = controller.GetEquippedMount(cannon);

        controller.RemoveWeapon(cannon);

        Assert.That(controller.GetEquippedMount(cannon), Is.Null);
        Assert.That(controller.GetAssignedMount(cannon), Is.Null);
        AssertHiddenOrDestroyed(cannonMount);
        AssertVisible(controller.GetEquippedMount(rocket));

        controller.ClearWeapons();

        Assert.That(controller.GetAssignedMount(rocket), Is.Null);
        Assert.That(controller.GetEquippedMount(rocket), Is.Null);
        Assert.That(owner.GetComponentsInChildren<Renderer>(false), Is.Empty);

        controller.AddWeapon(cannon, false);

        AutomaticWeaponMount restored = controller.GetAssignedMount(cannon);
        Assert.That(restored, Is.Not.Null);
        Assert.That(cannon.FireOrigin.Muzzle, Is.SameAs(restored.Muzzle));
        Assert.That(restored.IsAutomatic, Is.True);
        AssertVisible(restored);
    }

    [Test]
    public void DuplicateWeaponTypes_ShareOneVisibleModelUntilLastCopyIsRemoved()
    {
        GameObject owner = Track(new GameObject("Player"));
        Transform main = CreateChild(owner.transform, "Main", Vector3.zero);
        PlayerWeaponMountController controller = owner.AddComponent<PlayerWeaponMountController>();
        controller.Initialize(main);
        BasicProjectileWeapon manual = CreateWeapon(owner.transform, WeaponType.AutomaticCannon);
        BasicProjectileWeapon automatic = CreateWeapon(owner.transform, WeaponType.AutomaticCannon);
        manual.Runtime.State = WeaponState.Manual;
        controller.AddWeapon(manual, true);
        AutomaticWeaponMount mount = controller.GetEquippedMount(manual);
        int rendererCount = owner.GetComponentsInChildren<Renderer>(false).Length;

        controller.AddWeapon(automatic, false);

        Assert.That(controller.GetEquippedMount(automatic), Is.SameAs(mount));
        Assert.That(owner.GetComponentsInChildren<Renderer>(false), Has.Length.EqualTo(rendererCount));
        Assert.That(manual.FireOrigin.Muzzle, Is.SameAs(main));
        Assert.That(automatic.FireOrigin.Muzzle, Is.SameAs(mount.Muzzle));
        Assert.That(mount.IsAutomatic, Is.True, "An automatic copy still uses the shared emitter.");

        controller.RemoveWeapon(automatic);

        Assert.That(controller.GetEquippedMount(manual), Is.SameAs(mount));
        Assert.That(mount.IsAutomatic, Is.False);
        AssertVisible(mount);

        controller.RemoveWeapon(manual);

        Assert.That(owner.GetComponentsInChildren<Renderer>(false), Is.Empty);
    }

    [Test]
    public void ForcedRuntimeModes_RefreshOriginsAndLightsWithoutCyclingManualSlot()
    {
        GameObject owner = Track(new GameObject("Player"));
        Transform main = CreateChild(owner.transform, "Main", Vector3.zero);
        PlayerWeaponMountController controller = owner.AddComponent<PlayerWeaponMountController>();
        controller.Initialize(main);
        BasicProjectileWeapon cannon = CreateWeapon(owner.transform, WeaponType.AutomaticCannon);
        BasicProjectileWeapon flame = CreateWeapon(owner.transform, WeaponType.Flamethrower);
        controller.AddWeapon(cannon, true);
        controller.AddWeapon(flame, false);
        AutomaticWeaponMount cannonMount = controller.GetEquippedMount(cannon);
        AutomaticWeaponMount flameMount = controller.GetEquippedMount(flame);

        cannon.Runtime.State = WeaponState.Automatic;
        flame.Runtime.State = WeaponState.Automatic;
        controller.RefreshWeaponModes();

        Assert.That(controller.GetAssignedMount(cannon), Is.SameAs(cannonMount));
        Assert.That(cannon.FireOrigin.Muzzle, Is.SameAs(cannonMount.Muzzle));
        Assert.That(cannonMount.IsAutomatic, Is.True);
        Assert.That(flameMount.IsAutomatic, Is.True);

        cannon.Runtime.State = WeaponState.Manual;
        flame.Runtime.State = WeaponState.Manual;
        controller.RefreshWeaponModes();

        Assert.That(cannon.FireOrigin.Muzzle, Is.SameAs(main));
        Assert.That(flame.FireOrigin.Muzzle, Is.SameAs(main));
        Assert.That(controller.GetAssignedMount(cannon), Is.Null);
        Assert.That(controller.GetAssignedMount(flame), Is.Null);
        Assert.That(cannonMount.IsAutomatic, Is.False);
        Assert.That(flameMount.IsAutomatic, Is.False);
        AssertVisible(cannonMount);
        AssertVisible(flameMount);
    }

    [Test]
    public void EmptyManualAmmo_UpdatesRealMountLightsAndFiresNextAutomaticRoundFromCannon()
    {
        GameObject owner = Track(new GameObject("Player"));
        Transform main = CreateChild(owner.transform, "Main", new Vector3(0f, 0.3f, 1f));
        ProjectilePool pool = CreateProjectilePool(out Transform projectileContainer);
        WeaponData cannonData = CreateWeaponData(WeaponType.AutomaticCannon);
        cannonData.BaseManualAmmo = 3f;
        cannonData.BaseRange = 12f;
        cannonData.AutoTargetingMode = WeaponTargetingMode.ClosestInRange;
        cannonData.AutomaticAimConstraint = WeaponAutomaticAimConstraint.BodyForward180;
        WeaponData rocketData = CreateWeaponData(WeaponType.RocketLauncher);
        rocketData.BaseManualAmmo = 3f;
        WeaponManager manager = owner.AddComponent<WeaponManager>();
        SetPrivateField(manager, "_projectileSpawn", main);
        SetPrivateField(manager, "_projectilePool", pool);
        SetPrivateField(manager, "_startingWeapons", new List<WeaponData> { cannonData, rocketData });
        InvokePrivate(manager, "Awake");
        PlayerWeaponMountController controller = owner.GetComponent<PlayerWeaponMountController>();
        IReadOnlyList<IWeaponBehaviour> weapons = manager.GetEquippedWeapons();
        Assert.That(weapons, Has.Count.EqualTo(2));
        AutomaticWeaponMount cannonMount = controller.GetEquippedMount(weapons[0]);
        AutomaticWeaponMount rocketMount = controller.GetEquippedMount(weapons[1]);
        AssertNativeIndicatorState(cannonMount, false);
        AssertNativeIndicatorState(rocketMount, true);
        weapons[0].Runtime.CurrentAmmo = 0f;

        InvokePrivate(manager, "UpdateManualWeapon", 0f, Vector3.forward);

        Assert.That(weapons[0].Runtime.State, Is.EqualTo(WeaponState.Automatic));
        Assert.That(weapons[1].Runtime.State, Is.EqualTo(WeaponState.Manual));
        Assert.That(((IWeaponFireOriginReceiver)weapons[0]).FireOrigin.Muzzle, Is.SameAs(cannonMount.Muzzle));
        Assert.That(((IWeaponFireOriginReceiver)weapons[1]).FireOrigin.Muzzle, Is.SameAs(main));
        AssertNativeIndicatorState(cannonMount, true);
        AssertNativeIndicatorState(rocketMount, false);
        GameObject enemy = Track(new GameObject("Enemy Ahead"));
        enemy.transform.position = Vector3.forward * 6f;
        EnemyRegistry.Register(enemy.transform);
        _registeredEnemies.Add(enemy.transform);

        InvokePrivate(manager, "UpdateAutomaticWeapons", 0.1f, Vector3.forward);

        Projectile[] projectiles = projectileContainer.GetComponentsInChildren<Projectile>(false);
        Assert.That(projectiles, Has.Length.EqualTo(1));
        Assert.That(Vector3.Distance(projectiles[0].transform.position, cannonMount.Muzzle.position), Is.LessThan(0.0001f));
    }

    [TestCase(WeaponType.RotatingBlade)]
    [TestCase(WeaponType.AutomaticCannon)]
    [TestCase(WeaponType.Flamethrower)]
    [TestCase(WeaponType.RocketLauncher)]
    [TestCase(WeaponType.Mortar)]
    public void ImportedWearable_HasGreenIndicatorSlotsThatFollowRuntimeMode(WeaponType type)
    {
        GameObject owner = Track(new GameObject("Player"));
        Transform main = CreateChild(owner.transform, "Main", Vector3.zero);
        PlayerWeaponMountController controller = owner.AddComponent<PlayerWeaponMountController>();
        controller.Initialize(main);
        BasicProjectileWeapon weapon = CreateWeapon(owner.transform, type);
        weapon.Runtime.State = WeaponState.Manual;
        controller.AddWeapon(weapon, true);
        AutomaticWeaponMount mount = controller.GetEquippedMount(weapon);
        AssertNativeIndicatorState(mount, false);

        weapon.Runtime.State = WeaponState.Automatic;
        controller.RefreshWeaponModes();

        Assert.That(weapon.FireOrigin.Muzzle, Is.SameAs(mount.Muzzle));
        AssertNativeIndicatorState(mount, true);

        weapon.Runtime.State = WeaponState.Manual;
        controller.RefreshWeaponModes();

        Assert.That(weapon.FireOrigin.Muzzle, Is.SameAs(main));
        AssertNativeIndicatorState(mount, false);
        AssertVisible(mount);
    }

    [Test]
    public void GreenIndicator_ChangesOnlyItsMaterialSlot_AndPreservesSharedMaterials()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Assert.That(shader, Is.Not.Null);
        Material body = Track(new Material(shader) { name = "Rust Body", color = Color.red });
        Material indicator = Track(new Material(shader) { name = "AutoIndicator_Green", color = Color.green });
        indicator.SetColor("_EmissionColor", Color.green * 2f);
        Color bodySharedColor = body.color;
        Color indicatorSharedColor = indicator.color;
        Color indicatorSharedEmission = indicator.GetColor("_EmissionColor");
        GameObject owner = Track(new GameObject("Owner"));
        GameObject root = CreateChild(owner.transform, "Wearable", Vector3.zero).gameObject;
        Transform pivot = CreateChild(root.transform, "Pivot", Vector3.zero);
        GameObject visual = CreateChild(pivot, "Visual", Vector3.zero).gameObject;
        MeshRenderer renderer = visual.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = new[] { body, indicator };
        Color bodyTint = new(0.6f, 0.25f, 0.1f, 1f);
        MaterialPropertyBlock bodyBlock = new();
        bodyBlock.SetColor("_BaseColor", bodyTint);
        bodyBlock.SetColor("_Color", bodyTint);
        renderer.SetPropertyBlock(bodyBlock, 0);
        AutomaticWeaponMount mount = root.AddComponent<AutomaticWeaponMount>();
        mount.Configure(owner.transform, pivot, null, root.transform, visual);
        mount.ConfigureWearable();
        mount.Bind(CreateWeapon(owner.transform, WeaponType.AutomaticCannon).Runtime);

        mount.SetAutomatic(true);

        MaterialPropertyBlock indicatorBlock = new();
        renderer.GetPropertyBlock(indicatorBlock, 1);
        Color illuminated = indicatorBlock.GetColor("_BaseColor");
        Color illuminatedEmission = indicatorBlock.GetColor("_EmissionColor");
        Assert.That(illuminated.g, Is.GreaterThan(illuminated.r));
        Assert.That(illuminatedEmission.g, Is.GreaterThan(0f));
        Assert.That(indicatorBlock.GetColor("_Color"), Is.EqualTo(illuminated));

        mount.SetAutomatic(false);

        renderer.GetPropertyBlock(indicatorBlock, 1);
        Assert.That(indicatorBlock.GetColor("_EmissionColor").maxColorComponent, Is.Zero);
        Assert.That(indicatorBlock.GetColor("_BaseColor").g, Is.LessThan(illuminated.g));
        renderer.GetPropertyBlock(bodyBlock, 0);
        AssertColorWithinFloatPrecision(bodyBlock.GetColor("_BaseColor"), bodyTint);
        AssertColorWithinFloatPrecision(bodyBlock.GetColor("_Color"), bodyTint);
        MaterialPropertyBlock rendererBlock = new();
        renderer.GetPropertyBlock(rendererBlock);
        Assert.That(rendererBlock.isEmpty, Is.True, "Tinting the whole renderer also recolors the prop body.");
        Assert.That(renderer.sharedMaterials[0], Is.SameAs(body));
        Assert.That(renderer.sharedMaterials[1], Is.SameAs(indicator));
        Assert.That(body.color, Is.EqualTo(bodySharedColor));
        Assert.That(indicator.color, Is.EqualTo(indicatorSharedColor));
        Assert.That(indicator.GetColor("_EmissionColor"), Is.EqualTo(indicatorSharedEmission));
    }

    [Test]
    public void AllFiveWearables_HaveDistinctNonOverlappingBodyPlacements()
    {
        GameObject owner = Track(new GameObject("Player"));
        PlayerWeaponMountController controller = owner.AddComponent<PlayerWeaponMountController>();
        controller.Initialize(CreateChild(owner.transform, "Main", Vector3.zero));
        WeaponType[] types =
        {
            WeaponType.RotatingBlade, WeaponType.AutomaticCannon, WeaponType.Flamethrower,
            WeaponType.RocketLauncher, WeaponType.Mortar
        };
        Bounds[] bounds = new Bounds[types.Length];
        for (int i = 0; i < types.Length; i++)
        {
            BasicProjectileWeapon weapon = CreateWeapon(owner.transform, types[i]);
            controller.AddWeapon(weapon, false);
            AutomaticWeaponMount mount = controller.GetAssignedMount(weapon);
            Assert.That(mount, Is.Not.Null, types[i].ToString());
            bounds[i] = GetVisibleBounds(mount);
            Assert.That(bounds[i].size.sqrMagnitude, Is.GreaterThan(0f), types[i].ToString());
        }

        for (int i = 0; i < bounds.Length; i++)
        {
            for (int j = i + 1; j < bounds.Length; j++)
                Assert.That(bounds[i].Intersects(bounds[j]), Is.False, $"{types[i]} overlaps {types[j]}.");
        }

        Assert.That(Mathf.Abs(bounds[0].center.x), Is.GreaterThan(0.1f), "Sword charm sits on one side of the waist.");
        Assert.That(bounds[0].center.y, Is.LessThan(bounds[1].center.y), "Waist charm stays below the shoulder cannon.");
        Assert.That(bounds[2].center.x, Is.LessThan(0f), "Flamethrower belongs to the left arm.");
        Assert.That(bounds[3].center.z, Is.LessThan(0f), "Rocket belongs on the back.");
        Assert.That(bounds[4].center.z, Is.LessThan(0f), "Mortar belongs on the back.");
    }

    [TestCase(WeaponType.RotatingBlade)]
    [TestCase(WeaponType.AutomaticCannon)]
    [TestCase(WeaponType.Flamethrower)]
    [TestCase(WeaponType.RocketLauncher)]
    [TestCase(WeaponType.Mortar)]
    public void WearableGeometry_RemainsFixedWhenAimingAndRequestingRecoil(WeaponType type)
    {
        GameObject owner = Track(new GameObject("Player"));
        PlayerWeaponMountController controller = owner.AddComponent<PlayerWeaponMountController>();
        controller.Initialize(CreateChild(owner.transform, "Main", Vector3.zero));
        BasicProjectileWeapon weapon = CreateWeapon(owner.transform, type);
        controller.AddWeapon(weapon, false);
        AutomaticWeaponMount mount = controller.GetAssignedMount(weapon);
        Assert.That(mount, Is.Not.Null);
        Transform[] transforms = mount.GetComponentsInChildren<Transform>(true);
        Vector3[] positions = new Vector3[transforms.Length];
        Quaternion[] rotations = new Quaternion[transforms.Length];
        Vector3[] scales = new Vector3[transforms.Length];
        for (int i = 0; i < transforms.Length; i++)
        {
            positions[i] = transforms[i].localPosition;
            rotations[i] = transforms[i].localRotation;
            scales[i] = transforms[i].localScale;
        }

        Transform target = CreateChild(owner.transform, "Target Behind", new Vector3(-5f, 2f, -10f));
        mount.AimAt(target, target.position);
        mount.RequestRecoil(1f);
        InvokePrivate(mount, "LateUpdate");
        mount.AimAlong(Vector3.left);
        InvokePrivate(mount, "LateUpdate");

        for (int i = 0; i < transforms.Length; i++)
        {
            Assert.That(Vector3.Distance(transforms[i].localPosition, positions[i]), Is.LessThan(0.00001f), transforms[i].name);
            Assert.That(Quaternion.Angle(transforms[i].localRotation, rotations[i]), Is.LessThan(0.001f), transforms[i].name);
            Assert.That(transforms[i].localScale, Is.EqualTo(scales[i]), transforms[i].name);
        }
    }

    [Test]
    public void Full360Targeting_AcquiresEnemyBehindOwner_WhileCameraConeDoesNot()
    {
        GameObject owner = Track(new GameObject("Owner"));
        owner.transform.rotation = Quaternion.identity;
        GameObject enemy = Track(new GameObject("Enemy Behind"));
        enemy.transform.position = new Vector3(0f, 0f, -5f);
        EnemyRegistry.Register(enemy.transform);
        _registeredEnemies.Add(enemy.transform);

        WeaponData data = Track(ScriptableObject.CreateInstance<WeaponData>());
        data.AutoTargetingMode = WeaponTargetingMode.ClosestInRange;
        WeaponInstance runtime = new() { Data = data };
        ConfiguredEnemyTargeting targeting = new();

        data.AutomaticAimConstraint = WeaponAutomaticAimConstraint.CameraCone;
        bool coneFound = targeting.TryGetTarget(runtime, owner.transform, 10f, Vector3.forward, out _);

        data.AutomaticAimConstraint = WeaponAutomaticAimConstraint.Full360;
        bool fullCircleFound = targeting.TryGetTarget(runtime, owner.transform, 10f, Vector3.forward, out Transform target);

        Assert.That(coneFound, Is.False);
        Assert.That(fullCircleFound, Is.True);
        Assert.That(target, Is.SameAs(enemy.transform));
    }

    [Test]
    public void AutomaticFlamethrowerDirection_UsesBodyForward()
    {
        GameObject owner = Track(new GameObject("Owner"));
        owner.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        Transform spawn = CreateChild(owner.transform, "Spawn", Vector3.zero);
        WeaponData data = Track(ScriptableObject.CreateInstance<WeaponData>());
        data.WeaponType = WeaponType.Flamethrower;
        WeaponInstance runtime = new() { Data = data, State = WeaponState.Automatic };
        FlamethrowerWeapon weapon = new(null, null, spawn, null);
        weapon.Setup(runtime, owner.transform, null, null);

        MethodInfo method = typeof(FlamethrowerWeapon).GetMethod(
            "GetAutomaticFlameDirection",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Vector3 direction = (Vector3)method.Invoke(weapon, null);

        Assert.That(Vector3.Angle(direction, owner.transform.forward), Is.LessThan(0.01f));
    }

    [Test]
    public void PlayerPrefab_WiresMainFirePointAndMountController()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab");
        Assert.That(prefab, Is.Not.Null);

        WeaponManager manager = prefab.GetComponent<WeaponManager>();
        PlayerWeaponMountController mounts = prefab.GetComponent<PlayerWeaponMountController>();
        Assert.That(manager, Is.Not.Null);
        Assert.That(mounts, Is.Not.Null);
        Assert.That(manager.GetProjectileSpawn(), Is.Not.Null);
        Assert.That(manager.GetProjectileSpawn().name, Is.EqualTo("Main Weapon Fire Point"));
    }

    [Test]
    public void DirectionalWeaponAssets_UseApprovedCannonFrontArcAndRocketFull360()
    {
        WeaponData cannon = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/AutomaticCannon.asset");
        WeaponData sandboxCannon = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/Scripts/Weapon/Testing/SO/Sandbox_AutomaticCannon.asset");
        WeaponData rocket = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/RocketLauncher.asset");

        Assert.That(cannon, Is.Not.Null);
        Assert.That(sandboxCannon, Is.Not.Null);
        Assert.That(rocket, Is.Not.Null);
        Assert.That(cannon.AutomaticAimConstraint, Is.EqualTo(WeaponAutomaticAimConstraint.BodyForward180));
        Assert.That(sandboxCannon.AutomaticAimConstraint, Is.EqualTo(WeaponAutomaticAimConstraint.BodyForward180));
        Assert.That(rocket.AutomaticAimConstraint, Is.EqualTo(WeaponAutomaticAimConstraint.Full360));
    }

    [Test]
    public void CannonOriginChange_CancelsPendingBurstBeforeRoleSwap()
    {
        GameObject owner = Track(new GameObject("Owner"));
        Transform firstOrigin = CreateChild(owner.transform, "Shoulder", Vector3.zero);
        Transform secondOrigin = CreateChild(owner.transform, "Main", Vector3.forward);
        WeaponData data = Track(ScriptableObject.CreateInstance<WeaponData>());
        data.WeaponType = WeaponType.AutomaticCannon;
        WeaponInstance runtime = new() { Data = data, State = WeaponState.Automatic };
        AutomaticCannonWeapon weapon = new(null, null, firstOrigin);
        weapon.Setup(runtime, owner.transform, null, null);
        SetPrivateField(weapon, "_lineBurstActive", true);
        SetPrivateField(weapon, "_lineBurstRemaining", 2);

        weapon.SetFireOrigin(new WeaponFireOriginBinding(secondOrigin));

        Assert.That(ReadPrivate<bool>(weapon, "_lineBurstActive"), Is.False);
        Assert.That(ReadPrivate<int>(weapon, "_lineBurstRemaining"), Is.Zero);
        Assert.That(weapon.FireOrigin.Muzzle, Is.SameAs(secondOrigin));
    }

    private BasicProjectileWeapon CreateWeapon(Transform owner, WeaponType type)
    {
        WeaponData data = CreateWeaponData(type);
        WeaponInstance runtime = new() { Data = data, State = WeaponState.Automatic };
        BasicProjectileWeapon weapon = new(null, null, owner);
        weapon.Setup(runtime, owner, null, null);
        return weapon;
    }

    private WeaponData CreateWeaponData(WeaponType type)
    {
        WeaponData data = Track(ScriptableObject.CreateInstance<WeaponData>());
        data.WeaponType = type;
        data.EnsureSpecificTuningForCurrentType();
        return data;
    }

    private ProjectilePool CreateProjectilePool(out Transform container)
    {
        GameObject prefab = Track(new GameObject("Test Projectile"));
        prefab.AddComponent<Rigidbody>();
        prefab.AddComponent<SphereCollider>();
        prefab.AddComponent<Projectile>();
        prefab.SetActive(false);
        container = Track(new GameObject("Test Projectile Container")).transform;
        GameObject poolObject = Track(new GameObject("Test Projectile Pool"));
        poolObject.SetActive(false);
        ProjectilePool pool = poolObject.AddComponent<ProjectilePool>();
        SetPrivateField(pool, "_projectilePrefab", prefab);
        SetPrivateField(pool, "_container", container);
        SetPrivateField(pool, "_initialPoolSize", 1);
        SetPrivateField(pool, "_maxPoolSize", 1);
        SetPrivateField(pool, "_allowPoolGrowth", false);
        poolObject.SetActive(true);
        InvokePrivate(pool, "Awake");
        return pool;
    }

    private static Transform CreateChild(Transform parent, string name, Vector3 localPosition)
    {
        GameObject child = new(name);
        child.transform.SetParent(parent, false);
        child.transform.localPosition = localPosition;
        return child.transform;
    }

    private static void AssertVisible(AutomaticWeaponMount mount)
    {
        Assert.That(mount, Is.Not.Null);
        Renderer[] renderers = mount.GetComponentsInChildren<Renderer>(false);
        Assert.That(renderers, Is.Not.Empty, mount.name);
        Assert.That(System.Array.Exists(renderers, renderer => renderer.enabled), Is.True, mount.name);
    }

    private static void AssertColorWithinFloatPrecision(Color actual, Color expected)
    {
        // MaterialPropertyBlock color round-trips can introduce float conversion noise.
        const float tolerance = 0.000001f;
        Assert.That(actual.r, Is.EqualTo(expected.r).Within(tolerance));
        Assert.That(actual.g, Is.EqualTo(expected.g).Within(tolerance));
        Assert.That(actual.b, Is.EqualTo(expected.b).Within(tolerance));
        Assert.That(actual.a, Is.EqualTo(expected.a).Within(tolerance));
    }

    private static void AssertHiddenOrDestroyed(AutomaticWeaponMount mount)
    {
        if (mount != null)
            Assert.That(mount.GetComponentsInChildren<Renderer>(false), Is.Empty);
    }

    private static Bounds GetVisibleBounds(AutomaticWeaponMount mount)
    {
        AssertVisible(mount);
        Renderer[] renderers = mount.GetComponentsInChildren<Renderer>(false);
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            if (renderers[i].enabled)
                bounds.Encapsulate(renderers[i].bounds);
        }
        return bounds;
    }

    private static void AssertNativeIndicatorState(AutomaticWeaponMount mount, bool automatic)
    {
        Assert.That(mount, Is.Not.Null);
        Assert.That(mount.IsAutomatic, Is.EqualTo(automatic));
        int indicators = 0;
        MaterialPropertyBlock block = new();
        foreach (Renderer renderer in mount.GetComponentsInChildren<Renderer>(false))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null || !material.name.StartsWith("AutoIndicator", System.StringComparison.Ordinal))
                    continue;
                indicators++;
                block.Clear();
                renderer.GetPropertyBlock(block, i);
                Color emission = block.GetColor("_EmissionColor");
                if (automatic)
                {
                    Assert.That(material.IsKeywordEnabled("_EMISSION"), Is.True, material.name);
                    Assert.That(emission.g, Is.GreaterThan(0f), $"{mount.name}: {renderer.name} material slot {i}");
                    Assert.That(emission.g, Is.GreaterThan(emission.r));
                }
                else
                {
                    Assert.That(emission.maxColorComponent, Is.Zero, $"{mount.name}: {renderer.name} material slot {i}");
                }
            }
        }
        Assert.That(indicators, Is.GreaterThan(0), $"{mount.name} must preserve a controllable native green indicator.");
    }

    private static void InvokePrivate(object target, string methodName, params object[] arguments)
    {
        MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(method, Is.Not.Null);
        method.Invoke(target, arguments);
    }

    private T Track<T>(T value) where T : Object
    {
        _cleanup.Add(value);
        return value;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        field.SetValue(target, value);
    }

    private static T ReadPrivate<T>(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null);
        return (T)field.GetValue(target);
    }
}
