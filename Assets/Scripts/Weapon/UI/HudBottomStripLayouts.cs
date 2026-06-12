using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Jerarquía visual del BottomStrip (passives + armas). Se crea en el prefab vía editor, no en Play.
/// </summary>
public static class HudBottomStripLayouts
{
    public const float PassiveSlotSpacing = 20f;

    public static void BuildPassivesColumn(Transform columnCenter)
    {
        if (columnCenter == null)
            return;

        Transform existing = columnCenter.Find("Passives");
        if (existing != null)
            ClearChild(columnCenter, "Passives");

        var passivesGo = new GameObject("Passives", typeof(RectTransform));
        passivesGo.transform.SetParent(columnCenter, false);
        HudUiWire.StretchFull(passivesGo.GetComponent<RectTransform>());

        var layout = passivesGo.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = PassiveSlotSpacing;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childControlWidth = false;
        layout.childControlHeight = false;

        (PassiveItemSlot slot, int index, HudPlaceholderKind placeholder)[] slots =
        {
            (PassiveItemSlot.Head, 0, HudPlaceholderKind.Head),
            (PassiveItemSlot.Torso, 0, HudPlaceholderKind.Torso),
            (PassiveItemSlot.Arm, 0, HudPlaceholderKind.Arm),
            (PassiveItemSlot.Arm, 1, HudPlaceholderKind.Arm),
            (PassiveItemSlot.Leg, 0, HudPlaceholderKind.Leg),
            (PassiveItemSlot.Leg, 1, HudPlaceholderKind.Leg)
        };

        for (int i = 0; i < slots.Length; i++)
        {
            var slotRoot = new GameObject($"PassiveSlot_{i}", typeof(RectTransform));
            slotRoot.transform.SetParent(passivesGo.transform, false);
            slotRoot.GetComponent<RectTransform>().sizeDelta = new Vector2(52f, 56f);

            HudUiFactory.CreateIconSlot(slotRoot.transform, "Icon", 48f, null, slots[i].placeholder);
            CreateLevelBadge(slotRoot.transform, "Level");
        }
    }

    public static void BuildWeaponColumn(Transform columnRight)
    {
        if (columnRight == null)
            return;

        ClearChild(columnRight, "WeaponSlots");
        ClearChild(columnRight, "WeaponPanel");
        ClearChild(columnRight, "DashCharges");
        ClearChild(columnRight, "WeaponCluster");

        BuildWeaponSlotsRow(columnRight);
        BuildWeaponPanel(columnRight);
        BuildDashRow(columnRight);
    }

    private static void BuildWeaponSlotsRow(Transform parent)
    {
        var slotsGo = new GameObject("WeaponSlots", typeof(RectTransform));
        slotsGo.transform.SetParent(parent, false);
        var slotsRt = slotsGo.GetComponent<RectTransform>();
        slotsRt.anchorMin = new Vector2(0f, 1f);
        slotsRt.anchorMax = new Vector2(1f, 1f);
        slotsRt.pivot = new Vector2(1f, 1f);
        slotsRt.anchoredPosition = new Vector2(0f, 0f);
        slotsRt.sizeDelta = new Vector2(0f, 56f);

        var row = slotsGo.AddComponent<HorizontalLayoutGroup>();
        row.spacing = 8f;
        row.childAlignment = TextAnchor.MiddleRight;
        row.childControlWidth = false;
        row.childControlHeight = false;

        for (int i = 0; i < WeaponManager.MaxWeaponSlots; i++)
        {
            var slotRoot = new GameObject($"WeaponSlot_{i}", typeof(RectTransform));
            slotRoot.transform.SetParent(slotsGo.transform, false);
            slotRoot.GetComponent<RectTransform>().sizeDelta = new Vector2(52f, 52f);

            var frameGo = new GameObject("Frame", typeof(RectTransform));
            frameGo.transform.SetParent(slotRoot.transform, false);
            HudUiWire.StretchFull(frameGo.GetComponent<RectTransform>());
            var frame = frameGo.AddComponent<Image>();
            frame.sprite = HudUiFactory.WhiteSprite;
            frame.color = HudUiFactory.BorderColor;
            frame.raycastTarget = false;

            HudUiFactory.CreateIconSlot(slotRoot.transform, "Icon", 48f, null, HudPlaceholderKind.Weapon);
            CreateLevelBadge(slotRoot.transform, "Level", 11f);
        }
    }

    private static void BuildWeaponPanel(Transform parent)
    {
        var panelGo = new GameObject("WeaponPanel", typeof(RectTransform));
        panelGo.transform.SetParent(parent, false);
        var panelRt = panelGo.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0f, 0.5f);
        panelRt.anchorMax = new Vector2(1f, 0.5f);
        panelRt.pivot = new Vector2(1f, 0.5f);
        panelRt.anchoredPosition = Vector2.zero;
        panelRt.sizeDelta = new Vector2(0f, 88f);

