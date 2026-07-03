using UnityEngine;

/// <summary>
/// Player level progression: accumulates XP toward the next level and fires level-up events.
/// </summary>
[DisallowMultipleComponent]
public class PlayerXP : MonoBehaviour
{
    [SerializeField, Min(1), Tooltip("Starting level (normally 1).")]
    private int _startingLevel = 1;

    [SerializeField, Min(1), Tooltip("XP required to advance from level 1 to level 2.")]
    private int _firstLevelXpRequirement = 10;

    [SerializeField, Min(1f), Tooltip("Each level's XP cost is the previous cost multiplied by this value, rounded up.")]
    private float _experienceCostMultiplier = 1.2f;

    [SerializeField, Min(1), Tooltip("Maximum level that can be reached through XP.")]
    private int _levelCap = 36;

    [SerializeField, Tooltip("Log level-ups to the console.")]
    private bool _logLevelUps;

    [SerializeField] private int _currentLevel;
    [SerializeField] private int _xpTowardsNext;

    public int CurrentLevel => _currentLevel;
    public int XpTowardsNext => _xpTowardsNext;
    public int LevelCap => Mathf.Max(1, _levelCap);
    public bool IsAtLevelCap => _currentLevel >= LevelCap;
    public int XpRequiredForCurrentLevel => IsAtLevelCap ? 0 : GetXpRequiredCeiled();

    /// <summary>Progress from 0 to 1 toward the next level, for UI bars.</summary>
    public float NormalizedProgressToNextLevel
    {
        get
        {
            int need = XpRequiredForCurrentLevel;
            if (need <= 0)
                return 1f;

            return Mathf.Clamp01((float)_xpTowardsNext / need);
        }
    }

    /// <summary>Raised after leveling up; argument is the new level reached.</summary>
    public event System.Action<int> OnLevelUp;

    /// <summary>Raised when XP progress for the current level changes.</summary>
    public event System.Action OnXpProgressChanged;

    private void Awake()
    {
        _currentLevel = Mathf.Clamp(_startingLevel, 1, LevelCap);
        _xpTowardsNext = 0;
    }

    public void AddExperience(int amount)
    {
        if (amount <= 0 || IsAtLevelCap)
            return;

        _xpTowardsNext += amount;

        while (!IsAtLevelCap)
        {
            int required = GetXpRequiredCeiled();
            if (_xpTowardsNext < required)
                break;

            _xpTowardsNext -= required;
            _currentLevel++;

            if (_logLevelUps)
                Debug.Log($"Level {_currentLevel}", this);

            OnLevelUp?.Invoke(_currentLevel);
        }

        if (IsAtLevelCap)
            _xpTowardsNext = 0;

        OnXpProgressChanged?.Invoke();
    }

    private int GetXpRequiredCeiled()
    {
        int required = Mathf.Max(1, _firstLevelXpRequirement);
        int targetLevel = Mathf.Max(1, _currentLevel);
        float multiplier = Mathf.Max(1f, _experienceCostMultiplier);

        for (int level = 1; level < targetLevel; level++)
            required = Mathf.Max(1, Mathf.CeilToInt(required * multiplier));

        return required;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_startingLevel < 1)
            _startingLevel = 1;
        if (_firstLevelXpRequirement < 1)
            _firstLevelXpRequirement = 1;
        if (_experienceCostMultiplier < 1f)
            _experienceCostMultiplier = 1f;
        if (_levelCap < 1)
            _levelCap = 1;
        if (_startingLevel > _levelCap)
            _startingLevel = _levelCap;
    }
#endif
}
