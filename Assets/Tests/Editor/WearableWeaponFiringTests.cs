using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class WearableWeaponFiringTests
{
    private readonly List<Object> _cleanup = new();
    private readonly List<Transform> _enemies = new();

    [TearDown]
    public void TearDown()
    {
        foreach (Transform enemy in _enemies)
            EnemyRegistry.Unregister(enemy);
        _enemies.Clear();
        for (int i = _cleanup.Count - 1; i >= 0; i--)
        {
            if (_cleanup[i] != null)
                Object.DestroyImmediate(_cleanup[i]);
        }
        _cleanup.Clear();
    }

    [TestCase(-91f, false)]
    [TestCase(-89f, true)]
    [TestCase(0f, true)]
    [TestCase(89f, true)]
    [TestCase(91f, false)]
    [TestCase(180f, false)]
    public void CannonBodyCone_CoversOnlyTheForwardHalf(float angle, bool expected)
    {
        Transform owner = CreateObject("Owner").transform;
        RegisterEnemy(Quaternion.AngleAxis(angle, Vector3.up) * Vector3.forward * 5f);
        WeaponInstance runtime = CreateRuntime(WeaponType.AutomaticCannon, WeaponState.Automatic);
        runtime.Data.AutomaticAimConstraint = WeaponAutomaticAimConstraint.BodyForward180;

        bool found = new ConfiguredEnemyTargeting().TryGetTarget(runtime, owner, 12f, Vector3.back, out _);

        Assert.That(found, Is.EqualTo(expected));
    }

    [Test]
    public void CannonBodyCone_UsesRotatedBodyAndSkipsCloserEnemyBehindIt()
    {
        Transform owner = CreateObject("Owner").transform;
        owner.rotation = Quaternion.Euler(0f, 90f, 0f);
        RegisterEnemy(Vector3.left * 2f);
        Transform forwardEnemy = RegisterEnemy(Vector3.right * 6f);
        WeaponInstance runtime = CreateRuntime(WeaponType.AutomaticCannon, WeaponState.Automatic);
        runtime.Data.AutomaticAimConstraint = WeaponAutomaticAimConstraint.BodyForward180;

        bool found = new ConfiguredEnemyTargeting().TryGetTarget(runtime, owner, 12f, Vector3.left, out Transform target);

        Assert.That(found, Is.True);
        Assert.That(target, Is.SameAs(forwardEnemy));
    }

    [Test]
    public void CannonPendingBurst_StopsWhenPlayerTurnsAwayFromTarget()
    {
        Transform owner = CreateObject("Owner").transform;
        Transform target = RegisterEnemy(Vector3.forward * 5f);
        ProjectilePool pool = CreatePool(out Transform container);
        WeaponInstance runtime = CreateRuntime(WeaponType.AutomaticCannon, WeaponState.Automatic);
        runtime.Data.AutomaticAimConstraint = WeaponAutomaticAimConstraint.BodyForward180;
        runtime.Data.AutomaticCannon.CannonAutoBurstCount = 3;
        runtime.Data.AutomaticCannon.CannonBurstProjectileScatterDegrees = 0f;
        AutomaticCannonWeapon weapon = new(new FixedTargeting(target), pool, owner);
        weapon.Setup(runtime, owner, null, null);
        weapon.TickAutomatic(0.01f, Vector3.forward);
        Assert.That(container.GetComponentsInChildren<Projectile>(), Has.Length.EqualTo(1));

        owner.rotation = Quaternion.Euler(0f, 180f, 0f);
        weapon.TickAutomatic(0.1f, Vector3.forward);

        Assert.That(container.GetComponentsInChildren<Projectile>(), Has.Length.EqualTo(1));
        Assert.That(ReadPrivate<bool>(weapon, "_lineBurstActive"), Is.False);
    }

    [Test]
    public void ManualCannon_CanStillShootAgainstBodyForward()
    {
        Transform owner = CreateObject("Owner").transform;
        ProjectilePool pool = CreatePool(out Transform container);
        WeaponInstance runtime = CreateRuntime(WeaponType.AutomaticCannon, WeaponState.Manual);
        runtime.Data.AutomaticAimConstraint = WeaponAutomaticAimConstraint.BodyForward180;
        runtime.Data.AutomaticCannon.CannonBurstProjectileScatterDegrees = 0f;
        AutomaticCannonWeapon weapon = new(null, pool, owner);
        weapon.Setup(runtime, owner, null, null);

        weapon.TickManual(0.1f, Vector3.back, true);

        Projectile[] projectiles = container.GetComponentsInChildren<Projectile>();
        Assert.That(projectiles, Has.Length.EqualTo(1));
        Assert.That(Vector3.Dot(projectiles[0].transform.forward, Vector3.back), Is.GreaterThan(0.99f));
    }

    [Test]
    public void AutomaticRocketVolley_AllRoundsLeaveThePipeUpwardAndRetainVolleyCount()
    {
        Transform owner = CreateObject("Owner").transform;
        BoxCollider body = owner.gameObject.AddComponent<BoxCollider>();
        body.center = Vector3.up;
        body.size = new Vector3(1f, 2f, 0.6f);
        Transform muzzle = CreateObject("Pipe muzzle").transform;
        muzzle.position = new Vector3(0.4f, 1.5f, -0.5f);
        Transform target = RegisterEnemy(new Vector3(0f, 1f, 8f));
        ProjectilePool pool = CreatePool(out Transform container);
        WeaponInstance runtime = CreateRuntime(WeaponType.RocketLauncher, WeaponState.Automatic);
        runtime.Data.RocketLauncher.RocketAutoBaseRocketCount = 4;
        RocketLauncherWeapon weapon = new(new FixedTargeting(target), pool, muzzle);
        weapon.Setup(runtime, owner, null, null);

        weapon.TickAutomatic(0.1f, Vector3.back);

        Assert.That(container.GetComponentsInChildren<Projectile>(), Has.Length.EqualTo(1));
        for (int round = 1; round < 4; round++)
            weapon.TickAutomatic(runtime.Data.RocketLauncher.RocketAutoVolleyShotInterval, Vector3.back);

        Projectile[] projectiles = container.GetComponentsInChildren<Projectile>();
        Assert.That(projectiles, Has.Length.EqualTo(4));
        foreach (Projectile projectile in projectiles)
        {
            Assert.That(projectile.transform.position, Is.EqualTo(muzzle.position));
            Assert.That(projectile.UsesAutomaticRocketTrajectory, Is.True);
            Assert.That(Vector3.Dot(projectile.transform.forward, Vector3.up), Is.GreaterThan(0.99f));
            AutomaticRocketTrajectory path = ReadPrivate<AutomaticRocketTrajectory>(projectile, "_automaticRocketTrajectory");
            Assert.That(path.ClearanceHeight, Is.GreaterThan(body.bounds.max.y));
            Assert.That(path.Target, Is.EqualTo(EnemyRegistry.GetAimPoint(target)));
        }
    }

    [Test]
    public void AutomaticRocketVolley_UsesSpacingAndTheMovingMuzzle()
    {
        Transform owner = CreateObject("Owner").transform;
        Transform muzzle = CreateObject("Pipe").transform;
        Transform target = RegisterEnemy(Vector3.forward * 8f);
        Vector3 originalAimPoint = EnemyRegistry.GetAimPoint(target);
        ProjectilePool pool = CreatePool(out Transform container);
        WeaponInstance runtime = CreateRuntime(WeaponType.RocketLauncher, WeaponState.Automatic);
        runtime.Data.RocketLauncher.RocketAutoBaseRocketCount = 2;
        runtime.Data.RocketLauncher.RocketAutoVolleyShotInterval = 0.11f;
        RocketLauncherWeapon weapon = new(new FixedTargeting(target), pool, muzzle);
        weapon.Setup(runtime, owner, null, null);
        weapon.TickAutomatic(0.01f, Vector3.forward);
        weapon.TickAutomatic(0.05f, Vector3.forward);
        Assert.That(container.GetComponentsInChildren<Projectile>(), Has.Length.EqualTo(1));

        muzzle.position = new Vector3(2f, 1f, -0.5f);
        target.position += Vector3.right * 3f;
        weapon.TickAutomatic(0.07f, Vector3.forward);

        Projectile[] projectiles = container.GetComponentsInChildren<Projectile>();
        Assert.That(projectiles, Has.Length.EqualTo(2));
        Assert.That(projectiles[1].transform.position, Is.EqualTo(muzzle.position));
        Assert.That(ReadPrivate<AutomaticRocketTrajectory>(projectiles[1], "_automaticRocketTrajectory").Target, Is.EqualTo(originalAimPoint));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void AutomaticRocketVolley_CancelsOnOriginOrManualRoleChange(bool changeOrigin)
    {
        Transform owner = CreateObject("Owner").transform;
        Transform muzzle = CreateObject("Pipe").transform;
        Transform mainMuzzle = CreateObject("Main weapon").transform;
        Transform target = RegisterEnemy(Vector3.forward * 8f);
        ProjectilePool pool = CreatePool(out Transform container);
        WeaponInstance runtime = CreateRuntime(WeaponType.RocketLauncher, WeaponState.Automatic);
        runtime.Data.RocketLauncher.RocketAutoBaseRocketCount = 4;
        RocketLauncherWeapon weapon = new(new FixedTargeting(target), pool, muzzle);
        weapon.Setup(runtime, owner, null, null);
        weapon.TickAutomatic(0.01f, Vector3.forward);
        Assert.That(container.GetComponentsInChildren<Projectile>(), Has.Length.EqualTo(1));

        if (changeOrigin)
            weapon.SetFireOrigin(new WeaponFireOriginBinding(mainMuzzle));
        else
        {
            runtime.State = WeaponState.Manual;
            weapon.TickManual(0.01f, Vector3.forward, false);
            runtime.State = WeaponState.Automatic;
        }
        weapon.TickAutomatic(0.2f, Vector3.forward);

        Assert.That(container.GetComponentsInChildren<Projectile>(), Has.Length.EqualTo(1));
    }

    [Test]
    public void AutomaticRocketVolley_FinishesEveryRoundWhenItsCooldownExpiresEarly()
    {
        Transform owner = CreateObject("Owner").transform;
        Transform target = RegisterEnemy(Vector3.forward * 8f);
        ProjectilePool pool = CreatePool(out Transform container);
        WeaponInstance runtime = CreateRuntime(WeaponType.RocketLauncher, WeaponState.Automatic);
        runtime.Data.BaseAttackRate = 100f;
        runtime.Data.RocketLauncher.RocketAutoBaseRocketCount = 20;
        RocketLauncherWeapon weapon = new(new FixedTargeting(target), pool, owner);
        weapon.Setup(runtime, owner, null, null);
        weapon.TickAutomatic(0.01f, Vector3.forward);
        for (int round = 1; round < 20; round++)
        {
            weapon.TickAutomatic(runtime.Data.RocketLauncher.RocketAutoVolleyShotInterval, Vector3.forward);
            Assert.That(container.GetComponentsInChildren<Projectile>(), Has.Length.EqualTo(round + 1));
        }
    }

    [Test]
    public void ManualRocket_RemainsStraightAndCanReuseAnAutomaticProjectile()
    {
        Transform owner = CreateObject("Owner").transform;
        Collider ownerCollider = owner.gameObject.AddComponent<BoxCollider>();
        Transform muzzle = CreateObject("Muzzle").transform;
        muzzle.position = new Vector3(0.5f, 1.5f, -0.5f);
        Transform target = RegisterEnemy(Vector3.forward * 8f);
        ProjectilePool pool = CreatePool(out Transform container);
        WeaponInstance runtime = CreateRuntime(WeaponType.RocketLauncher, WeaponState.Automatic);
        runtime.Data.RocketLauncher.RocketAutoBaseRocketCount = 1;
        RocketLauncherWeapon weapon = new(new FixedTargeting(target), pool, muzzle);
        weapon.Setup(runtime, owner, null, null);
        weapon.TickAutomatic(0.1f, Vector3.forward);
        Projectile automatic = container.GetComponentInChildren<Projectile>();
        Assert.That(automatic.UsesAutomaticRocketTrajectory, Is.True);
        Assert.That(InvokePrivate<bool>(automatic, "IsIgnoredCollision", ownerCollider), Is.True);
        pool.Release(automatic.gameObject);
        Assert.That(automatic.UsesAutomaticRocketTrajectory, Is.False);

        runtime.State = WeaponState.Manual;
        weapon.TickManual(10f, Vector3.back, true);

        Projectile manual = container.GetComponentInChildren<Projectile>();
        Assert.That(manual, Is.SameAs(automatic));
        Assert.That(manual.UsesAutomaticRocketTrajectory, Is.False);
        Assert.That(manual.transform.position, Is.EqualTo(muzzle.position));
        Assert.That(Vector3.Dot(manual.transform.forward, Vector3.back), Is.GreaterThan(0.99f));
        Assert.That(InvokePrivate<bool>(manual, "IsIgnoredCollision", ownerCollider), Is.False);
        Assert.That(ReadPrivate<float>(manual, "_maxTravelDistance"), Is.EqualTo(runtime.Data.BaseRange));
    }

    [Test]
    public void RocketTrajectory_RisesBeforeTurningAndReachesTargetAboveThePlayer()
    {
        AutomaticRocketTrajectory path = new();
        Vector3 launch = new(0.35f, 1.5f, -0.55f);
        Vector3 target = new(0f, 1f, 8f);
        path.Configure(launch, target, 2.6f);
        Assert.That(path.EvaluatePosition(0f), Is.EqualTo(launch));
        Assert.That(path.EvaluatePosition(0.5f), Is.EqualTo(launch + Vector3.up * 0.5f));
        Assert.That(path.EvaluateDirection(0.5f), Is.EqualTo(Vector3.up));
        Assert.That(Vector3.Distance(path.EvaluatePosition(path.TotalLength), target), Is.LessThan(0.0001f));

        for (int i = 0; i <= 100; i++)
        {
            Vector3 position = path.EvaluatePosition(path.TotalLength * i / 100f);
            if (Mathf.Abs(position.x) <= 0.5f && Mathf.Abs(position.z) <= 0.3f)
                Assert.That(position.y, Is.GreaterThan(2f), "The bend must pass above the player's body.");
        }
        float riseLength = path.ClearanceHeight - launch.y;
        Assert.That(Vector3.Angle(path.EvaluateDirection(riseLength), path.EvaluateDirection(riseLength + 0.001f)), Is.LessThan(1f));
    }

    [Test]
    public void RocketTrajectory_MaxRangeBudgetIncludesTheAscent()
    {
        Projectile projectile = CreateProjectile("Rocket");
        projectile.ConfigurePooled(0.1f);
        projectile.Launch(Vector3.up);
        projectile.ConfigureMaxTravel(20f, true);
        projectile.ConfigureAutomaticRocketLaunch(Vector3.forward * 20f, 3f, null);

        float distanceBudget = ReadPrivate<float>(projectile, "_maxTravelDistance");
        AutomaticRocketTrajectory trajectory = ReadPrivate<AutomaticRocketTrajectory>(projectile, "_automaticRocketTrajectory");
        Assert.That(distanceBudget, Is.GreaterThan(20f));
        Assert.That(distanceBudget, Is.GreaterThanOrEqualTo(trajectory.TotalLength));
        Assert.That(ReadPrivate<float>(projectile, "_activeMaxLifetime"), Is.GreaterThan(distanceBudget / projectile.ActiveSpeed));
    }

    [Test]
    public void AutomaticRocket_RangeCompletionUsesTheExactEndpoint()
    {
        ProjectilePool pool = CreatePool(out _);
        Projectile projectile = pool.TryGet().GetComponent<Projectile>();
        projectile.transform.position = new Vector3(0f, 1f, -0.5f);
        projectile.ConfigurePooled(3f);
        projectile.Launch(Vector3.up);
        Vector3 endpoint = new(0f, 1f, 8f);
        projectile.ConfigureMaxTravel(Vector3.Distance(projectile.transform.position, endpoint), false);
        projectile.ConfigureAutomaticRocketLaunch(endpoint, 2.6f, null);
        AutomaticRocketTrajectory trajectory = ReadPrivate<AutomaticRocketTrajectory>(projectile, "_automaticRocketTrajectory");
        float previousDistance = trajectory.TotalLength - 0.01f;
        Vector3 previousPosition = trajectory.EvaluatePosition(previousDistance);
        projectile.transform.position = previousPosition;
        projectile.GetComponent<Rigidbody>().position = previousPosition;
        SetPrivate(projectile, "_automaticRocketDistance", previousDistance);

        typeof(Projectile).GetMethod("FixedUpdate", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(projectile, null);

        Assert.That(projectile.gameObject.activeSelf, Is.False);
        Assert.That(Vector3.Distance(projectile.transform.position, endpoint), Is.LessThan(0.0001f));
    }

    [Test]
    public void AutomaticRocket_SweepsWorldGeometryWhileAscending()
    {
        Transform owner = CreateObject("Owner").transform;
        Projectile projectile = CreateProjectile("Rocket");
        GameObject ceiling = CreateObject("Ceiling");
        BoxCollider ceilingCollider = ceiling.AddComponent<BoxCollider>();
        ceiling.transform.position = Vector3.up * 2f;
        ceilingCollider.size = new Vector3(4f, 0.1f, 4f);
        projectile.GetComponent<SphereCollider>().radius = 0.05f;
        projectile.ConfigurePooled(3f);
        projectile.Launch(Vector3.up);
        projectile.ConfigureAutomaticRocketLaunch(Vector3.forward * 8f, 3f, owner);
        // Visual-only collision consumes the rocket without producing unrelated explosion VFX.
        SetPrivate(projectile, "_visualOnly", true);
        Physics.SyncTransforms();

        bool hit = InvokePrivate<bool>(projectile, "TryConsumeSweptWorldCollision", Vector3.zero, Vector3.up * 3f);

        Assert.That(hit, Is.True);
    }

    private WeaponInstance CreateRuntime(WeaponType type, WeaponState state)
    {
        WeaponData data = Track(ScriptableObject.CreateInstance<WeaponData>());
        data.WeaponType = type;
        data.BaseRange = 20f;
        data.BaseManualAmmo = 100f;
        data.EnsureSpecificTuningForCurrentType();
        return new WeaponInstance { Data = data, State = state, CurrentAmmo = 100f };
    }

    private Transform RegisterEnemy(Vector3 position)
    {
        Transform enemy = CreateObject("Enemy").transform;
        enemy.position = position;
        EnemyRegistry.Register(enemy);
        _enemies.Add(enemy);
        return enemy;
    }

    private ProjectilePool CreatePool(out Transform container)
    {
        GameObject poolObject = CreateObject("Pool");
        poolObject.SetActive(false);
        ProjectilePool pool = poolObject.AddComponent<ProjectilePool>();
        container = CreateObject("Projectiles").transform;
        Projectile prefab = CreateProjectile("Projectile prefab");
        prefab.gameObject.SetActive(false);
        SetPrivate(pool, "_projectilePrefab", prefab.gameObject);
        SetPrivate(pool, "_container", container);
        return pool;
    }

    private Projectile CreateProjectile(string name)
    {
        GameObject gameObject = CreateObject(name);
        gameObject.AddComponent<Rigidbody>();
        gameObject.AddComponent<SphereCollider>();
        return gameObject.AddComponent<Projectile>();
    }

    private GameObject CreateObject(string name) => Track(new GameObject(name));

    private T Track<T>(T item) where T : Object
    {
        _cleanup.Add(item);
        return item;
    }

    private static void SetPrivate(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static T ReadPrivate<T>(object target, string name) =>
        (T)target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);

    private static T InvokePrivate<T>(object target, string name, params object[] args) =>
        (T)target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, args);

    private sealed class FixedTargeting : IWeaponTargeting
    {
        private readonly Transform _target;
        public FixedTargeting(Transform target) => _target = target;
        public bool TryGetTarget(WeaponInstance weapon, Transform owner, float range, Vector3 aimDirection, out Transform target)
        {
            target = _target;
            return target != null;
        }
    }
}
