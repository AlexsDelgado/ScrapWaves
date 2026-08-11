using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Ventana estilo tienda para el menú principal: lista de logros con progreso y grid de
/// ítems desbloqueables (comprables con Scrap y/o gateados por un logro). Construida con
/// ScrollRect + LayoutGroup para que nunca se desborde de la pantalla sin importar cuánto
/// contenido se agregue — la presentación final de arte queda pendiente de un pase de UI/UX
/// aparte, esto es solo la estructura funcional.
/// </summary>
[DisallowMultipleComponent]
public class ObjectivesMenuUI : MonoBehaviour
{
    [SerializeField] private UnlockCatalog _catalog;
    [SerializeField, Min(160)] private float _cardWidth = 240f;
    [SerializeField, Min(140)] private float _cardHeight = 190f;

    private Canvas _canvas;
    private TextMeshProUGUI _scrapText;
    private RectTransform _achievementsContent;
    private RectTransform _shopContent;
    private bool _isVisible;

    public bool IsVisible => _isVisible;

    public void Show()
    {
        _isVisible = true;
        EnsureCatalog();
        EnsureUi();
        Refresh();
        _canvas.gameObject.SetActive(true);
    }

    public void Hide()
    {
        _isVisible = false;
        if (_canvas != null)
            _canvas.gameObject.SetActive(false);
    }

    private void EnsureCatalog()
    {
        if (_catalog != null)
            return;

#if UNITY_EDITOR
        _catalog = UnityEditor.AssetDatabase.LoadAssetAtPath<UnlockCatalog>(
            "Assets/ScriptableObjects/Meta/UnlockCatalog.asset");
#endif
    }

    private void OnEnable()
    {
        if (SaveManager.Instance != null)
        {
            SaveManager.Instance.OnScrapChanged += Refresh;
            SaveManager.Instance.OnUnlocksChanged += Refresh;
            SaveManager.Instance.OnAchievementUnlocked += HandleAchievementUnlocked;
        }
    }

    private void OnDisable()
    {
        if (SaveManager.Instance != null)
        {
            SaveManager.Instance.OnScrapChanged -= Refresh;
            SaveManager.Instance.OnUnlocksChanged -= Refresh;
            SaveManager.Instance.OnAchievementUnlocked -= HandleAchievementUnlocked;
        }
    }

    private void HandleAchievementUnlocked(AchievementDefinition achievement) => Refresh();

    private void Refresh()
    {
        if (_canvas == null)
            return;

        if (_scrapText != null)
            _scrapText.text = $"Scrap: {(SaveManager.Instance != null ? SaveManager.Instance.Scrap : 0)}";

        RefreshAchievements();
        RefreshShop();
    }

    // ---------------------------------------------------------------- Logros

    private void RefreshAchievements()
    {
        ClearChildren(_achievementsContent);
        if (SaveManager.Instance == null)
            return;

        IReadOnlyList<AchievementDefinition> achievements = SaveManager.Instance.AchievementCatalog;
        for (int i = 0; i < achievements.Count; i++)
        {
            AchievementDefinition achievement = achievements[i];
            if (achievement != null)
                CreateAchievementRow(achievement);
        }
    }

    private void CreateAchievementRow(AchievementDefinition achievement)
    {
        bool unlocked = SaveManager.Instance.IsAchievementUnlocked(achievement);
        float progress = SaveManager.Instance.GetProgress(achievement);
        string status = unlocked
            ? "Completado"
            : $"{Mathf.Min(progress, achievement.TargetValue):0.#} / {achievement.TargetValue:0.#}";

        var row = new GameObject($"Achievement_{achievement.AchievementId}", typeof(RectTransform));
        row.transform.SetParent(_achievementsContent, false);

        var rowBg = row.AddComponent<Image>();
        rowBg.sprite = HudUiFactory.WhiteSprite;
        rowBg.color = unlocked ? new Color(0.18f, 0.32f, 0.2f, 1f) : new Color(0.14f, 0.14f, 0.16f, 1f);

        var rowLayout = row.AddComponent<VerticalLayoutGroup>();
        rowLayout.padding = new RectOffset(14, 14, 10, 10);
        rowLayout.spacing = 4f;
        rowLayout.childControlWidth = true;
        rowLayout.childControlHeight = true;
        rowLayout.childForceExpandWidth = true;
        rowLayout.childForceExpandHeight = false;

        var rowFitter = row.AddComponent<ContentSizeFitter>();
        rowFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        TextMeshProUGUI title = CreateFlowLabel(row.transform, "Title", $"{achievement.DisplayName} — {status}", 18f, FontStyles.Bold, Color.white, 26f);

        if (!string.IsNullOrEmpty(achievement.Description))
            CreateFlowLabel(row.transform, "Description", achievement.Description, 14f, FontStyles.Normal, HudUiFactory.MutedTextColor, -1f);
    }

