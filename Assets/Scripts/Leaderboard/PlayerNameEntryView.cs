using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>Controls an authored name entry screen; the screen stack owns its visibility and Cancel button.</summary>
[DisallowMultipleComponent]
public sealed class PlayerNameEntryView : MonoBehaviour
{
    [SerializeField] private TMP_InputField _input;
    [SerializeField] private TMP_Text _validation;
    [SerializeField] private Button _startButton;
    [SerializeField] private Button _cancelButton;

    private bool _bound;
    private bool _ready;
    private bool _submitted;
    public bool HasRequiredReferences => _input != null && _validation != null && _startButton != null && _cancelButton != null;
    public event Action<string> Confirmed;

    private void Awake() => Bind();

    private void OnDestroy()
    {
        if (!_bound)
            return;
        if (_input != null)
        {
            _input.onValueChanged.RemoveListener(HandleChanged);
            _input.onSubmit.RemoveListener(HandleSubmit);
        }
        if (_startButton != null)
            _startButton.onClick.RemoveListener(Submit);
    }

    public void Prepare()
    {
        Bind();
        _ready = false;
        _submitted = false;
        if (_input != null)
        {
            _input.SetTextWithoutNotify(string.Empty);
            _input.interactable = true;
        }
        if (_cancelButton != null)
            _cancelButton.interactable = true;
        RefreshValidation(false);
    }

    public void ActivateInput()
    {
        _ready = true;
        RefreshValidation(false);
        if (_input == null || !_input.gameObject.activeInHierarchy)
            return;
        EventSystem.current?.SetSelectedGameObject(_input.gameObject);
        _input.ActivateInputField();
    }

    public void ResetEntry()
    {
        _ready = false;
        _submitted = false;
        if (_input != null)
        {
            _input.DeactivateInputField();
            _input.SetTextWithoutNotify(string.Empty);
        }
    }

    public void ShowLaunchError()
    {
        _submitted = false;
        if (_input != null)
            _input.interactable = true;
        if (_cancelButton != null)
            _cancelButton.interactable = true;
        ActivateInput();
        if (_validation != null)
            _validation.text = "Unable to start. Please try again.";
    }

    private void Bind()
    {
        if (_bound || !HasRequiredReferences)
            return;
        _bound = true;
        _input.characterLimit = DemoLeaderboardNames.MaximumLength;
        _input.lineType = TMP_InputField.LineType.SingleLine;
        _input.richText = false;
        if (_input.textComponent != null)
            _input.textComponent.richText = false;
        _validation.richText = false;
        _input.onValueChanged.AddListener(HandleChanged);
        _input.onSubmit.AddListener(HandleSubmit);
        _startButton.onClick.AddListener(Submit);
        ConfigureNavigation();
    }

    private void HandleChanged(string value) => RefreshValidation(value.Length > 0);
    private void HandleSubmit(string value)
    {
        if (_input != null && !_input.wasCanceled)
            Submit();
    }

    private void Submit()
    {
        if (!_ready || _submitted || !HasRequiredReferences || !_startButton.IsInteractable())
            return;
        if (!DemoLeaderboardNames.TryNormalize(_input.text, out string name))
        {
            RefreshValidation(true);
            return;
        }
        _submitted = true;
        _startButton.interactable = false;
        _cancelButton.interactable = false;
        _input.interactable = false;
        Confirmed?.Invoke(name);
    }

    private void RefreshValidation(bool showError)
    {
        bool valid = _input != null && DemoLeaderboardNames.TryNormalize(_input.text, out _);
        if (_startButton != null)
            _startButton.interactable = _ready && !_submitted && valid;
        if (_validation != null)
            _validation.text = showError && !valid ? "Enter a name (1-16 characters)." : "1-16 characters";
    }

    private void ConfigureNavigation()
    {
        SetNavigation(_input, _cancelButton, _startButton, null, null);
        SetNavigation(_startButton, _input, _cancelButton, _cancelButton, _cancelButton);
        SetNavigation(_cancelButton, _startButton, _input, _startButton, _startButton);
    }

    private static void SetNavigation(Selectable control, Selectable up, Selectable down, Selectable left, Selectable right)
    {
        Navigation navigation = control.navigation;
        navigation.mode = Navigation.Mode.Explicit;
        navigation.selectOnUp = up;
        navigation.selectOnDown = down;
        navigation.selectOnLeft = left;
        navigation.selectOnRight = right;
        control.navigation = navigation;
    }
}
