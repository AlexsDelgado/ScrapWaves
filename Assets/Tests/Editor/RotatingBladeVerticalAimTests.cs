using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class RotatingBladeVerticalAimTests
{
    private const float Range = 4f;
    private const float Width = 0.4f;
    private readonly List<Object> _created = new();
    private readonly List<Transform> _targets = new();
    private readonly List<RotatingBladeWeapon> _weapons = new();
    private GameObject _challengeTracker;

    [SetUp]
    public void SetUp()
    {
        if (ChallengeProgressTracker.Instance != null) return;
        _challengeTracker = NewObject("Sword aim test challenge tracker");
        var tracker = _challengeTracker.AddComponent<ChallengeProgressTracker>();
        typeof(ChallengeProgressTracker).GetProperty(nameof(ChallengeProgressTracker.Instance)).SetValue(null, tracker);
    }

    [TearDown]
    public void TearDown()
    {
        foreach (Transform target in _targets)
            if (target != null) EnemyRegistry.Unregister(target);
        foreach (RotatingBladeWeapon weapon in _weapons)
        {
            RotatingBladeVfx vfx = GetVfx(weapon);
            if (vfx != null) Object.DestroyImmediate(vfx.gameObject);
        }
        for (int i = _created.Count - 1; i >= 0; i--)
            if (_created[i] != null) Object.DestroyImmediate(_created[i]);
        _targets.Clear();
        _weapons.Clear();
        _created.Clear();
        _challengeTracker = null;
    }

    [TestCase(0f, false)]
    [TestCase(45f, false)]
    [TestCase(-45f, false)]
    [TestCase(90f, false)]
    [TestCase(-90f, false)]
    [TestCase(0f, true)]
    [TestCase(45f, true)]
    [TestCase(-45f, true)]
    [TestCase(90f, true)]
    [TestCase(-90f, true)]
    public void Thrust_DamageAndVisualsFollowFullAim(float pitch, bool multiBlade)
    {
        RotatingBladeWeapon weapon = CreateWeapon(multiBlade ? WeaponUpgradePath.PathA : WeaponUpgradePath.None, out Transform owner);
        Vector3 direction = PitchDirection(pitch);
        Vector3 sideways = Vector3.Cross(direction, Mathf.Abs(direction.y) > 0.99f ? Vector3.forward : Vector3.up).normalized;
        TestDamageable hit = Target(owner.position + direction * 2f);
        TestDamageable behind = Target(owner.position - direction * 2f);
        TestDamageable beyond = Target(owner.position + direction * (Range + 1f));
        TestDamageable outsideWidth = Target(owner.position + direction * 2f + sideways * (Width + 0.4f));

        weapon.UseActiveAbility(direction * 7f);

        Assert.That(hit.Hits, Is.EqualTo(1));
        Assert.That(weapon.Runtime.CurrentAmmo, Is.EqualTo(18f));
        AssertThrust(GetVfx(weapon), owner.position, direction);
        if (multiBlade)
        {
            Assert.That(weapon.Runtime.AbilityCooldownTimer, Is.Zero);
            for (int i = 0; i < 2; i++)
            {
                weapon.TickManual(0.11f, -direction, false);
                AssertThrust(GetVfx(weapon), owner.position, direction);
            }
        }
        Assert.That(hit.Hits, Is.EqualTo(multiBlade ? 3 : 1));
        Assert.That(behind.Hits, Is.Zero);
        Assert.That(beyond.Hits, Is.Zero);
        Assert.That(outsideWidth.Hits, Is.Zero);
        Assert.That(weapon.Runtime.AbilityCooldownTimer, Is.EqualTo(3f).Within(0.001f));
        Assert.That(weapon.Runtime.CurrentAmmo, Is.EqualTo(18f));
        int completedHits = hit.Hits;
        weapon.UseActiveAbility(direction);
        weapon.TickManual(0.5f, direction, false);
        Assert.That(hit.Hits, Is.EqualTo(completedHits), "Completed thrusts must not restart during cooldown.");
        Assert.That(weapon.Runtime.CurrentAmmo, Is.EqualTo(18f));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Thrust_UsesSuppliedAimEvenWhenMainCameraFacesElsewhere(bool multiBlade)
    {
        GameObject cameraObject = NewObject("Unrelated main camera");
        cameraObject.tag = "MainCamera";
        cameraObject.AddComponent<Camera>();
        cameraObject.transform.rotation = Quaternion.LookRotation(Vector3.right);
        RotatingBladeWeapon weapon = CreateWeapon(multiBlade ? WeaponUpgradePath.PathA : WeaponUpgradePath.None, out Transform owner);
        TestDamageable target = Target(owner.position + Vector3.up * 2f);

        weapon.UseActiveAbility(Vector3.up);

        Assert.That(target.Hits, Is.EqualTo(1));
        AssertThrust(GetVfx(weapon), owner.position, Vector3.up);
    }

    [Test]
    public void Thrust_PreservesReticleAimConvergenceOnObstacle()
    {
        RotatingBladeWeapon weapon = CreateWeapon(WeaponUpgradePath.None, out Transform owner);
        owner.position = new Vector3(2000f, 1000f, 2000f);
        var provider = owner.gameObject.AddComponent<ReticleAimProvider>();
        Camera camera = NewObject("Offset gameplay camera").AddComponent<Camera>();
        camera.transform.position = owner.position + new Vector3(-2f, 3f, -6f);
        camera.transform.rotation = Quaternion.identity;
        SetField(provider, "_aimCamera", camera);
        GameObject obstacle = NewObject("Reticle obstacle");
        obstacle.transform.position = camera.transform.position + Vector3.forward * 10f;
        obstacle.AddComponent<BoxCollider>().size = new Vector3(2f, 2f, 0.5f);
        Physics.SyncTransforms();
        AimSolution aim = provider.ResolveWeaponAim(owner.position, weapon.Runtime);
        Assert.That(aim.IsValid, Is.True);
        Assert.That(aim.TargetPoint.z, Is.EqualTo(obstacle.transform.position.z - 0.25f).Within(0.01f));
        Assert.That(aim.Direction.y, Is.GreaterThan(0.2f));
        Assert.That(aim.Direction.x, Is.LessThan(-0.2f));
        TestDamageable target = Target(owner.position + aim.Direction * 2f);

        weapon.UseActiveAbility(aim.Direction);

        Assert.That(target.Hits, Is.EqualTo(1));
        AssertThrust(GetVfx(weapon), owner.position, aim.Direction);
    }

    [Test]
    public void MultiThrust_DelayedHitsFollowOwnerAndKeepActivationAim()
    {
        RotatingBladeWeapon weapon = CreateWeapon(WeaponUpgradePath.PathA, out Transform owner);
        Vector3 direction = PitchDirection(45f);
        TestDamageable first = Target(owner.position + direction * 2f);
        weapon.UseActiveAbility(direction);
        owner.position += Vector3.right * 10f;
        TestDamageable moved = Target(owner.position + direction * 2f);

        weapon.TickManual(0.11f, Vector3.down, false);
        AssertThrust(GetVfx(weapon), owner.position, direction);
        weapon.Runtime.State = WeaponState.Automatic;
        weapon.TickAutomatic(0.11f, Vector3.down);
        AssertThrust(GetVfx(weapon), owner.position, direction);

        Assert.That(first.Hits, Is.EqualTo(1));
        Assert.That(moved.Hits, Is.EqualTo(2));
        Assert.That(weapon.Runtime.AbilityCooldownTimer, Is.EqualTo(3f).Within(0.001f));
    }

    [Test]
    public void ZeroAim_FallsBackToOwnerForwardIncludingPitch()
    {
        RotatingBladeWeapon weapon = CreateWeapon(WeaponUpgradePath.None, out Transform owner);
        owner.rotation = Quaternion.Euler(-35f, 70f, 0f);
        weapon.UseActiveAbility(Vector3.zero);
        AssertThrust(GetVfx(weapon), owner.position, owner.forward);
    }

    [Test]
    public void AtomicSharpness_RemainsAHorizontalGroundDash()
    {
        // This upgrade moves the player using a planar motor. Changing that movement
        // is separate from allowing the base and multi-blade thrusts to follow pitch.
        RotatingBladeWeapon weapon = CreateWeapon(WeaponUpgradePath.PathB, out Transform owner);
        weapon.UseActiveAbility(PitchDirection(45f));
        LineRenderer line = GetVfx(weapon).transform.Find("Blade Thrust").GetComponent<LineRenderer>();
        Assert.That(line.enabled, Is.True);
        Assert.That(line.GetPosition(1).y, Is.EqualTo(line.GetPosition(0).y).Within(0.001f));
        Assert.That(weapon.Runtime.AbilityCooldownTimer, Is.EqualTo(3f).Within(0.001f));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void UnavailableAbility_DoesNotSpendAmmoOrCreateVfx(int reason)
    {
        RotatingBladeWeapon weapon = CreateWeapon(WeaponUpgradePath.None, out _);
        if (reason == 0) weapon.Runtime.CurrentAmmo = 0f;
        if (reason == 1) weapon.Runtime.AbilityCooldownTimer = 1f;
        if (reason == 2) weapon.Runtime.State = WeaponState.Automatic;
        float ammo = weapon.Runtime.CurrentAmmo;
        weapon.UseActiveAbility(Vector3.up);
        Assert.That(weapon.Runtime.CurrentAmmo, Is.EqualTo(ammo));
        Assert.That(GetVfx(weapon), Is.Null);
    }

    [TestCase(0f)]
    [TestCase(45f)]
    [TestCase(-45f)]
    [TestCase(90f)]
    [TestCase(-90f)]
    public void ThrustRibbon_StaysWithinDamageRangeWhileFadingAndCanBeReused(float pitch)
    {
        RotatingBladeWeapon weapon = CreateWeapon(WeaponUpgradePath.None, out Transform owner);
        weapon.Runtime.Data.RotatingBlade.BladeVisualDuration = 100f;
        Vector3 direction = PitchDirection(pitch);
        weapon.UseActiveAbility(direction);
        RotatingBladeVfx vfx = GetVfx(weapon);
        SetPulseTimers(vfx, 50f);
        Invoke(vfx, "Update");
        AssertThrust(vfx, owner.position, direction);
        SetPulseTimers(vfx, 0f);
        Invoke(vfx, "Update");
        Assert.That(vfx.ActiveThrustSurfaceCount, Is.Zero);
        Assert.That(vfx.transform.Find("Blade Thrust").GetComponent<LineRenderer>().enabled, Is.False);
        vfx.ShowThrust(owner.position, -direction, Range, Width, 1f);
        Assert.That(vfx.ActiveThrustSurfaceCount, Is.EqualTo(1));
        AssertThrust(vfx, owner.position, -direction);
    }

    private RotatingBladeWeapon CreateWeapon(WeaponUpgradePath path, out Transform owner)
    {
        owner = NewObject("Sword aim test owner").transform;
        owner.position = new Vector3(50f, 50f, 50f);
        WeaponData data = ScriptableObject.CreateInstance<WeaponData>();
        _created.Add(data);
        data.WeaponId = "SwordVerticalAimRegression";
        data.WeaponType = WeaponType.RotatingBlade;
        data.BaseDamage = 10f;
        data.BaseRange = Range;
        data.BaseManualAmmo = 20f;
        data.ActiveAbilityAmmoCost = 2f;
        data.SkillCooldown = 3f;
        data.EnsureSpecificTuningForCurrentType();
        data.RotatingBlade.BladeManualRange = Range;
        data.RotatingBlade.BladeActiveBaseRangeMultiplier = 1f;
        data.RotatingBlade.BladeActiveMaxRangeMultiplier = 1f;
        data.RotatingBlade.BladeActiveLineWidth = Width;
        data.RotatingBlade.BladeActiveKnockbackScale = 0f;
        data.RotatingBlade.BladeVisualDuration = 1f;
        data.LevelData = new List<WeaponLevelData>
        {
            new() { Level = 1, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f },
            new() { Level = 7, DamageMultiplier = 1f, AttackRateMultiplier = 1f, ManualAmmoMultiplier = 1f }
        };
        var runtime = new WeaponInstance { Data = data, Level = path == WeaponUpgradePath.None ? 1 : 7,
            SelectedPath = path, State = WeaponState.Manual, CurrentAmmo = 20f };
        var weapon = new RotatingBladeWeapon(null, null, null);
        weapon.Setup(runtime, owner, null, null);
        _weapons.Add(weapon);
        return weapon;
    }

    private TestDamageable Target(Vector3 position)
    {
        GameObject target = NewObject("Sword aim target");
        target.transform.position = position;
        _targets.Add(target.transform);
        var damageable = target.AddComponent<TestDamageable>();
        EnemyRegistry.Register(target.transform);
        return damageable;
    }

    private GameObject NewObject(string name)
    {
        var result = new GameObject(name);
        _created.Add(result);
        return result;
    }

    private static Vector3 PitchDirection(float pitch) => pitch == 90f ? Vector3.up
        : pitch == -90f ? Vector3.down : Quaternion.Euler(-pitch, 0f, 0f) * Vector3.forward;

    private static RotatingBladeVfx GetVfx(RotatingBladeWeapon weapon) =>
        (RotatingBladeVfx)typeof(RotatingBladeWeapon).GetField("_vfx", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(weapon);

    private static void AssertThrust(RotatingBladeVfx vfx, Vector3 origin, Vector3 direction)
    {
        Assert.That(vfx, Is.Not.Null);
        LineRenderer line = vfx.transform.Find("Blade Thrust").GetComponent<LineRenderer>();
        Assert.That(line.enabled, Is.True);
        Assert.That(Vector3.Distance(line.GetPosition(0), origin), Is.LessThan(0.001f));
        Assert.That(Vector3.Distance(line.GetPosition(1), origin + direction * Range), Is.LessThan(0.001f));
        MeshFilter latestRibbon = null;
        foreach (MeshFilter candidate in vfx.GetComponentsInChildren<MeshFilter>())
        {
            if (candidate.name.StartsWith("Blade Thrust Ribbon") && candidate.GetComponent<MeshRenderer>().enabled)
                latestRibbon = candidate;
        }
        Assert.That(latestRibbon, Is.Not.Null);
        Assert.That(Vector3.Distance(latestRibbon.transform.position, origin), Is.LessThan(0.001f));
        Assert.That(Vector3.Dot(latestRibbon.transform.forward, direction), Is.GreaterThan(0.999f));
        float maximum = float.NegativeInfinity;
        foreach (Vector3 vertex in latestRibbon.sharedMesh.vertices)
        {
            float projection = Vector3.Dot(latestRibbon.transform.TransformPoint(vertex) - origin, direction);
            Assert.That(float.IsNaN(projection) || float.IsInfinity(projection), Is.False);
            Assert.That(projection, Is.InRange(-0.001f, Range + 0.001f));
            maximum = Mathf.Max(maximum, projection);
        }
        Assert.That(maximum, Is.EqualTo(Range).Within(0.001f));
    }

    private static void SetPulseTimers(RotatingBladeVfx vfx, float timer)
    {
        SetField(vfx, "_thrustTimer", timer);
        var pulses = (IEnumerable)typeof(RotatingBladeVfx).GetField("_thrustSurfaces", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(vfx);
        foreach (object pulse in pulses) pulse.GetType().GetField("Timer").SetValue(pulse, timer);
    }

    private static void SetField(object target, string name, object value) =>
        target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    private static void Invoke(object target, string method) =>
        target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, null);

    private sealed class TestDamageable : MonoBehaviour, IDamageable
    {
        public int Hits { get; private set; }
        public bool ApplyDamage(int amount) { Hits++; return true; }
    }
}