using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class WeaponBalanceSheetApplier
{
    private sealed class WeaponBalance
    {
        public string AssetPath;
        public float BaseDamage;
        public float BaseKnockback;
        public float BaseManualAmmo;
        public float ActiveAbilityAmmoCost;
        public float SkillCooldown;
        public float CannonManualBurstsPerSecond;
        public float MortarHeatManualSpeedBonus;
        public float[] BaseDamageByLevel;
        public float[] BaseAmmoByLevel;
        public float[] PathADamageByLevel;
        public float[] PathAAmmoByLevel;
        public float[] PathBDamageByLevel;
        public float[] PathBAmmoByLevel;
    }

    [MenuItem("Tools/ScrapWaves/Apply Weapon Balance Sheet Snapshot")]
    public static void ApplyFromSheetSnapshot()
    {
        foreach (WeaponBalance balance in Balances)
            Apply(balance);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("Applied weapon balance sheet snapshot to production WeaponData assets.");
    }

    private static readonly WeaponBalance[] Balances =
    {
        new()
        {
            AssetPath = "Assets/ScriptableObjects/WeaponSO/Flamethrower.asset",
            BaseDamage = 25f,
            BaseKnockback = 0f,
            BaseManualAmmo = 100f,
            SkillCooldown = 14f,
            BaseDamageByLevel = new[] { 25f, 35f, 45f, 55f, 65f },
            BaseAmmoByLevel = new[] { 100f, 120f, 140f, 160f, 180f },
            PathADamageByLevel = new[] { 75f, 85f, 95f, 105f, 150f },
            PathAAmmoByLevel = new[] { 200f, 220f, 240f, 260f, 300f },
            PathBDamageByLevel = new[] { 80f, 95f, 110f, 125f, 200f },
            PathBAmmoByLevel = new[] { 200f, 220f, 240f, 260f, 300f }
        },
        new()
        {
            AssetPath = "Assets/ScriptableObjects/WeaponSO/RocketLauncher.asset",
            BaseDamage = 70f,
            BaseKnockback = 1.5f,
            BaseManualAmmo = 40f,
            SkillCooldown = 8f,
            BaseDamageByLevel = new[] { 70f, 80f, 90f, 100f, 110f },
            BaseAmmoByLevel = new[] { 40f, 45f, 50f, 55f, 60f },
            PathADamageByLevel = new[] { 130f, 150f, 170f, 190f, 250f },
            PathAAmmoByLevel = new[] { 70f, 75f, 80f, 85f, 100f },
            PathBDamageByLevel = new[] { 120f, 130f, 140f, 150f, 175f },
            PathBAmmoByLevel = new[] { 70f, 75f, 80f, 85f, 100f }
        },
        new()
        {
            AssetPath = "Assets/ScriptableObjects/WeaponSO/Mortar.asset",
            BaseDamage = 100f,
            BaseKnockback = 1f,
            BaseManualAmmo = 15f,
            ActiveAbilityAmmoCost = 5f,
            SkillCooldown = 10f,
            MortarHeatManualSpeedBonus = 0.5f,
            BaseDamageByLevel = new[] { 100f, 125f, 150f, 175f, 200f },
            BaseAmmoByLevel = new[] { 15f, 16f, 17f, 18f, 20f },
            PathADamageByLevel = new[] { 100f, 112.5f, 125f, 137.5f, 200f },
            PathAAmmoByLevel = new[] { 21f, 22f, 23f, 24f, 30f },
            PathBDamageByLevel = new[] { 225f, 250f, 275f, 300f, 400f },
            PathBAmmoByLevel = new[] { 21f, 22f, 23f, 24f, 30f }
        },
        new()
        {
            AssetPath = "Assets/ScriptableObjects/WeaponSO/AutomaticCannon.asset",
            BaseDamage = 20f,
            BaseKnockback = 0.2f,
            BaseManualAmmo = 200f,
            SkillCooldown = 6f,
            CannonManualBurstsPerSecond = 3f,
            BaseDamageByLevel = new[] { 20f, 25f, 30f, 35f, 40f },
            BaseAmmoByLevel = new[] { 200f, 220f, 240f, 260f, 280f },
            PathADamageByLevel = new[] { 45f, 55f, 60f, 70f, 100f },
            PathAAmmoByLevel = new[] { 400f, 440f, 480f, 520f, 600f },
            PathBDamageByLevel = new[] { 300f, 340f, 380f, 420f, 600f },
            PathBAmmoByLevel = new[] { 40f, 45f, 50f, 55f, 80f }
        },
        new()
        {
            AssetPath = "Assets/ScriptableObjects/WeaponSO/RotatingBlade.asset",
            BaseDamage = 60f,
            BaseKnockback = 0.3f,
            BaseManualAmmo = 50f,
            SkillCooldown = 5f,
            BaseDamageByLevel = new[] { 60f, 70f, 80f, 90f, 115f },
            BaseAmmoByLevel = new[] { 50f, 55f, 60f, 65f, 75f },
            PathADamageByLevel = new[] { 55f, 55f, 55f, 55f, 65f },
            PathAAmmoByLevel = new[] { 80f, 85f, 90f, 95f, 120f },
            PathBDamageByLevel = new[] { 215f, 235f, 255f, 275f, 295f },
            PathBAmmoByLevel = new[] { 80f, 85f, 90f, 95f, 120f }
        }
    };

    private static void Apply(WeaponBalance balance)
    {
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>(balance.AssetPath);
        if (data == null)
        {
            Debug.LogWarning($"Weapon balance skipped missing asset: {balance.AssetPath}");
            return;
        }

        data.BaseDamage = balance.BaseDamage;
        data.BaseKnockback = balance.BaseKnockback;
        data.BaseManualAmmo = balance.BaseManualAmmo;
        if (balance.ActiveAbilityAmmoCost > 0f)
            data.ActiveAbilityAmmoCost = balance.ActiveAbilityAmmoCost;
        data.SkillCooldown = balance.SkillCooldown;
        if (data.WeaponType == WeaponType.AutomaticCannon && balance.CannonManualBurstsPerSecond > 0f)
            data.AutomaticCannon.CannonManualBurstsPerSecond = balance.CannonManualBurstsPerSecond;
        if (data.WeaponType == WeaponType.Mortar && balance.MortarHeatManualSpeedBonus > 0f)
        {
            data.Mortar.MortarHeatManualSpeedBonus = balance.MortarHeatManualSpeedBonus;
            data.Mortar.MortarActiveShellCount = 5;
        }

        data.LevelData = BuildLevelData(1, balance.BaseDamage, balance.BaseManualAmmo, balance.BaseDamageByLevel, balance.BaseAmmoByLevel);

        EnsurePathData(data);
        data.PathA.DamageMultiplier = 1f;
        data.PathA.AttackRateMultiplier = 1f;
        data.PathA.ManualAmmoOverride = -1f;
        data.PathA.LevelData = BuildLevelData(6, balance.BaseDamage, balance.BaseManualAmmo, balance.PathADamageByLevel, balance.PathAAmmoByLevel);

        data.PathB.DamageMultiplier = 1f;
        data.PathB.AttackRateMultiplier = 1f;
        data.PathB.ManualAmmoOverride = -1f;
        data.PathB.LevelData = BuildLevelData(6, balance.BaseDamage, balance.BaseManualAmmo, balance.PathBDamageByLevel, balance.PathBAmmoByLevel);

        EditorUtility.SetDirty(data);
    }

    private static List<WeaponLevelData> BuildLevelData(
        int firstLevel,
        float baseDamage,
        float baseManualAmmo,
        IReadOnlyList<float> damageByLevel,
        IReadOnlyList<float> ammoByLevel)
    {
        List<WeaponLevelData> levels = new();
        int count = Mathf.Min(damageByLevel.Count, ammoByLevel.Count);
        for (int i = 0; i < count; i++)
        {
            levels.Add(new WeaponLevelData
            {
                Level = firstLevel + i,
                DamageMultiplier = SafeRatio(damageByLevel[i], baseDamage),
                AttackRateMultiplier = 1f,
                ManualAmmoMultiplier = SafeRatio(ammoByLevel[i], baseManualAmmo)
            });
        }

        return levels;
    }

    private static float SafeRatio(float value, float basis)
    {
        return basis > 0f ? value / basis : 1f;
    }

    private static void EnsurePathData(WeaponData data)
    {
        data.PathA ??= new WeaponUpgradePathData();
        data.PathB ??= new WeaponUpgradePathData();
    }
}
