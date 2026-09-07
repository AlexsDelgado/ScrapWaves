using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Lista curada de contenido de Objetivos/Tienda.
/// </summary>
[CreateAssetMenu(fileName = "UnlockCatalog", menuName = "ScrapWaves/Meta/Unlock Catalog")]
public class UnlockCatalog : ScriptableObject
{
    [SerializeField] private List<WeaponData> _weapons = new();
    [SerializeField] private List<PassiveItemData> _passiveItems = new();
    [SerializeField] private List<WeaponPathUnlockData> _weaponPathUnlocks = new();

    public IReadOnlyList<WeaponData> Weapons => _weapons;
    public IReadOnlyList<PassiveItemData> PassiveItems => _passiveItems;
    public IReadOnlyList<WeaponPathUnlockData> WeaponPathUnlocks => _weaponPathUnlocks;

    public void RegisterRuntimePathUnlock(WeaponPathUnlockData pathUnlock)
    {
        if (pathUnlock == null)
            return;
        for (int i = 0; i < _weaponPathUnlocks.Count; i++)
        {
            if (_weaponPathUnlocks[i] != null && _weaponPathUnlocks[i].UnlockId == pathUnlock.UnlockId)
                return;
        }

        _weaponPathUnlocks.Add(pathUnlock);
    }
}
