using UnityEngine;

/// <summary>Momento del run en el que una entrada de diálogo puede sonar.</summary>
public enum DialogueTrigger
{
    /// <summary>Cuando el jugador ya eligió su primera arma y el juego corre.</summary>
    RunStart,
    /// <summary>Al empezar el overheat número Occurrence (0 = cualquiera).</summary>
    OverheatStarted,
    /// <summary>Al terminar el overheat número Occurrence (0 = cualquiera).</summary>
    OverheatFinished,
    /// <summary>Cuando aparece un jefe; BossFilter limita a uno (p. ej. "Stalker").</summary>
    BossSpawned,
    /// <summary>Cuando muere un jefe; BossFilter limita a uno.</summary>
    BossDefeated,
    /// <summary>Al recoger una batería (Occurrence = cuál; 0 = cualquiera).</summary>
    BatteryCollected,
    /// <summary>Guía: el jugador lleva Seconds sin matar enemigos.</summary>
    NoKillsFor,
    /// <summary>Guía: pasaron Seconds de run y todavía no abrió la crafting station.</summary>
    CraftingNotVisitedFor,
    /// <summary>Guía: pasaron Seconds de run y todavía no gastó materiales (no mejoró ni tinkereó armas).</summary>
    NoCraftingFor
}

/// <summary>
/// Una línea (o varias páginas) que dice un hablante cuando se cumple su disparador. Los game designers crean
/// una por mensaje y la suman a un <see cref="DialogueSet"/>.
/// </summary>
[CreateAssetMenu(menuName = "ScrapWaves/Dialogue/Entry", fileName = "Dialogue_")]
public class DialogueEntry : ScriptableObject
{
    [SerializeField] private DialogueSpeaker _speaker;
    [Tooltip("Cada página se escribe y se muestra por separado, en orden.")]
    [SerializeField, TextArea(2, 5)] private string[] _pages = { "" };

    [Header("Cuándo")]
    [SerializeField] private DialogueTrigger _trigger = DialogueTrigger.RunStart;
    [Tooltip("Para overheat y baterías: número de ocurrencia en el run (1 = la primera). 0 = cualquiera.")]
    [SerializeField, Min(0)] private int _occurrence;
    [Tooltip("Para jefes: parte del nombre del prefab (\"Stalker\"). Vacío = cualquier jefe.")]
    [SerializeField] private string _bossFilter;
    [Tooltip("Para disparadores de guía: segundos de juego sin que pase lo esperado.")]
    [SerializeField, Min(1f)] private float _seconds = 30f;
    [Tooltip("Espera antes de mostrarse una vez disparada.")]
    [SerializeField, Min(0f)] private float _delay;

    [Header("Cómo")]
    [Tooltip("Muestra el nombre y el retrato reales, y el hablante queda revelado el resto del run.")]
    [SerializeField] private bool _revealsSpeaker;
    [Tooltip("Si hay varias en cola, sale primero la de mayor prioridad.")]
    [SerializeField] private int _priority;
    [SerializeField] private bool _oncePerRun = true;
    [Tooltip("Una entrada de guía que se repite espera al menos esto antes de volver a sonar.")]
    [SerializeField, Min(0f)] private float _repeatCooldown = 60f;
    [Tooltip("Segundos en pantalla después de terminar de escribir. 0 = según el largo del texto.")]
    [SerializeField, Min(0f)] private float _holdSeconds;
    [Tooltip("Al empezar a sonar, la flecha guía apunta a la crafting station.")]
    [SerializeField] private bool _showsCraftingGuide;

    public DialogueSpeaker Speaker => _speaker;
    public string[] Pages => _pages;
    public DialogueTrigger Trigger => _trigger;
    public int Occurrence => _occurrence;
    public string BossFilter => _bossFilter;
    public float Seconds => _seconds;
    public float Delay => _delay;
    public bool RevealsSpeaker => _revealsSpeaker;
    public int Priority => _priority;
    public bool OncePerRun => _oncePerRun;
    public float RepeatCooldown => _repeatCooldown;
    public float HoldSeconds => _holdSeconds;
    public bool ShowsCraftingGuide => _showsCraftingGuide;

    public bool MatchesOccurrence(int count) => _occurrence <= 0 || _occurrence == count;

    public bool MatchesBoss(string bossName) =>
        string.IsNullOrWhiteSpace(_bossFilter)
        || (!string.IsNullOrEmpty(bossName) && bossName.IndexOf(_bossFilter.Trim(), System.StringComparison.OrdinalIgnoreCase) >= 0);

    public bool IsGuide => _trigger is DialogueTrigger.NoKillsFor or DialogueTrigger.CraftingNotVisitedFor
        or DialogueTrigger.NoCraftingFor;
}
