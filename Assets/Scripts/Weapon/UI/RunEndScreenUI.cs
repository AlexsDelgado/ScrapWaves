using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class RunEndScreenUI : MonoBehaviour
{
    public static RunEndScreenUI Instance { get; private set; }

    [SerializeField] private Color _overlayColor = new(0.018f, 0.026f, 0.022f, 0.55f);
    [SerializeField] private Color _victoryTextColor = new(0.659f, 0.78f, 0.561f, 1f);
    [SerializeField] private Color _defeatTextColor = new(0.851f, 0.416f, 0.196f, 1f);

    [Header("Authored UI")]
    [SerializeField] private GameObject _root;
    [SerializeField] private TextMeshProUGUI _titleText;
    [SerializeField] private TextMeshProUGUI _statsText;
    [SerializeField] private Button _retryButton;
    [SerializeField] private Button _mainMenuButton;
    private ThirdPersonCamera _camera;
    private bool _isWired;

    private void Awake()
    {
        Instance = this;
        _isWired = TryWireFromHierarchy();
        if (_isWired)
            _root.SetActive(false);
        else
            Debug.LogError(
                $"[{nameof(RunEndScreenUI)}] The authored RunEndRoot hierarchy is incomplete. " +
                "Expected RunEndRoot/Panel with Title and Stats.",
                this);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void Show(GameManager.GameState state, string title)
    {
        if (!_isWired)
            _isWired = TryWireFromHierarchy();

        if (!_isWired)
        {
            Debug.LogError($"[{nameof(RunEndScreenUI)}] Cannot show the end screen because its authored UI is incomplete.", this);
            return;
        }

        _titleText.text = title;
        _titleText.color = state == GameManager.GameState.Victory ? _victoryTextColor : _defeatTextColor;

        PlayerXP xp = FindAnyObjectByType<PlayerXP>();
        int level = xp != null ? xp.CurrentLevel : 1;

        _statsText.text =
            $"Time: {RunSessionStats.FormatElapsed()}\n" +
            $"Kills: {RunCombatStats.EnemiesEliminated}\n" +
            $"Level: {level}\n" +
            $"Bosses: {RunSessionStats.BossKills}";

        Transform overlay = _root.transform.Find("Overlay") ?? _root.transform.Find("Backdrop");
        if (overlay != null && overlay.TryGetComponent(out Image overlayImage))
            overlayImage.color = _overlayColor;

        if (_camera == null)
            _camera = FindAnyObjectByType<ThirdPersonCamera>();
        _camera?.SetLookBlockedByUi(true);

        _root.SetActive(true);
    }

    private bool TryWireFromHierarchy()
    {
        Transform runEndRoot = _root != null ? _root.transform : transform.Find("RunEndRoot");
        if (runEndRoot == null)
            return false;

        _root = runEndRoot.gameObject;
        Transform panel = runEndRoot.Find("Panel");
        if (_titleText == null) _titleText = panel != null ? HudUiWire.FindTmp(panel, "Title") : HudUiWire.FindTmp(runEndRoot, "Title");
        if (_statsText == null) _statsText = panel != null ? HudUiWire.FindTmp(panel, "Stats") : HudUiWire.FindTmp(runEndRoot, "Stats");
        if (_retryButton == null) _retryButton = panel != null ? HudUiWire.FindButton(panel, "RetryButton") : HudUiWire.FindButton(runEndRoot, "RetryButton");
        if (_mainMenuButton == null) _mainMenuButton = panel != null ? HudUiWire.FindButton(panel, "MainMenuButton") : HudUiWire.FindButton(runEndRoot, "MainMenuButton");

        if (_titleText == null || _statsText == null)
            return false;

        if (_retryButton != null)
        {
            _retryButton.onClick.RemoveListener(Retry);
            _retryButton.onClick.AddListener(Retry);
        }

        if (_mainMenuButton != null)
        {
            _mainMenuButton.onClick.RemoveListener(ReturnToMainMenu);
            _mainMenuButton.onClick.AddListener(ReturnToMainMenu);
        }

        return true;
    }

#if UNITY_EDITOR
    public void AuthorUi()
    {
        if (Application.isPlaying)
            throw new System.InvalidOperationException("Author run-end UI outside Play Mode.");

        bool styled = GameplayHudHierarchyBuilder.HasStyledRunEndFrame(transform);
        if (styled && TryWireFromHierarchy())
            return;

        Transform existing = transform.Find("RunEndRoot");
        if (existing != null)
            DestroyImmediate(existing.gameObject);

        _root = null;
        _titleText = null;
        _statsText = null;
        _retryButton = null;
        _mainMenuButton = null;

        GameplayHudHierarchyBuilder.BuildRunEndHierarchy(transform);
        if (!TryWireFromHierarchy())
            throw new System.InvalidOperationException("Failed to author run-end UI.");
    }
#endif

    private void Retry()
    {
        if (GameManager.Instance != null)
            GameManager.Instance.ResetTimeScaleForReload();
        else
            Time.timeScale = 1f;

        Scene scene = SceneManager.GetActiveScene();
        SceneManager.LoadScene(scene.buildIndex);
    }

    private void ReturnToMainMenu()
    {
        SceneNavigation.LoadTitle();
    }
}
