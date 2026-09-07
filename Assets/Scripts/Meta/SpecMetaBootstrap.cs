using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Registra challenges Spec (página 23), Path B unlockables, y asegura power-up / meta en el player.
/// </summary>
public static class SpecMetaBootstrap
{
    private static bool s_Registered;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void OnSceneLoaded()
    {
        EnsurePlayerComponents();
        EnsureRegistered();
    }

    public static void EnsureRegistered()
    {
        if (s_Registered || SaveManager.Instance == null)
            return;

        var list = new List<AchievementDefinition>
        {
            AchievementDefinition.CreateRuntime(
                "thaw_them_out", "Thaw them out", "Melt 2500 enemies with your flamethrower.",
                AchievementConditionType.WeaponKillsTotal, 2500f, true, "Flamethrower", null,
                "WeaponPath_Flamethrower_PathB", 50),
            AchievementDefinition.CreateRuntime(
                "to_little_pieces", "To little pieces", "Explode 2500 enemies with your rocket launcher.",
                AchievementConditionType.WeaponKillsTotal, 2500f, true, "RocketLauncher", null,
                "WeaponPath_RocketLauncher_PathB", 50),
            AchievementDefinition.CreateRuntime(
                "make_it_rain", "Make it rain", "Kill 2500 enemies with your mortar.",
                AchievementConditionType.WeaponKillsTotal, 2500f, true, "Mortar", null,
                "WeaponPath_Mortar_PathB", 50),
            AchievementDefinition.CreateRuntime(
                "go_for_the_head", "Go for the head", "Kill 2500 enemies with your automatic cannon.",
                AchievementConditionType.WeaponKillsTotal, 2500f, true, "AutomaticCannon", null,
                "WeaponPath_AutomaticCannon_PathB", 50),
            AchievementDefinition.CreateRuntime(
                "studied_the_blade", "Studied the blade", "Kill 2500 enemies with your rotating blade.",
                AchievementConditionType.WeaponKillsTotal, 2500f, true, "RotatingBlade", null,
                "WeaponPath_RotatingBlade_PathB", 50),
            AchievementDefinition.CreateRuntime(
                "high_value_targets", "High-value targets", "Kill 250 variant, elite or boss enemies.",
                AchievementConditionType.EliteOrBossKillsTotal, 250f, true, null, null,
                "Shop_Head_BountyHunterModule", 40),
            AchievementDefinition.CreateRuntime(
                "resourcefulness", "Resourcefulness is a virtue", "Loot 10000 drops.",
                AchievementConditionType.DropsLootedTotal, 10000f, true, null, null,
                "Shop_Head_ScavengerModule", 40),
            AchievementDefinition.CreateRuntime(
                "not_how_you_heal", "Not how you heal yourself", "Go from 100% to 0% HP in under 5 seconds.",
                AchievementConditionType.RunChallenge, 1f, false, null, "not_how_you_heal",
                "Shop_Core_ScrapReassembly", 30),
            AchievementDefinition.CreateRuntime(
                "survival_adept", "Survival adept", "Spend 2 minutes without taking damage.",
                AchievementConditionType.RunChallenge, 120f, false, null, "survival_adept",
                "Shop_Core_ElectromagneticShield", 30),
            AchievementDefinition.CreateRuntime(
                "one_shot_one_kill", "One shot, one kill", "Deal 1000 damage in a single shot.",
                AchievementConditionType.RunChallenge, 1000f, false, null, "one_shot_one_kill",
                "Shop_Arm_AdvancedTargetingModule", 30),
        };

        SaveManager.Instance.RegisterRuntimeAchievements(list);
        RegisterPathUnlockCards();
        s_Registered = true;
    }

    private static void EnsurePlayerComponents()
    {
        PlayerMovement player = Object.FindAnyObjectByType<PlayerMovement>();
        if (player == null)
            return;

        if (player.GetComponent<TemporaryPowerupController>() == null)
            player.gameObject.AddComponent<TemporaryPowerupController>();
        if (player.GetComponent<MetaProgressionApplier>() == null)
            player.gameObject.AddComponent<MetaProgressionApplier>();
    }

    private static void RegisterPathUnlockCards()
    {
        UnlockCatalog catalog = Resources.Load<UnlockCatalog>("Meta/UnlockCatalog");
        if (catalog == null)
            return;

        for (int i = 0; i < catalog.Weapons.Count; i++)
        {
            WeaponData weapon = catalog.Weapons[i];
            if (weapon == null || string.IsNullOrEmpty(weapon.WeaponId))
                continue;
            if (!IsSpecWeapon(weapon.WeaponId))
                continue;

            WeaponPathUnlockData path = WeaponPathUnlockData.CreateRuntime(weapon, WeaponUpgradePath.PathB, 50);
            catalog.RegisterRuntimePathUnlock(path);
        }
    }

    private static bool IsSpecWeapon(string weaponId)
    {
        return weaponId == "Flamethrower"
            || weaponId == "RocketLauncher"
            || weaponId == "Mortar"
            || weaponId == "AutomaticCannon"
            || weaponId == "RotatingBlade";
    }
}
