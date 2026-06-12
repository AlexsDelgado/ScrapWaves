using UnityEngine;

/// <summary>
/// Legacy — reemplazado por <see cref="LevelUpOrchestrator"/>. Se mantiene el archivo por referencia de UI migrada a <see cref="LevelUpChoiceUI"/>.
/// </summary>
[RequireComponent(typeof(PlayerXP))]
[RequireComponent(typeof(PlayerStats))]
[DisallowMultipleComponent]
public class UpgradeManager : MonoBehaviour
{
    [SerializeField, HideInInspector] private bool _disabledLegacy = true;
}
