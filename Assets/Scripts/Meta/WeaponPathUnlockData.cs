using UnityEngine;

/// <summary>
/// Unlockable de Path B de un arma (challenge → tienda → advanced tinkering).
/// </summary>
[CreateAssetMenu(fileName = "WeaponPathUnlock", menuName = "ScrapWaves/Meta/Weapon Path Unlock")]
public class WeaponPathUnlockData : ScriptableObject, IUnlockable
{
    [SerializeField] private WeaponData _weapon;
    [SerializeField] private WeaponUpgradePath _path = WeaponUpgradePath.PathB;
    [SerializeField] private string _unlockId;
    [SerializeField] private string _displayName;
    [SerializeField] private bool _unlockedFromStart;
    [SerializeField] private UnlockRequirement _requirement = new();

    public WeaponData Weapon => _weapon;
    public WeaponUpgradePath Path => _path;
    public string UnlockId => string.IsNullOrEmpty(_unlockId)
        ? (_weapon != null ? $"WeaponPath_{_weapon.WeaponId}_{_path}" : name)
        : _unlockId;
    public string DisplayName => string.IsNullOrEmpty(_displayName)
        ? (_weapon != null ? $"{_weapon.DisplayName} — {_path}" : name)
        : _displayName;
    public bool UnlockedFromStart => _unlockedFromStart;
    public UnlockRequirement Requirement => _requirement;

    public static WeaponPathUnlockData CreateRuntime(WeaponData weapon, WeaponUpgradePath path, int scrapPrice = 50)
    {
        if (weapon == null)
            return null;

        WeaponPathUnlockData data = CreateInstance<WeaponPathUnlockData>();
        data.name = BuildUnlockId(weapon, path);
        data._weapon = weapon;
        data._path = path;
        data._unlockId = BuildUnlockId(weapon, path);
        data._displayName = $"{weapon.DisplayName} — {path}";
        data._unlockedFromStart = false;
        data._requirement = new UnlockRequirement { ScrapPrice = scrapPrice };
        return data;
    }

    public static string BuildUnlockId(WeaponData weapon, WeaponUpgradePath path)
    {
        if (weapon == null)
            return null;
        return $"WeaponPath_{weapon.WeaponId}_{path}";
    }
}
