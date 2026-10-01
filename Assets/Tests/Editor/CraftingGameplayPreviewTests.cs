#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public class CraftingGameplayPreviewTests
{
    private readonly List<Object> _cleanup = new();
    [TearDown] public void Cleanup() { foreach(var item in _cleanup) Object.DestroyImmediate(item); _cleanup.Clear(); }

    [TestCase("Flamethrower")] [TestCase("RocketLauncher")] [TestCase("Mortar")]
    [TestCase("AutomaticCannon")] [TestCase("RotatingBlade")]
    public void ProductionLevelsPathsModesAndHeat_UseGameplayGettersWithoutMutatingTuning(string name)
    {
        WeaponData data=AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/"+name+".asset");
        string serialized=EditorJsonUtility.ToJson(data);
        PlayerStats stats=Stats(); HeatManager heat=stats.gameObject.AddComponent<HeatManager>();
        int events=0; Action<WeaponDamageRoll> listener=_=>events++; WeaponDamageResolver.OnDamageResolved+=listener;
        try
        {
            foreach(var path in new[]{WeaponUpgradePath.None,WeaponUpgradePath.PathA,WeaponUpgradePath.PathB})
            foreach(var mode in new[]{WeaponState.Automatic,WeaponState.Manual})
            foreach(float heatPoints in new[]{0f,heat.TotalHeatCapacity})
            {
                Set(heat,"_currentHeat",heatPoints);
                int first=path==WeaponUpgradePath.None?1:6, last=path==WeaponUpgradePath.None?4:9;
                for(int level=first;level<=last;level++)
                {
                    BasicProjectileWeapon live=Weapon(data,stats,heat,level,path,mode);
                    BasicProjectileWeapon future=Weapon(data,stats,heat,level+1,path,mode);
                    string runtime=JsonUtility.ToJson(live.Runtime); var random=UnityEngine.Random.state;
                    WeaponDiagnosticsSnapshot preview=live.CaptureUpgradeDiagnostics(level+1);
                    Assert.That(Flatten(preview),Is.EqualTo(Flatten(future.CaptureDiagnostics())),name+" "+level+" "+path+" "+mode);
                    var readout=CraftingUpgradePreview.Build(live);
                    Assert.That(readout.Rows.Length,Is.EqualTo(3));
                    Assert.That(readout.Notice,Does.Contain(mode==WeaponState.Manual?"Manual:":"Automatic:"));
                    Assert.That(readout.Notice,Does.Contain("No crits, target bonuses or active abilities"));
                    Assert.That(JsonUtility.ToJson(live.Runtime),Is.EqualTo(runtime));
                    Assert.That(UnityEngine.Random.state,Is.EqualTo(random));
                }
            }
            Assert.That(events,Is.Zero);
            Assert.That(EditorJsonUtility.ToJson(data),Is.EqualTo(serialized));
        }
        finally { WeaponDamageResolver.OnDamageResolved-=listener; }
    }

    [Test]
    public void StaleCsvRowsCannotOverrideLiveDamage_AndUnchangedValuesAreHonest()
    {
        WeaponData data=Copy("RocketLauncher");
        data.BalanceStats.Add(new WeaponBalanceStatRow{StatId="Damage",Level=1,Zone=WeaponBalanceZone.Basic,Value=999});
        data.BalanceStats.Add(new WeaponBalanceStatRow{StatId="Damage",Level=2,Zone=WeaponBalanceZone.Basic,Value=1999});
        PlayerStats stats=Stats(); BasicProjectileWeapon live=Weapon(data,stats,null,1,WeaponUpgradePath.None,WeaponState.Automatic);
        CraftingUpgradePreview readout=CraftingUpgradePreview.Build(live);
        Assert.That(readout.Rows[0].Value,Is.EqualTo("25 (unchanged)"));
        Assert.That(readout.Notice,Does.StartWith("No changes to these gameplay stats."));
        Assert.That(CraftingUI.FormatTuning(data,"Damage",1,WeaponUpgradePath.None),Is.EqualTo("25"));
        Assert.That(CraftingUI.FormatTuning(data,"Damage",2,WeaponUpgradePath.None),Is.EqualTo("25"));
        stats.AddModifier(new StatModifier(StatType.DamageMultiplier,1f,StatUpgradeSource.LevelUp));
        Assert.That(CraftingUpgradePreview.Build(live).Rows[0].Value,Is.EqualTo("50 (unchanged)"));
    }

    [Test]
    public void ChangedGameplayRowsTakePriority_AndBindingUsesAuthoredHierarchy()
    {
        WeaponData data=Copy("RocketLauncher");
        PlayerStats stats=Stats(); BasicProjectileWeapon live=Weapon(data,stats,null,6,WeaponUpgradePath.PathA,WeaponState.Automatic);
        var root=Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/UI/RunMenus/CraftingMenu.prefab"));_cleanup.Add(root);
        var view=root.GetComponent<CraftingMenuView>();int children=root.GetComponentsInChildren<Transform>(true).Length;
        CraftingUI.BindUpgradePreview(view,live);
        Assert.That(view.UpgradeStatLabels[0].text,Is.EqualTo("Auto damage / hit"));
        Assert.That(view.UpgradeStatValues[0].text,Is.EqualTo("46 → 54"));
        Assert.That(view.UpgradeStatLabels[1].text,Is.EqualTo("Manual capacity"));
        Assert.That(view.UpgradeStatValues[1].text,Is.EqualTo("70 → 75"));
        Assert.That(view.UpgradePreviewNotice,Is.Not.Null);
        Assert.That(view.UpgradePreviewNotice.text,Does.StartWith("2 gameplay stat changes."));
        Assert.That(root.GetComponentsInChildren<Transform>(true).Length,Is.EqualTo(children));
    }

    [Test]
    public void ModeHeatAndMaximumAreExplicit_WithoutHypotheticalLevelEleven()
    {
        WeaponData data=Copy("RotatingBlade"); PlayerStats stats=Stats(); HeatManager heat=stats.gameObject.AddComponent<HeatManager>();
        Set(heat,"_currentHeat",heat.TotalHeatCapacity);
        BasicProjectileWeapon live=Weapon(data,stats,heat,10,WeaponUpgradePath.PathB,WeaponState.Manual);
        var preview=CraftingUpgradePreview.Build(live,true);
        string damage=live.CaptureDiagnostics().Sections.Single(section=>section.Name=="Manual").Values.Single(value=>value.Label=="Damage / hit (non-critical)").Value;
        Assert.That(preview.Rows[0].Value,Is.EqualTo(damage));
        Assert.That(preview.Notice,Does.StartWith("Maximum level."));
        Assert.That(preview.Notice,Does.Contain("Manual:"));
        Assert.That(live.Runtime.Level,Is.EqualTo(10));
    }

    private WeaponData Copy(string name) { var data=Object.Instantiate(AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/"+name+".asset"));_cleanup.Add(data);return data; }
    private static BasicProjectileWeapon Weapon(WeaponData data,PlayerStats stats,HeatManager heat,int level,WeaponUpgradePath path,WeaponState mode)
    {
        var weapon=(BasicProjectileWeapon)WeaponBehaviourFactory.Create(data,null,null,null,null);
        weapon.Setup(new WeaponInstance{Data=data,Level=level,SelectedPath=path,State=mode,CurrentAmmo=17,AbilityCooldownTimer=0.5f},stats.transform,stats,heat);return weapon;
    }
    private PlayerStats Stats()
    {
        var owner=new GameObject("Crafting preview test stats");_cleanup.Add(owner);var stats=owner.AddComponent<PlayerStats>();
        var definitions=new List<StatDefinition>();
        foreach(StatType type in Enum.GetValues(typeof(StatType)))
        {
            var definition=ScriptableObject.CreateInstance<StatDefinition>();_cleanup.Add(definition);
            Set(definition,"<StatType>k__BackingField",type);Set(definition,"<BaseValue>k__BackingField",type==StatType.AbilityCooldownReduction?0f:type==StatType.CriticalDamage?2f:1f);definitions.Add(definition);
        }
        Set(stats,"_statDefinitions",definitions);typeof(PlayerStats).GetMethod("Awake",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(stats,null);return stats;
    }
    private static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
    private static string[] Flatten(WeaponDiagnosticsSnapshot snapshot)=>snapshot.Sections.SelectMany(section=>section.Values.Select(value=>section.Name+"|"+value.Label+"|"+value.Value)).ToArray();
}
#endif
