using UnityEngine;

/// <summary>
/// Player level progression: accumulates XP toward the next level, applies a scaling curve, and fires events.
/// Receives XP through <see cref="AddExperience"/> (typically from <see cref="XPPickup.GrantExperience"/>).
/// </summary>
[DisallowMultipleComponent]
public class PlayerXP : MonoBehaviour
{
    [SerializeField, Min(1), Tooltip("Starting level (normally 1).")]
    private int _startingLevel = 1;

    [SerializeField, Min(0.01f), Tooltip("XP reference needed for the first segment (starting level to next). The curve multiplies this value.")]
    private float _baseXpToNextLevel = 80f;

    [SerializeField, Tooltip("X axis = current player level. Y axis = multiplier over Base XP to reach the next level (1 = no extra, 2 = double required XP).")]
    private AnimationCurve _scalingMultiplierByLevel = DefaultScalingCurve();

    [SerializeField, Tooltip("Log level-ups to the console.")]
    private bool _logLevelUps;

    [SerializeField]private int _currentLevel;
    [SerializeField]private int _xpTowardsNext;

    public int CurrentLevel => _currentLevel;
    public int XpTowardsNext => _xpTowardsNext;
    public int XpRequiredForCurrentLevel => GetXpRequiredCeiled();

    /// <summary>Progreso 0–1 hacia el siguiente nivel (para barras de UI).</summary>
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

    /// <summary>Disparado al subir de nivel; argumento = nuevo nivel alcanzado.</summary>
    public event System.Action<int> OnLevelUp;

    /// <summary>Disparado cuando cambia la XP del tramo actual (recogida, subida de nivel, etc.).</summary>
    public event System.Action OnXpProgressChanged;

    private void Awake()
    {
        _currentLevel = Mathf.Max(1, _startingLevel);
        _xpTowardsNext = 0;
    }

    private static AnimationCurve DefaultScalingCurve()
    {
        return new AnimationCurve(
            new Keyframe(1f, 1f),
            new Keyframe(10f, 1.6f),
            new Keyframe(25f, 2.8f),
            new Keyframe(50f, 5f));
    }

    /// <summary>Añade XP y procesa una o varias subidas de nivel según el umbral escalado.</summary>
    public void AddExperience(int amount)
    {
        if (amount <= 0)
            return;

        _xpTowardsNext += amount;

        while (_xpTowardsNext >= GetXpRequiredCeiled())
        {
            int required = GetXpRequiredCeiled();
            _xpTowardsNext -= required;
            _currentLevel++;

            if (_logLevelUps)
                Debug.Log($"Level {_currentLevel}", this);

            OnLevelUp?.Invoke(_currentLevel);
        }

        OnXpProgressChanged?.Invoke();
    }

    private int GetXpRequiredCeiled()
    {
        return Mathf.Max(1, Mathf.CeilToInt(GetXpRequiredFloat()));
    }

    private float GetXpRequiredFloat()
    {
        float mult = _scalingMultiplierByLevel.Evaluate(_currentLevel);
        if (mult <= 0f)
            mult = 0.01f;
        return _baseXpToNextLevel * mult;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_startingLevel < 1)
            _startingLevel = 1;
        if (_baseXpToNextLevel < 0.01f)
            _baseXpToNextLevel = 0.01f;
    }
#endif
}
