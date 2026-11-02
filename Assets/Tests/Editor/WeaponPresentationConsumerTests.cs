#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class WeaponPresentationConsumerTests
{
    private readonly List<Object> _cleanup = new();

    [TearDown]
    public void Cleanup()
    {
        for (int i = _cleanup.Count - 1; i >= 0; i--)
            if (_cleanup[i] != null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
    }

    [TestCase("AutomaticCannon")]
    [TestCase("Flamethrower")]
    [TestCase("Mortar")]
    [TestCase("RocketLauncher")]
    [TestCase("RotatingBlade")]
    public void AbilityOnlyEditIsReportedByOrdinaryUpgradePreview(string name)
    {
        WeaponData data = Copy(name);
        WeaponStatLevelRow first = Row(data, 1, WeaponUpgradePath.None);
        WeaponStatLevelRow next = Row(data, 2, WeaponUpgradePath.None);
        first.AbilityCooldownSeconds = Value(3f);
        next.AbilityCooldownSeconds = Value(8f);
        BasicProjectileWeapon weapon = Weapon(data, Stats(), 1, WeaponUpgradePath.None);
        AssertObservationOnly(weapon, () =>
        {
            CraftingUpgradePreview preview = CraftingUpgradePreview.Build(weapon);
            Assert.That(preview.Changes.Any(row => row.Label == "Q Ability cooldown duration"
                && row.Value == "3 s → 8 s"), Is.True, string.Join("; ", preview.Changes.Select(row => row.Label + "=" + row.Value)));
            Assert.That(preview.Notice, Does.Contain("other modes and Q"));
        });
    }

    [Test]
    public void UniqueAreaAndOtherModeEditsAreNotReportedAsNoGameplayChanges()
    {
        WeaponData data = Copy("RocketLauncher");
        WeaponStatLevelRow first = Row(data, 1, WeaponUpgradePath.None);
        WeaponStatLevelRow next = Row(data, 2, WeaponUpgradePath.None);
        first.Manual.ExplosionRadiusMeters = Value(0.5f);
        next.Manual.ExplosionRadiusMeters = Value(4f);
        BasicProjectileWeapon weapon = Weapon(data, Stats(), 1, WeaponUpgradePath.None, WeaponState.Automatic);
        AssertObservationOnly(weapon, () =>
        {
            CraftingUpgradePreview preview = CraftingUpgradePreview.Build(weapon);
            Assert.That(preview.Changes.Any(row => row.Label == "Manual Explosion radius"
                && row.Value == "0.5 m → 4 m"), Is.True);
            Assert.That(preview.Notice, Does.Not.StartWith("No changes"));
        });
    }

    [Test]
    public void DenseUpgradeSummaryFitsExistingTextBoundsAndKeepsEveryComparison()
    {
        WeaponData data = Copy("RocketLauncher");
        Row(data, 5, WeaponUpgradePath.None).Manual.ExplosionRadiusMeters = Value(0.5f);
        WeaponStatLevelRow next = Row(data, 6, WeaponUpgradePath.PathA);
        next.Automatic.Damage = Value(333f);
        next.Automatic.RangeMeters = Value(123f);
        next.Automatic.AttackCyclesPerSecond = Value(2f);
        next.Manual.Damage = Value(222f);
        next.Manual.RangeMeters = Value(111f);
        next.Manual.AmmoCapacity = Value(999f);
        next.Manual.ExplosionRadiusMeters = Value(4f);
        next.Ability.Damage = Value(400f);
        next.Ability.RangeMeters = Value(66f);
        next.Ability.ExplosionRadiusMeters = Value(9f);
        next.CriticalChance = Value(0.25f);
        next.CriticalDamageMultiplier = Value(7f);
        next.AbilityCooldownSeconds = Value(17f);
        BasicProjectileWeapon weapon = Weapon(data, Stats(), 5, WeaponUpgradePath.None);
        CraftingUpgradePreview preview = CraftingUpgradePreview.BuildAdvanced(weapon, WeaponUpgradePath.PathA);
        Assert.That(preview.Changes.Length, Is.GreaterThan(12));
        Assert.That(preview.Rows.Length, Is.EqualTo(3));
        Assert.That(preview.Notice.Split('\n').Length, Is.EqualTo(2));
        Assert.That(preview.Notice.Length, Is.LessThan(210));
        Assert.That(preview.Notice, Does.Contain($"Showing 3 of {preview.Changes.Length}"));
        string advanced = CraftingUpgradePreview.DescribeAdvanced(weapon, WeaponUpgradePath.PathA);
        Assert.That(advanced.Split('\n').Length, Is.EqualTo(2));
        Assert.That(advanced.Split('\n').All(line => line.Length <= 70), Is.True, advanced);
        Assert.That(advanced, Does.Contain("17 s").And.Contain("further stat changes"));
        string prose = new string('X', 200);
        string withProse = CraftingUpgradePreview.DescribeAdvanced(weapon, WeaponUpgradePath.PathA, prose);
        Assert.That(withProse.Split('\n').All(line => line.Length <= 70), Is.True, withProse);
        Assert.That(preview.Changes.Any(row => row.Label == "Manual Explosion radius"
            && row.Value == "0.5 m → 4 m"), Is.True);
        Assert.That(preview.Changes.Any(row => row.Label == "Q Critical chance"), Is.True);
    }

    [TestCase("AutomaticCannon")]
    [TestCase("Flamethrower")]
    [TestCase("Mortar")]
    [TestCase("RocketLauncher")]
    [TestCase("RotatingBlade")]
    public void BothAdvancedPreviewsUseExactPathRowsAndPreserveLevelFive(string name)
    {
        WeaponData data = Copy(name);
        Row(data, 5, WeaponUpgradePath.None).AbilityCooldownSeconds = Value(13f);
        Row(data, 6, WeaponUpgradePath.PathA).AbilityCooldownSeconds = Value(7f);
        Row(data, 6, WeaponUpgradePath.PathB).AbilityCooldownSeconds = Value(2f);
        PlayerStats stats = Stats();
        BasicProjectileWeapon weapon = Weapon(data, stats, 5, WeaponUpgradePath.None);
        AssertObservationOnly(weapon, () =>
        {
            foreach (WeaponUpgradePath path in new[] { WeaponUpgradePath.PathA, WeaponUpgradePath.PathB })
            {
                WeaponDiagnosticsSnapshot expected = Weapon(data, stats, 6, path).CaptureDiagnostics();
                WeaponDiagnosticsSnapshot actual = weapon.CaptureUpgradeDiagnostics(6, path);
                Assert.That(Flatten(actual), Is.EqualTo(Flatten(expected)), name + " " + path);
                CraftingUpgradePreview preview = CraftingUpgradePreview.BuildAdvanced(weapon, path);
                string after = path == WeaponUpgradePath.PathA ? "7 s" : "2 s";
                Assert.That(preview.Changes.Any(row => row.Label == "Q Ability cooldown duration"
                    && row.Value == "13 s → " + after), Is.True);
                Assert.That(CraftingUpgradePreview.DescribeAdvanced(weapon, path), Does.Contain(after));
            }
        });
        Assert.That(weapon.Runtime.Level, Is.EqualTo(5));
        Assert.That(weapon.Runtime.SelectedPath, Is.EqualTo(WeaponUpgradePath.None));
    }

    [TestCase("AutomaticCannon")]
    [TestCase("Flamethrower")]
    [TestCase("Mortar")]
    [TestCase("RocketLauncher")]
    [TestCase("RotatingBlade")]
    public void AimConvergenceUsesOnlyExactManualOverride(string name)
    {
        WeaponData data = Copy(name);
        PlayerStats stats = Stats();
        BasicProjectileWeapon weapon = Weapon(data, stats, 6, WeaponUpgradePath.PathA);
        Assert.That(weapon.GetManualAimConvergenceDistance(), Is.Null);
        Assert.That(ReticleAimProvider.ResolveConvergenceDistance(weapon.Runtime, weapon), Is.EqualTo(data.BaseRange));
        Row(data, 6, WeaponUpgradePath.PathA).Manual.RangeMeters = Value(4.25f);
        Row(data, 6, WeaponUpgradePath.PathB).Manual.RangeMeters = Value(9.5f);
        Assert.That(ReticleAimProvider.ResolveConvergenceDistance(weapon.Runtime, weapon),
            Is.EqualTo(weapon.GetManualAimConvergenceDistance().Value));
        float pathA = weapon.GetManualAimConvergenceDistance().Value;
        weapon.Runtime.SelectedPath = WeaponUpgradePath.PathB;
        float pathB = weapon.GetManualAimConvergenceDistance().Value;
        Assert.That(pathA, Is.EqualTo(4.25f).Within(0.0001f));
        Assert.That(pathB, Is.EqualTo(9.5f).Within(0.0001f));
        weapon.Runtime.Level = 5;
        weapon.Runtime.SelectedPath = WeaponUpgradePath.None;
        Assert.That(weapon.GetManualAimConvergenceDistance(), Is.Null);
        Assert.That(ReticleAimProvider.ResolveConvergenceDistance(weapon.Runtime, weapon), Is.EqualTo(data.BaseRange));
    }

    [Test]
    public void AimConsumerNeverCapturesDebugGeometryAndHonorsSubMetreExplicitRange()
    {
        WeaponData data = Copy("AutomaticCannon");
        data.BaseRange = 0.25f;
        var weapon = new AimProbeWeapon { Distance = 0.25f };
        weapon.Setup(new WeaponInstance { Data = data, Level = 1 }, null, null, null);
        GameObject owner = NewObject("Aim provider consumer test");
        Camera camera = owner.AddComponent<Camera>();
        ReticleAimProvider provider = owner.AddComponent<ReticleAimProvider>();
        Set(provider, "_aimCamera", camera);
        Set(provider, "_aimMask", (LayerMask)0);
        AimSolution authored = provider.ResolveWeaponAim(Vector3.zero, weapon.Runtime, weapon);
        Assert.That(Vector3.Distance(authored.TargetPoint, Vector3.zero), Is.EqualTo(0.25f).Within(0.0001f));
        AimSolution fallback = ReticleAimProvider.CreateWeaponFallback(Vector3.zero, weapon.Runtime, weapon, null, Vector3.forward);
        Assert.That(fallback.TargetPoint.z, Is.EqualTo(0.25f).Within(0.0001f));
        weapon.Distance = null;
        AimSolution inherited = provider.ResolveWeaponAim(Vector3.zero, weapon.Runtime, weapon);
        Assert.That(Vector3.Distance(inherited.TargetPoint, Vector3.zero), Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void GeometrySnapshotDoesNotRetainMutableCallerArray()
    {
        var guides = new[] { new WeaponGeometryGuide(WeaponStatMode.Manual, WeaponGeometryPurpose.Explosion,
            WeaponGeometryShape.Sphere, WeaponGeometryAnchor.Muzzle, radiusMeters: 0.25f) };
        var snapshot = new WeaponGeometrySnapshot(guides);
        guides[0] = new WeaponGeometryGuide(WeaponStatMode.Manual, WeaponGeometryPurpose.Explosion,
            WeaponGeometryShape.Sphere, WeaponGeometryAnchor.Muzzle, radiusMeters: 100f);
        Assert.That(snapshot.Guides[0].RadiusMeters, Is.EqualTo(0.25f));
        Assert.That(snapshot.Guides, Is.Not.InstanceOf<WeaponGeometryGuide[]>());
        Assert.That(new BasicProjectileWeapon(null, null, null).CaptureGeometry(), Is.Null);
    }

    [TestCase(WeaponAutomaticAimConstraint.BodyForward180, WeaponTargetingMode.IgnoreCameraClosest, 180f, true)]
    [TestCase(WeaponAutomaticAimConstraint.Full360, WeaponTargetingMode.RandomInRange, 360f, false)]
    public void AcquisitionGeometrySharesActualTargetingConstraint(WeaponAutomaticAimConstraint constraint,
        WeaponTargetingMode mode, float angle, bool bodyForward)
    {
        WeaponData data = Copy("AutomaticCannon");
        data.AutomaticAimConstraint = constraint;
        data.AutoTargetingMode = mode;
        var weapon = new AimProbeWeapon();
        weapon.Setup(new WeaponInstance { Data = data, Level = 1 }, null, null, null);
        WeaponGeometryGuide guide = weapon.TargetingGuide(12.5f);
        ConfiguredEnemyTargeting.GetAcquisitionConstraint(weapon.Runtime, out float runtimeAngle, out bool runtimeBody);
        Assert.That(guide.FullAngleDegrees, Is.EqualTo(runtimeAngle).And.EqualTo(angle));
        Assert.That(runtimeBody, Is.EqualTo(bodyForward));
        Assert.That(guide.Direction, Is.EqualTo(bodyForward ? WeaponGeometryDirection.OwnerForward : WeaponGeometryDirection.Aim));
        Assert.That(guide.RangeMeters, Is.EqualTo(12.5f));
        Assert.That(guide.RadiusMeters, Is.EqualTo(12.5f));
    }

    [Test]
    public void RuntimeDebugSpherePreservesSmallRadiusAndZeroSuppressesGeometry()
    {
        WeaponDebugGizmos gizmos = NewObject("Small radius debug test").AddComponent<WeaponDebugGizmos>();
        Invoke(gizmos, "BeginRuntimeFrame");
        Invoke(gizmos, "DrawSphere", Vector3.zero, 0f, Color.white, true);
        Assert.That(gizmos.GetComponentsInChildren<LineRenderer>().Length, Is.Zero);
        Invoke(gizmos, "DrawSphere", Vector3.zero, 0.001f, Color.white, true);
        LineRenderer[] lines = gizmos.GetComponentsInChildren<LineRenderer>();
        Assert.That(lines.Length, Is.EqualTo(3));
        foreach (LineRenderer line in lines)
            for (int i = 0; i < line.positionCount; i++)
                Assert.That(line.GetPosition(i).magnitude, Is.EqualTo(0.001f).Within(0.000001f));
    }

    [TestCase("Base", "StatProfile damage replacement")]
    [TestCase("WeaponLevel", "Level multiplier bypassed")]
    [TestCase("UpgradePath", "Path multiplier bypassed")]
    public void HitAttributionExplainsCapturedAuthoredSourceAfterLiveWeaponChanges(string factor, string explanation)
    {
        WeaponData data = Copy("RocketLauncher");
        Row(data, 6, WeaponUpgradePath.PathA).Manual.Damage = Value(37f);
        BasicProjectileWeapon weapon = Weapon(data, Stats(), 6, WeaponUpgradePath.PathA, WeaponState.Manual);
        var context = new WeaponDamageContext(Stats(), weapon.Runtime, false, 1f, 1f, false, 1f, mode: WeaponStatMode.Manual);
        WeaponDamageRoll captured = default;
        Action<WeaponDamageRoll> listener = value => captured = value;
        WeaponDamageResolver.OnDamageResolved += listener;
        try { context.CalculateDamageValue(false); }
        finally { WeaponDamageResolver.OnDamageResolved -= listener; }
        weapon.Runtime.Level = 10;
        weapon.Runtime.SelectedPath = WeaponUpgradePath.PathB;
        weapon.Runtime.State = WeaponState.Automatic;
        weapon.Runtime.Data = Copy("Mortar");
        GameObject owner = NewObject("Captured attribution test");
        owner.SetActive(false);
        StatAttributionPanel panel = owner.AddComponent<StatAttributionPanel>();
        FieldInfo selected = typeof(StatAttributionPanel).GetField("_selectedFactor", BindingFlags.Instance | BindingFlags.NonPublic);
        selected.SetValue(panel, Enum.Parse(selected.FieldType, factor));
        Invoke(panel, "AppendFactorExplanation", captured, captured.Factors);
        var builder = (StringBuilder)typeof(StatAttributionPanel).GetField("_builder", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
        string text = builder.ToString();
        Assert.That(text, Does.Contain(explanation));
        Assert.That(text, Does.Contain("fired weapon     RocketLauncher").And.Contain("fired level      6")
            .And.Contain("fired path       PathA").And.Contain("fired mode       Manual"));
        Assert.That(text, Does.Not.Contain("fired level      10").And.Not.Contain("fired path       PathB"));
        Assert.That(text, Does.Not.Contain("Mortar"));
    }

    [TestCase("AutomaticCannon")]
    [TestCase("Flamethrower")]
    [TestCase("Mortar")]
    [TestCase("RocketLauncher")]
    [TestCase("RotatingBlade")]
    public void ProgressionRateVisibilityMatchesLiveConsumersAtEveryLevelAndPath(string name)
    {
        PlayerStats stats = Stats();
        foreach (var key in ProgressionKeys())
        {
            WeaponData data = Copy(name);
            var level = new WeaponLevelData { Level = key.level };
            data.LevelData = new List<WeaponLevelData> { level };
            data.PathA = new WeaponUpgradePathData();
            data.PathB = new WeaponUpgradePathData();
            BasicProjectileWeapon weapon = Weapon(data, stats, key.level, key.path);
            WeaponUpgradePathData path = WeaponMath.GetPathData(weapon.Runtime);
            if (path != null) path.LevelData.Add(level);
            WeaponStatLevelRow row = Row(data, key.level, key.path);
            bool ordinaryConsumesRate = data.WeaponType != WeaponType.Flamethrower;
            string beforePolicy = EditorJsonUtility.ToJson(data);
            Assert.That(WeaponStatInspectorPolicy.ShowsProgressionAttackRate(weapon.Runtime, WeaponStatAuthoringMode.Automatic), Is.EqualTo(ordinaryConsumesRate));
            Assert.That(WeaponStatInspectorPolicy.ShowsProgressionAttackRate(weapon.Runtime, WeaponStatAuthoringMode.Manual), Is.EqualTo(ordinaryConsumesRate));
            Assert.That(WeaponStatInspectorPolicy.ShowsProgressionAttackRate(weapon.Runtime, WeaponStatAuthoringMode.Ability), Is.False);
            Assert.That(EditorJsonUtility.ToJson(data), Is.EqualTo(beforePolicy), "Inspecting hidden sources must preserve them.");

            WeaponDiagnosticsSnapshot before = weapon.CaptureDiagnostics();
            level.AttackRateMultiplier = 3f;
            if (path != null) path.AttackRateMultiplier = 2f;
            WeaponDiagnosticsSnapshot after = weapon.CaptureDiagnostics();
            foreach (string mode in new[] { "Automatic", "Manual", "Active Ability" })
                Assert.That(ModeValues(before, mode).SequenceEqual(ModeValues(after, mode)),
                    Is.EqualTo(mode == "Active Ability" || !ordinaryConsumesRate), $"{name}/{key.level}/{key.path}/{mode}");

            row.Automatic.AttackCyclesPerSecond = Value(3f);
            row.Manual.AttackCyclesPerSecond = Value(4f);
            if (data.WeaponType == WeaponType.RotatingBlade) row.Blade.RotationsPerSecond = Value(.25f);
            Assert.That(WeaponStatInspectorPolicy.ShowsProgressionAttackRate(weapon.Runtime, WeaponStatAuthoringMode.Automatic), Is.False);
            Assert.That(WeaponStatInspectorPolicy.ShowsProgressionAttackRate(weapon.Runtime, WeaponStatAuthoringMode.Manual), Is.False);
            before = weapon.CaptureDiagnostics();
            level.AttackRateMultiplier = 7f;
            if (path != null) path.AttackRateMultiplier = 5f;
            Assert.That(Flatten(weapon.CaptureDiagnostics()), Is.EqualTo(Flatten(before)), "Fully replaced cadence/spin must ignore the hidden progression source.");
        }
    }

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void BladeAutomaticRateRemainsVisibleUntilContactAndRotationAreBothReplaced(bool contactAuthored, bool spinAuthored)
    {
        WeaponData data = Copy("RotatingBlade");
        WeaponStatLevelRow row = Row(data, 6, WeaponUpgradePath.PathA);
        row.Automatic.AttackCyclesPerSecond = new WeaponOptionalFloat { Enabled = contactAuthored, Value = 2f };
        row.Blade.RotationsPerSecond = new WeaponOptionalFloat { Enabled = spinAuthored, Value = 0f };
        data.PathA.AttackRateMultiplier = 1f;
        BasicProjectileWeapon weapon = Weapon(data, Stats(), 6, WeaponUpgradePath.PathA);
        Assert.That(WeaponStatInspectorPolicy.ShowsProgressionAttackRate(weapon.Runtime, WeaponStatAuthoringMode.Automatic),
            Is.EqualTo(!contactAuthored || !spinAuthored));
        string[] before = ModeValues(weapon.CaptureDiagnostics(), "Automatic");
        data.PathA.AttackRateMultiplier = 4f;
        Assert.That(before.SequenceEqual(ModeValues(weapon.CaptureDiagnostics(), "Automatic")), Is.EqualTo(contactAuthored && spinAuthored));
        Assert.That(WeaponStatInspectorPolicy.ShowsProgressionAttackRate(weapon.Runtime, WeaponStatAuthoringMode.Manual), Is.True,
            "Automatic overrides do not hide the independent Manual source.");
    }

    [TestCase("AutomaticCannon")]
    [TestCase("Flamethrower")]
    [TestCase("Mortar")]
    [TestCase("RocketLauncher")]
    [TestCase("RotatingBlade")]
    public void AmmoSourceVisibilityTracksReplacementAndKeepsPathScalingSignEditable(string name)
    {
        foreach (var key in ProgressionKeys())
        {
            WeaponData data = Copy(name);
            data.BaseManualAmmo = 10f;
            var level = new WeaponLevelData { Level = key.level, ManualAmmoMultiplier = 3f };
            data.LevelData = new List<WeaponLevelData> { level };
            data.PathA = new WeaponUpgradePathData();
            data.PathB = new WeaponUpgradePathData();
            var instance = new WeaponInstance { Data = data, Level = key.level, SelectedPath = key.path };
            WeaponUpgradePathData path = WeaponMath.GetPathData(instance);
            if (path != null) path.LevelData.Add(level);
            WeaponStatLevelRow row = Row(data, key.level, key.path);
            string beforePolicy = EditorJsonUtility.ToJson(data);
            Assert.That(WeaponStatInspectorPolicy.ShowsLevelAmmoMultiplier(instance, WeaponStatAuthoringMode.Manual), Is.True);
            Assert.That(WeaponStatInspectorPolicy.ShowsPathAmmoOverride(instance, WeaponStatAuthoringMode.Manual), Is.EqualTo(path != null));
            foreach (var mode in new[] { WeaponStatAuthoringMode.Automatic, WeaponStatAuthoringMode.Ability })
            {
                Assert.That(WeaponStatInspectorPolicy.ShowsLevelAmmoMultiplier(instance, mode), Is.False);
                Assert.That(WeaponStatInspectorPolicy.ShowsPathAmmoOverride(instance, mode), Is.False);
            }
            Assert.That(EditorJsonUtility.ToJson(data), Is.EqualTo(beforePolicy));
            Assert.That(WeaponMath.GetManualAmmoCapacity(instance, 2f), Is.EqualTo(60f));
            if (path != null)
            {
                path.ManualAmmoOverride = 13f;
                Assert.That(WeaponStatInspectorPolicy.ShowsLevelAmmoMultiplier(instance, WeaponStatAuthoringMode.Manual), Is.False);
                Assert.That(WeaponMath.GetManualAmmoCapacity(instance, 2f), Is.EqualTo(13f));
            }
            row.Manual.AmmoCapacity = Value(7f);
            Assert.That(WeaponStatInspectorPolicy.ShowsLevelAmmoMultiplier(instance, WeaponStatAuthoringMode.Manual), Is.False);
            Assert.That(WeaponMath.GetManualAmmoCapacity(instance, 2f), Is.EqualTo(path != null ? 7f : 14f));
            level.ManualAmmoMultiplier = 9f;
            Assert.That(WeaponMath.GetManualAmmoCapacity(instance, 2f), Is.EqualTo(path != null ? 7f : 14f));
            if (path != null)
            {
                Assert.That(WeaponStatInspectorPolicy.ShowsPathAmmoOverride(instance, WeaponStatAuthoringMode.Manual), Is.True);
                path.ManualAmmoOverride = 0f;
                Assert.That(WeaponMath.GetManualAmmoCapacity(instance, 2f), Is.EqualTo(7f), "Zero retains the fixed-capacity scaling contract.");
                path.ManualAmmoOverride = -1f;
                Assert.That(WeaponMath.GetManualAmmoCapacity(instance, 2f), Is.EqualTo(14f), "The retained path control still governs player ammo scaling.");
            }
        }
    }

    private static IEnumerable<(int level, WeaponUpgradePath path)> ProgressionKeys()
    {
        for (int level = 1; level <= 5; level++) yield return (level, WeaponUpgradePath.None);
        for (int level = 6; level <= 10; level++)
        {
            yield return (level, WeaponUpgradePath.PathA);
            yield return (level, WeaponUpgradePath.PathB);
        }
    }

    private static string[] ModeValues(WeaponDiagnosticsSnapshot snapshot, string name) => snapshot.Sections.Single(section => section.Name == name)
        .Values.Select(value => value.Label + "|" + value.Value).ToArray();

    private sealed class AimProbeWeapon : BasicProjectileWeapon
    {
        public float? Distance;
        public AimProbeWeapon() : base(null, null, null) { }
        public override float? GetManualAimConvergenceDistance() => Distance;
        public override WeaponGeometrySnapshot CaptureGeometry() => throw new InvalidOperationException("Aiming must not allocate debug geometry.");
        public WeaponGeometryGuide TargetingGuide(float range) => CreateAutomaticTargetingGuide(range);
    }

    private void AssertObservationOnly(BasicProjectileWeapon weapon, Action observation)
    {
        string dataBefore = EditorJsonUtility.ToJson(weapon.Runtime.Data);
        string runtimeBefore = JsonUtility.ToJson(weapon.Runtime);
        UnityEngine.Random.State randomBefore = UnityEngine.Random.state;
        int reports = 0;
        Action<WeaponDamageRoll> listener = _ => reports++;
        WeaponDamageResolver.OnDamageResolved += listener;
        try { observation(); }
        finally { WeaponDamageResolver.OnDamageResolved -= listener; }
        Assert.That(reports, Is.Zero);
        Assert.That(EditorJsonUtility.ToJson(weapon.Runtime.Data), Is.EqualTo(dataBefore));
        Assert.That(JsonUtility.ToJson(weapon.Runtime), Is.EqualTo(runtimeBefore));
        Assert.That(UnityEngine.Random.state, Is.EqualTo(randomBefore));
    }

    private static WeaponOptionalFloat Value(float value) => new() { Enabled = true, Value = value };
    private static string[] Flatten(WeaponDiagnosticsSnapshot snapshot) => snapshot.Sections.SelectMany(section =>
        section.Values.Select(value => section.Name + "|" + value.Label + "|" + value.Value)).ToArray();

    private WeaponData Copy(string name)
    {
        WeaponData data = Object.Instantiate(AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/" + name + ".asset"));
        _cleanup.Add(data);
        data.StatProfile = new WeaponStatProfile();
        return data;
    }

    private static WeaponStatLevelRow Row(WeaponData data, int level, WeaponUpgradePath path)
    {
        WeaponStatLevelRow row = WeaponStatProfile.CreateRow(data.WeaponType, level, path);
        data.StatProfile.Levels.Add(row);
        return row;
    }

    private static BasicProjectileWeapon Weapon(WeaponData data, PlayerStats stats, int level, WeaponUpgradePath path,
        WeaponState state = WeaponState.Automatic)
    {
        var weapon = (BasicProjectileWeapon)WeaponBehaviourFactory.Create(data, new ConfiguredEnemyTargeting(), null, stats.transform, null);
        weapon.Setup(new WeaponInstance { Data = data, Level = level, SelectedPath = path, State = state, CurrentAmmo = 5f }, stats.transform, stats, null);
        return weapon;
    }

    private PlayerStats Stats()
    {
        var stats = NewObject("Consumer neutral stats").AddComponent<PlayerStats>();
        var definitions = new List<StatDefinition>();
        foreach (StatType type in Enum.GetValues(typeof(StatType)))
        {
            StatDefinition definition = ScriptableObject.CreateInstance<StatDefinition>();
            _cleanup.Add(definition);
            Set(definition, "<StatType>k__BackingField", type);
            Set(definition, "<BaseValue>k__BackingField", type == StatType.AbilityCooldownReduction || type == StatType.CriticalChance
                ? 0f : type == StatType.CriticalDamage ? 2f : 1f);
            definitions.Add(definition);
        }
        Set(stats, "_statDefinitions", definitions);
        Invoke(stats, "Awake");
        return stats;
    }

    private GameObject NewObject(string name) { var go = new GameObject(name); _cleanup.Add(go); return go; }
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    private static object Invoke(object target, string name, params object[] values) => target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, values);
}
#endif
