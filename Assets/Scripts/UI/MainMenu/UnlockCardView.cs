using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum UnlockCardState
{
    Owned,
    Purchasable,
    InsufficientScrap,
    AchievementLocked,
    Unavailable
}

/// <summary>Presentation and focus behavior for one authored unlock-card prefab.</summary>
[DisallowMultipleComponent]
public sealed class UnlockCardView : MonoBehaviour, ISelectHandler, IPointerEnterHandler
{
    [SerializeField] private Button _button;
    [SerializeField] private TextMeshProUGUI _nameText;
    [SerializeField] private TextMeshProUGUI _typeText;
    [SerializeField] private TextMeshProUGUI _priceText;
    [SerializeField] private TextMeshProUGUI _requirementText;
    [SerializeField] private TextMeshProUGUI _statusText;
    [SerializeField] private GameObject _ownedState;
    [SerializeField] private GameObject _lockedState;
    [SerializeField] private GameObject _purchasableState;
    [SerializeField] private GameObject _selectedState;

    private Action<UnlockCardView> _selected;

    public IUnlockable Item { get; private set; }
    public string DisplayName { get; private set; }
    public string ItemType { get; private set; }
    public UnlockCardState State { get; private set; }
    public Button Button => _button;

    public void Bind(
        IUnlockable item,
        string displayName,
        string itemType,
        UnlockCardState state,
        Action<UnlockCardView> selected)
    {
        Unbind();
        Item = item;
        DisplayName = string.IsNullOrEmpty(displayName) ? item?.UnlockId ?? string.Empty : displayName;
        ItemType = itemType ?? string.Empty;
        State = state;
        _selected = selected;

        if (_button != null)
        {
            // Locked cards remain selectable so their requirements stay inspectable.
            _button.interactable = item != null;
            _button.onClick.AddListener(HandleActivated);
        }

        UnlockRequirement requirement = item?.Requirement;
        SetText(_nameText, DisplayName);
        SetText(_typeText, ItemType);
        SetText(_priceText, requirement != null && requirement.ScrapPrice > 0 ? $"{requirement.ScrapPrice} SCRAP" : "NO SCRAP COST");
        SetText(
            _requirementText,
            requirement?.RequiredAchievement != null
                ? $"REQUIRES: {requirement.RequiredAchievement.DisplayName}"
                : string.Empty);
        SetText(_statusText, GetStateLabel(state));

        SetActive(_ownedState, state == UnlockCardState.Owned);
        SetActive(_lockedState, state is UnlockCardState.AchievementLocked or UnlockCardState.InsufficientScrap or UnlockCardState.Unavailable);
        SetActive(_purchasableState, state == UnlockCardState.Purchasable);
        SetSelected(false);
    }

    public void Unbind()
    {
        if (_button != null)
            _button.onClick.RemoveListener(HandleActivated);
        _selected = null;
    }

    public void SetSelected(bool selected)
    {
        SetActive(_selectedState, selected);
    }

    public void Focus()
    {
        if (_button != null && EventSystem.current != null)
            EventSystem.current.SetSelectedGameObject(_button.gameObject);
    }

    public void OnSelect(BaseEventData eventData)
    {
        NotifySelected();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (_button == null || !_button.IsInteractable())
            return;

        if (EventSystem.current != null && EventSystem.current.currentSelectedGameObject != _button.gameObject)
            EventSystem.current.SetSelectedGameObject(_button.gameObject);
        else
            NotifySelected();
    }

    private void HandleActivated()
    {
        NotifySelected();
    }

    private void NotifySelected()
    {
        if (Item != null)
            _selected?.Invoke(this);
    }

    private static string GetStateLabel(UnlockCardState state)
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

    private static void SetText(TMP_Text target, string value)
    {
        if (target != null)
            target.text = value ?? string.Empty;
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }
}
