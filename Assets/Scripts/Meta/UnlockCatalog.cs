using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lista curada por diseño de todo el contenido que puede aparecer en la ventana de
/// Objetivos/Tienda. No hace falta listar TODO lo del juego: alcanza con lo que se quiera
/// mostrar ahí (incluyendo contenido ya desbloqueado desde el inicio, si se quiere mostrar
/// con un check).
/// </summary>
[CreateAssetMenu(fileName = "UnlockCatalog", menuName = "ScrapWaves/Meta/Unlock Catalog")]
public class UnlockCatalog : ScriptableObject
{
    [SerializeField] private List<WeaponData> _weapons = new();
    [SerializeField] private List<PassiveItemData> _passiveItems = new();

    public IReadOnlyList<WeaponData> Weapons => _weapons;
    public IReadOnlyList<PassiveItemData> PassiveItems => _passiveItems;
}
