using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Pestaña Upgrades: meta stats (0–10) e ítems (0–3) con el mismo patrón visual que Unlocks.
/// </summary>
[DisallowMultipleComponent]
public class MetaUpgradeShopUI : MonoBehaviour
{
    private enum OfferKind
    {
        Stat,
        Item
    }

    private sealed class UpgradeOffer
    {
        public string Id;
        public OfferKind Kind;
        public StatType StatType;
        public string PassiveUnlockId;
        public string DisplayName;
        public string Category;
        public string Description;
        public int Level;
        public int MaxLevel;
        public int NextCost;
        public UnlockCardState State;
    }

    private static readonly StatType[] StatRows =
    {
        StatType.DamageMultiplier,
        StatType.AttackSpeedMultiplier,
        StatType.ProjectileAreaSize,
        StatType.CriticalChance,
        StatType.CriticalDamage,
        StatType.MaxHealth,
        StatType.HealthRegeneration,
        StatType.PickupRange
    };

    [Header("Data")]
    [SerializeField] private MetaStatUpgradeCosts _costs;
    [SerializeField] private UnlockCatalog _catalog;

    [Header("Grid (Unlocks-style)")]
    [SerializeField] private ScrollRect _scrollRect;
    [SerializeField] private RectTransform _content;
    [SerializeField] private UnlockCardView _cardPrefab;
    [SerializeField] private GameObject _emptyState;
    [SerializeField] private GameObject _dataUnavailableState;

    [Header("Detail")]
    [SerializeField] private GameObject _detailRoot;
    [SerializeField] private GameObject _detailEmptyState;
    [SerializeField] private TextMeshProUGUI _nameText;
    [SerializeField] private TextMeshProUGUI _typeText;
    [SerializeField] private TextMeshProUGUI _statusText;
    [SerializeField] private TextMeshProUGUI _requirementText;
    [SerializeField] private Button _purchaseButton;
    [SerializeField] private TextMeshProUGUI _purchaseButtonLabel;
    [SerializeField] private TextMeshProUGUI _feedbackText;
    [SerializeField] private TextMeshProUGUI _scrapText;

    private readonly List<UnlockCardView> _cards = new();
    private readonly List<UpgradeOffer> _offers = new();
    private SaveManager _subscribed;
    private UnlockCardView _selectedCard;
    private string _selectedOfferId;
    private bool _layoutBuilt;
    private bool _armed;
    private bool _handlingPurchase;
    private bool _hideInsufficientScrap;
    private bool _hideStat;
    private bool _hideItem;

    public void FocusFirst()
    {
        if (_selectedCard != null)
        {
            _selectedCard.Focus();
            return;
        }

        if (_cards.Count > 0)
        {
            SelectCard(_cards[0]);
            _cards[0].Focus();
        }
        else if (_purchaseButton != null)
            Focus(_purchaseButton.gameObject);
    }

    private void OnEnable()
    {
        EnsureCosts();
        EnsureLayout();
        EnsureFilterBar();
        WirePurchase();
        Subscribe();
        Rebuild();
    }

    private void OnDisable()
    {
        Unsubscribe();
        UnwirePurchase();
    }

    private void WirePurchase()
    {
        if (_purchaseButton == null)
            return;
        _purchaseButton.onClick.RemoveListener(HandlePurchaseRequested);
        _purchaseButton.onClick.AddListener(HandlePurchaseRequested);
    }

    private void UnwirePurchase()
    {
        if (_purchaseButton != null)
            _purchaseButton.onClick.RemoveListener(HandlePurchaseRequested);
    }

    private void EnsureCosts()
    {
        if (_costs == null)
            _costs = Resources.Load<MetaStatUpgradeCosts>("Meta/MetaStatUpgradeCosts");
        if (_costs == null)
            _costs = ScriptableObject.CreateInstance<MetaStatUpgradeCosts>();
        if (_catalog == null)
            _catalog = Resources.Load<UnlockCatalog>("Meta/UnlockCatalog");
        if (_cardPrefab == null)
            _cardPrefab = Resources.Load<UnlockCardView>("UI/UnlockCard");
    }

    private void EnsureLayout()
    {
        if (_layoutBuilt)
            return;

        RectTransform root = transform as RectTransform;
        if (root == null)
            root = gameObject.AddComponent<RectTransform>();

        if (_content == null || _scrollRect == null || _detailRoot == null)
            BuildDefaultShell(root);

        _layoutBuilt = _content != null && _cardPrefab != null;
    }

