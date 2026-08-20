using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public enum ObjectivesMenuTab
{
    Objectives,
    Unlocks
}

/// <summary>
/// Binds progression data to the authored title-screen Objectives and Unlocks presentation.
/// Every fixed screen object is supplied by the scene. Runtime-instantiated content is limited
/// to the assigned ObjectiveRowView and UnlockCardView prefab templates.
/// </summary>
[DisallowMultipleComponent]
public class ObjectivesMenuUI : MonoBehaviour
{
    [Header("Authored shell")]
    [SerializeField] private GameObject _screenRoot;
    [SerializeField] private Button _backButton;
    [SerializeField] private TextMeshProUGUI _scrapText;
    [SerializeField, Tooltip("Optional title-screen stack. When assigned, Back requests its authored close transition after purchase cancellation.")]
    private TitleScreenScreenStack _screenStack;

    [Header("Authored tabs")]
    [SerializeField] private Button _objectivesTabButton;
    [SerializeField] private Button _unlocksTabButton;
    [SerializeField] private GameObject _objectivesTabSelectedState;
    [SerializeField] private GameObject _unlocksTabSelectedState;
    [SerializeField] private GameObject _objectivesTabRoot;
    [SerializeField] private GameObject _unlocksTabRoot;
    [SerializeField] private bool _rememberLastTabForSession = true;

    [Header("Objectives tab")]
    [SerializeField] private ScrollRect _objectivesScrollRect;
    [SerializeField] private RectTransform _objectivesContent;
    [SerializeField] private ObjectiveRowView _objectiveRowPrefab;
    [SerializeField] private GameObject _objectivesEmptyState;
    [SerializeField] private GameObject _objectivesDataUnavailableState;
    [SerializeField] private GameObject _objectiveDetailRoot;
    [SerializeField] private GameObject _objectiveDetailEmptyState;
    [SerializeField] private TextMeshProUGUI _objectiveNameText;
    [SerializeField] private TextMeshProUGUI _objectiveDescriptionText;
    [SerializeField] private TextMeshProUGUI _objectiveProgressText;
    [SerializeField] private Slider _objectiveProgressBar;
    [SerializeField] private TextMeshProUGUI _objectiveCompletionText;
    [SerializeField] private TextMeshProUGUI _objectiveRewardText;

    [Header("Unlocks tab")]
    [SerializeField] private UnlockCatalog _catalog;
    [SerializeField] private ScrollRect _unlocksScrollRect;
    [SerializeField] private RectTransform _unlocksContent;
    [SerializeField] private UnlockCardView _unlockCardPrefab;
    [SerializeField] private GameObject _unlocksEmptyState;
    [SerializeField] private GameObject _unlocksDataUnavailableState;
    [SerializeField] private GameObject _unlockDetailRoot;
    [SerializeField] private GameObject _unlockDetailEmptyState;
    [SerializeField] private TextMeshProUGUI _unlockNameText;
    [SerializeField] private TextMeshProUGUI _unlockTypeText;
    [SerializeField] private TextMeshProUGUI _unlockStatusText;
    [SerializeField] private TextMeshProUGUI _unlockRequirementText;
    [SerializeField] private Button _purchaseButton;
    [SerializeField] private TextMeshProUGUI _purchaseButtonLabel;
    [SerializeField] private TextMeshProUGUI _purchaseFeedbackText;

    private readonly List<ObjectiveRowView> _objectiveRows = new();
    private readonly List<UnlockCardView> _unlockCards = new();

    private SaveManager _subscribedSaveManager;
    private ObjectiveRowView _selectedObjectiveRow;
    private UnlockCardView _selectedUnlockCard;
    private IUnlockable _armedPurchase;
    private string _selectedAchievementId;
    private string _selectedUnlockId;
    private ObjectivesMenuTab _activeTab;
    private bool _initialized;
    private bool _authoredReferencesReported;
    private bool _hasShown;
    private bool _isVisible;
    private bool _refreshing;
    private bool _refreshPending;
    private bool _handlingPurchase;