    // ---------------------------------------------------------------- Tienda

    private void RefreshShop()
    {
        ClearChildren(_shopContent);
        if (_catalog == null)
            return;

        for (int i = 0; i < _catalog.Weapons.Count; i++)
        {
            WeaponData weapon = _catalog.Weapons[i];
            if (weapon != null)
                CreateShopCard(weapon, weapon.DisplayName);
        }

        for (int i = 0; i < _catalog.PassiveItems.Count; i++)
        {
            PassiveItemData item = _catalog.PassiveItems[i];
            if (item != null)
                CreateShopCard(item, item.DisplayName);
        }
    }

    private void CreateShopCard(IUnlockable item, string displayName)
    {
        if (item == null || SaveManager.Instance == null)
            return;

        bool unlocked = SaveManager.Instance.IsUnlocked(item);
        UnlockRequirement requirement = item.Requirement;

        // El tamaño real de la card lo fija el GridLayoutGroup del contenedor (cellSize);
        // no hace falta imponerlo acá.
        var card = new GameObject($"ShopCard_{item.UnlockId}", typeof(RectTransform));
        card.transform.SetParent(_shopContent, false);

        var cardBg = card.AddComponent<Image>();
        cardBg.sprite = HudUiFactory.WhiteSprite;
        cardBg.color = unlocked ? new Color(0.18f, 0.32f, 0.2f, 1f) : new Color(0.16f, 0.17f, 0.2f, 1f);

        var cardLayout = card.AddComponent<VerticalLayoutGroup>();
        cardLayout.padding = new RectOffset(10, 10, 10, 10);
        cardLayout.spacing = 6f;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = true;
        cardLayout.childForceExpandWidth = true;
        cardLayout.childForceExpandHeight = false;

        CreateFlowLabel(card.transform, "Title", displayName ?? item.UnlockId, 16f, FontStyles.Bold, Color.white, 40f);

        var statusGo = new GameObject("Status", typeof(RectTransform));
        statusGo.transform.SetParent(card.transform, false);
        var statusLabel = statusGo.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(statusLabel);
        statusLabel.fontSize = 13f;
        statusLabel.color = HudUiFactory.MutedTextColor;
        statusLabel.alignment = TextAlignmentOptions.Top;
        statusLabel.enableWordWrapping = true;
        statusLabel.text = BuildStatusText(unlocked, requirement);
        statusGo.AddComponent<LayoutElement>().flexibleHeight = 1f;

        Button buyBtn = HudUiFactory.CreateButton(card.transform, unlocked ? "Desbloqueado" : "Comprar", new Vector2(_cardWidth - 20f, 34f));
        buyBtn.gameObject.AddComponent<LayoutElement>().preferredHeight = 34f;
        bool canBuyNow = !unlocked && CanAfford(requirement);
        buyBtn.interactable = canBuyNow;
        buyBtn.onClick.AddListener(() =>
        {
            if (SaveManager.Instance != null && SaveManager.Instance.TryPurchase(item))
                Refresh();
        });
    }

    private static bool CanAfford(UnlockRequirement requirement)
    {
        if (requirement == null)
            return false;
        if (requirement.RequiredAchievement != null && !SaveManager.Instance.IsAchievementUnlocked(requirement.RequiredAchievement))
            return false;
        return SaveManager.Instance.Scrap >= requirement.ScrapPrice;
    }

