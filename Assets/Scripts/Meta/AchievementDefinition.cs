using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Definición data-driven de un logro/challenge meta (persistente entre runs).
/// </summary>
[CreateAssetMenu(fileName = "Achievement", menuName = "ScrapWaves/Meta/Achievement Definition")]
public class AchievementDefinition : ScriptableObject
{
    [SerializeField] private string _achievementId;
    [SerializeField] private string _displayName;
    [SerializeField, TextArea] private string _description;
    [SerializeField] private Sprite _icon;
    [SerializeField] private AchievementConditionType _conditionType;
    [SerializeField] private float _targetValue = 1f;

    [SerializeField, Tooltip("WeaponLevelReached / WeaponKillsTotal: WeaponId del arma.")]
    private string _weaponIdFilter;

    [SerializeField, Tooltip("Custom / RunChallenge: clave de progreso.")]
    private string _customKey;

    [SerializeField, Tooltip("Si true, el progreso persiste entre runs (Spec Cumulative=Yes).")]
    private bool _cumulative = true;

    [SerializeField, Min(0)] private int _scrapReward;

    [SerializeField, Tooltip("UnlockIds otorgados al completar (pasivos, WeaponPath_*, etc.).")]
    private List<string> _rewardUnlockIds = new();

    public string AchievementId => string.IsNullOrEmpty(_achievementId) ? name : _achievementId;
    public string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;
    public string Description => _description;
    public Sprite Icon => _icon;
    public AchievementConditionType ConditionType => _conditionType;
    public float TargetValue => _targetValue;
    public string WeaponIdFilter => _weaponIdFilter;
    public string CustomKey => _customKey;
    public bool Cumulative => _cumulative;
    public int ScrapReward => _scrapReward;
    public IReadOnlyList<string> RewardUnlockIds => _rewardUnlockIds;

    public static AchievementDefinition CreateRuntime(
        string id,
        string displayName,
        string description,
        AchievementConditionType conditionType,
        float targetValue,
        bool cumulative,
        string weaponIdFilter,
        string customKey,
        string rewardUnlockId,
        int scrapReward)
    {
        AchievementDefinition def = CreateInstance<AchievementDefinition>();
        def.name = id;
        def._achievementId = id;
        def._displayName = displayName;
        def._description = description;
        def._conditionType = conditionType;
        def._targetValue = targetValue;
        def._cumulative = cumulative;
        def._weaponIdFilter = weaponIdFilter ?? string.Empty;
        def._customKey = customKey ?? string.Empty;
        def._scrapReward = scrapReward;
        def._rewardUnlockIds = new List<string>();
        if (!string.IsNullOrEmpty(rewardUnlockId))
            def._rewardUnlockIds.Add(rewardUnlockId);
        return def;
    }
}
