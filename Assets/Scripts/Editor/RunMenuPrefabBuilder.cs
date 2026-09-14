#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// Explicit, one-time authoring tool for the run choice and crafting menus.
/// Existing prefab and copy assets are preserved so subsequent changes can be made by hand.
/// </summary>
public static class RunMenuPrefabBuilder
{
    public const string PrefabFolder = "Assets/Prefabs/UI/RunMenus";
    public const string LevelUpPrefabPath = PrefabFolder + "/LevelUpMenu.prefab";
    public const string WeaponSelectionPrefabPath = PrefabFolder + "/WeaponSelectionMenu.prefab";
    public const string CraftingPrefabPath = PrefabFolder + "/CraftingMenu.prefab";
    public const string ContentPath = "Assets/Data/UI/RunMenus/RunMenuContent.asset";
    private const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

    // Match the authored title screen and pause menu. Each resulting Graphic remains editable.
    private static readonly Color DeepSteel = new(0.067f, 0.078f, 0.075f, 1f);
    private static readonly Color Plate = new(0.122f, 0.145f, 0.133f, 1f);
    private static readonly Color SelectedPlate = new(0.22f, 0.25f, 0.22f, 1f);
    private static readonly Color Bone = new(0.949f, 0.961f, 0.922f, 1f);
    private static readonly Color MutedSteel = new(0.678f, 0.741f, 0.69f, 1f);
    private static readonly Color Rust = new(0.851f, 0.416f, 0.196f, 1f);
    private static readonly Color RustLight = new(0.95f, 0.60f, 0.36f, 1f);
    private static TMP_FontAsset s_font;

    /// <summary>Compatibility entry point. Menu ownership and binding now belong to scenes.</summary>
    public static void BuildAndAttach() => CreateMissingMenuAssets();

    [MenuItem("ScrapWaves/UI/Create Missing Run Menu Assets")]
    public static void CreateMissingMenuAssets()
    {
        s_font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
        if (s_font == null)
            throw new InvalidOperationException($"Missing menu font: {FontPath}");
        EnsureFolder(PrefabFolder);
        EnsureFolder("Assets/Data/UI/RunMenus");
        GetOrCreateContent();
        GetOrCreatePrefab(LevelUpPrefabPath, () => CreateChoiceMenu(false));
        GetOrCreatePrefab(WeaponSelectionPrefabPath, () => CreateChoiceMenu(true));
        GetOrCreatePrefab(CraftingPrefabPath, CreateCraftingMenu);
        Debug.Log("Missing run menu assets created. Existing prefabs and copy were preserved. Place and bind menu instances under each gameplay scene's UI root.");
    }

    private static RunMenuContent GetOrCreateContent()
    {
        RunMenuContent existing = AssetDatabase.LoadAssetAtPath<RunMenuContent>(ContentPath);
        if (existing != null)
            return existing;

        string[] names = { "Flamethrower", "AutomaticCannon", "RocketLauncher", "Mortar", "RotatingBlade" };
        var copies = new WeaponMenuCopy[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            string path = $"Assets/ScriptableObjects/WeaponSO/{names[i]}.asset";
            WeaponData weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
            if (weapon == null)
                throw new InvalidOperationException($"Missing production weapon for menu copy: {path}");
            copies[i] = new WeaponMenuCopy
            {
                Weapon = weapon,
                Summary = string.Empty,
                Description = string.Empty,
                PathADescription = string.Empty,
                PathBDescription = string.Empty
            };
        }

        var content = ScriptableObject.CreateInstance<RunMenuContent>();
        content.Weapons = copies;
        AssetDatabase.CreateAsset(content, ContentPath);
        return content;
    }

    private static GameObject GetOrCreatePrefab(string path, Func<GameObject> create)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
            return existing;

