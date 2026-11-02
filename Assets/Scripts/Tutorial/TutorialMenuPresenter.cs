using UnityEngine;
using UnityEngine.UI;

/// <summary>Replay and persistent post-run guidance bound to editor-authored controls.</summary>
public sealed class TutorialMenuPresenter : MonoBehaviour
{
    [SerializeField] private TitleScreenController _title;
    [SerializeField] private ObjectivesMenuUI _objectives;
    [SerializeField] private Button _replay;
    [SerializeField] private Button _info;
    [SerializeField] private Button _close;
    [SerializeField] private GameObject _help;
    [SerializeField] private CanvasGroup _pendingAccent;
    private bool _wasVisible;
    private void Awake()
    {
        _replay.onClick.AddListener(Replay);
        _info.onClick.AddListener(ShowHelp);
        _close.onClick.AddListener(CloseHelp);
        _help.SetActive(false);
    }
    private void Update()
    {
        bool visible = _objectives != null && _objectives.IsVisible;
        bool pending = SaveManager.Instance != null && SaveManager.Instance.MetaTutorialPending;
        if (_pendingAccent != null)
        {
            bool reduced = UserSettingsService.Instance != null && UserSettingsService.Instance.ReducedMotion;
            _pendingAccent.alpha = pending && !visible ? (reduced ? 1f : .7f + .3f * Mathf.Sin(Time.unscaledTime * 3f)) : 0;
        }
        _info.gameObject.SetActive(visible);
        if (visible && !_wasVisible && pending)
        {
            _objectives.ShowUpgradesTab();
            ShowHelp();
        }
        if (!visible) _help.SetActive(false);
        if (visible && _help.activeSelf && TutorialInput.ConsumeAdvance()) CloseHelp();
        _wasVisible = visible;
    }
    private void Replay() => _title.PlayTutorial();
    public void ShowHelp() => _help.SetActive(true);
    public void CloseHelp()
    {
        if (!_help.activeSelf) return;
        _help.SetActive(false);
        SaveManager.Instance?.CompleteMetaTutorial();
    }
    private void OnDestroy()
    {
        if (_replay != null) _replay.onClick.RemoveListener(Replay);
        if (_info != null) _info.onClick.RemoveListener(ShowHelp);
        if (_close != null) _close.onClick.RemoveListener(CloseHelp);
    }
}
