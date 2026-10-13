using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class PassiveItemUiTextTests
{
    private const string PassiveFolder = "Assets/ScriptableObjects/PlayerSO/Passives/";
    private readonly List<PassiveItemData> _cleanup = new();

    [TearDown]
    public void TearDown()
    {
        foreach (PassiveItemData data in _cleanup)
            Object.DestroyImmediate(data);
        _cleanup.Clear();
    }

    [Test]
    public void NewAndUpgradeOffers_ShowAuthoredLevelTotals_WithoutChangingItemOrInstance()
    {
        PassiveItemData data = CreateItem(
            Bonus(StatType.DamageMultiplier, 0.15f, 0.3f),
            Bonus(StatType.MaxHealth, 30f, 60f));
        var instance = new PassiveItemInstance { Data = data, Level = 1, Slot = PassiveItemSlot.Core };
        string authoredData = JsonUtility.ToJson(data);

        Assert.That(PassiveItemUiText.BuildOfferDescription(new PassiveItemOffer(data, false, null)),
            Is.EqualTo("Slot: Core.\n+15% Damage +30 HP Max Health"));
        Assert.That(PassiveItemUiText.BuildOfferDescription(new PassiveItemOffer(data, true, instance)),
            Is.EqualTo("Upgrade to level 2.\n+30% Damage +60 HP Max Health"));

        Assert.That(JsonUtility.ToJson(data), Is.EqualTo(authoredData));
        Assert.That(instance.Level, Is.EqualTo(1));
        Assert.That(instance.Data, Is.SameAs(data));
    }

    [TestCase("Shop_Arm_HonedWeaponrySystem", 1, "Slot: Arm.\n+15% Damage")]
    [TestCase("Shop_Core_PlatedReactor", 1, "Slot: Core.\n+30 HP Max Health")]
    [TestCase("Shop_Core_RegenerativeNanotech", 1, "Slot: Core.\n+1 HP/s Health Regen +0.5 s Regen Delay Reduction")]
    [TestCase("Shop_Core_ScrapReassembly", 2, "Upgrade to level 2.\n+0.15% Lifesteal")]
    [TestCase("Shop_Head_ScavengerModule", 1, "Slot: Head.\n+5% Scavenging")]
    [TestCase("Shop_Leg_BionicBoots", 1, "Slot: Leg.\n+10% Movement Speed")]
    [TestCase("Shop_Leg_ExplosiveBoosters", 1, "Slot: Leg.\n+1 Dash Charges +10% Dash Speed")]
    public void ProductionOffers_UseStatUnits_WithoutEditingAuthoredAssets(string assetName, int level, string expected)
    {
        PassiveItemData data = AssetDatabase.LoadAssetAtPath<PassiveItemData>(PassiveFolder + assetName + ".asset");
        Assert.That(data, Is.Not.Null, assetName);
        string authoredData = EditorJsonUtility.ToJson(data);
        var instance = level > 1 ? new PassiveItemInstance { Data = data, Level = level - 1 } : null;

        string description = PassiveItemUiText.BuildOfferDescription(new PassiveItemOffer(data, level > 1, instance));

        Assert.That(description, Is.EqualTo(expected));
        Assert.That(EditorJsonUtility.ToJson(data), Is.EqualTo(authoredData));
    }

    [Test]
    public void PenaltyOffers_ShowOneNegativeSign_ForPercentAndFlatBonuses()
    {
        PassiveItemData data = CreateItem(
            Bonus(StatType.DamageMultiplier, -0.1f),
            Bonus(StatType.MovementSpeed, -0.25f),
            new PassiveStatBonus
            {
                StatType = StatType.JumpHeight,
                ModifierType = StatModifierType.Multiplicative,
                ValuesPerLevel = new[] { 0.8f }
            });

        Assert.That(PassiveItemUiText.BuildOfferDescription(new PassiveItemOffer(data, false, null)),
            Is.EqualTo("Slot: Core.\n-10% Damage -0.25 m/s Movement Speed -20% Jump Height"));
    }

    [Test]
    public void CooldownOffer_IdentifiesTheBonusAsAReduction()
    {
        PassiveItemData data = CreateItem(Bonus(StatType.AbilityCooldownReduction, 0.02f));

        Assert.That(PassiveItemUiText.BuildOfferDescription(new PassiveItemOffer(data, false, null)),
            Is.EqualTo("Slot: Core.\n+2% Ability Cooldown Reduction"));
    }

    [Test]
    public void ZeroBonuses_RemainOmittedAlongsideSmallNonzeroBonuses()
    {
        PassiveItemData data = CreateItem(Bonus(StatType.MaxHealth, 0f), Bonus(StatType.JumpHeight, 0.0002f));

        Assert.That(PassiveItemUiText.BuildOfferDescription(new PassiveItemOffer(data, false, null)),
            Is.EqualTo("Slot: Core.\n+<0.001 m Jump Height"));
    }

    private PassiveItemData CreateItem(params PassiveStatBonus[] bonuses)
    {
        PassiveItemData data = ScriptableObject.CreateInstance<PassiveItemData>();
        _cleanup.Add(data);
        SetPrivateField(data, "_slot", PassiveItemSlot.Core);
        SetPrivateField(data, "_bonusesPerLevel", new List<PassiveStatBonus>(bonuses));
        return data;
    }

    private static PassiveStatBonus Bonus(StatType type, params float[] values) => new()
    {
        StatType = type,
        ModifierType = StatModifierType.Additive,
        ValuesPerLevel = values
    };

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, fieldName);
        field.SetValue(target, value);
    }
}