        GameObject root = create();
        try
        {
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path);
            if (saved == null)
                throw new InvalidOperationException($"Unable to save menu prefab: {path}");
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static GameObject CreateChoiceMenu(bool firstWeapon)
    {
        string name = firstWeapon ? "WeaponSelectionMenu" : "LevelUpMenu";
        Canvas canvas = CreateCanvas(name, 5000);
        var view = canvas.gameObject.AddComponent<ChoiceMenuView>();
        view.Canvas = canvas;
        float height = firstWeapon ? 740f : 700f;
        RectTransform window = CreateWindow(canvas.transform, new Vector2(1400f, height));
        view.TitleText = Label(window, "Title", firstWeapon ? "CHOOSE YOUR FIRST WEAPON" : "LEVEL UP",
            38f, FontStyles.Bold, TextAlignmentOptions.Center);
        Top(view.TitleText.rectTransform, 44f, 1312f, 34f, 54f);
        view.SubtitleText = Label(window, "Subtitle", firstWeapon ? "Choose 1 of 2 weapons" : "Choose one upgrade",
            27f, FontStyles.Normal, TextAlignmentOptions.Center, MutedSteel);
        Top(view.SubtitleText.rectTransform, 44f, 1312f, 92f, 40f);
        view.FooterText = Label(window, "Footer", firstWeapon ? "Select a weapon to start your run." : "Select an upgrade to continue",
            23f, FontStyles.Normal, TextAlignmentOptions.Center, MutedSteel);
        Bottom(view.FooterText.rectTransform, 44f, 1312f, 19f, 36f);

        RectTransform cards = Rect(window, "Cards");
        Stretch(cards, 44f, 44f, 154f, 74f);
        var row = cards.gameObject.AddComponent<HorizontalLayoutGroup>();
        ConfigureRow(row, 24f);
        int count = firstWeapon ? 2 : 3;
        view.Cards = new ChoiceCardView[count];
        for (int i = 0; i < count; i++)
            view.Cards[i] = CreateChoiceCard(cards, i, firstWeapon);
        if (!firstWeapon)
            ApplyLevelUpTextFit(canvas.gameObject);
        return canvas.gameObject;
    }

    /// <summary>
    /// Adjusts an editable level-up prefab instance for long passive names and bonus lists.
    /// This does not load or save assets; callers explicitly control whether to persist it.
    /// </summary>
    public static void ApplyLevelUpTextFit(GameObject root)
    {
        if (root == null)
            throw new ArgumentNullException(nameof(root));
        ChoiceMenuView view = root.GetComponent<ChoiceMenuView>();
        if (view == null || view.Cards == null || view.Cards.Length != 3)
            throw new InvalidOperationException("Expected the authored three-card level-up menu.");

        foreach (ChoiceCardView card in view.Cards)
        {
            if (card == null || card.NameText == null || card.SummaryText == null || card.DescriptionText == null)
                throw new InvalidOperationException("Level-up card text references must be assigned before adjusting text fit.");
            RectTransform iconFrame = card.transform.Find("IconFrame") as RectTransform;
            if (iconFrame == null)
                throw new InvalidOperationException("Expected an authored IconFrame on each level-up card.");

            iconFrame.anchorMin = iconFrame.anchorMax = new Vector2(0.5f, 1f);
            iconFrame.pivot = new Vector2(0.5f, 1f);
            iconFrame.anchoredPosition = new Vector2(0f, -28f);
            iconFrame.sizeDelta = new Vector2(120f, 120f);
            AcrossTop(card.NameText.rectTransform, 24f, 169f, 72f);
            AcrossTop(card.SummaryText.rectTransform, 24f, 245f, 30f);
            AcrossTop(card.DescriptionText.rectTransform, 26f, 276f, 106f);
            card.DescriptionText.fontSize = 22f;
        }
    }

