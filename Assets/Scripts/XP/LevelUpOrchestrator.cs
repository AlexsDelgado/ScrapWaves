using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(PlayerXP))]
[DisallowMultipleComponent]
public class LevelUpOrchestrator : MonoBehaviour
{
    [SerializeField] private PassiveItemLevelUpHandler _passiveHandler;
    [SerializeField] private WeaponLevelUpHandler _weaponHandler;
    [SerializeField] private PlayerStatsLevelUpHandler _statsHandler;
    [SerializeField] private LevelUpStatFeedback _statFeedback;

    private PlayerXP _playerXp;
    private readonly Queue<int> _pendingLevels = new();
    private bool _isProcessing;

    private void Awake()
    {
        _playerXp = GetComponent<PlayerXP>();
        if (_passiveHandler == null)
            _passiveHandler = GetComponent<PassiveItemLevelUpHandler>();
        if (_weaponHandler == null)
            _weaponHandler = GetComponent<WeaponLevelUpHandler>();
        if (_statsHandler == null)
            _statsHandler = GetComponent<PlayerStatsLevelUpHandler>();
        if (_statFeedback == null)
            _statFeedback = GetComponent<LevelUpStatFeedback>();
    }

    private void OnEnable()
    {
        if (_playerXp != null)
            _playerXp.OnLevelUp += HandleLevelUp;
    }

    private void OnDisable()
    {
        if (_playerXp != null)
            _playerXp.OnLevelUp -= HandleLevelUp;
    }

    private void HandleLevelUp(int newLevel)
    {
        _pendingLevels.Enqueue(newLevel);
        if (!_isProcessing)
            StartCoroutine(ProcessQueueCoroutine());
    }

    private IEnumerator ProcessQueueCoroutine()
    {
        _isProcessing = true;

        while (_pendingLevels.Count > 0)
        {
            int newLevel = _pendingLevels.Dequeue();
            yield return ProcessSingleLevelUp(newLevel);
        }

        _isProcessing = false;
    }

    private IEnumerator ProcessSingleLevelUp(int newLevel)
    {
        if (_passiveHandler != null)
            yield return _passiveHandler.PresentAndApplyCoroutine(newLevel);

        if (_statsHandler == null)
            yield break;

        List<StatUpgradeResult> upgrades = _statsHandler.ApplyLevelUpStats(newLevel);
        if (_statFeedback != null)
            _statFeedback.Show(upgrades);
    }
}
