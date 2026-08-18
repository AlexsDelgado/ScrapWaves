using System.Text;
using UnityEngine;

public static class PassiveItemUiText
{
    public static string BuildOfferDescription(PassiveItemOffer offer)
    {
        if (offer.Data == null)
            return string.Empty;

        if (offer.IsUpgrade && offer.TargetInstance != null)
        {
            int nextLevel = offer.TargetInstance.Level + 1;
            return $"Mejora a nivel {nextLevel}.\n{BuildBonusesLine(offer.Data, nextLevel)}";
        }

        return $"Slot: {offer.Data.Slot}.\n{BuildBonusesLine(offer.Data, 1)}";
    }

    public static HudPlaceholderKind GetPlaceholderKind(PassiveItemData data)
    {
        if (data == null)
            return HudPlaceholderKind.None;

        return data.Slot switch
        {
            PassiveItemSlot.Head => HudPlaceholderKind.Head,
            PassiveItemSlot.Core => HudPlaceholderKind.Core,
            PassiveItemSlot.Arm => HudPlaceholderKind.Arm,
            PassiveItemSlot.Leg => HudPlaceholderKind.Leg,
            _ => HudPlaceholderKind.None
        };
    }

    private static string BuildBonusesLine(PassiveItemData data, int level)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < data.BonusesPerLevel.Count; i++)
        {
            PassiveStatBonus bonus = data.BonusesPerLevel[i];
            float value = bonus.GetValueForLevel(level);
            if (Mathf.Approximately(value, 0f))
                continue;

            if (sb.Length > 0)
                sb.Append(' ');

            string sign = bonus.ModifierType == StatModifierType.Multiplicative ? "x" : "+";
            sb.Append($"{sign}{value:0.##} {StatDisplayNames.GetDisplayName(bonus.StatType)}");
        }

        return sb.Length > 0 ? sb.ToString() : "Sin bonus configurado.";
    }
}
