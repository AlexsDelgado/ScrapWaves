using UnityEngine;

/// <summary>
/// Estados de partida: juego activo, victoria (puerta de salida) o game over (vida 0).
/// Pausa con <see cref="Time.timeScale"/> y muestra el panel de fin de partida authored en el HUD.
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

    [SerializeField, Min(1), Tooltip("Obsoleto: la victoria es por puerta de salida. Se conserva solo para referencia.")]
    private int _bossKillsRequiredForVictory = 2;

    private GameState _state = GameState.Playing;
    private int _bossKills;

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
    }

    /// <summary>Victoria al interactuar con la puerta de salida.</summary>
    public void TriggerVictory()
    {
        if (_state != GameState.Playing)
            return;

        EnterEndState(GameState.Victory, "¡VICTORIA!");
    }

    private void EnterEndState(GameState endState, string message)
    {
        _state = endState;
        Time.timeScale = 0f;

        ReportRunToSaveSystem(endState == GameState.Victory);

        if (RunEndScreenUI.Instance == null)
        {
            Debug.LogError(
                $"[{nameof(GameManager)}] An authored {nameof(RunEndScreenUI)} is required in the gameplay HUD.",
                this);
            return;
        }

        RunEndScreenUI.Instance.Show(endState, message);
    }

    /// <summary>Reinicia escena o menú (Time.timeScale = 1 antes de cargar).</summary>
    public void ResetTimeScaleForReload()
    {
        Time.timeScale = 1f;
    }

    /// <summary>
    /// Acredita el progreso de la run terminada a la meta-progresión persistente (SaveManager).
    /// Fórmula de Scrap placeholder para balancear más adelante: tiempo sobrevivido + bosses +
    /// materiales sobrantes (los raros valen más, mismo criterio que XP común/rara del diseño).
    /// </summary>
    private static void ReportRunToSaveSystem(bool victory)
    {
        if (SaveManager.Instance == null)
            return;

        SaveManager.Instance.ReportRunEnded(
            victory,
            RunSessionStats.BossKills,
            RunCombatStats.EnemiesEliminated,
            RunSessionStats.ElapsedSeconds,
            CalculateScrapEarned());
    }

    private static int CalculateScrapEarned()
    {
        int scrap = Mathf.RoundToInt(RunSessionStats.ElapsedSeconds / 10f) + RunSessionStats.BossKills * 25;

        MaterialInventory inventory = FindAnyObjectByType<MaterialInventory>();
        if (inventory != null)
        {
            foreach (MaterialType type in (MaterialType[])System.Enum.GetValues(typeof(MaterialType)))
                scrap += inventory.GetAmount(type) * (IsRareMaterial(type) ? 5 : 1);
        }

        return scrap;
    }

    private static bool IsRareMaterial(MaterialType type) =>
        type == MaterialType.JellifiedFuel || type == MaterialType.PlasticExplosive || type == MaterialType.Wiring;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_bossKillsRequiredForVictory < 1)
            _bossKillsRequiredForVictory = 1;
    }
#endif
}
