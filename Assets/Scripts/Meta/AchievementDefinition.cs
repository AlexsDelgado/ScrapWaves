using UnityEngine;

/// <summary>
/// Definición data-driven de un logro meta (persistente entre runs). No confundir con
/// objetivos dentro de una run (LevelExitObjective, Overheat), que son transitorios.
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

    [SerializeField, Tooltip("Solo para WeaponLevelReached: WeaponId del arma requerida.")]
    private string _weaponIdFilter;

    [SerializeField, Tooltip("Solo para Custom: clave que reporta el sistema que dispara este logro (SaveManager.ReportCustomProgress).")]
    private string _customKey;

    [SerializeField, Min(0), Tooltip("Scrap otorgado automáticamente al completar el logro (además de cualquier desbloqueo que dependa de él).")]
    private int _scrapReward;

    public string AchievementId => string.IsNullOrEmpty(_achievementId) ? name : _achievementId;
    public string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;
    public string Description => _description;
    public Sprite Icon => _icon;
    public AchievementConditionType ConditionType => _conditionType;
    public float TargetValue => _targetValue;
    public string WeaponIdFilter => _weaponIdFilter;
    public string CustomKey => _customKey;
    public int ScrapReward => _scrapReward;
}