        CreateAnchoredTmp(panelGo.transform, "WeaponName", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -2f), 28f, 17f, FontStyles.Bold);
        CreateAnchoredTmp(panelGo.transform, "WeaponLevel", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -28f), 20f, 14f, FontStyles.Normal);

        var ammoBarGo = new GameObject("AmmoBar", typeof(RectTransform));
        ammoBarGo.transform.SetParent(panelGo.transform, false);
        var ammoBarRt = ammoBarGo.GetComponent<RectTransform>();
        ammoBarRt.anchorMin = new Vector2(0f, 0.5f);
        ammoBarRt.anchorMax = new Vector2(1f, 0.5f);
        ammoBarRt.pivot = new Vector2(0.5f, 0.5f);
        ammoBarRt.anchoredPosition = new Vector2(-40f, -8f);
        ammoBarRt.sizeDelta = new Vector2(-100f, 18f);
        HudUiFactory.CreateHorizontalBar(ammoBarGo.transform, "AmmoFill", new Vector2(200f, 18f), new Color(0.95f, 0.85f, 0.25f, 1f));

        var ammoLabel = CreateAnchoredTmp(panelGo.transform, "AmmoLabel", new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -26f), 18f, 13f, FontStyles.Normal);
        ammoLabel.color = HudUiFactory.MutedTextColor;
        ammoLabel.text = "0/0";

        var abilityGo = new GameObject("AbilityCooldown", typeof(RectTransform));
        abilityGo.transform.SetParent(panelGo.transform, false);
        var abilityRt = abilityGo.GetComponent<RectTransform>();
        abilityRt.anchorMin = new Vector2(0f, 0f);
        abilityRt.anchorMax = new Vector2(0f, 0f);
        abilityRt.pivot = new Vector2(0f, 0f);
        abilityRt.anchoredPosition = Vector2.zero;
        abilityRt.sizeDelta = new Vector2(48f, 48f);
        HudUiFactory.CreateRadialFill(abilityGo.transform, "AbilityCooldownFill", 48f, new Color(0.3f, 0.75f, 1f, 1f));
        var qLabel = HudUiFactory.CreateLabel(abilityGo.transform, "QLabel", "Q", 18f, TextAlignmentOptions.Center);
        qLabel.fontStyle = FontStyles.Bold;

        var rotationGo = new GameObject("RotationCooldown", typeof(RectTransform));
        rotationGo.transform.SetParent(panelGo.transform, false);
        var rotationRt = rotationGo.GetComponent<RectTransform>();
        rotationRt.anchorMin = new Vector2(1f, 0f);
        rotationRt.anchorMax = new Vector2(1f, 0f);
        rotationRt.pivot = new Vector2(1f, 0f);
        rotationRt.anchoredPosition = Vector2.zero;
        rotationRt.sizeDelta = new Vector2(44f, 44f);
        HudUiFactory.CreateRadialFill(rotationGo.transform, "RotationCooldownFill", 44f, new Color(0.75f, 0.75f, 0.8f, 1f));
        var rotLabel = CreateAnchoredTmp(rotationGo.transform, "RotLabel", Vector2.zero, Vector2.one, Vector2.zero, 44f, 10f, FontStyles.Normal);
        rotLabel.alignment = TextAlignmentOptions.Center;
        rotLabel.text = "Rot";

        var abilityStatus = CreateAnchoredTmp(panelGo.transform, "AbilityStatus", new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(56f, 0f), 16f, 12f, FontStyles.Italic);
        abilityStatus.color = HudUiFactory.MutedTextColor;
    }

    private static void BuildDashRow(Transform parent)
    {
        var dashGo = new GameObject("DashCharges", typeof(RectTransform));
        dashGo.transform.SetParent(parent, false);
        var dashRt = dashGo.GetComponent<RectTransform>();
        dashRt.anchorMin = new Vector2(0f, 0f);
        dashRt.anchorMax = new Vector2(1f, 0f);
        dashRt.pivot = new Vector2(1f, 0f);
        dashRt.anchoredPosition = Vector2.zero;
        dashRt.sizeDelta = new Vector2(0f, 28f);

        var layoutGo = new GameObject("Layout", typeof(RectTransform));
        layoutGo.transform.SetParent(dashGo.transform, false);
        HudUiWire.StretchFull(layoutGo.GetComponent<RectTransform>());
        var layout = layoutGo.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = 6f;
        layout.childAlignment = TextAnchor.MiddleRight;
        layout.childControlWidth = false;
        layout.childControlHeight = false;
        layout.reverseArrangement = true;

        for (int i = 0; i < 3; i++)
        {
            var go = new GameObject($"Charge_{i}", typeof(RectTransform));
            go.transform.SetParent(layoutGo.transform, false);
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(20f, 20f);
            var img = go.AddComponent<Image>();
            img.sprite = HudUiFactory.WhiteSprite;
            img.color = new Color(0.3f, 0.85f, 1f, 1f);
            img.raycastTarget = false;
        }
    }

    private static void CreateLevelBadge(Transform parent, string name, float fontSize = 12f)
    {
        var badgeGo = new GameObject(name, typeof(RectTransform));
        badgeGo.transform.SetParent(parent, false);
        var badgeRt = badgeGo.GetComponent<RectTransform>();
        badgeRt.anchorMin = new Vector2(1f, 0f);
        badgeRt.anchorMax = new Vector2(1f, 0f);
        badgeRt.pivot = new Vector2(1f, 0f);
        badgeRt.anchoredPosition = new Vector2(4f, -2f);
        badgeRt.sizeDelta = new Vector2(28f, 18f);
        var badge = badgeGo.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(badge);
        badge.fontSize = fontSize;
        badge.alignment = TextAlignmentOptions.BottomRight;
        badge.color = Color.white;
        badge.raycastTarget = false;
    }

    private static TextMeshProUGUI CreateAnchoredTmp(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, float height, float fontSize, FontStyles style)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = new Vector2(0f, anchorMin.y);
        rt.offsetMin = offsetMin;
        rt.offsetMax = new Vector2(0f, offsetMin.y + height);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(tmp);
        tmp.fontSize = fontSize;
        tmp.fontStyle = style;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        return tmp;
    }

    private static void ClearChild(Transform parent, string childName)
    {
        Transform child = parent.Find(childName);
        if (child == null)
            return;

#if UNITY_EDITOR
        if (!Application.isPlaying)
            Object.DestroyImmediate(child.gameObject);
        else
#endif
            Object.Destroy(child.gameObject);
    }
}
