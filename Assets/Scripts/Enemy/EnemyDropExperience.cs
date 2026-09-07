using UnityEngine;

/// <summary>
/// Historicamente spawneaba orbes de XP al morir. Desactivado: la XP solo se obtiene
/// al recoger materiales (común +1 / rara +5 vía <see cref="MaterialCatalog"/>).
/// Se mantiene el componente para no romper prefabs existentes.
/// </summary>
[RequireComponent(typeof(EnemyHealth))]
public class EnemyDropExperience : MonoBehaviour
{
#pragma warning disable CS0414
    [SerializeField, Tooltip("Legacy: ya no se usa. La XP viene de materials al pickup.")]
    private XPPool _xpPoolOverride;

    [SerializeField, Min(0), Tooltip("Legacy: ya no se spawnean orbes al matar.")]
    private int _experienceAmount = 8;
#pragma warning restore CS0414
}