    public bool IsVisible => _isVisible;
    public bool IsPurchaseArmed => _armedPurchase != null;
    public ObjectivesMenuTab ActiveTab => _activeTab;
    public string SelectedAchievementId => _selectedAchievementId;
    public string SelectedUnlockId => _selectedUnlockId;
    public bool HasRequiredReferences =>
        _screenRoot != null &&
        _backButton != null &&
        _scrapText != null &&
        _objectivesTabButton != null &&
        _unlocksTabButton != null &&
        _objectivesTabRoot != null &&
        _unlocksTabRoot != null &&
        _objectivesScrollRect != null &&
        _objectivesScrollRect.viewport != null &&
        _objectivesContent != null &&
        _objectivesScrollRect.content == _objectivesContent &&
        _objectiveRowPrefab != null &&
        _objectivesEmptyState != null &&
        _objectivesDataUnavailableState != null &&
        _objectiveDetailRoot != null &&
        _objectiveDetailEmptyState != null &&
        _objectiveNameText != null &&
        _objectiveDescriptionText != null &&
        _objectiveProgressText != null &&
        _objectiveProgressBar != null &&
        _objectiveCompletionText != null &&
        _objectiveRewardText != null &&
        _catalog != null &&
        _unlocksScrollRect != null &&
        _unlocksScrollRect.viewport != null &&
        _unlocksContent != null &&
        _unlocksScrollRect.content == _unlocksContent &&
        _unlockCardPrefab != null &&
        _unlocksEmptyState != null &&
        _unlocksDataUnavailableState != null &&
        _unlockDetailRoot != null &&
        _unlockDetailEmptyState != null &&
        _unlockNameText != null &&
        _unlockTypeText != null &&
        _unlockStatusText != null &&
        _unlockRequirementText != null &&
        _purchaseButton != null &&
        _purchaseButtonLabel != null &&
        _purchaseFeedbackText != null;

    public event Action Closed;

    private void Awake()
    {
        EnsureInitialized();
    }

    private void OnEnable()
    {
        if (_isVisible)
            SubscribeToProgression();
    }

    private void OnDisable()
    {
        UnsubscribeFromProgression();
    }

    private void OnDestroy()
    {
        UnsubscribeFromProgression();
        UnwireAuthoredControls();
    }

    private void Update()
    {
        if (!_isVisible || (_screenStack != null && _screenStack.IsInputLocked))
            return;

        bool previousTab = (Keyboard.current != null && Keyboard.current.aKey.wasPressedThisFrame) ||
                           (Gamepad.current != null && Gamepad.current.leftShoulder.wasPressedThisFrame);
        bool nextTab = (Keyboard.current != null && Keyboard.current.dKey.wasPressedThisFrame) ||
                       (Gamepad.current != null && Gamepad.current.rightShoulder.wasPressedThisFrame);

        if (previousTab && _activeTab != ObjectivesMenuTab.Objectives)
            SetActiveTab(ObjectivesMenuTab.Objectives, true);
        else if (nextTab && _activeTab != ObjectivesMenuTab.Unlocks)
            SetActiveTab(ObjectivesMenuTab.Unlocks, true);
    }

    public void Show()
    {
        EnsureInitialized();
        ReportMissingAuthoredReferences();

        if (_screenRoot == null)
            return;

        _isVisible = true;
        SubscribeToProgression();
        RequestRefresh();

        ObjectivesMenuTab tab = _hasShown && _rememberLastTabForSession
            ? _activeTab
            : ObjectivesMenuTab.Objectives;
        SetActiveTab(tab, false);

        _screenRoot.SetActive(true);
        _hasShown = true;
        if (_screenStack == null)
            FocusActiveTab();
    }

    public void Hide()
    {
        if (!_isVisible)
            return;

        CancelArmedPurchase(false);
        _isVisible = false;
        UnsubscribeFromProgression();

        if (_screenRoot != null)
            _screenRoot.SetActive(false);

        Closed?.Invoke();
    }

    public void ShowObjectivesTab()
    {
        SetActiveTab(ObjectivesMenuTab.Objectives, true);
    }

    public void ShowUnlocksTab()
    {
        SetActiveTab(ObjectivesMenuTab.Unlocks, true);
    }

    /// <summary>Used by the visible Back control and by authored cancel handlers.</summary>
    public void HandleBackRequested()
    {
        if (_armedPurchase != null)
        {
            CancelArmedPurchase(true);
            return;
        }

        if (_screenStack != null && _screenStack.CurrentState == TitleScreenLocalState.Objectives)
        {
            _screenStack.CloseCurrent();
            return;
        }

        Hide();
    }