    private static ChoiceCardView CreateChoiceCard(Transform parent, int index, bool firstWeapon)
    {
        RectTransform card = Rect(parent, $"Choice_{index + 1}");
        card.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        ImageGraphic(card, MutedSteel);
        RectTransform fill = Rect(card, "Background");
        Stretch(fill, 2f, 2f, 2f, 2f);
        ImageGraphic(fill, Plate);
        var view = card.gameObject.AddComponent<ChoiceCardView>();

        RectTransform iconFrame = Rect(card, "IconFrame");
        iconFrame.anchorMin = iconFrame.anchorMax = new Vector2(0.5f, 1f);
        iconFrame.pivot = new Vector2(0.5f, 1f);
        iconFrame.anchoredPosition = new Vector2(0f, -52f);
        iconFrame.sizeDelta = new Vector2(132f, 132f);
        ImageGraphic(iconFrame, new Color(MutedSteel.r, MutedSteel.g, MutedSteel.b, 0.75f));
        RectTransform icon = Rect(iconFrame, "Icon");
        Stretch(icon, 2f, 2f, 2f, 2f);
        view.Icon = ImageGraphic(icon, Color.white);
        view.Icon.preserveAspect = true;

        view.NameText = Label(card, "Name", firstWeapon ? "WEAPON" : "UPGRADE", 28f, FontStyles.Bold, TextAlignmentOptions.Center);
        AcrossTop(view.NameText.rectTransform, 24f, 218f, 42f);
        view.SummaryText = Label(card, "Summary", string.Empty, 23f, FontStyles.Normal, TextAlignmentOptions.Center, MutedSteel);
        AcrossTop(view.SummaryText.rectTransform, 24f, 263f, 42f);
        view.DescriptionText = Label(card, "Description", string.Empty, 24f, FontStyles.Normal, TextAlignmentOptions.Center);
        AcrossTop(view.DescriptionText.rectTransform, 26f, 308f, firstWeapon ? 94f : 74f);
        view.SelectButton = Button(card, "SelectButton", "SELECT", true, out _);
        AcrossBottom((RectTransform)view.SelectButton.transform, 28f, 28f, 56f);
        return view;
    }

    private static GameObject CreateCraftingMenu()
    {
        Canvas canvas = CreateCanvas("CraftingMenu", 5100);
        var view = canvas.gameObject.AddComponent<CraftingMenuView>();
        view.Canvas = canvas;
        RectTransform window = CreateWindow(canvas.transform, new Vector2(1740f, 740f));
        TMP_Text title = Label(window, "Title", "CRAFTING STATION", 33f, FontStyles.Bold);
        Top(title.rectTransform, 44f, 1200f, 25f, 56f);
        view.CloseButton = Button(window, "CloseStation", "Close station", true, out _);
        Top((RectTransform)view.CloseButton.transform, 1460f, 236f, 26f, 54f);
        Rule(window, "HeaderRule", 44f, 1652f, 99f);
        CreateMaterials(window, view);
        Rule(window, "MaterialsRule", 44f, 1652f, 204f);

        TMP_Text weaponsHeading = Label(window, "WeaponsHeading", "WEAPONS", 19f, FontStyles.Bold);
        Top(weaponsHeading.rectTransform, 44f, 475f, 230f, 28f);
        view.Slots = new CraftingWeaponSlotView[3];
        for (int i = 0; i < view.Slots.Length; i++)
            view.Slots[i] = CreateWeaponSlot(window, i);
        RectTransform divider = Rect(window, "DetailsDivider");
        Top(divider, 562f, 2f, 244f, 436f);
        ImageGraphic(divider, MutedSteel);

        RectTransform details = Rect(window, "Details");
        Top(details, 612f, 1084f, 238f, 436f);
        CreateUpgradePanel(details, view);
        CreateTinkerPanel(details, view);
        CreateAdvancedPanel(details, view);
        view.TinkerPanel.SetActive(false);
        view.AdvancedPanel.SetActive(false);

        view.StatusText = Label(window, "Status", string.Empty, 22f, FontStyles.Normal, TextAlignmentOptions.Center, Bone);
        Bottom(view.StatusText.rectTransform, 44f, 1652f, 19f, 32f);
        return canvas.gameObject;
    }

