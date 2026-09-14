using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>Content and selection binding for an authored choice card.</summary>
[DisallowMultipleComponent]
public sealed class ChoiceCardView : MonoBehaviour
{
    public Image Icon;
    public TMP_Text NameText;
    public TMP_Text SummaryText;
    public TMP_Text DescriptionText;
    public Button SelectButton;

    private UnityAction _selectionListener;

    public bool IsConfigured => Icon != null && NameText != null && SummaryText != null
        && DescriptionText != null && SelectButton != null;

    public void Bind(string name, string summary, string description, Sprite icon, Action onSelected)
    {
        Clear();
        NameText.text = name ?? string.Empty;
        SummaryText.text = summary ?? string.Empty;
        DescriptionText.text = description ?? string.Empty;
        Icon.sprite = icon;
        Icon.enabled = icon != null;
        SelectButton.interactable = true;
        _selectionListener = () => onSelected?.Invoke();
        SelectButton.onClick.AddListener(_selectionListener);
    }

    public void Clear()
    {
        if (SelectButton != null && _selectionListener != null)
            SelectButton.onClick.RemoveListener(_selectionListener);
        _selectionListener = null;
    }
}
