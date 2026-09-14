using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public sealed class CraftingMaterialField
{
    public MaterialType Type;
    public TMP_Text NameText;
    public TMP_Text AmountText;
}

[Serializable]
public sealed class CraftingCandidateField
{
    public GameObject Root;
    public Image Icon;
    public TMP_Text NameText;
}

/// <summary>References to the hand-authored crafting hierarchy. Never constructs UI.</summary>
public sealed class CraftingMenuView : MonoBehaviour
{
    [Header("Shell")]
    public Canvas Canvas;
    public Button CloseButton;
    public TMP_Text StatusText;
    public CraftingMaterialField[] Materials;
    public CraftingWeaponSlotView[] Slots;

    [Header("Upgrade")]
    public GameObject UpgradePanel;
    public TMP_Text UpgradeNameText;
    public TMP_Text UpgradeLevelText;
    public TMP_Text[] UpgradeStatLabels;
    public TMP_Text[] UpgradeStatValues;
    public TMP_Text UpgradeCostText;
    public Button UpgradeButton;
    public TMP_Text UpgradeButtonText;

    [Header("Tinker")]
    public GameObject TinkerPanel;
    public TMP_Text TinkerCostLabel;
    public TMP_Text TinkerCostText;
    public Button TinkerButton;
    public CraftingCandidateField[] Candidates;

    [Header("Advanced Tinkering")]
    public GameObject AdvancedPanel;
    public TMP_Text AdvancedNameText;
    public TMP_Text AdvancedLevelText;
    public TMP_Text AdvancedPathText;
    public TMP_Text AdvancedDescriptionText;
    public TMP_Text AdvancedNoticeText;
    public TMP_Text AdvancedCostText;
    public Button AcceptButton;
    public Button DeclineButton;

    public bool IsConfigured
    {
        get
        {
            if (Canvas == null || CloseButton == null || StatusText == null
                || UpgradePanel == null || TinkerPanel == null || AdvancedPanel == null
                || UpgradeNameText == null || UpgradeLevelText == null || UpgradeCostText == null
                || UpgradeButton == null || UpgradeButtonText == null || TinkerCostLabel == null
                || TinkerCostText == null || TinkerButton == null || AdvancedNameText == null
                || AdvancedLevelText == null || AdvancedPathText == null || AdvancedDescriptionText == null
                || AdvancedNoticeText == null || AdvancedCostText == null || AcceptButton == null || DeclineButton == null
                || Slots == null || Slots.Length != 3 || Materials == null || Materials.Length != 6
                || Candidates == null || Candidates.Length < 5 || UpgradeStatLabels == null || UpgradeStatLabels.Length != 3
                || UpgradeStatValues == null || UpgradeStatValues.Length != 3) return false;
            foreach (CraftingWeaponSlotView slot in Slots)
                if (slot == null || slot.Button == null || slot.Background == null || slot.Border == null
                    || slot.Icon == null || slot.NameText == null || slot.LevelText == null) return false;
            foreach (CraftingMaterialField material in Materials)
                if (material == null || material.NameText == null || material.AmountText == null) return false;
            foreach (CraftingCandidateField candidate in Candidates)
                if (candidate == null || candidate.Root == null || candidate.Icon == null || candidate.NameText == null) return false;
            for (int i = 0; i < 3; i++)
                if (UpgradeStatLabels[i] == null || UpgradeStatValues[i] == null) return false;
            return true;
        }
    }

    public void ShowPanel(GameObject selected)
    {
        UpgradePanel.SetActive(selected == UpgradePanel);
        TinkerPanel.SetActive(selected == TinkerPanel);
        AdvancedPanel.SetActive(selected == AdvancedPanel);
    }
}