    private static string BuildStatusText(bool unlocked, UnlockRequirement requirement)
    {
        if (unlocked)
            return "✓ Desbloqueado";
        if (requirement == null)
            return "No disponible todavía";

        var parts = new List<string>();
        if (requirement.RequiredAchievement != null)
        {
            bool achievementDone = SaveManager.Instance != null && SaveManager.Instance.IsAchievementUnlocked(requirement.RequiredAchievement);
            parts.Add(achievementDone ? $"✓ {requirement.RequiredAchievement.DisplayName}" : $"Requiere: {requirement.RequiredAchievement.DisplayName}");
        }
        if (requirement.ScrapPrice > 0)
            parts.Add($"{requirement.ScrapPrice} Scrap");

        return parts.Count > 0 ? string.Join("\n", parts) : "No disponible todavía";
    }

    // ---------------------------------------------------------------- Helpers de layout

    private static TextMeshProUGUI CreateFlowLabel(Transform parent, string name, string text, float fontSize, FontStyles style, Color color, float fixedHeight)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var label = go.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(label);
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.color = color;
        label.alignment = TextAlignmentOptions.TopLeft;
        label.enableWordWrapping = true;
        label.text = text;

        var layoutElement = go.AddComponent<LayoutElement>();
        if (fixedHeight > 0f)
            layoutElement.preferredHeight = fixedHeight;
        else
            layoutElement.flexibleHeight = 1f;