    private static void CreateMaterials(RectTransform window, CraftingMenuView view)
    {
        MaterialType[] types = { MaterialType.SheetMetal, MaterialType.MetalPipe, MaterialType.Gears,
            MaterialType.JellifiedFuel, MaterialType.PlasticExplosive, MaterialType.Wiring };
        string[] names = { "Sheet metal", "Metal pipes", "Gears", "Jellified fuel", "Plastic explosives", "Wiring" };
        view.Materials = new CraftingMaterialField[types.Length];
        for (int i = 0; i < types.Length; i++)
        {
            RectTransform field = Rect(window, types[i].ToString());
            Top(field, 44f + i * 278f, 254f, 117f, 76f);
            TMP_Text name = Label(field, "Name", names[i], 21f, FontStyles.Normal, TextAlignmentOptions.Left, MutedSteel);
            AcrossTop(name.rectTransform, 0f, 0f, 28f);
            TMP_Text amount = Label(field, "Amount", "0", 28f, FontStyles.Bold);
            AcrossTop(amount.rectTransform, 0f, 32f, 38f);
            view.Materials[i] = new CraftingMaterialField { Type = types[i], NameText = name, AmountText = amount };
        }
    }

    private static CraftingWeaponSlotView CreateWeaponSlot(RectTransform window, int index)
    {
        RectTransform root = Rect(window, $"WeaponSlot_{index + 1}");
        Top(root, 44f, 475f, 276f + index * 132f, 106f);
        var view = root.gameObject.AddComponent<CraftingWeaponSlotView>();
        view.Background = ImageGraphic(root, Plate, true);
        RectTransform normalBorder = Rect(root, "Border");
        Stretch(normalBorder);
        AddFrameEdges(normalBorder, new Color(MutedSteel.r, MutedSteel.g, MutedSteel.b, 0.45f));
        RectTransform border = Rect(root, "SelectedBorder");
        Stretch(border);
        view.Border = ImageGraphic(border, Color.clear);
        AddFrameEdges(border, MutedSteel);
        view.NormalColor = Plate;
        view.SelectedColor = SelectedPlate;
        view.Button = root.gameObject.AddComponent<Button>();
        ConfigureButton(view.Button, view.Background);
        RectTransform frame = Rect(root, "IconFrame");
        Top(frame, 22f, 64f, 21f, 64f);
        ImageGraphic(frame, MutedSteel);
        RectTransform icon = Rect(frame, "Icon");
        Stretch(icon, 2f, 2f, 2f, 2f);
        view.Icon = ImageGraphic(icon, Color.white);
        view.Icon.preserveAspect = true;
        view.NameText = Label(root, "Name", "Empty slot", 25f, FontStyles.Bold);
        Top(view.NameText.rectTransform, 106f, 345f, 17f, 36f);
        view.LevelText = Label(root, "Level", "+ Tinker", 23f, FontStyles.Normal, TextAlignmentOptions.Left, MutedSteel);
        Top(view.LevelText.rectTransform, 106f, 345f, 57f, 32f);
        return view;
    }

