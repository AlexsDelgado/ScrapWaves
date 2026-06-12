using System.Text;
using UnityEngine;

public static class PassiveItemUiText
{
    public static string BuildOfferDescription(PassiveItemOffer offer)
    {
        if (offer.Data == null)
            return string.Empty;

        if (offer.IsUpgrade && offer.TargetInstance != null)
            return $"Mejora a nivel {offer.TargetInstance.Level + 1}.\n{BuildBonusesLine(offer.Data)}";

        return $"Slot: {offer.Data.Slot}.\n{BuildBonusesLine(offer.Data)}";
    }

    public static HudPlaceholderKind GetPlaceholderKind(PassiveItemData data)
    {
        if (data == null)
            return HudPlaceholderKind.None;

        return data.Slot switch
        {
            PassiveItemSlot.Head => HudPlaceholderKind.Head,
            PassiveItemSlot.Torso => HudPlaceholderKind.Torso,
            PassiveItemSlot.Arm => HudPlaceholderKind.Arm,
            PassiveItemSlot.Leg => HudPlaceholderKind.Leg,
            _ => HudPlaceholderKind.None
        };
    }

    private static string BuildBonusesLine(PassiveItemData data)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < data.BonusesPerLevel.Count; i++)
        {
            PassiveStatBonus bonus = data.BonusesPerLevel[i];
            if (Mathf.Approximately(bonus.ValuePerLevel, 0f))
                continue;

            if (sb.Length > 0)
                sb.Append(' ');
            sb.Append($"+{bonus.ValuePerLevel:0.##} {StatDisplayNames.GetDisplayName(bonus.StatType)}/nivel");
        }

        return sb.Length > 0 ? sb.ToString() : "Sin bonus configurado.";
    }
}
