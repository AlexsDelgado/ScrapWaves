using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dispara las entradas de un <see cref="DialogueSet"/> según lo que pasa en el run (overheat, jefes, baterías)
/// y según la guía por inactividad (sin kills, crafting station sin visitar). Muestra una entrada por vez en
/// <see cref="DialogueBoxUI"/>; el resto espera en cola por prioridad.
/// </summary>
[DisallowMultipleComponent]
public class DialogueDirector : MonoBehaviour
{
    [SerializeField] private DialogueSet _set;
    [SerializeField] private DialogueBoxUI _box;

    [Header("Sistemas (vacío = se buscan en la escena)")]
    [SerializeField] private WeaponManager _weaponManager;
    [SerializeField] private OverheatManager _overheatManager;
    [SerializeField] private BossManager _bossManager;
    [SerializeField] private CraftingStation _craftingStation;

    [Tooltip("Las guías por inactividad no interrumpen: solo suenan si no hay nada en pantalla ni en cola.")]
    [SerializeField, Min(0f)] private float _guideGapSeconds = 8f;
    [SerializeField] private bool _logTriggers;

    private struct Pending
    {
        public DialogueEntry Entry;
        public float ReadyAt;
    }

    /// <summary>Una entrada empieza a mostrarse (p. ej. GuideArrowController reacciona a ShowsCraftingGuide).</summary>
    public static event System.Action<DialogueEntry> EntryStarted;

    private readonly List<Pending> _queue = new();
    private readonly HashSet<DialogueEntry> _played = new();
    private readonly Dictionary<DialogueEntry, float> _lastPlayedAt = new();
    private readonly HashSet<DialogueSpeaker> _revealed = new();
    private readonly Dictionary<EnemyHealth, string> _knownBosses = new();
    private readonly List<EnemyHealth> _scratch = new();

    private LevelExitObjective _exitObjective;
    private MaterialInventory _inventory;
    private bool _runStarted;
    private bool _craftingVisited;
    private bool _materialsSpent;
    private float _runTime;
    private float _noKillTime;
    private float _lastDialogueEndedAt = float.NegativeInfinity;
    private int _overheatsStarted;
    private int _overheatsFinished;
    private int _lastKillCount;

    private void Awake()
    {
        if (_box == null) _box = GetComponentInChildren<DialogueBoxUI>(true);
        if (_weaponManager == null) _weaponManager = FindAnyObjectByType<WeaponManager>();
        if (_overheatManager == null) _overheatManager = FindAnyObjectByType<OverheatManager>();
        if (_bossManager == null) _bossManager = FindAnyObjectByType<BossManager>();
        if (_craftingStation == null) _craftingStation = FindAnyObjectByType<CraftingStation>();
    }

    private void OnEnable()
    {
        if (_overheatManager != null)
        {
            _overheatManager.OnOverheatStarted += HandleOverheatStarted;
            _overheatManager.OnOverheatFinished += HandleOverheatFinished;
        }
        if (_bossManager != null)
        {
            _bossManager.OnActiveBossesChanged += HandleBossesChanged;
            _bossManager.OnBossDefeated += HandleBossDefeated;
        }
        if (_craftingStation != null)
            _craftingStation.OnInteracted += HandleCraftingOpened;
        RunCombatStats.OnEnemiesEliminatedChanged += HandleKillsChanged;
        _lastKillCount = RunCombatStats.EnemiesEliminated;
        _inventory = MaterialInventory.Instance != null ? MaterialInventory.Instance : FindAnyObjectByType<MaterialInventory>();
        if (_inventory != null)
            _inventory.OnMaterialsSpent += HandleMaterialsSpent;
    }

    private void OnDisable()
    {
        if (_overheatManager != null)
        {
            _overheatManager.OnOverheatStarted -= HandleOverheatStarted;
            _overheatManager.OnOverheatFinished -= HandleOverheatFinished;
        }
        if (_bossManager != null)
        {
            _bossManager.OnActiveBossesChanged -= HandleBossesChanged;
            _bossManager.OnBossDefeated -= HandleBossDefeated;
        }
        if (_craftingStation != null)
            _craftingStation.OnInteracted -= HandleCraftingOpened;
        RunCombatStats.OnEnemiesEliminatedChanged -= HandleKillsChanged;
        if (_inventory != null)
            _inventory.OnMaterialsSpent -= HandleMaterialsSpent;
        _inventory = null;
        if (_exitObjective != null)
            _exitObjective.OnKeyProgressChanged -= HandleBatteryProgress;
        _exitObjective = null;
    }

    private void Update()
    {
        BindExitObjective();
        if (!IsGameRunning())
            return;

        if (!_runStarted)
        {
            if (_weaponManager == null || _weaponManager.GetEquippedWeapons().Count == 0)
                return;
            _runStarted = true;
            Fire(DialogueTrigger.RunStart);
        }

        float dt = Time.deltaTime;
        _runTime += dt;
        _noKillTime += dt;
        EvaluateGuides();
        PumpQueue();
    }

    /// <summary>Encola una entrada a mano (para triggers de escena o scripts puntuales).</summary>
    public void Play(DialogueEntry entry)
    {
        if (entry != null && CanPlay(entry))
            Enqueue(entry);
    }

    private bool IsGameRunning() =>
        (GameManager.Instance == null || GameManager.Instance.IsPlaying)
        && !GameplayPause.IsUiPaused && Time.timeScale > 0f;

    // LevelExitObjective se registra en su OnEnable, que puede correr después del nuestro.
    private void BindExitObjective()
    {
        if (_exitObjective != null || LevelExitObjective.Instance == null)
            return;
        _exitObjective = LevelExitObjective.Instance;
        _exitObjective.OnKeyProgressChanged += HandleBatteryProgress;
    }

