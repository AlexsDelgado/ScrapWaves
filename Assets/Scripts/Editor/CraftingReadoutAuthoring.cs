#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Explicit editor-only migration. Existing authored readouts are left untouched.</summary>
public static class CraftingReadoutAuthoring
{
    public const string HudPath = "Assets/Prefabs/UI/GameplayHud V2.prefab";
    [MenuItem("ScrapWaves/UI/Author Crafting Availability Readouts")]
    public static void AuthorPrefabs()
    {
        Modify(RunMenuPrefabBuilder.CraftingPrefabPath, root => AuthorMenu(root.GetComponent<CraftingMenuView>()));
        Modify(HudPath, AuthorHud);
        AssetDatabase.SaveAssets();
    }
    [MenuItem("ScrapWaves/UI/Tune Translucent Crafting Pulse")]
    public static void TuneHighlights()
    {
        foreach (string path in new[] { RunMenuPrefabBuilder.CraftingPrefabPath, HudPath })
            Modify(path, root => {
                foreach (CraftingAvailabilityView view in root.GetComponentsInChildren<CraftingAvailabilityView>(true)) Tune(view);
            });
        AssetDatabase.SaveAssets();
    }
    private static void Tune(CraftingAvailabilityView view)
    {
        view.GlowColor = new Color(0.35f,0.9f,0.48f,0.045f);
        view.OutlineColor = new Color(0.35f,0.9f,0.48f,0.65f);
        view.PulseSpeed = 2.4f; view.PulseDepth = 0.72f;
        view.OutlineEdges = new Image[4];
        string[] sides = { "Top", "Bottom", "Left", "Right" };
        for (int i=0;i<sides.Length;i++) view.OutlineEdges[i]=view.Glow.transform.Find(sides[i]).GetComponent<Image>();
        view.Glow.color = view.GlowColor;
        foreach (Image edge in view.OutlineEdges) edge.color = view.OutlineColor;
    }
    private static void Modify(string path, Action<GameObject> author)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try { author(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
        finally { PrefabUtility.UnloadPrefabContents(root); }
    }
    public static void AuthorMenu(CraftingMenuView view)
    {
        if (view.BalanceReadout == null)
        {
            Transform parent = view.Materials[0].NameText.transform.parent.parent;
            RectTransform area = Rect(parent, "MaterialBalances", new Vector2(44, -117), new Vector2(1652, 76));
            view.BalanceReadout = Readout(area, view.Materials[0].AmountText.font, true);
            foreach (CraftingMaterialField field in view.Materials)
            { field.NameText.gameObject.SetActive(false); field.AmountText.gameObject.SetActive(false); }
        }
        if (view.UpgradeReadout == null) view.UpgradeReadout = Cost(view.UpgradeCostText);
        if (view.TinkerReadout == null) view.TinkerReadout = Cost(view.TinkerCostText);
        if (view.AdvancedReadout == null) view.AdvancedReadout = Cost(view.AdvancedCostText);
        EnsureLayout(view.BalanceReadout, true);
        EnsureLayout(view.UpgradeReadout, false);
        EnsureLayout(view.TinkerReadout, false);
        EnsureLayout(view.AdvancedReadout, false);
        foreach (CraftingWeaponSlotView slot in view.Slots)
            if (slot.Availability == null) slot.Availability = Availability(slot.transform, slot.NameText.font);
    }
    public static void AuthorHud(GameObject root)
    {
        foreach (WeaponClusterHud hud in root.GetComponentsInChildren<WeaponClusterHud>(true))
        {
            Transform cluster = hud.transform.Find("WeaponCluster") ?? hud.transform;
            Transform slots = cluster.Find("WeaponSlots");
            TMP_Text text = hud.GetComponentInChildren<TMP_Text>(true);
            if (slots == null || text == null) continue;
            for (int i = 0; i < WeaponManager.MaxWeaponSlots; i++)
            {
                Transform slot = slots.Find($"WeaponSlot_{i}");
                if (slot != null && slot.GetComponent<CraftingAvailabilityView>() == null)
                    Availability(slot, text.font);
            }
        }
    }
    private static void EnsureLayout(CraftingMaterialReadout readout, bool balance)
    {
        if (readout.GetComponent<HorizontalLayoutGroup>() != null) return;
        var layout=readout.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.spacing=balance?24:8;layout.childAlignment=TextAnchor.MiddleLeft;
        layout.childControlWidth=false;layout.childControlHeight=false;
        layout.childForceExpandWidth=false;layout.childForceExpandHeight=false;
        foreach (var entry in readout.Entries)
        {
            RectTransform rect=(RectTransform)entry.Root.transform;
            rect.sizeDelta=new Vector2(balance?254:172,rect.sizeDelta.y);
        }
        readout.Notice.gameObject.AddComponent<LayoutElement>().ignoreLayout=true;
    }
    private static CraftingMaterialReadout Cost(TMP_Text legacy)
    {
        RectTransform area = Rect(legacy.transform.parent, legacy.name + "MaterialReadout", Vector2.zero, Vector2.zero);
        RectTransform source = legacy.rectTransform;
        area.anchorMin=source.anchorMin; area.anchorMax=source.anchorMax; area.pivot=source.pivot;
        area.sizeDelta=source.sizeDelta; area.anchoredPosition=source.anchoredPosition;
        legacy.gameObject.SetActive(false);
        return Readout(area, legacy.font, false);
    }
    private static CraftingMaterialReadout Readout(RectTransform area, TMP_FontAsset font, bool balance)
    {
        CraftingMaterialReadout view = area.gameObject.AddComponent<CraftingMaterialReadout>();
        view.Entries = new CraftingMaterialReadout.Entry[6];
        float stride = balance ? 278 : 180;
        foreach (MaterialType type in Enum.GetValues(typeof(MaterialType)))
        {
            int i=(int)type;
            RectTransform row=Rect(area, type.ToString(), new Vector2(i*stride, 0), new Vector2(stride-8, balance ? 76 : 45));
            Image icon=Rect(row,"Icon",new Vector2(0,-4),new Vector2(36,36)).gameObject.AddComponent<Image>();
            icon.sprite=AssetDatabase.LoadAssetAtPath<Sprite>($"Assets/Art/UI/Icons/Materials/Material_{type}.png");
            if (icon.sprite == null) throw new InvalidOperationException($"Missing material sprite {type}");
            icon.preserveAspect=true; icon.raycastTarget=false;
            TMP_Text qty=Text(Rect(row,"Quantity",new Vector2(44,-4),new Vector2(stride-52,36)),font,balance?28:23);
            qty.text=balance?"0":"0/0";
            view.Entries[i]=new CraftingMaterialReadout.Entry { Type=type,Root=row.gameObject,Icon=icon,Quantity=qty };
        }
        view.Notice=Text(Rect(area,"Notice",Vector2.zero,new Vector2(1084,45)),font,23);
        view.Notice.gameObject.SetActive(false);
        return view;
    }
    private static CraftingAvailabilityView Availability(Transform parent,TMP_FontAsset font)
    {
        CraftingAvailabilityView view=parent.gameObject.AddComponent<CraftingAvailabilityView>();
        RectTransform glow=Rect(parent,"CraftingAvailabilityGlow",Vector2.zero,Vector2.zero);
        glow.anchorMin=Vector2.zero; glow.anchorMax=Vector2.one; glow.offsetMin=new Vector2(-3,-3); glow.offsetMax=new Vector2(3,3);
        view.Glow=glow.gameObject.AddComponent<Image>(); view.Glow.color=new Color(0.35f,0.9f,0.48f,0.06f); view.Glow.raycastTarget=false;
        view.GlowColor=new Color(0.35f,0.9f,0.48f,0.08f);
        foreach (string side in new[] {"Top","Bottom","Left","Right"})
        {
            RectTransform edge=Rect(glow,side,Vector2.zero,Vector2.zero);
            bool horizontal=side=="Top"||side=="Bottom";
            edge.anchorMin=side=="Top"?new Vector2(0,1):side=="Right"?new Vector2(1,0):Vector2.zero;
            edge.anchorMax=side=="Bottom"?new Vector2(1,0):side=="Left"?new Vector2(0,1):Vector2.one;
            edge.pivot=new Vector2(0.5f,0.5f); edge.sizeDelta=horizontal?new Vector2(0,3):new Vector2(3,0); edge.anchoredPosition=Vector2.zero;
            Image image=edge.gameObject.AddComponent<Image>(); image.color=new Color(0.35f,0.9f,0.48f,0.9f); image.raycastTarget=false;
        }
        RectTransform mark=Rect(parent,"CraftingAvailabilityMarker",Vector2.zero,new Vector2(24,24));
        mark.anchorMin=mark.anchorMax=mark.pivot=Vector2.one; mark.anchoredPosition=new Vector2(-3,-3);
        view.Marker=Text(mark,font,20); view.Marker.alignment=TextAlignmentOptions.Center; view.Marker.color=new Color(0.35f,0.9f,0.48f,1);
        Tune(view);
        view.Glow.gameObject.SetActive(false); view.Marker.gameObject.SetActive(false);
        return view;
    }
    private static RectTransform Rect(Transform parent,string name,Vector2 position,Vector2 size)
    {
        RectTransform rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent,false);rect.anchorMin=rect.anchorMax=rect.pivot=new Vector2(0,1);rect.anchoredPosition=position;rect.sizeDelta=size;return rect;
    }
    private static TMP_Text Text(RectTransform rect,TMP_FontAsset font,float size)
    {
        TextMeshProUGUI text=rect.gameObject.AddComponent<TextMeshProUGUI>();text.font=font;text.fontSize=size;text.color=Color.white;
        text.alignment=TextAlignmentOptions.MidlineLeft;text.textWrappingMode=TextWrappingModes.NoWrap;text.raycastTarget=false;return text;
    }
}
#endif