    public void RefreshContent()
    {
        RequestRefresh();
    }

    private void EnsureInitialized()
    {
        if (_initialized)
            return;

        _initialized = true;
        WireAuthoredControls();
        if (_screenStack != null)
        {
            _screenStack.ScreenOpened -= HandleScreenOpened;
            _screenStack.ScreenOpened += HandleScreenOpened;
        }
    }

    private void WireAuthoredControls()
    {
        WireButton(_backButton, HandleBackRequested);
        WireButton(_objectivesTabButton, ShowObjectivesTab);
        WireButton(_unlocksTabButton, ShowUnlocksTab);
        WireButton(_purchaseButton, HandlePurchaseRequested);
    }

    private void UnwireAuthoredControls()
    {
        if (_screenStack != null)
            _screenStack.ScreenOpened -= HandleScreenOpened;
        UnwireButton(_backButton, HandleBackRequested);
        UnwireButton(_objectivesTabButton, ShowObjectivesTab);
        UnwireButton(_unlocksTabButton, ShowUnlocksTab);
        UnwireButton(_purchaseButton, HandlePurchaseRequested);
    }

    private void HandleScreenOpened(TitleScreenLocalState state)
    {
        if (state == TitleScreenLocalState.Objectives &&
            _isVisible &&
            (_screenStack == null || !_screenStack.IsInputLocked))
        {
            FocusActiveTab();
        }
    }

