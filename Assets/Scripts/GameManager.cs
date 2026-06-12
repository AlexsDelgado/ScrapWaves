using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Estados de partida: juego activo, victoria (bosses requeridos derrotados) o game over (vida 0).
/// Pausa con <see cref="Time.timeScale"/> y muestra un panel simple en runtime.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-20)]
public class GameManager : MonoBehaviour
{
    public enum GameState
    {
        Playing,
        Victory,
        GameOver
    }

    public static GameManager Instance { get; private set; }

    [SerializeField, Tooltip("Vacío = FindAnyObjectByType.")]
    private PlayerHealth _playerHealth;

    [SerializeField, Tooltip("Cuenta bajas de boss vía evento del BossManager.")]
    private BossManager _bossManager;

    [SerializeField, Min(1), Tooltip("Cuántos bosses hay que derrotar (en total, en cualquier Overheat) para ganar.")]
    private int _bossKillsRequiredForVictory = 2;

    [SerializeField] private Color _overlayColor = new Color(0f, 0f, 0f, 0.72f);
    [SerializeField] private Color _victoryTextColor = new Color(0.4f, 1f, 0.5f, 1f);
    [SerializeField] private Color _defeatTextColor = new Color(1f, 0.35f, 0.3f, 1f);

    private GameState _state = GameState.Playing;
    private int _bossKills;
    private TextMeshProUGUI _statusText;
    private Canvas _endCanvas;

    public GameState State => _state;
    public bool IsPlaying => _state == GameState.Playing;
    public int BossKills => _bossKills;

    private void Awake()
    {
        if (_playerHealth == null)
            _playerHealth = FindAnyObjectByType<PlayerHealth>();
        if (_bossManager == null)
            _bossManager = FindAnyObjectByType<BossManager>();

        RunSessionStats.Reset();
    }

    private void OnEnable()
    {
        Instance = this;

        if (_playerHealth != null)
            _playerHealth.OnPlayerDied += OnPlayerDied;

        if (_bossManager != null)
            _bossManager.OnBossDefeated += OnBossDefeated;
    }

    private void OnDisable()
    {
        if (_playerHealth != null)
            _playerHealth.OnPlayerDied -= OnPlayerDied;

        if (_bossManager != null)
            _bossManager.OnBossDefeated -= OnBossDefeated;

        if (Instance == this)
            Instance = null;
    }

    private void OnPlayerDied()
    {
        if (_state != GameState.Playing)
            return;

        EnterEndState(GameState.GameOver, "GAME OVER");
    }

    private void OnBossDefeated()
    {
        if (_state != GameState.Playing)
            return;

        _bossKills++;
        RunSessionStats.RegisterBossKill();
        if (_bossKills >= _bossKillsRequiredForVictory)
            EnterEndState(GameState.Victory, "¡VICTORIA!");
    }

    private void EnterEndState(GameState endState, string message)
    {
        _state = endState;
        Time.timeScale = 0f;

        if (RunEndScreenUI.Instance != null)
        {
            RunEndScreenUI.Instance.Show(endState, message);
            return;
        }

        BuildEndScreenIfNeeded();
        if (_statusText != null)
        {
            _statusText.text = message;
            _statusText.color = endState == GameState.Victory ? _victoryTextColor : _defeatTextColor;
        }

        if (_endCanvas != null)
            _endCanvas.gameObject.SetActive(true);
    }

    /// <summary>Reinicia escena o menú (Time.timeScale = 1 antes de cargar).</summary>
    public void ResetTimeScaleForReload()
    {
        Time.timeScale = 1f;
    }

    private void BuildEndScreenIfNeeded()
    {
        if (_endCanvas != null)
            return;

        var root = new GameObject("EndGame_UI");
        root.transform.SetParent(transform, false);
        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0)
            root.layer = uiLayer;

        _endCanvas = root.AddComponent<Canvas>();
        _endCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _endCanvas.sortingOrder = 2000;

        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        root.AddComponent<GraphicRaycaster>();

        var bgGo = new GameObject("Dim");
        bgGo.transform.SetParent(root.transform, false);
        var bgRt = bgGo.AddComponent<RectTransform>();
        bgRt.anchorMin = Vector2.zero;
        bgRt.anchorMax = Vector2.one;
        bgRt.offsetMin = Vector2.zero;
        bgRt.offsetMax = Vector2.zero;
        var bg = bgGo.AddComponent<Image>();
        bg.color = _overlayColor;
        bg.raycastTarget = true;

        var textGo = new GameObject("Message");
        textGo.transform.SetParent(root.transform, false);
        var textRt = textGo.AddComponent<RectTransform>();
        textRt.anchorMin = new Vector2(0.5f, 0.5f);
        textRt.anchorMax = new Vector2(0.5f, 0.5f);
        textRt.pivot = new Vector2(0.5f, 0.5f);
        textRt.sizeDelta = new Vector2(900f, 200f);

        _statusText = textGo.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(_statusText);
        _statusText.fontSize = 64f;
        _statusText.fontStyle = FontStyles.Bold;
        _statusText.alignment = TextAlignmentOptions.Center;
        _statusText.text = "";
        _statusText.raycastTarget = false;

        root.SetActive(false);
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_bossKillsRequiredForVictory < 1)
            _bossKillsRequiredForVictory = 1;
    }
#endif
}