    private void EnsureFilterBar()
    {
        Transform filterParent = _scrollRect != null && _scrollRect.transform.parent != null
            ? _scrollRect.transform.parent
            : transform;

        ObjectivesFilterChipBar bar = ObjectivesFilterChipBar.Ensure(filterParent, "UpgradesFilterBar");
        bar.AddOrBindChip(
            "HideInsufficientScrap",
            "Hide no scrap",
            () => _hideInsufficientScrap,
            value =>
            {
                _hideInsufficientScrap = value;
                Rebuild();
            });
        bar.AddOrBindChip(
            "HideStat",
            "Hide stat",
            () => _hideStat,
            value =>
            {
                _hideStat = value;
                Rebuild();
            });
        bar.AddOrBindChip(
            "HideItem",
            "Hide item",
            () => _hideItem,
            value =>
            {
                _hideItem = value;
                Rebuild();
            });

        if (_scrollRect != null)
            ObjectivesFilterChipBar.SetTopInset(
                _scrollRect.transform as RectTransform,
                ObjectivesFilterChipBar.Height);
    }

    private void BuildDefaultShell(RectTransform root)
    {
        Image panel = GetComponent<Image>();
        if (panel == null)
        {
            panel = gameObject.AddComponent<Image>();
            panel.color = new Color(0.066f, 0.078f, 0.074f, 0.01f);
            panel.raycastTarget = false;
        }

        // Left grid panel
        RectTransform gridPanel = CreatePanel(root, "UpgradeGridPanel",
            new Vector2(0f, 0f), new Vector2(0.69f, 1f),
            new Color(0.122f, 0.145f, 0.133f, 0.88f));
        gridPanel.offsetMin = new Vector2(0f, 0f);
        gridPanel.offsetMax = new Vector2(-6f, 0f);

        GameObject scrollGo = new("Scroll", typeof(RectTransform), typeof(ScrollRect));
        scrollGo.transform.SetParent(gridPanel, false);
        RectTransform scrollRt = scrollGo.GetComponent<RectTransform>();
        Stretch(scrollRt, Vector2.zero, Vector2.one, new Vector2(8f, 8f), new Vector2(-8f, -8f));

        GameObject viewportGo = new("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D));
        viewportGo.transform.SetParent(scrollGo.transform, false);
        RectTransform viewportRt = viewportGo.GetComponent<RectTransform>();
        Stretch(viewportRt, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
        viewportGo.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.01f);

        GameObject contentGo = new("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
        contentGo.transform.SetParent(viewportGo.transform, false);
        RectTransform contentRt = contentGo.GetComponent<RectTransform>();
        contentRt.anchorMin = new Vector2(0f, 1f);
        contentRt.anchorMax = new Vector2(1f, 1f);
        contentRt.pivot = new Vector2(0.5f, 1f);
        contentRt.anchoredPosition = Vector2.zero;
        contentRt.sizeDelta = new Vector2(0f, 0f);

        GridLayoutGroup grid = contentGo.GetComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(300f, 190f);
        grid.spacing = new Vector2(12f, 12f);
        grid.padding = new RectOffset(8, 8, 8, 8);
        grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        grid.constraintCount = 3;
        grid.childAlignment = TextAnchor.UpperLeft;

        ContentSizeFitter fitter = contentGo.GetComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        ScrollRect scroll = scrollGo.GetComponent<ScrollRect>();
        scroll.viewport = viewportRt;
        scroll.content = contentRt;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        _scrollRect = scroll;
        _content = contentRt;

        _emptyState = CreateLabelObject(gridPanel, "EmptyState", "NO UPGRADES AVAILABLE",
            new Vector2(0.1f, 0.4f), new Vector2(0.9f, 0.6f));
        _emptyState.SetActive(false);
        _dataUnavailableState = CreateLabelObject(gridPanel, "DataUnavailable", "UPGRADE DATA UNAVAILABLE",
            new Vector2(0.1f, 0.4f), new Vector2(0.9f, 0.6f));
        _dataUnavailableState.SetActive(false);

        // Right detail panel
        RectTransform detailPanel = CreatePanel(root, "UpgradeDetailPanel",
            new Vector2(0.71f, 0f), new Vector2(1f, 1f),
            new Color(0.035f, 0.043f, 0.039f, 0.9f));
        detailPanel.offsetMin = new Vector2(6f, 0f);
        detailPanel.offsetMax = Vector2.zero;
        _detailRoot = detailPanel.gameObject;

        _nameText = CreateDetailText(detailPanel, "Name", 28f, FontStyles.Bold,
            new Vector2(0.06f, 0.78f), new Vector2(0.94f, 0.94f));
        _typeText = CreateDetailText(detailPanel, "Type", 16f, FontStyles.Normal,
            new Vector2(0.06f, 0.68f), new Vector2(0.94f, 0.78f));
        _statusText = CreateDetailText(detailPanel, "Status", 16f, FontStyles.Bold,
            new Vector2(0.06f, 0.58f), new Vector2(0.94f, 0.68f));
        _requirementText = CreateDetailText(detailPanel, "Requirement", 15f, FontStyles.Normal,
            new Vector2(0.06f, 0.36f), new Vector2(0.94f, 0.58f));
        _requirementText.color = new Color(0.85f, 0.42f, 0.2f, 1f);

        GameObject buyGo = new("PurchaseButton", typeof(RectTransform), typeof(Image), typeof(Button));
        buyGo.transform.SetParent(detailPanel, false);
        RectTransform buyRt = buyGo.GetComponent<RectTransform>();
        Stretch(buyRt, new Vector2(0.08f, 0.14f), new Vector2(0.92f, 0.28f), Vector2.zero, Vector2.zero);
        Image buyImg = buyGo.GetComponent<Image>();
        buyImg.color = new Color(0.122f, 0.145f, 0.133f, 1f);
        _purchaseButton = buyGo.GetComponent<Button>();
        _purchaseButton.targetGraphic = buyImg;
        ColorBlock colors = _purchaseButton.colors;
        colors.highlightedColor = new Color(0.659f, 0.780f, 0.561f, 1f);
        colors.pressedColor = new Color(0.851f, 0.416f, 0.196f, 1f);
        colors.selectedColor = new Color(0.659f, 0.780f, 0.561f, 1f);
        _purchaseButton.colors = colors;

        _purchaseButtonLabel = CreateDetailText(buyRt, "Label", 18f, FontStyles.Bold,
            Vector2.zero, Vector2.one);
        _purchaseButtonLabel.alignment = TextAlignmentOptions.Center;
        _purchaseButtonLabel.text = "UPGRADE";

        _feedbackText = CreateDetailText(detailPanel, "Feedback", 14f, FontStyles.Normal,
            new Vector2(0.06f, 0.04f), new Vector2(0.94f, 0.12f));
        _feedbackText.color = new Color(0.75f, 0.85f, 0.78f, 1f);

        _detailEmptyState = CreateLabelObject(detailPanel, "DetailEmpty", "SELECT AN UPGRADE",
            new Vector2(0.1f, 0.4f), new Vector2(0.9f, 0.6f));
    }

    private static RectTransform CreatePanel(RectTransform parent, string name, Vector2 min, Vector2 max, Color color)
    {
        GameObject go = new(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        Stretch(rt, min, max, Vector2.zero, Vector2.zero);
        Image img = go.GetComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return rt;
    }

    private static GameObject CreateLabelObject(Transform parent, string name, string text, Vector2 min, Vector2 max)
    {
        GameObject go = new(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        Stretch(rt, min, max, Vector2.zero, Vector2.zero);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = 18f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.95f, 0.96f, 0.92f, 1f);
        tmp.raycastTarget = false;
        return go;
    }

    private static TextMeshProUGUI CreateDetailText(
        Transform parent,
        string name,
        float size,
        FontStyles style,
        Vector2 min,
        Vector2 max)
    {
        GameObject go = new(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        RectTransform rt = go.GetComponent<RectTransform>();
        Stretch(rt, min, max, Vector2.zero, Vector2.zero);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.color = new Color(0.95f, 0.96f, 0.92f, 1f);
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.raycastTarget = false;
        tmp.text = string.Empty;
        return tmp;
    }

    private static void Stretch(RectTransform rt, Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    private void Subscribe()
    {
        if (SaveManager.Instance == null || _subscribed == SaveManager.Instance)
            return;
        Unsubscribe();
        _subscribed = SaveManager.Instance;
        _subscribed.OnScrapChanged += Rebuild;
        _subscribed.OnUnlocksChanged += Rebuild;
    }

    private void Unsubscribe()
    {
        if (_subscribed == null)
            return;
        _subscribed.OnScrapChanged -= Rebuild;
        _subscribed.OnUnlocksChanged -= Rebuild;
        _subscribed = null;
    }

    public void Rebuild()
    {
        EnsureCosts();
        EnsureLayout();
        ClearCards();
        UpdateScrap();
        _offers.Clear();
        _armed = false;

        bool dataOk = SaveManager.Instance != null && _costs != null && _cardPrefab != null && _content != null;
        SetActive(_dataUnavailableState, !dataOk);
        if (!dataOk)
        {
            SetActive(_emptyState, false);
            ClearDetails();
            return;
        }

        BuildOffers();
        SetActive(_emptyState, _offers.Count == 0);
        if (_offers.Count == 0)
        {
            ClearDetails();
            return;
        }

        string restoreId = _selectedOfferId;
        for (int i = 0; i < _offers.Count; i++)
        {
            UpgradeOffer offer = _offers[i];
            UnlockCardView card = Instantiate(_cardPrefab, _content, false);
            card.gameObject.SetActive(true);
            string price = offer.State == UnlockCardState.Owned
                ? "MAX LEVEL"
                : $"{offer.NextCost} SCRAP";
            string status = offer.State == UnlockCardState.Owned
                ? $"LEVEL {offer.Level}/{offer.MaxLevel}"
                : offer.State == UnlockCardState.Purchasable
                    ? $"LEVEL {offer.Level}/{offer.MaxLevel}"
                    : offer.State == UnlockCardState.InsufficientScrap
                        ? "INSUFFICIENT SCRAP"
                        : "LOCKED";
            card.BindCustom(
                offer,
                offer.DisplayName,
                offer.Category,
                price,
                offer.Description,
                status,
                offer.State,
                HandleCardFocused);
            _cards.Add(card);
        }

        UnlockCardView restore = FindCard(restoreId);
        SelectCard(restore != null ? restore : _cards[0]);
    }

    private void BuildOffers()
    {
        if (!_hideStat)
        {
            for (int i = 0; i < StatRows.Length; i++)
            {
                StatType type = StatRows[i];
                int level = SaveManager.Instance.GetMetaStatLevel(type);
                int max = 10;
                int nextCost = level >= max ? 0 : _costs.GetStatUpgradeCost(level + 1);
                UnlockCardState state = ResolveState(level, max, nextCost);
                if (_hideInsufficientScrap && state == UnlockCardState.InsufficientScrap)
                    continue;

                _offers.Add(new UpgradeOffer
                {
                    Id = $"stat:{type}",
                    Kind = OfferKind.Stat,
                    StatType = type,
                    DisplayName = Pretty(type),
                    Category = "STAT",
                    Description = $"Permanent base multiplier for {Pretty(type)}. +5% per level.",
                    Level = level,
                    MaxLevel = max,
                    NextCost = nextCost,
                    State = state
                });
            }
        }

        if (_catalog == null || _hideItem)
            return;

        for (int i = 0; i < _catalog.PassiveItems.Count; i++)
        {
            PassiveItemData item = _catalog.PassiveItems[i];
            if (item == null || !IsMetaUpgradeable(item))
                continue;
            if (!SaveManager.Instance.IsUnlocked(item))
                continue;

            int level = SaveManager.Instance.GetMetaItemUpgradeLevel(item.UnlockId);
            int max = 3;
            int nextCost = level >= max ? 0 : _costs.GetItemUpgradeCost(level + 1);
            UnlockCardState state = ResolveState(level, max, nextCost);
            if (_hideInsufficientScrap && state == UnlockCardState.InsufficientScrap)
                continue;

            _offers.Add(new UpgradeOffer
            {
                Id = $"item:{item.UnlockId}",
                Kind = OfferKind.Item,
                PassiveUnlockId = item.UnlockId,
                DisplayName = item.DisplayName,
                Category = "ITEM",
                Description = $"Increases this passive's power by ×1.1 per meta level (max {max}).",
                Level = level,
                MaxLevel = max,
                NextCost = nextCost,
                State = state
            });
        }
    }

    private UnlockCardState ResolveState(int level, int max, int nextCost)
    {
        if (level >= max)
            return UnlockCardState.Owned;
        if (SaveManager.Instance.Scrap < nextCost)
            return UnlockCardState.InsufficientScrap;
        return UnlockCardState.Purchasable;
    }

    private void HandleCardFocused(UnlockCardView card)
    {
        SelectCard(card);
    }

    private void SelectCard(UnlockCardView card)
    {
        if (_selectedCard != null)
            _selectedCard.SetSelected(false);

        _selectedCard = card;
        _armed = false;
        if (card == null)
        {
            _selectedOfferId = null;
            ClearDetails();
            return;
        }

        card.SetSelected(true);
        UpgradeOffer offer = card.CustomPayload as UpgradeOffer;
        _selectedOfferId = offer?.Id;
        UpdateDetails(offer);
    }

    private void UpdateDetails(UpgradeOffer offer)
    {
        if (offer == null)
        {
            ClearDetails();
            return;
        }

        SetActive(_detailRoot, true);
        SetActive(_detailEmptyState, false);
        SetText(_nameText, offer.DisplayName);
        SetText(_typeText, offer.Category);
        SetText(_statusText, $"LEVEL {offer.Level}/{offer.MaxLevel}");
        SetText(_requirementText, offer.Description);
        SetFeedback(string.Empty);

        if (_purchaseButton == null)
            return;

        if (offer.State == UnlockCardState.Owned)
        {
            _purchaseButton.interactable = false;
            SetText(_purchaseButtonLabel, "MAXED");
        }
        else if (offer.State == UnlockCardState.InsufficientScrap)
        {
            _purchaseButton.interactable = false;
            SetText(_purchaseButtonLabel, $"{offer.NextCost} SCRAP");
        }
        else
        {
            _purchaseButton.interactable = true;
            SetText(_purchaseButtonLabel, _armed ? "CONFIRM UPGRADE" : $"UPGRADE — {offer.NextCost} SCRAP");
        }
    }

    private void ClearDetails()
    {
        SetActive(_detailEmptyState, true);
        SetText(_nameText, string.Empty);
        SetText(_typeText, string.Empty);
        SetText(_statusText, string.Empty);
        SetText(_requirementText, string.Empty);
        SetText(_purchaseButtonLabel, "UNAVAILABLE");
        SetFeedback(string.Empty);
        if (_purchaseButton != null)
            _purchaseButton.interactable = false;
    }

    private void HandlePurchaseRequested()
    {
        if (_handlingPurchase || _selectedCard == null)
            return;

        UpgradeOffer offer = _selectedCard.CustomPayload as UpgradeOffer;
        if (offer == null || offer.State != UnlockCardState.Purchasable)
            return;

        if (!_armed)
        {
            _armed = true;
            UpdateDetails(offer);
            SetFeedback("Press again to confirm.");
            return;
        }

        _handlingPurchase = true;
        bool ok = offer.Kind == OfferKind.Stat
            ? SaveManager.Instance.TryPurchaseMetaStatUpgrade(offer.StatType, _costs)
            : SaveManager.Instance.TryPurchaseMetaItemUpgrade(offer.PassiveUnlockId, _costs);

        if (ok)
        {
            SetFeedback("Upgrade purchased.");
            if (offer.Kind == OfferKind.Stat)
                FindAnyObjectByType<MetaProgressionApplier>()?.Apply();
            Rebuild();
        }
        else
        {
            _armed = false;
            SetFeedback("Not enough scrap.");
            UpdateDetails(offer);
        }

        _handlingPurchase = false;
    }

    private UnlockCardView FindCard(string offerId)
    {
        if (string.IsNullOrEmpty(offerId))
            return null;
        for (int i = 0; i < _cards.Count; i++)
        {
            if (_cards[i].CustomPayload is UpgradeOffer offer && offer.Id == offerId)
                return _cards[i];
        }

        return null;
    }

    private void ClearCards()
    {
        for (int i = 0; i < _cards.Count; i++)
        {
            if (_cards[i] == null)
                continue;
            _cards[i].Unbind();
            Destroy(_cards[i].gameObject);
        }

        _cards.Clear();
        _selectedCard = null;
    }

    private void UpdateScrap()
    {
        if (_scrapText == null || SaveManager.Instance == null)
            return;
        _scrapText.text = SaveManager.Instance.Scrap.ToString();
    }

    private void SetFeedback(string message) => SetText(_feedbackText, message);

    private static void SetText(TMP_Text text, string value)
    {
        if (text != null)
            text.text = value ?? string.Empty;
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }

    private static void Focus(GameObject target)
    {
        if (target != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(target);
    }

    private static string Pretty(StatType type)
    {
        return type switch
        {
            StatType.DamageMultiplier => "Damage",
            StatType.AttackSpeedMultiplier => "Attack Speed",
            StatType.ProjectileAreaSize => "Area Size",
            StatType.CriticalChance => "Crit Chance",
            StatType.CriticalDamage => "Crit Damage",
            StatType.MaxHealth => "Max Health",
            StatType.HealthRegeneration => "Regen",
            StatType.PickupRange => "Pickup Range",
            _ => type.ToString()
        };
    }

    private static bool IsMetaUpgradeable(PassiveItemData data)
    {
        if (data == null)
            return false;
        IReadOnlyList<PassiveStatBonus> bonuses = data.BonusesPerLevel;
        for (int i = 0; i < bonuses.Count; i++)
        {
            StatType t = bonuses[i].StatType;
            if (t == StatType.ShieldCharges || t == StatType.AirJumps || t == StatType.DashCharges)
                return false;
        }

        return true;
    }
}
