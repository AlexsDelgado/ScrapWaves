using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class ProjectileKillCreditTests
{
    private readonly List<Object> _cleanup = new();
    private SaveManager _previousSave;
    private ChallengeProgressTracker _previousTracker;
    private string _previousPendingWeapon;
    private SaveManager _save;
    private SaveData _data;
    private string _directory;
    private string _path;

    [SetUp]
    public void SetUp()
    {
        _previousSave = SaveManager.Instance;
        _previousTracker = ChallengeProgressTracker.Instance;
        _previousPendingWeapon = WeaponDamageApplier.PendingSourceWeaponId;
        WeaponDamageApplier.ClearPendingWeapon();
        _directory = Path.Combine(Path.GetTempPath(), "ScrapWavesKillCredit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _path = Path.Combine(_directory, "save.json");

        // Inactive objects avoid bootstrap Awake and never touch the player's real save.
        GameObject services = CreateObject("Isolated kill-credit services");
        services.SetActive(false);
        _save = services.AddComponent<SaveManager>();
        _data = new SaveData();
        SetField(_save, "_data", _data);
        SetField(_save, "_path", _path);
        SetSingleton(typeof(SaveManager), _save);
        SetSingleton(typeof(ChallengeProgressTracker), services.AddComponent<ChallengeProgressTracker>());
    }

    [TearDown]
    public void TearDown()
    {
        SetSingleton(typeof(SaveManager), _previousSave);
        SetSingleton(typeof(ChallengeProgressTracker), _previousTracker);
        WeaponDamageApplier.PendingSourceWeaponId = _previousPendingWeapon;
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
        if (File.Exists(_path)) File.Delete(_path);
        if (Directory.Exists(_directory)) Directory.Delete(_directory);
    }

    [TestCase("AutomaticCannon", "trigger")]
    [TestCase("AutomaticCannon", "sweep")]
    [TestCase("RocketLauncher", "explosion")]
    [TestCase("RocketLauncher", "cone")]
    public void LethalProjectileDamage_CreditsWeaponBeforeDeathNotification(string weaponId, string route)
    {
        WeaponDamageContext context = CreateContext(weaponId);
        Projectile projectile = CreateProjectile(context);
        // Modern feedback intentionally does not configure the legacy presentation weapon.
        WeaponFeedbackContext feedback = new(context.Weapon, WeaponFeedbackMode.Automatic, 0f,
            Vector3.zero, Vector3.forward);
        projectile.ConfigureFeedback(null, in feedback, replaceExplosionVfx: true);
        EnemyHealth target = CreateEnemy(Vector3.forward * 3f);
        string idAtDeath = null;
        target.OnDied += () => idAtDeath = target.LastDamagingWeaponId;

        Physics.SyncTransforms();
        if (route == "trigger") Invoke(projectile, "OnTriggerEnter", target.GetComponent<Collider>());
        else if (route == "sweep") Invoke(projectile, "TryConsumeSweptWorldCollision", Vector3.zero, Vector3.forward * 5f);
        else if (route == "explosion")
        {
            projectile.ConfigureExplosion(5f, 0f);
            Invoke(projectile, "ApplyExplosionDamage");
            Invoke(projectile, "ApplyExplosionDamage"); // Dead targets must not count twice.
        }
        else
        {
            projectile.ConfigureFragmentCone(90f, 5f, 1f);
            Invoke(projectile, "ApplyFragmentConeDamage");
        }

        Assert.That(target.CurrentHealth, Is.Zero);
        Assert.That(idAtDeath, Is.EqualTo(weaponId));
        Assert.That(_data.WeaponKills.Single().WeaponId, Is.EqualTo(weaponId));
        Assert.That(_data.WeaponKills.Single().Kills, Is.EqualTo(1));
    }

    [Test]
    public void ClearingOrReplacingFeedback_DoesNotChangeCapturedDamageIdentity()
    {
        WeaponDamageContext context = CreateContext("AutomaticCannon");
        WeaponDamageContext copied = context.WithScales(1f, 1f).WithFeedbackMetadata(0, DamageFeedbackKind.Direct);
        context.Weapon.Data.WeaponId = "ChangedAfterLaunch";
        Projectile projectile = CreateProjectile(copied);
        WeaponFeedbackContext feedback = new(context.Weapon, WeaponFeedbackMode.Manual, 0f,
            Vector3.zero, Vector3.forward);
        projectile.ConfigureFeedback(null, in feedback);
        projectile.ClearPresentation();
        EnemyHealth target = CreateEnemy(Vector3.forward * 3f);

        Invoke(projectile, "OnTriggerEnter", target.GetComponent<Collider>());

        Assert.That(_data.WeaponKills.Single().WeaponId, Is.EqualTo("AutomaticCannon"));
    }

    [Test]
    public void PoolReuse_ReplacesIdentity_AndRawOrVisualShotsDoNotRetainIt()
    {
        ProjectilePool pool = CreatePool(out _);
        WeaponDamageContext cannon = CreateContext("AutomaticCannon");
        Assert.That(pool.TrySpawnProjectile(Vector3.zero, Quaternion.identity, Vector3.forward, 10, 0f,
            cannon, out Projectile first), Is.True);
        pool.Release(first.gameObject);
        WeaponDamageContext rocket = CreateContext("RocketLauncher");
        Assert.That(pool.TrySpawnProjectile(Vector3.zero, Quaternion.identity, Vector3.forward, 10, 0f,
            rocket, out Projectile reused), Is.True);
        Assert.That(reused, Is.SameAs(first));
        EnemyHealth credited = CreateEnemy(Vector3.forward * 3f);
        Invoke(reused, "OnTriggerEnter", credited.GetComponent<Collider>());
        Assert.That(_data.WeaponKills.Single().WeaponId, Is.EqualTo("RocketLauncher"));

        Assert.That(pool.TrySpawnProjectile(Vector3.zero, Quaternion.identity, Vector3.forward, 10), Is.True);
        EnemyHealth unowned = CreateEnemy(Vector3.right * 3f);
        Invoke(reused, "OnTriggerEnter", unowned.GetComponent<Collider>());
        Assert.That(unowned.LastDamagingWeaponId, Is.Null);
        Assert.That(_data.WeaponKills.Single().Kills, Is.EqualTo(1));

        reused.ConfigureWeaponDamage(cannon);
        reused.ConfigureVisualOnly(1f);
        Assert.That(GetField<WeaponDamageContext>(reused, "_weaponDamageContext").SourceWeaponId, Is.Null);
    }

    [Test]
    public void ClusterChildren_CreditRocketWithoutAnyPresentationSetup()
    {
        ProjectilePool pool = CreatePool(out Transform container);
        WeaponDamageContext context = CreateContext("RocketLauncher");
        Projectile parent = CreateProjectile(context);
        parent.ConfigureExplosionCluster(pool, 1, 10, 1f, 0f, 0f, 1f, 4f, 90f, 3f, 1f,
            context.WithScales(1f, 1f, DamageFeedbackKind.Fragment));
        SetField(parent, "_replaceExplosionVfx", true);

        Invoke(parent, "SpawnExplosionCluster");

        Projectile child = container.GetComponentsInChildren<Projectile>().Single();
        child.ClearPresentation();
        SetField(child, "_replaceExplosionVfx", true);
        EnemyHealth explosionTarget = CreateEnemy(child.transform.position + Vector3.right * 0.4f);
        EnemyHealth fragmentTarget = CreateEnemy(child.transform.position + Vector3.right * 2f);
        Physics.SyncTransforms();
        Invoke(child, "ApplyExplosionDamage");
        Assert.That(explosionTarget.CurrentHealth, Is.Zero);
        Assert.That(fragmentTarget.CurrentHealth, Is.Zero);
        Assert.That(_data.WeaponKills.Single().WeaponId, Is.EqualTo("RocketLauncher"));
        Assert.That(_data.WeaponKills.Single().Kills, Is.EqualTo(2));
    }

    [TestCase("AutomaticCannon", "go_for_the_head")]
    [TestCase("RocketLauncher", "to_little_pieces")]
    public void NextKill_UnlocksAt2500_AndPreservesExistingSaveProgress(string weaponId, string achievementId)
    {
        AchievementDefinition achievement = AchievementDefinition.CreateRuntime(achievementId, achievementId, "",
            AchievementConditionType.WeaponKillsTotal, 2500f, true, weaponId, null,
            "WeaponPath_" + weaponId + "_PathB", 50);
        _cleanup.Add(achievement);
        SetField(_save, "_achievementCatalog", new List<AchievementDefinition> { achievement });
        _data.Scrap = 137;
        _data.UnlockedAchievementIds.Add("existing_achievement");
        _data.WeaponKills.Add(new WeaponKillRecord { WeaponId = weaponId, Kills = 2499 });
        _data.WeaponKills.Add(new WeaponKillRecord { WeaponId = "OtherWeapon", Kills = 12 });
        _save.SaveNow();
        Invoke(_save, "Load");
        Assert.That(_save.GetProgress(achievement), Is.EqualTo(2499));
        Assert.That(_save.IsAchievementUnlocked(achievement), Is.False);

        Projectile shot = CreateProjectile(CreateContext(weaponId));
        EnemyHealth target = CreateEnemy(Vector3.forward * 3f);
        Invoke(shot, "OnTriggerEnter", target.GetComponent<Collider>());
        Invoke(_save, "Load");

        Assert.That(_save.GetProgress(achievement), Is.EqualTo(2500));
        Assert.That(_save.IsAchievementUnlocked(achievement), Is.True);
        SaveData persisted = JsonUtility.FromJson<SaveData>(File.ReadAllText(_path));
        Assert.That(persisted.WeaponKills.Find(x => x.WeaponId == "OtherWeapon").Kills, Is.EqualTo(12));
        Assert.That(persisted.UnlockedAchievementIds, Does.Contain("existing_achievement"));
        Assert.That(persisted.UnlockedIds, Does.Contain("WeaponPath_" + weaponId + "_PathB"));
        Assert.That(persisted.Scrap, Is.EqualTo(187));
    }

    private WeaponDamageContext CreateContext(string weaponId)
    {
        WeaponData weapon = ScriptableObject.CreateInstance<WeaponData>();
        _cleanup.Add(weapon);
        weapon.WeaponId = weaponId;
        weapon.BaseDamage = 10f;
        PlayerStats stats = CreateObject("Damage stats").AddComponent<PlayerStats>();
        var definitions = new List<StatDefinition>();
        foreach (StatType type in new[] { StatType.DamageMultiplier, StatType.EliteDamageMultiplier })
        {
            StatDefinition definition = ScriptableObject.CreateInstance<StatDefinition>();
            _cleanup.Add(definition);
            SetField(definition, "<StatType>k__BackingField", type);
            SetField(definition, "<BaseValue>k__BackingField", 1f);
            definitions.Add(definition);
        }
        SetField(stats, "_statDefinitions", definitions);
        Invoke(stats, "Awake");
        return new WeaponDamageContext(stats, new WeaponInstance { Data = weapon, State = WeaponState.Automatic },
            false, 1f, 1f, false, 0f);
    }

    private Projectile CreateProjectile(WeaponDamageContext context)
    {
        Projectile projectile = CreateObject("Credit projectile").AddComponent<Projectile>();
        projectile.ConfigurePooled(2f, 10);
        projectile.ConfigureWeaponDamage(context);
        projectile.Launch(Vector3.forward);
        projectile.GetComponent<SphereCollider>().radius = 0.1f;
        return projectile;
    }

    private EnemyHealth CreateEnemy(Vector3 position)
    {
        GameObject target = CreateObject("Credit target");
        target.transform.position = position;
        target.AddComponent<SphereCollider>().radius = 0.2f;
        EnemyHealth health = target.AddComponent<EnemyHealth>();
        health.ApplyConfiguredMaxHealth(1);
        return health;
    }

    private ProjectilePool CreatePool(out Transform container)
    {
        GameObject prefab = CreateObject("Credit projectile prefab");
        prefab.SetActive(false);
        prefab.AddComponent<Projectile>();
        container = CreateObject("Credit pool container").transform;
        GameObject root = CreateObject("Credit pool");
        root.SetActive(false);
        ProjectilePool pool = root.AddComponent<ProjectilePool>();
        SetField(pool, "_projectilePrefab", prefab);
        SetField(pool, "_container", container);
        SetField(pool, "_initialPoolSize", 1);
        SetField(pool, "_maxPoolSize", 1);
        Invoke(pool, "Awake");
        return pool;
    }

    private GameObject CreateObject(string name)
    {
        GameObject result = new(name);
        _cleanup.Add(result);
        return result;
    }

    private static void SetSingleton(Type type, object instance) => type.GetProperty("Instance").SetValue(null, instance);
    private static void SetField(object owner, string name, object value) =>
        owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(owner, value);
    private static T GetField<T>(object owner, string name) =>
        (T)owner.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(owner);
    private static object Invoke(object owner, string name, params object[] args) =>
        owner.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(owner, args);
}
