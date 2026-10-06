using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class FlamethrowerRadialBurstTests
{
    readonly List<Object> _cleanup = new();
    static readonly MethodInfo Sample = typeof(FlamethrowerCueVfx).GetMethod("ApplyFrame", BindingFlags.Instance | BindingFlags.NonPublic);
    [SetUp] public void Setup() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    [TearDown] public void Cleanup()
    {
        foreach (var puddle in Object.FindObjectsByType<FlamethrowerFuelPuddle>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            Object.DestroyImmediate(puddle.gameObject);
        for (int i=_cleanup.Count-1;i>=0;i--) if (_cleanup[i]!=null) Object.DestroyImmediate(_cleanup[i]);
        _cleanup.Clear();
    }
    T Track<T>(T item) where T:Object { _cleanup.Add(item); return item; }
    static void Set(object target,string field,object value) => target.GetType().GetField(field,BindingFlags.NonPublic|BindingFlags.Instance).SetValue(target,value);

    [TestCase(WeaponUpgradePath.None,1f,6f)]
    [TestCase(WeaponUpgradePath.None,1.75f,10.5f)]
    [TestCase(WeaponUpgradePath.PathA,1f,7.2f)]
    [TestCase(WeaponUpgradePath.PathA,1.5f,10.8f)]
    [TestCase(WeaponUpgradePath.PathB,1f,5.4f)]
    [TestCase(WeaponUpgradePath.PathB,2f,10.8f)]
    public void ActualQFeedback_PresentationMatchesDamageRadiusAndCenter(WeaponUpgradePath path,float area,float expectedRadius)
    {
        var owner=Track(new GameObject("Q radius owner")); owner.transform.position=new Vector3(17f,3f,-11f);
        PlayerStats stats=owner.AddComponent<PlayerStats>();
        var definitions=new List<StatDefinition>();
        foreach (var type in new[] {StatType.ProjectileAreaSize,StatType.DamageMultiplier,StatType.EliteDamageMultiplier,
            StatType.CriticalChance,StatType.CriticalDamage,StatType.AttackSpeedMultiplier,StatType.AmmoMultiplier,
            StatType.Knockback,StatType.AbilityDamageMultiplier,StatType.AbilityCooldownReduction,
            StatType.CloseRangeDamageMultiplier,StatType.LongRangeDamageMultiplier})
        {
            var definition=Track(ScriptableObject.CreateInstance<StatDefinition>());
            Set(definition,"<StatType>k__BackingField",type);
            Set(definition,"<BaseValue>k__BackingField",type==StatType.CriticalChance || type==StatType.AbilityCooldownReduction ? 0f : 1f);
            definitions.Add(definition);
        }
        Set(stats,"_statDefinitions",definitions);
        typeof(PlayerStats).GetMethod("Awake",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(stats,null);
        stats.AddModifier(new StatModifier(StatType.ProjectileAreaSize,area,StatUpgradeSource.TemporaryEffect,
            modifierType:StatModifierType.Multiplicative));
        var data=AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Flamethrower.asset");
        var runtime=new WeaponInstance {Data=data,CurrentAmmo=100f,Level=path==WeaponUpgradePath.None ? 1 : 6,
            State=WeaponState.Manual,SelectedPath=path};
        var weapon=new FlamethrowerWeapon(null,null,owner.transform,null);
        weapon.Setup(runtime,owner.transform,stats,null);
        var sink=new ShotRecorder(); weapon.SetPresentationSink(sink);
        weapon.UseActiveAbility(new Vector3(.3f,.8f,1f));
        Assert.That(sink.Shots,Is.EqualTo(1));
        Assert.That(sink.Shot.ExplosionRadius,Is.EqualTo(expectedRadius).Within(.0001f));
        Assert.That(runtime.CurrentAmmo,Is.EqualTo(60f));
        Assert.That(runtime.AbilityCooldownTimer,Is.EqualTo(14f).Within(.001f));
        var profile=data.PresentationProfile;
        Assert.That(profile.TryResolveCue(WeaponFeedbackEvent.ShotFired,in sink.Shot,out var cue),Is.True);
        var context=WeaponPresentationContext.FromFeedback(cue.Cue,in sink.Shot,profile,cue,GameFeelQualityLevel.High,false);
        var pooled=Create(cue.VfxPrefab); pooled.Play(in context,cue.Duration,0f,false);
        var vfx=pooled.GetComponent<FlamethrowerCueVfx>();
        Assert.That(vfx.ActiveDamageRadius,Is.EqualTo(expectedRadius).Within(.0001f));
        Assert.That(pooled.transform.position,Is.EqualTo(owner.transform.position));
        // Footprint is already full-sized on the damage frame. Aim pitch cannot tilt it.
        foreach (var vertex in vfx.ActiveBoundaryMesh.vertices)
        {
            var world=pooled.transform.Find("Q Radial Ignition").TransformPoint(vertex);
            Assert.That(world.y,Is.InRange(owner.transform.position.y,owner.transform.position.y+expectedRadius*.019f));
        }
        AssertFootprint(vfx,owner.transform.position,expectedRadius);
        for (int i=0;i<=26;i++)
        {
            Sample.Invoke(vfx,new object[]{i/60f/.72f});
            AssertAllGeometryInside(vfx,owner.transform.position,expectedRadius);
        }
        Vector3 emittedAt=pooled.transform.position; owner.transform.position+=Vector3.one*100;
        Assert.That(pooled.transform.position,Is.EqualTo(emittedAt),"A Q burst stays at its damage origin after the player moves.");
    }

    [Test]
    public void PoolReuse_ResetsRadiusClearsAllLayersAndDoesNotAllocateNewObjects()
    {
        var profile=AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>("Assets/ScriptableObjects/WeaponPresentation/FlamethrowerPresentation.asset");
        profile.TryGetCueData(WeaponPresentationCue.FlamethrowerActiveBurst,out var cue);
        var pooled=Create(cue.VfxPrefab); var vfx=pooled.GetComponent<FlamethrowerCueVfx>();
        int objects=pooled.GetComponentsInChildren<Transform>(true).Length;
        var mesh=vfx.ActiveBoundaryMesh;
        Set(vfx,"_size",10f); Set(vfx,"_explosionRadiusMultiplier",9f);
        for (int cycle=0;cycle<8;cycle++)
        {
            float radius=cycle%2==0 ? 12f : 2.7f;
            var origin=new Vector3(cycle,2f,-cycle);
            var context=new WeaponPresentationContext(cue.Cue,null,origin,Vector3.up,explosionRadius:radius);
            pooled.Play(in context,cue.Duration,cycle,false);
            AssertFootprint(vfx,origin,radius);
            Assert.That(vfx.ActiveVisualLifetime,Is.LessThanOrEqualTo(cue.Duration));
            Sample.Invoke(vfx,new object[]{vfx.ActiveVisualLifetime/.72f});
            foreach (var renderer in pooled.GetComponentsInChildren<MeshRenderer>()) Assert.That(renderer.enabled,Is.False);
            Assert.That(pooled.ShouldRelease(cycle+cue.Duration),Is.True);
            pooled.Release(); Assert.That(pooled.IsActive,Is.False);
            Assert.That(vfx.ActiveBoundaryMesh,Is.SameAs(mesh));
            Assert.That(pooled.GetComponentsInChildren<Transform>(true).Length,Is.EqualTo(objects));
        }
    }

    [Test]
    public void LowQualityAndReducedFlash_PreserveFullBoundaryWithSmallerDecorativeBudget()
    {
        var profile=AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>("Assets/ScriptableObjects/WeaponPresentation/FlamethrowerPresentation.asset");
        profile.TryGetCueData(WeaponPresentationCue.FlamethrowerActiveBurst,out var cue);
        var pooled=Create(cue.VfxPrefab); var vfx=pooled.GetComponent<FlamethrowerCueVfx>();
        var high=new WeaponPresentationContext(cue.Cue,null,Vector3.zero,Vector3.forward,explosionRadius:9f);
        pooled.Play(in high,cue.Duration,0f,false); int tongues=vfx.ActiveTongueCount,embers=vfx.ActiveEmberCount;
        pooled.Release();
        var low=new WeaponPresentationContext(cue.Cue,null,Vector3.zero,Vector3.forward,explosionRadius:9f,
            quality:GameFeelQualityLevel.Low,reducedFlash:true,reducedFlashIntensity:0f);
        pooled.Play(in low,cue.Duration,1f,false);
        AssertFootprint(vfx,Vector3.zero,9f);
        Assert.That(vfx.ActiveTongueCount,Is.LessThan(tongues)); Assert.That(vfx.ActiveEmberCount,Is.LessThan(embers));
        Assert.That(pooled.GetComponentsInChildren<ParticleSystem>(),Is.Empty);
        Assert.That(pooled.GetComponentsInChildren<Renderer>()[0].enabled,Is.True);
    }

    [Test]
    public void InspectorCurves_DriveTheActiveLayers()
    {
        var profile=AssetDatabase.LoadAssetAtPath<WeaponPresentationProfile>("Assets/ScriptableObjects/WeaponPresentation/FlamethrowerPresentation.asset");
        profile.TryGetCueData(WeaponPresentationCue.FlamethrowerActiveBurst,out var cue);
        var pooled=Create(cue.VfxPrefab); var vfx=pooled.GetComponent<FlamethrowerCueVfx>();
        var settings=(FlamethrowerRadialBurstSettings)typeof(FlamethrowerCueVfx).GetField("_activeBurstSettings",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(vfx);
        settings.FlameOpacity=AnimationCurve.Constant(0,1,0);
        var context=new WeaponPresentationContext(cue.Cue,null,Vector3.zero,Vector3.forward,explosionRadius:6);
        pooled.Play(in context,cue.Duration,0,false);
        Assert.That(pooled.transform.Find("Q Radial Ignition/Sharp Flame Tongues").GetComponent<Renderer>().enabled,Is.False);
        Assert.That(pooled.transform.Find("Q Radial Ignition/Instant Damage Footprint").GetComponent<Renderer>().enabled,Is.True);
    }

    PooledWeaponVfx Create(GameObject prefab)
    {
        var instance=Track(Object.Instantiate(prefab)); var pooled=instance.GetComponent<PooledWeaponVfx>(); pooled.Initialize(); return pooled;
    }
    static void AssertFootprint(FlamethrowerCueVfx vfx,Vector3 origin,float radius)
    {
        float max=0f; var root=vfx.transform.Find("Q Radial Ignition");
        foreach (var point in vfx.ActiveBoundaryMesh.vertices)
        {
            Vector3 world=root.TransformPoint(point)-origin;
            max=Mathf.Max(max,new Vector2(world.x,world.z).magnitude);
        }
        Assert.That(max,Is.EqualTo(radius).Within(.0002f));
    }
    static void AssertAllGeometryInside(FlamethrowerCueVfx vfx,Vector3 origin,float radius)
    {
        foreach (var filter in vfx.GetComponentsInChildren<MeshFilter>())
        foreach (var point in filter.sharedMesh.vertices)
        {
            Vector3 world=filter.transform.TransformPoint(point)-origin;
            Assert.That(new Vector2(world.x,world.z).magnitude,Is.LessThanOrEqualTo(radius+.0002f));
        }
    }
    sealed class ShotRecorder:IWeaponFeedbackSink
    {
        public WeaponFeedbackContext Shot; public int Shots;
        public void OnShotFired(in WeaponFeedbackContext context) { Shot=context; Shots++; }
        public void OnChargeStarted(in WeaponFeedbackContext context) { }
        public void OnChargeUpdated(in WeaponFeedbackContext context,float progress) { }
        public void OnChargeCancelled(in WeaponFeedbackContext context) { }
        public void OnSustainedFireStarted(in WeaponFeedbackContext context) { }
        public void OnSustainedFireStopped(in WeaponFeedbackContext context) { }
        public void OnProjectileImpact(in WeaponFeedbackContext context) { }
        public void OnDamageConfirmed(in WeaponFeedbackContext context) { }
        public void OnStatusApplied(in WeaponFeedbackContext context) { }
        public void OnAmmoEmpty(in WeaponFeedbackContext context) { }
        public void OnHeatThresholdCrossed(in WeaponFeedbackContext context,float threshold) { }
        public void ConfigureProjectile(Projectile projectile,ProjectilePresentationArchetypeId archetype,in WeaponFeedbackContext context) { }
        public void Emit(in WeaponPresentationContext context) { }
        public WeaponPresentationLoopHandle BeginLoop(in WeaponPresentationContext context)=>default;
        public void UpdateLoop(WeaponPresentationLoopHandle handle,in WeaponPresentationContext context) { }
        public void EndLoop(WeaponPresentationLoopHandle handle,in WeaponPresentationContext context) { }
    }
}
