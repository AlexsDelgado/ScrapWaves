using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class RunEndScreenUI : MonoBehaviour
{
    public static RunEndScreenUI Instance { get; private set; }

    [SerializeField] private Color _overlayColor = new(0f, 0f, 0f, 0.72f);
    [SerializeField] private Color _victoryTextColor = new(0.4f, 1f, 0.5f, 1f);
    [SerializeField] private Color _defeatTextColor = new Color(1f, 0.35f, 0.3f, 1f);

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
        if (TryWireFromHierarchy())
            return;
        if (_root != null)
            throw new System.InvalidOperationException("The existing run-end hierarchy is incomplete; repair its references before authoring.");
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