    private static void CreateUpgradePanel(RectTransform parent, CraftingMenuView view)
    {
        RectTransform panel = Rect(parent, "Upgrade");
        Stretch(panel);
        view.UpgradePanel = panel.gameObject;
        view.UpgradeNameText = Label(panel, "WeaponName", "Weapon", 32f, FontStyles.Bold);
        AcrossTop(view.UpgradeNameText.rectTransform, 0f, 0f, 44f);
        view.UpgradeLevelText = Label(panel, "Level", "LV 1 → 2", 29f);
        AcrossTop(view.UpgradeLevelText.rectTransform, 0f, 48f, 40f);
        TMP_Text configuredHeading = Label(panel, "ConfiguredStatsHeading", "Configured weapon stats", 18f,
            FontStyles.Normal, TextAlignmentOptions.Left, MutedSteel);
        AcrossTop(configuredHeading.rectTransform, 0f, 99f, 27f);
        string[] labels = { "Damage", "Auto range", "Manual ammo" };
        view.UpgradeStatLabels = new TMP_Text[3];
        view.UpgradeStatValues = new TMP_Text[3];
        for (int i = 0; i < labels.Length; i++)
        {
            RectTransform stat = Rect(panel, $"Stat_{i + 1}");
            Top(stat, i * 365f, 338f, 132f, 100f);
            TMP_Text label = Label(stat, "Label", labels[i], 24f, FontStyles.Normal, TextAlignmentOptions.Left, MutedSteel);
            AcrossTop(label.rectTransform, 0f, 0f, 36f);
            view.UpgradeStatLabels[i] = label;
            view.UpgradeStatValues[i] = Label(stat, "Value", "—", 29f, FontStyles.Bold);
            AcrossTop(view.UpgradeStatValues[i].rectTransform, 0f, 45f, 46f);
        }
        Rule(panel, "CostRule", 0f, 1084f, 246f);
        TMP_Text costHeading = Label(panel, "CostHeading", "COST", 19f, FontStyles.Bold);
        AcrossTop(costHeading.rectTransform, 0f, 272f, 26f);
        view.UpgradeCostText = Label(panel, "Cost", "—", 26f, FontStyles.Bold);
        AcrossTop(view.UpgradeCostText.rectTransform, 0f, 306f, 52f);
        view.UpgradeButton = Button(panel, "UpgradeButton", "UPGRADE", true, out TMP_Text buttonText);
        view.UpgradeButtonText = buttonText;
        AcrossBottom((RectTransform)view.UpgradeButton.transform, 0f, 0f, 54f);
    }

    private static void CreateTinkerPanel(RectTransform parent, CraftingMenuView view)
    {
        RectTransform panel = Rect(parent, "Tinker");
        Stretch(panel);
        view.TinkerPanel = panel.gameObject;
        TMP_Text heading = Label(panel, "Heading", "New weapon", 32f, FontStyles.Bold);
        AcrossTop(heading.rectTransform, 0f, 0f, 44f);
        TMP_Text explanation = Label(panel, "Explanation", "One random weapon you do not own.", 27f);
        AcrossTop(explanation.rectTransform, 0f, 50f, 46f);
        RectTransform candidates = Rect(panel, "Candidates");
        Top(candidates, 0f, 1084f, 128f, 128f);
        ConfigureRow(candidates.gameObject.AddComponent<HorizontalLayoutGroup>(), 20f);
        view.Candidates = new CraftingCandidateField[5];
        for (int i = 0; i < view.Candidates.Length; i++)
        {
            RectTransform candidate = Rect(candidates, $"Candidate_{i + 1}");
            candidate.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
            RectTransform frame = Rect(candidate, "IconFrame");
            Top(frame, 0f, 72f, 0f, 72f);
            ImageGraphic(frame, MutedSteel);
            RectTransform icon = Rect(frame, "Icon");
            Stretch(icon, 2f, 2f, 2f, 2f);
            Image iconImage = ImageGraphic(icon, Color.white);
            iconImage.preserveAspect = true;
            TMP_Text label = Label(candidate, "Name", "Weapon", 22f, FontStyles.Bold);
            AcrossTop(label.rectTransform, 0f, 83f, 45f);
            view.Candidates[i] = new CraftingCandidateField { Root = candidate.gameObject, Icon = iconImage, NameText = label };
        }
        Rule(panel, "CostRule", 0f, 1084f, 270f);
        view.TinkerCostLabel = Label(panel, "CostHeading", "COST", 19f, FontStyles.Bold);
        AcrossTop(view.TinkerCostLabel.rectTransform, 0f, 293f, 27f);
        view.TinkerCostText = Label(panel, "Cost", "—", 26f, FontStyles.Bold);
        AcrossTop(view.TinkerCostText.rectTransform, 0f, 329f, 45f);
        view.TinkerButton = Button(panel, "TinkerButton", "TINKER", true, out _);
        AcrossBottom((RectTransform)view.TinkerButton.transform, 0f, 0f, 54f);
    }