    private static void WireButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button == null)
            return;

        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private static void UnwireButton(Button button, UnityEngine.Events.UnityAction action)
    {
        if (button != null)
            button.onClick.RemoveListener(action);
    }

    private void SubscribeToProgression()
    {
        SaveManager current = SaveManager.Instance;
        if (_subscribedSaveManager == current)
            return;

        UnsubscribeFromProgression();
        _subscribedSaveManager = current;
        if (_subscribedSaveManager == null)
            return;

        _subscribedSaveManager.OnScrapChanged += HandleProgressionChanged;
        _subscribedSaveManager.OnUnlocksChanged += HandleProgressionChanged;
        _subscribedSaveManager.OnAchievementUnlocked += HandleAchievementUnlocked;
    }

    private void UnsubscribeFromProgression()
    {
        if (_subscribedSaveManager == null)
            return;

        _subscribedSaveManager.OnScrapChanged -= HandleProgressionChanged;
        _subscribedSaveManager.OnUnlocksChanged -= HandleProgressionChanged;
        _subscribedSaveManager.OnAchievementUnlocked -= HandleAchievementUnlocked;
        _subscribedSaveManager = null;
    }

    private void HandleProgressionChanged()
    {
        RequestRefresh();
    }

    private void HandleAchievementUnlocked(AchievementDefinition achievement)
    {
        RequestRefresh();
    }

    private void RequestRefresh()
    {
        if (!_isVisible)
            return;

        if (_refreshing || _handlingPurchase)
        {
            _refreshPending = true;
            return;
        }

        do
        {
            _refreshPending = false;
            _refreshing = true;
            RefreshAuthoredPresentation();
            _refreshing = false;
        }
        while (_refreshPending && !_handlingPurchase);
    }

    private void RefreshAuthoredPresentation()
    {
        SaveManager saveManager = SaveManager.Instance;
        if (_scrapText != null)
            _scrapText.text = saveManager != null ? $"SCRAP: {saveManager.Scrap}" : "SCRAP: ----";

        RefreshObjectiveRows(saveManager);
        RefreshUnlockCards(saveManager);
        ApplyTabVisibility();
    }

    private void RefreshObjectiveRows(SaveManager saveManager)
    {
        string selectionToRestore = _selectedAchievementId;
        ClearObjectiveRows();

        bool dataAvailable = saveManager != null;
        IReadOnlyList<AchievementDefinition> achievements = dataAvailable
            ? saveManager.AchievementCatalog
            : null;
        int validCount = CountValidAchievements(achievements);
        bool templateAvailable = _objectiveRowPrefab != null && _objectivesContent != null;

        SetActive(_objectivesDataUnavailableState, !dataAvailable || (validCount > 0 && !templateAvailable));
        SetActive(_objectivesEmptyState, dataAvailable && validCount == 0);

        if (!dataAvailable || validCount == 0 || !templateAvailable)
        {
            ClearObjectiveDetails();
            return;
        }

        for (int i = 0; i < achievements.Count; i++)
        {
            AchievementDefinition achievement = achievements[i];
            if (achievement == null)
                continue;

            float current = Mathf.Min(saveManager.GetProgress(achievement), achievement.TargetValue);
            bool completed = saveManager.IsAchievementUnlocked(achievement);
            ObjectiveRowView row = Instantiate(_objectiveRowPrefab, _objectivesContent, false);
            row.gameObject.SetActive(true);
            row.Bind(
                achievement,
                current,
                achievement.TargetValue,
                completed,
                HandleObjectiveFocused);
            _objectiveRows.Add(row);
        }

        ObjectiveRowView selection = FindObjectiveRow(selectionToRestore);
        SelectObjective(selection != null ? selection : _objectiveRows[0]);
    }

    private void RefreshUnlockCards(SaveManager saveManager)
    {
        string selectionToRestore = _selectedUnlockId;
        string armedId = _armedPurchase?.UnlockId;
        ClearUnlockCards();

        bool dataAvailable = saveManager != null && _catalog != null;
        int validCount = dataAvailable ? CountCatalogItems(_catalog) : 0;
        bool templateAvailable = _unlockCardPrefab != null && _unlocksContent != null;

        SetActive(_unlocksDataUnavailableState, !dataAvailable || (validCount > 0 && !templateAvailable));
        SetActive(_unlocksEmptyState, dataAvailable && validCount == 0);

        if (!dataAvailable || validCount == 0 || !templateAvailable)
        {
            _armedPurchase = null;
            ClearUnlockDetails();
            return;
        }

        for (int i = 0; i < _catalog.Weapons.Count; i++)
        {
            WeaponData weapon = _catalog.Weapons[i];
            if (weapon != null)
                CreateUnlockCard(weapon, weapon.DisplayName, "WEAPON", saveManager);
        }

        for (int i = 0; i < _catalog.PassiveItems.Count; i++)
        {
            PassiveItemData passive = _catalog.PassiveItems[i];
            if (passive != null)
                CreateUnlockCard(passive, passive.DisplayName, "PASSIVE", saveManager);
        }

        UnlockCardView selection = FindUnlockCard(selectionToRestore);
        SelectUnlock(selection != null ? selection : _unlockCards[0]);

        if (!string.IsNullOrEmpty(armedId) &&
            _selectedUnlockCard != null &&
            _selectedUnlockCard.State == UnlockCardState.Purchasable &&
            _selectedUnlockCard.Item.Requirement != null &&
            _selectedUnlockCard.Item.Requirement.ScrapPrice > 0 &&
            _selectedUnlockCard.Item.UnlockId == armedId)
        {
            _armedPurchase = _selectedUnlockCard.Item;
            UpdateUnlockDetails();
        }
    }

    private void CreateUnlockCard(IUnlockable item, string displayName, string itemType, SaveManager saveManager)
    {
        UnlockCardState state = ResolveUnlockState(item, saveManager);
        UnlockCardView card = Instantiate(_unlockCardPrefab, _unlocksContent, false);
        card.gameObject.SetActive(true);
        card.Bind(item, displayName, itemType, state, HandleUnlockFocused);
        _unlockCards.Add(card);
    }

    private void HandleObjectiveFocused(ObjectiveRowView row)
    {
        if (row != null)
            SelectObjective(row);
    }

    private void SelectObjective(ObjectiveRowView row)
    {
        _selectedObjectiveRow = row;
        _selectedAchievementId = row != null ? row.Achievement.AchievementId : null;

        for (int i = 0; i < _objectiveRows.Count; i++)
            _objectiveRows[i].SetSelected(_objectiveRows[i] == row);

        UpdateObjectiveDetails();
        ScrollIntoView(_objectivesScrollRect, row != null ? row.transform as RectTransform : null);
    }

    private void UpdateObjectiveDetails()
    {
        if (_selectedObjectiveRow == null)
        {
            ClearObjectiveDetails();
            return;
        }

        AchievementDefinition achievement = _selectedObjectiveRow.Achievement;
        SetActive(_objectiveDetailRoot, true);
        SetActive(_objectiveDetailEmptyState, false);

        SetText(_objectiveNameText, achievement.DisplayName);
        SetText(_objectiveDescriptionText, achievement.Description);
        SetText(
            _objectiveProgressText,
            $"{ObjectiveRowView.FormatValue(_selectedObjectiveRow.CurrentProgress)} / {ObjectiveRowView.FormatValue(_selectedObjectiveRow.TargetProgress)}");
        SetText(_objectiveCompletionText, _selectedObjectiveRow.IsComplete ? "COMPLETE" : "IN PROGRESS");
        SetText(_objectiveRewardText, BuildObjectiveRewardText(achievement));

        if (_objectiveProgressBar != null)
        {
            _objectiveProgressBar.minValue = 0f;
            _objectiveProgressBar.maxValue = Mathf.Max(0.0001f, _selectedObjectiveRow.TargetProgress);
            _objectiveProgressBar.SetValueWithoutNotify(_selectedObjectiveRow.CurrentProgress);
        }
    }

    private string BuildObjectiveRewardText(AchievementDefinition achievement)
    {
        StringBuilder builder = new();
        if (achievement.ScrapReward > 0)
            builder.Append("REWARD: ").Append(achievement.ScrapReward).Append(" SCRAP");

        if (_catalog != null)
        {
            AppendAssociatedUnlocks(builder, achievement, _catalog.Weapons);
            AppendAssociatedUnlocks(builder, achievement, _catalog.PassiveItems);
        }

        return builder.Length > 0 ? builder.ToString() : "NO REWARD LISTED";
    }

    private static void AppendAssociatedUnlocks<T>(StringBuilder builder, AchievementDefinition achievement, IReadOnlyList<T> items)
        where T : UnityEngine.Object, IUnlockable
    {
        if (items == null)
            return;

        for (int i = 0; i < items.Count; i++)
        {
            IUnlockable item = items[i];
            if (item?.Requirement?.RequiredAchievement != achievement)
                continue;

            if (builder.Length > 0)
                builder.Append("\n");
            builder.Append("UNLOCKS: ").Append(item.UnlockId);
        }
    }

    private void ClearObjectiveDetails()
    {
        _selectedObjectiveRow = null;
        _selectedAchievementId = null;
        SetActive(_objectiveDetailRoot, false);
        SetActive(_objectiveDetailEmptyState, true);
        SetText(_objectiveNameText, string.Empty);
        SetText(_objectiveDescriptionText, string.Empty);
        SetText(_objectiveProgressText, string.Empty);
        SetText(_objectiveCompletionText, string.Empty);
        SetText(_objectiveRewardText, string.Empty);
        if (_objectiveProgressBar != null)
            _objectiveProgressBar.SetValueWithoutNotify(0f);
    }

    private void HandleUnlockFocused(UnlockCardView card)
    {
        if (card != null)
            SelectUnlock(card);
    }

    private void SelectUnlock(UnlockCardView card)
    {
        if (_armedPurchase != null &&
            (card == null || card.Item == null || card.Item.UnlockId != _armedPurchase.UnlockId))
        {
            CancelArmedPurchase(false);
        }

        _selectedUnlockCard = card;
        _selectedUnlockId = card?.Item?.UnlockId;

        for (int i = 0; i < _unlockCards.Count; i++)
            _unlockCards[i].SetSelected(_unlockCards[i] == card);

        SetPurchaseFeedback(string.Empty);
        UpdateUnlockDetails();
        ScrollIntoView(_unlocksScrollRect, card != null ? card.transform as RectTransform : null);
    }

    private void UpdateUnlockDetails()
    {
        if (_selectedUnlockCard == null || _selectedUnlockCard.Item == null)
        {
            ClearUnlockDetails();
            return;
        }

        IUnlockable item = _selectedUnlockCard.Item;
        UnlockRequirement requirement = item.Requirement;
        SetActive(_unlockDetailRoot, true);
        SetActive(_unlockDetailEmptyState, false);
        SetText(_unlockNameText, _selectedUnlockCard.DisplayName);
        SetText(_unlockTypeText, _selectedUnlockCard.ItemType);
        SetText(_unlockStatusText, GetUnlockStateLabel(_selectedUnlockCard.State));
        SetText(_unlockRequirementText, BuildRequirementText(requirement));

        if (_purchaseButton == null)
            return;

        bool purchasable = _selectedUnlockCard.State == UnlockCardState.Purchasable;
        _purchaseButton.interactable = purchasable;

        string actionLabel = _selectedUnlockCard.State switch
        {
            UnlockCardState.Owned => "OWNED",
            UnlockCardState.AchievementLocked => "LOCKED",
            UnlockCardState.InsufficientScrap => requirement != null ? $"NEED {requirement.ScrapPrice} SCRAP" : "LOCKED",
            UnlockCardState.Unavailable => "UNAVAILABLE",
            _ when _armedPurchase != null && _armedPurchase.UnlockId == item.UnlockId => $"CONFIRM — {requirement.ScrapPrice} SCRAP",
            _ when requirement != null && requirement.ScrapPrice > 0 => $"PURCHASE — {requirement.ScrapPrice} SCRAP",
            _ => "UNLOCK"
        };
        SetText(_purchaseButtonLabel, actionLabel);
    }

    private void HandlePurchaseRequested()
    {
        if (_selectedUnlockCard == null ||
            _selectedUnlockCard.Item == null ||
            _selectedUnlockCard.State != UnlockCardState.Purchasable ||
            SaveManager.Instance == null)
        {
            return;
        }

        IUnlockable item = _selectedUnlockCard.Item;
        UnlockRequirement requirement = item.Requirement;
        if (requirement == null)
            return;

        if (requirement.ScrapPrice > 0 &&
            (_armedPurchase == null || _armedPurchase.UnlockId != item.UnlockId))
        {
            _armedPurchase = item;
            SetPurchaseFeedback("PRESS AGAIN TO CONFIRM");
            UpdateUnlockDetails();
            return;
        }

        _handlingPurchase = true;
        bool purchased = SaveManager.Instance.TryPurchase(item);
        _handlingPurchase = false;
        _armedPurchase = null;
        RequestRefresh();
        SetPurchaseFeedback(purchased ? "PURCHASE COMPLETE" : "PURCHASE REJECTED");
        UpdateUnlockDetails();
    }

    private void CancelArmedPurchase(bool restorePurchaseFocus)
    {
        if (_armedPurchase == null)
            return;

        _armedPurchase = null;
        SetPurchaseFeedback("PURCHASE CANCELLED");
        UpdateUnlockDetails();

        if (restorePurchaseFocus && _purchaseButton != null && _purchaseButton.interactable)
            Focus(_purchaseButton.gameObject);
    }

    private void ClearUnlockDetails()
    {
        _selectedUnlockCard = null;
        _selectedUnlockId = null;
        SetActive(_unlockDetailRoot, false);
        SetActive(_unlockDetailEmptyState, true);
        SetText(_unlockNameText, string.Empty);
        SetText(_unlockTypeText, string.Empty);
        SetText(_unlockStatusText, string.Empty);
        SetText(_unlockRequirementText, string.Empty);
        SetText(_purchaseButtonLabel, "UNAVAILABLE");
        SetPurchaseFeedback(string.Empty);
        if (_purchaseButton != null)
            _purchaseButton.interactable = false;
    }

    private void SetActiveTab(ObjectivesMenuTab tab, bool focus)
    {
        if (tab != _activeTab)
            CancelArmedPurchase(false);

        _activeTab = tab;
        ApplyTabVisibility();
        if (focus)
            FocusActiveTab();
    }

    private void ApplyTabVisibility()
    {
        bool objectivesActive = _activeTab == ObjectivesMenuTab.Objectives;
        SetActive(_objectivesTabRoot, objectivesActive);
        SetActive(_unlocksTabRoot, !objectivesActive);
        SetActive(_objectivesTabSelectedState, objectivesActive);
        SetActive(_unlocksTabSelectedState, !objectivesActive);
    }

    private void FocusActiveTab()
    {
        if (_activeTab == ObjectivesMenuTab.Objectives)
        {
            if (_selectedObjectiveRow != null)
            {
                _selectedObjectiveRow.Focus();
                ScrollIntoView(_objectivesScrollRect, _selectedObjectiveRow.transform as RectTransform);
            }
            else if (_objectivesTabButton != null)
                Focus(_objectivesTabButton.gameObject);
            return;
        }

        if (_selectedUnlockCard != null)
        {
            _selectedUnlockCard.Focus();
            ScrollIntoView(_unlocksScrollRect, _selectedUnlockCard.transform as RectTransform);
        }
        else if (_unlocksTabButton != null)
            Focus(_unlocksTabButton.gameObject);
    }

    private static void ScrollIntoView(ScrollRect scrollRect, RectTransform target)
    {
        if (scrollRect == null ||
            target == null ||
            scrollRect.content == null ||
            scrollRect.viewport == null ||
            !scrollRect.gameObject.activeInHierarchy ||
            !target.gameObject.activeInHierarchy)
        {
            return;
        }

        // Dynamic rows/cards are laid out immediately before focus can move to them. Rebuild
        // first so the real content height (notably the 17-card unlock grid) is available.
        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(scrollRect.content);
        Canvas.ForceUpdateCanvases();

        Bounds targetBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(scrollRect.viewport, target);
        Rect viewportRect = scrollRect.viewport.rect;
        const float focusPadding = 8f;
        float upperLimit = viewportRect.yMax - focusPadding;
        float lowerLimit = viewportRect.yMin + focusPadding;
        float verticalOffset = 0f;

        if (targetBounds.max.y > upperLimit)
            verticalOffset = targetBounds.max.y - upperLimit;
        else if (targetBounds.min.y < lowerLimit)
            verticalOffset = targetBounds.min.y - lowerLimit;

        if (Mathf.Abs(verticalOffset) <= 0.01f)
            return;

        scrollRect.StopMovement();
        Vector2 contentPosition = scrollRect.content.anchoredPosition;
        contentPosition.y -= verticalOffset;
        scrollRect.content.anchoredPosition = contentPosition;
        Canvas.ForceUpdateCanvases();
    }

    private static void Focus(GameObject target)
    {
        if (target != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(target);
    }

    private ObjectiveRowView FindObjectiveRow(string achievementId)
    {
        if (string.IsNullOrEmpty(achievementId))
            return null;

        for (int i = 0; i < _objectiveRows.Count; i++)
        {
            ObjectiveRowView row = _objectiveRows[i];
            if (row.Achievement != null && row.Achievement.AchievementId == achievementId)
                return row;
        }

        return null;
    }

    private UnlockCardView FindUnlockCard(string unlockId)
    {
        if (string.IsNullOrEmpty(unlockId))
            return null;

        for (int i = 0; i < _unlockCards.Count; i++)
        {
            UnlockCardView card = _unlockCards[i];
            if (card.Item != null && card.Item.UnlockId == unlockId)
                return card;
        }

        return null;
    }

    private void ClearObjectiveRows()
    {
        _selectedObjectiveRow = null;
        for (int i = 0; i < _objectiveRows.Count; i++)
        {
            ObjectiveRowView row = _objectiveRows[i];
            if (row == null)
                continue;
            row.Unbind();
            DestroyDynamicInstance(row.gameObject);
        }
        _objectiveRows.Clear();
    }

    private void ClearUnlockCards()
    {
        _selectedUnlockCard = null;
        for (int i = 0; i < _unlockCards.Count; i++)
        {
            UnlockCardView card = _unlockCards[i];
            if (card == null)
                continue;
            card.Unbind();
            DestroyDynamicInstance(card.gameObject);
        }
        _unlockCards.Clear();
    }

    private static void DestroyDynamicInstance(GameObject instance)
    {
        if (instance == null)
            return;

        if (Application.isPlaying)
            Destroy(instance);
        else
            DestroyImmediate(instance);
    }

    private static int CountValidAchievements(IReadOnlyList<AchievementDefinition> achievements)
    {
        if (achievements == null)
            return 0;

        int count = 0;
        for (int i = 0; i < achievements.Count; i++)
        {
            if (achievements[i] != null)
                count++;
        }
        return count;
    }

    private static int CountCatalogItems(UnlockCatalog catalog)
    {
        int count = 0;
        for (int i = 0; i < catalog.Weapons.Count; i++)
        {
            if (catalog.Weapons[i] != null)
                count++;
        }
        for (int i = 0; i < catalog.PassiveItems.Count; i++)
        {
            if (catalog.PassiveItems[i] != null)
                count++;
        }
        return count;
    }

    private static UnlockCardState ResolveUnlockState(IUnlockable item, SaveManager saveManager)
    {
        if (item == null || saveManager == null)
            return UnlockCardState.Unavailable;
        if (saveManager.IsUnlocked(item))
            return UnlockCardState.Owned;

        UnlockRequirement requirement = item.Requirement;
        if (requirement == null)
            return UnlockCardState.Unavailable;
        if (requirement.RequiredAchievement != null && !saveManager.IsAchievementUnlocked(requirement.RequiredAchievement))
            return UnlockCardState.AchievementLocked;
        if (saveManager.Scrap < requirement.ScrapPrice)
            return UnlockCardState.InsufficientScrap;
        return UnlockCardState.Purchasable;
    }

    private static string GetUnlockStateLabel(UnlockCardState state)
    {
        return state switch
        {
            UnlockCardState.Owned => "OWNED",
            UnlockCardState.Purchasable => "AVAILABLE",
            UnlockCardState.InsufficientScrap => "INSUFFICIENT SCRAP",
            UnlockCardState.AchievementLocked => "ACHIEVEMENT LOCKED",
            _ => "UNAVAILABLE"
        };
    }

    private static string BuildRequirementText(UnlockRequirement requirement)
    {
        if (requirement == null)
            return "NO UNLOCK ROUTE AVAILABLE";

        StringBuilder builder = new();
        if (requirement.RequiredAchievement != null)
            builder.Append("REQUIRES: ").Append(requirement.RequiredAchievement.DisplayName);
        if (requirement.ScrapPrice > 0)
        {
            if (builder.Length > 0)
                builder.Append("\n");
            builder.Append("PRICE: ").Append(requirement.ScrapPrice).Append(" SCRAP");
        }
        return builder.Length > 0 ? builder.ToString() : "NO SCRAP COST";
    }

    private void SetPurchaseFeedback(string message)
    {
        SetText(_purchaseFeedbackText, message);
    }

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

    private void ReportMissingAuthoredReferences()
    {
        if (_authoredReferencesReported)
            return;

        _authoredReferencesReported = true;
        StringBuilder missing = new();
        AppendMissing(missing, _screenRoot, nameof(_screenRoot));
        AppendMissing(missing, _backButton, nameof(_backButton));
        AppendMissing(missing, _scrapText, nameof(_scrapText));
        AppendMissing(missing, _objectivesTabButton, nameof(_objectivesTabButton));
        AppendMissing(missing, _unlocksTabButton, nameof(_unlocksTabButton));
        AppendMissing(missing, _objectivesTabRoot, nameof(_objectivesTabRoot));
        AppendMissing(missing, _unlocksTabRoot, nameof(_unlocksTabRoot));
        AppendMissing(missing, _objectivesScrollRect, nameof(_objectivesScrollRect));
        AppendMissing(missing, _objectivesContent, nameof(_objectivesContent));
        AppendMissing(missing, _objectiveRowPrefab, nameof(_objectiveRowPrefab));
        AppendMissing(missing, _objectivesEmptyState, nameof(_objectivesEmptyState));
        AppendMissing(missing, _objectivesDataUnavailableState, nameof(_objectivesDataUnavailableState));
        AppendMissing(missing, _objectiveDetailRoot, nameof(_objectiveDetailRoot));
        AppendMissing(missing, _unlocksScrollRect, nameof(_unlocksScrollRect));
        AppendMissing(missing, _unlocksContent, nameof(_unlocksContent));
        AppendMissing(missing, _unlockCardPrefab, nameof(_unlockCardPrefab));
        AppendMissing(missing, _unlocksEmptyState, nameof(_unlocksEmptyState));
        AppendMissing(missing, _unlocksDataUnavailableState, nameof(_unlocksDataUnavailableState));
        AppendMissing(missing, _unlockDetailRoot, nameof(_unlockDetailRoot));
        AppendMissing(missing, _purchaseButton, nameof(_purchaseButton));

        if (missing.Length > 0)
            Debug.LogError($"ObjectivesMenuUI on '{name}' is missing authored references: {missing}", this);
    }

    private static void AppendMissing(StringBuilder builder, UnityEngine.Object reference, string fieldName)
    {
        if (reference != null)
            return;
        if (builder.Length > 0)
            builder.Append(", ");
        builder.Append(fieldName);
    }
}