        return label;
    }

    private static void ClearChildren(Transform parent)
    {
        if (parent == null)
            return;

        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }

    private static (RectTransform section, RectTransform content) CreateScrollSection(Transform parent, string name, float flexibleHeight, bool grid, float cardWidth, float cardHeight)
    {
        var sectionGo = new GameObject(name, typeof(RectTransform));
        sectionGo.transform.SetParent(parent, false);
        var sectionLayoutElement = sectionGo.AddComponent<LayoutElement>();
        sectionLayoutElement.flexibleHeight = flexibleHeight;
        sectionLayoutElement.flexibleWidth = 1f;

        var sectionBg = sectionGo.AddComponent<Image>();
        sectionBg.sprite = HudUiFactory.WhiteSprite;
        sectionBg.color = new Color(1f, 1f, 1f, 0.04f);

        var viewportGo = new GameObject("Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(sectionGo.transform, false);
        var viewportRt = viewportGo.GetComponent<RectTransform>();
        viewportRt.anchorMin = Vector2.zero;
        viewportRt.anchorMax = Vector2.one;
        viewportRt.offsetMin = Vector2.zero;
        viewportRt.offsetMax = Vector2.zero;
        viewportGo.AddComponent<RectMask2D>();

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewportGo.transform, false);
        var contentRt = contentGo.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = Vector2.zero;

        if (grid)
        {
            var gridLayout = contentGo.AddComponent<GridLayoutGroup>();
            gridLayout.padding = new RectOffset(12, 12, 12, 12);
            gridLayout.spacing = new Vector2(14f, 14f);
            gridLayout.cellSize = new Vector2(cardWidth, cardHeight);
            gridLayout.constraint = GridLayoutGroup.Constraint.Flexible;
            gridLayout.childAlignment = TextAnchor.UpperLeft;
        }
        else
        {
            var vLayout = contentGo.AddComponent<VerticalLayoutGroup>();
            vLayout.padding = new RectOffset(12, 12, 12, 12);
            vLayout.spacing = 8f;
            vLayout.childControlWidth = true;
            vLayout.childControlHeight = false;
            vLayout.childForceExpandWidth = true;
            vLayout.childForceExpandHeight = false;
        }

        var fitter = contentGo.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scrollRect = sectionGo.AddComponent<ScrollRect>();
        scrollRect.viewport = viewportRt;
        scrollRect.content = contentRt;
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;
        scrollRect.scrollSensitivity = 24f;

        return (sectionGo.GetComponent<RectTransform>(), contentRt);
    }

    private static TextMeshProUGUI CreateSectionHeader(Transform parent, string name, string text)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var label = go.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(label);
        label.fontSize = 22f;
        label.fontStyle = FontStyles.Bold;
        label.color = Color.white;
        label.alignment = TextAlignmentOptions.BottomLeft;
        label.text = text;
        go.AddComponent<LayoutElement>().preferredHeight = 32f;
        return label;
    }

    // ---------------------------------------------------------------- Construcción de la UI

    private void EnsureUi()
    {
        if (_canvas != null)
            return;

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        var canvasGo = new GameObject("ObjectivesCanvas", typeof(RectTransform));
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 5100;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // Backdrop: opaco, cubre toda la pantalla (no debe verse el fondo del juego).
        var backdrop = new GameObject("Backdrop", typeof(RectTransform));
        backdrop.transform.SetParent(canvasGo.transform, false);
        Stretch(backdrop.GetComponent<RectTransform>());
        var backdropImg = backdrop.AddComponent<Image>();
        backdropImg.sprite = HudUiFactory.WhiteSprite;
        backdropImg.color = new Color(0.02f, 0.02f, 0.025f, 1f);

        // Window: panel centrado de tamaño fijo, todo el contenido vive adentro.
        var window = new GameObject("Window", typeof(RectTransform));
        window.transform.SetParent(canvasGo.transform, false);
        var windowRt = window.GetComponent<RectTransform>();
        windowRt.anchorMin = new Vector2(0.5f, 0.5f);
        windowRt.anchorMax = new Vector2(0.5f, 0.5f);
        windowRt.pivot = new Vector2(0.5f, 0.5f);
        windowRt.sizeDelta = new Vector2(1500f, 880f);
        var windowImg = window.AddComponent<Image>();
        windowImg.sprite = HudUiFactory.WhiteSprite;
        windowImg.color = new Color(0.08f, 0.085f, 0.095f, 1f);

        var windowLayout = window.AddComponent<VerticalLayoutGroup>();
        windowLayout.padding = new RectOffset(32, 32, 24, 24);
        windowLayout.spacing = 10f;
        windowLayout.childControlWidth = true;
        windowLayout.childControlHeight = true;
        windowLayout.childForceExpandWidth = true;
        windowLayout.childForceExpandHeight = false;

        // Barra superior: título + contador de Scrap.
        var topBar = new GameObject("TopBar", typeof(RectTransform));
        topBar.transform.SetParent(window.transform, false);
        topBar.AddComponent<LayoutElement>().preferredHeight = 44f;
        var topBarLayout = topBar.AddComponent<HorizontalLayoutGroup>();
        topBarLayout.childControlWidth = true;
        topBarLayout.childControlHeight = true;
        topBarLayout.childForceExpandWidth = false;
        topBarLayout.childForceExpandHeight = true;

        var title = HudUiFactory.CreateLabel(topBar.transform, "Title", "Objetivos", 30f, TextAlignmentOptions.Left);
        title.fontStyle = FontStyles.Bold;
        title.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        _scrapText = HudUiFactory.CreateLabel(topBar.transform, "ScrapCounter", "Scrap: 0", 22f, TextAlignmentOptions.Right);
        _scrapText.color = new Color(1f, 0.85f, 0.4f);
        _scrapText.gameObject.AddComponent<LayoutElement>().preferredWidth = 260f;

        // Logros (scrolleable).
        CreateSectionHeader(window.transform, "AchievementsHeader", "Logros");
        (_, RectTransform achievementsContent) = CreateScrollSection(
            window.transform, "AchievementsScroll", flexibleHeight: 1f, grid: false, cardWidth: 0f, cardHeight: 0f);
        _achievementsContent = achievementsContent;

        // Tienda (scrolleable, grid con wrap automático).
        CreateSectionHeader(window.transform, "ShopHeader", "Tienda");
        (_, RectTransform shopContent) = CreateScrollSection(
            window.transform, "ShopScroll", flexibleHeight: 1.6f, grid: true, cardWidth: _cardWidth, cardHeight: _cardHeight);
        _shopContent = shopContent;

        // Cerrar.
        var bottomBar = new GameObject("BottomBar", typeof(RectTransform));
        bottomBar.transform.SetParent(window.transform, false);
        bottomBar.AddComponent<LayoutElement>().preferredHeight = 56f;
        var bottomLayout = bottomBar.AddComponent<HorizontalLayoutGroup>();
        bottomLayout.childAlignment = TextAnchor.MiddleCenter;
        bottomLayout.childControlWidth = false;
        bottomLayout.childControlHeight = false;

        Button closeBtn = HudUiFactory.CreateButton(bottomBar.transform, "Cerrar", new Vector2(220f, 48f));
        closeBtn.onClick.AddListener(Hide);

        _canvas.gameObject.SetActive(false);
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
