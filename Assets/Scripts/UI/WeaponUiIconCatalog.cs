using System;
using UnityEngine;

[CreateAssetMenu(fileName = "WeaponUiIconCatalog", menuName = "ScrapWaves/UI/Weapon UI Icon Catalog")]
public sealed class WeaponUiIconCatalog : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public WeaponType Type;
        public Sprite Locked;
        public Sprite Selected;
    }

    [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

    public Sprite Resolve(WeaponData data, bool selected)
    {
        if (data == null)
            return null;

        if (TryGet(data.WeaponType, out Sprite locked, out Sprite selectedSprite))
        {
            Sprite pick = selected ? selectedSprite : locked;
            if (pick != null)
                return pick;
        }

        return data.Icon;
    }

    public bool TryGet(WeaponType type, out Sprite locked, out Sprite selected)
    {
        locked = null;
        selected = null;
        if (_entries == null)
            return false;

        for (int i = 0; i < _entries.Length; i++)
        {
            if (_entries[i].Type != type)
                continue;
            locked = _entries[i].Locked;
            selected = _entries[i].Selected;
            return locked != null || selected != null;
        }

        return false;
    }
}

/// <summary>Loads the authored weapon icon catalog from Resources for HUD/crafting.</summary>
public static class WeaponUiIcons
{
    private const string ResourcesPath = "UI/WeaponUiIconCatalog";
    private static WeaponUiIconCatalog s_catalog;

    public static WeaponUiIconCatalog Catalog
    {
        get
        {
            if (s_catalog == null)
                s_catalog = Resources.Load<WeaponUiIconCatalog>(ResourcesPath);
            return s_catalog;
        }
    }

    public static Sprite Resolve(WeaponData data, bool selected)
    {
        WeaponUiIconCatalog catalog = Catalog;
        if (catalog != null)
            return catalog.Resolve(data, selected);
        return data != null ? data.Icon : null;
    }

#if UNITY_EDITOR
    public static void SetCatalogForTests(WeaponUiIconCatalog catalog) => s_catalog = catalog;
#endif
}
