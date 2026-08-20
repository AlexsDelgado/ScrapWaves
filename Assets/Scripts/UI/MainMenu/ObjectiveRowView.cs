using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Presentation and focus behavior for one authored objective-row prefab.</summary>
[DisallowMultipleComponent]
public sealed class ObjectiveRowView : MonoBehaviour, ISelectHandler, IPointerEnterHandler
{
    [SerializeField] private Button _button;
    [SerializeField] private TextMeshProUGUI _nameText;
    [SerializeField] private TextMeshProUGUI _progressText;
    [SerializeField] private Slider _progressBar;
    [SerializeField] private GameObject _completeStamp;
    [SerializeField] private GameObject _selectedState;

    private Action<ObjectiveRowView> _selected;

    public AchievementDefinition Achievement { get; private set; }
    public float CurrentProgress { get; private set; }
    public float TargetProgress { get; private set; }
    public bool IsComplete { get; private set; }
    public Button Button => _button;

    public void Bind(
        AchievementDefinition achievement,
        float currentProgress,
        float targetProgress,
        bool isComplete,
        Action<ObjectiveRowView> selected)
    {
        Unbind();
        Achievement = achievement;
        CurrentProgress = Mathf.Clamp(currentProgress, 0f, Mathf.Max(0f, targetProgress));
        TargetProgress = Mathf.Max(0f, targetProgress);
        IsComplete = isComplete;
        _selected = selected;

        if (_button != null)
        {
            _button.interactable = achievement != null;
            _button.onClick.AddListener(HandleActivated);
        }

        if (_nameText != null)
            _nameText.text = achievement != null ? achievement.DisplayName : string.Empty;
        if (_progressText != null)
            _progressText.text = $"{FormatValue(CurrentProgress)} / {FormatValue(TargetProgress)}";
        if (_progressBar != null)
        {
            _progressBar.minValue = 0f;
            _progressBar.maxValue = Mathf.Max(0.0001f, TargetProgress);
            _progressBar.SetValueWithoutNotify(CurrentProgress);
        }

        SetActive(_completeStamp, IsComplete);
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

    public static string FormatValue(float value)
    {
        return value.ToString("0.#", CultureInfo.InvariantCulture);
    }

    private void HandleActivated()
    {
        NotifySelected();
    }

    private void NotifySelected()
    {
        if (Achievement != null)
            _selected?.Invoke(this);
    }

    private static void SetActive(GameObject target, bool active)
    {
        if (target != null && target.activeSelf != active)
            target.SetActive(active);
    }
}