    private void HandleOverheatStarted() => Fire(DialogueTrigger.OverheatStarted, ++_overheatsStarted);

    private void HandleOverheatFinished(OverheatEndReason reason)
    {
        if (reason != OverheatEndReason.Interrupted)
            Fire(DialogueTrigger.OverheatFinished, ++_overheatsFinished);
    }

    private void HandleBatteryProgress(int collected, int required) =>
        Fire(DialogueTrigger.BatteryCollected, collected);

    private void HandleCraftingOpened() => _craftingVisited = true;

    private void HandleMaterialsSpent() => _materialsSpent = true;

    private void HandleKillsChanged()
    {
        int kills = RunCombatStats.EnemiesEliminated;
        if (kills > _lastKillCount)
            _noKillTime = 0f;
        _lastKillCount = kills;
    }

    // BossManager saca al jefe muerto de ActiveBosses antes de avisar: lo que falta del caché es lo que murió.
    private void HandleBossDefeated()
    {
        _scratch.Clear();
        foreach (KeyValuePair<EnemyHealth, string> known in _knownBosses)
            if (!IsActiveBoss(known.Key))
                _scratch.Add(known.Key);
        foreach (EnemyHealth boss in _scratch)
        {
            Fire(DialogueTrigger.BossDefeated, 0, _knownBosses[boss]);
            _knownBosses.Remove(boss);
        }
    }

    private void HandleBossesChanged()
    {
        if (_bossManager == null)
            return;
        foreach (EnemyHealth boss in _bossManager.ActiveBosses)
        {
            if (boss == null || _knownBosses.ContainsKey(boss))
                continue;
            string bossName = boss.gameObject.name;
            _knownBosses.Add(boss, bossName);
            Fire(DialogueTrigger.BossSpawned, 0, bossName);
        }
        // Los despawns del final del overheat no son derrotas: se olvidan sin disparar nada.
        _scratch.Clear();
        foreach (EnemyHealth known in _knownBosses.Keys)
            if (!IsActiveBoss(known))
                _scratch.Add(known);
        foreach (EnemyHealth gone in _scratch)
            _knownBosses.Remove(gone);
    }

    private bool IsActiveBoss(EnemyHealth boss)
    {
        if (boss == null || _bossManager == null)
            return false;
        foreach (EnemyHealth active in _bossManager.ActiveBosses)
            if (active == boss)
                return true;
        return false;
    }

    private void Fire(DialogueTrigger trigger, int occurrence = 0, string bossName = null)
    {
        if (_set == null || (!_runStarted && trigger != DialogueTrigger.RunStart))
            return;
        foreach (DialogueEntry entry in _set.Entries)
        {
            if (entry == null || entry.Trigger != trigger || !CanPlay(entry))
                continue;
            bool countsOccurrences = trigger is DialogueTrigger.OverheatStarted or DialogueTrigger.OverheatFinished
                or DialogueTrigger.BatteryCollected;
            if (countsOccurrences && !entry.MatchesOccurrence(occurrence))
                continue;
            bool isBossTrigger = trigger is DialogueTrigger.BossSpawned or DialogueTrigger.BossDefeated;
            if (isBossTrigger && !entry.MatchesBoss(bossName))
                continue;
            if (_logTriggers)
                Debug.Log($"[{nameof(DialogueDirector)}] {trigger} ({occurrence}, {bossName}) → {entry.name}", this);
            Enqueue(entry);
        }
    }

    private void EvaluateGuides()
    {
        if (_set == null || _box == null || _box.IsShowing || _queue.Count > 0
            || _runTime - _lastDialogueEndedAt < _guideGapSeconds)
            return;
        foreach (DialogueEntry entry in _set.Entries)
        {
            if (entry == null || !entry.IsGuide || !CanPlay(entry))
                continue;
            bool due = entry.Trigger switch
            {
                DialogueTrigger.NoKillsFor => _noKillTime >= entry.Seconds,
                DialogueTrigger.CraftingNotVisitedFor => !_craftingVisited && _runTime >= entry.Seconds,
                DialogueTrigger.NoCraftingFor => !_materialsSpent && _runTime >= entry.Seconds,
                _ => false
            };
            if (!due)
                continue;
            if (entry.Trigger == DialogueTrigger.NoKillsFor)
                _noKillTime = 0f;
            Enqueue(entry);
            return;
        }
    }

    private bool CanPlay(DialogueEntry entry)
    {
        if (entry.OncePerRun && _played.Contains(entry))
            return false;
        if (_lastPlayedAt.TryGetValue(entry, out float last) && _runTime - last < entry.RepeatCooldown)
            return false;
        foreach (Pending p in _queue)
            if (p.Entry == entry)
                return false;
        return true;
    }

    private void Enqueue(DialogueEntry entry)
    {
        _played.Add(entry);
        _lastPlayedAt[entry] = _runTime;
        int index = _queue.FindIndex(p => p.Entry.Priority < entry.Priority);
        var pending = new Pending { Entry = entry, ReadyAt = _runTime + entry.Delay };
        if (index < 0) _queue.Add(pending);
        else _queue.Insert(index, pending);
    }

    private void PumpQueue()
    {
        if (_box == null || _box.IsShowing || _queue.Count == 0)
            return;
        int index = _queue.FindIndex(p => p.ReadyAt <= _runTime);
        if (index < 0)
            return;
        DialogueEntry entry = _queue[index].Entry;
        _queue.RemoveAt(index);

        if (entry.RevealsSpeaker && entry.Speaker != null)
            _revealed.Add(entry.Speaker);
        bool revealed = entry.Speaker != null && _revealed.Contains(entry.Speaker);
        _box.Play(entry, revealed, () => _lastDialogueEndedAt = _runTime);
        EntryStarted?.Invoke(entry);
    }
}