    private static void CreateAdvancedPanel(RectTransform parent, CraftingMenuView view)
    {
        RectTransform panel = Rect(parent, "AdvancedTinkering");
        Stretch(panel);
        view.AdvancedPanel = panel.gameObject;
        view.AdvancedNameText = Label(panel, "WeaponName", "Weapon", 32f, FontStyles.Bold);
        AcrossTop(view.AdvancedNameText.rectTransform, 0f, 0f, 44f);
        view.AdvancedLevelText = Label(panel, "Level", "LV 5 → 6", 29f);
        AcrossTop(view.AdvancedLevelText.rectTransform, 0f, 48f, 40f);
        view.AdvancedPathText = Label(panel, "PathName", "Advanced path", 28f, FontStyles.Bold);
        AcrossTop(view.AdvancedPathText.rectTransform, 0f, 116f, 40f);
        view.AdvancedDescriptionText = Label(panel, "PathDescription", string.Empty, 25f);
        AcrossTop(view.AdvancedDescriptionText.rectTransform, 0f, 161f, 72f);
        Rule(panel, "OfferRule", 0f, 1084f, 246f);
        view.AdvancedNoticeText = Label(panel, "OfferNotice", string.Empty, 24f);
        AcrossTop(view.AdvancedNoticeText.rectTransform, 0f, 265f, 62f);
        view.AdvancedCostText = Label(panel, "Cost", "—", 25f, FontStyles.Bold);
        AcrossTop(view.AdvancedCostText.rectTransform, 0f, 329f, 44f);
        RectTransform actions = Rect(panel, "Actions");
        AcrossBottom(actions, 0f, 0f, 54f);
        ConfigureRow(actions.gameObject.AddComponent<HorizontalLayoutGroup>(), 28f);
        view.AcceptButton = Button(actions, "AcceptButton", "ACCEPT", true, out _);
        view.AcceptButton.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
        view.DeclineButton = Button(actions, "DeclineButton", "DECLINE", false, out _);
        view.DeclineButton.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;
    }

    private static Canvas CreateCanvas(string name, int sortingOrder)
    {
        var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        root.layer = LayerMask.NameToLayer("UI");
        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;
        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        scaler.matchWidthOrHeight = 0.5f;
        RectTransform backdrop = Rect(root.transform, "Backdrop");
        Stretch(backdrop);
        ImageGraphic(backdrop, new Color(0.018f, 0.026f, 0.022f, 0.35f), true);
        return canvas;
    }

    private static RectTransform CreateWindow(Transform parent, Vector2 size)
    {
        RectTransform window = Rect(parent, "Window");
        window.anchorMin = window.anchorMax = window.pivot = new Vector2(0.5f, 0.5f);
        window.sizeDelta = size;
        ImageGraphic(window, MutedSteel, true);
        RectTransform innerBorder = Rect(window, "InnerBorder");
        Stretch(innerBorder, 3f, 3f, 3f, 3f);
        ImageGraphic(innerBorder, DeepSteel);
        RectTransform fineBorder = Rect(innerBorder, "FineBorder");
        Stretch(fineBorder, 3f, 3f, 3f, 3f);
        ImageGraphic(fineBorder, new Color(MutedSteel.r, MutedSteel.g, MutedSteel.b, 0.55f));
        RectTransform fill = Rect(fineBorder, "Fill");
        Stretch(fill, 1f, 1f, 1f, 1f);
        ImageGraphic(fill, DeepSteel);
        return window;
    }

    private static Button Button(Transform parent, string name, string text, bool primary, out TMP_Text label)
    {
        RectTransform root = Rect(parent, name);
        ImageGraphic(root, MutedSteel);
        RectTransform fill = Rect(root, "Fill");
        Stretch(fill, 2f, 2f, 2f, 2f);
        Image image = ImageGraphic(fill, primary ? Rust : SelectedPlate, true);
        RectTransform topEdge = Rect(fill, "TopEdge");
        AcrossTop(topEdge, 0f, 0f, 2f);
        ImageGraphic(topEdge, primary ? RustLight : MutedSteel);
        Button button = root.gameObject.AddComponent<Button>();
        ConfigureButton(button, image);
        label = Label(root, "Label", text, 23f, FontStyles.Bold, TextAlignmentOptions.Center);
        Stretch(label.rectTransform, 18f, 18f, 6f, 6f);
        return button;
    }

