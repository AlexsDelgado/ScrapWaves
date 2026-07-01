using UnityEngine;

/// <summary>
/// Data-driven upgrade definition. Create assets with: Create > Scrap Waves > Upgrade.
/// </summary>
[CreateAssetMenu(fileName = "Upgrade", menuName = "Scrap Waves/Upgrade", order = 0)]
public class Upgrade : ScriptableObject
{
    [SerializeField, Tooltip("Texto en la UI de elección.")]
    private string _displayName;

    [TextArea(2, 4)]
    [SerializeField, Tooltip("Opcional, para tooltips o UI futura.")]
    private string _description;

    [SerializeField]
    private StatType _targetStat;

    [SerializeField, Tooltip("Daño / vida máx.: suma entera (se redondea). Fire rate: reduce segundos del intervalo (ej. 0.03). Move speed: suma unidades/s.")]
    private float _value;

    public string DisplayName => string.IsNullOrEmpty(_displayName) ? name : _displayName;
    public string Description => _description;
    public StatType TargetStat => _targetStat;
    public float Value => _value;
}
