using System;
using UnityEngine;

[Serializable]
public sealed class WeaponMenuCopy
{
    public WeaponData Weapon;
    [TextArea(1, 3)] public string Summary;
    [TextArea(2, 5)] public string Description;
    [TextArea(2, 5)] public string PathADescription;
    [TextArea(2, 5)] public string PathBDescription;
}

[CreateAssetMenu(menuName = "ScrapWaves/UI/Run Menu Content")]
public sealed class RunMenuContent : ScriptableObject
{
    [Tooltip("Optional presentation copy only. Blank fields stay blank in the menus.")]
    public WeaponMenuCopy[] Weapons = Array.Empty<WeaponMenuCopy>();

    public WeaponMenuCopy Find(WeaponData weapon)
    {
        if (weapon == null) return null;
        foreach (WeaponMenuCopy entry in Weapons)
            if (entry != null && entry.Weapon == weapon) return entry;
        return null;
    }
}