    private static void ConfigureButton(Button button, Graphic graphic)
    {
        button.targetGraphic = graphic;
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.selectedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        colors.disabledColor = new Color(0.45f, 0.45f, 0.45f, 0.8f);
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        Navigation navigation = button.navigation;
        navigation.mode = Navigation.Mode.Automatic;
        button.navigation = navigation;
    }

    private static TMP_Text Label(Transform parent, string name, string text, float size,
        FontStyles style = FontStyles.Normal, TextAlignmentOptions alignment = TextAlignmentOptions.Left, Color? color = null)
    {
        RectTransform rect = Rect(parent, name);
        var label = rect.gameObject.AddComponent<TextMeshProUGUI>();
        label.font = s_font;
        label.text = text;
        label.fontSize = size;
        label.fontStyle = style;
        label.alignment = alignment;
        label.color = color ?? Bone;
        label.raycastTarget = false;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }

    private static Image ImageGraphic(RectTransform rect, Color color, bool raycast = false)
    {
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycast;
        return image;
    }

    private static RectTransform Rect(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        return rect;
    }

    private static void ConfigureRow(HorizontalLayoutGroup row, float spacing)
    {
        row.spacing = spacing;
        row.childAlignment = TextAnchor.MiddleCenter;
        row.childControlWidth = true;
        row.childControlHeight = true;
        row.childForceExpandWidth = true;
        row.childForceExpandHeight = true;
    }

    private static void AddFrameEdges(RectTransform parent, Color color)
    {
        RectTransform top = Rect(parent, "Top");
        AcrossTop(top, 0f, 0f, 2f);
        ImageGraphic(top, color);
        RectTransform bottom = Rect(parent, "Bottom");
        AcrossBottom(bottom, 0f, 0f, 2f);
        ImageGraphic(bottom, color);
        RectTransform left = Rect(parent, "Left");
        left.anchorMin = Vector2.zero;
        left.anchorMax = new Vector2(0f, 1f);
        left.offsetMin = Vector2.zero;
        left.offsetMax = new Vector2(2f, 0f);
        ImageGraphic(left, color);
        RectTransform right = Rect(parent, "Right");
        right.anchorMin = new Vector2(1f, 0f);
        right.anchorMax = Vector2.one;
        right.offsetMin = new Vector2(-2f, 0f);
        right.offsetMax = Vector2.zero;
        ImageGraphic(right, color);
    }

    private static void Stretch(RectTransform rect, float left = 0f, float right = 0f, float top = 0f, float bottom = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void Top(RectTransform rect, float x, float width, float top, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(x, -top);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void Bottom(RectTransform rect, float x, float width, float bottom, float height)
    {
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = Vector2.zero;
        rect.anchoredPosition = new Vector2(x, bottom);
        rect.sizeDelta = new Vector2(width, height);
    }

    private static void AcrossTop(RectTransform rect, float inset, float top, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(inset, -top - height);
        rect.offsetMax = new Vector2(-inset, -top);
    }

    private static void AcrossBottom(RectTransform rect, float inset, float bottom, float height)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.offsetMin = new Vector2(inset, bottom);
        rect.offsetMax = new Vector2(-inset, bottom + height);
    }

    private static void Rule(Transform parent, string name, float x, float width, float top)
    {
        RectTransform rect = Rect(parent, name);
        Top(rect, x, width, top, 2f);
        ImageGraphic(rect, new Color(MutedSteel.r, MutedSteel.g, MutedSteel.b, 0.8f));
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        int separator = path.LastIndexOf('/');
        string parent = path.Substring(0, separator);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(separator + 1));
    }
}
#endif
