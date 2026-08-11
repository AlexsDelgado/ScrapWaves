using System.Collections.Generic;
using UnityEngine;

public enum CraftingActionKind
{
    UpgradeLevel,
    TinkerNewWeapon,
    AdvancedTinkering
}

public readonly struct CraftingActionResult
{
    public CraftingActionResult(bool success, string message)
    {
        Success = success;
        Message = message;
    }

    public bool Success { get; }
    public string Message { get; }
}

[DisallowMultipleComponent]
public class WeaponCraftingService : MonoBehaviour
{
    [SerializeField] private MaterialUsageBalanceSO _materialBalance;
    [SerializeField] private MaterialInventory _inventory;
    [SerializeField] private WeaponManager _weaponManager;
    [SerializeField] private List<WeaponData> _weaponPool = new();

    private readonly Dictionary<string, bool> _advancedRejected = new();
    private readonly Dictionary<string, WeaponUpgradePath> _guaranteedPath = new();

    private void Awake()
    {
        if (_inventory == null)
            _inventory = GetComponent<MaterialInventory>();
        if (_weaponManager == null)
            _weaponManager = GetComponent<WeaponManager>();

        if (_materialBalance == null)
            _materialBalance = EconomyBootstrap.RuntimeMaterialBalance;
    }

    public void SetMaterialBalance(MaterialUsageBalanceSO balance) => _materialBalance = balance;

    public IReadOnlyList<MaterialCost> GetUpgradeCost(WeaponData weapon, WeaponUpgradePath path, int targetLevel)
    {
        if (weapon == null || _materialBalance == null)
            return new List<MaterialCost>();

        return WeaponCraftingCostCalculator.GetUpgradeCost(_materialBalance, weapon.WeaponType, path, targetLevel);
    }

    public IReadOnlyList<MaterialCost> GetTinkeringCost(int targetSlotIndex)
    {
        return WeaponCraftingCostCalculator.GetTinkeringSlotCost(targetSlotIndex, false);
    }

    public IReadOnlyList<MaterialCost> GetAdvancedTinkeringCost(WeaponData weapon)
    {
        int slot = GetWeaponSlotIndex(weapon);
        bool rejected = weapon != null && _advancedRejected.TryGetValue(weapon.WeaponId, out bool value) && value;
        return WeaponCraftingCostCalculator.GetAdvancedTinkeringCost(Mathf.Max(1, slot), rejected);
    }

    public CraftingActionResult TryUpgradeWeapon(WeaponData weapon, int targetLevel)
    {
        if (weapon == null || _weaponManager == null || _inventory == null)
            return new CraftingActionResult(false, "Sistema de crafting no configurado.");

        if (!_weaponManager.TryGetEquippedWeapon(weapon, out WeaponInstance instance))
            return new CraftingActionResult(false, "Arma no equipada.");

        if (targetLevel <= instance.Level || targetLevel > 10)
            return new CraftingActionResult(false, "Nivel inválido.");

        if (targetLevel == 6 && instance.Level == 5 && instance.SelectedPath == WeaponUpgradePath.None)
            return new CraftingActionResult(false, "Requiere Advanced Tinkering.");

        List<MaterialCost> costs = new(GetUpgradeCost(weapon, instance.SelectedPath, targetLevel));
        if (!_inventory.TrySpend(costs))
            return new CraftingActionResult(false, "Materiales insuficientes.");

        while (instance.Level < targetLevel)
            _weaponManager.UpgradeWeapon(instance);

        return new CraftingActionResult(true, $"{weapon.DisplayName} nivel {instance.Level}.");
    }

    public CraftingActionResult TryTinkerRandomWeapon()
    {
        if (_weaponManager == null || _inventory == null)
            return new CraftingActionResult(false, "Sistema de crafting no configurado.");

        if (!_weaponManager.CanAddWeapon())
            return new CraftingActionResult(false, "Slots de arma llenos.");

        int nextSlot = _weaponManager.GetEquippedWeapons().Count + 1;
        List<MaterialCost> costs = new(GetTinkeringCost(nextSlot));
        if (!_inventory.TrySpend(costs))
            return new CraftingActionResult(false, "Materiales insuficientes para Tinkering.");

        List<WeaponData> candidates = BuildUnequippedWeapons();
        if (candidates.Count == 0)
            return new CraftingActionResult(false, "No quedan armas por craftear.");

        WeaponData chosen = candidates[Random.Range(0, candidates.Count)];
        _weaponManager.AddWeapon(chosen);
        return new CraftingActionResult(true, $"Nueva arma: {chosen.DisplayName}.");
    }

    public CraftingActionResult TryAdvancedTinkering(WeaponData weapon, WeaponUpgradePath path, bool accept)
    {
        if (weapon == null || _weaponManager == null || _inventory == null)
            return new CraftingActionResult(false, "Sistema de crafting no configurado.");

        if (!_weaponManager.TryGetEquippedWeapon(weapon, out WeaponInstance instance))
            return new CraftingActionResult(false, "Arma no equipada.");

        if (instance.Level != 5)
            return new CraftingActionResult(false, "Solo disponible en nivel 5.");

        List<MaterialCost> costs = new(GetAdvancedTinkeringCost(weapon));
        if (!_inventory.TrySpend(costs))
            return new CraftingActionResult(false, "Materiales insuficientes para Advanced Tinkering.");

        if (accept)
        {
            _weaponManager.UpgradeWeapon(instance);
            _weaponManager.ApplyUpgradePath(instance, path);
            _advancedRejected.Remove(weapon.WeaponId);
            _guaranteedPath.Remove(weapon.WeaponId);
            return new CraftingActionResult(true, $"Path {path} aplicado. Nivel {instance.Level}.");
        }

        _advancedRejected[weapon.WeaponId] = true;
        _guaranteedPath[weapon.WeaponId] = GetGuaranteedAlternatePath(weapon, path);
        return new CraftingActionResult(true, "Oferta rechazada. Costo de re-tinkering +50%.");
    }

    public List<WeaponData> BuildUnequippedWeapons()
    {
        var list = new List<WeaponData>();
        for (int i = 0; i < _weaponPool.Count; i++)
        {
            WeaponData data = _weaponPool[i];
            if (data == null)
                continue;
            if (_weaponManager.TryGetEquippedWeapon(data, out _))
                continue;
            if (SaveManager.Instance != null && !SaveManager.Instance.IsUnlocked(data))
                continue;
            list.Add(data);
        }

        return list;
    }

    public WeaponUpgradePath GetGuaranteedAlternatePath(WeaponData weapon, WeaponUpgradePath offered)
    {
        return offered == WeaponUpgradePath.PathA ? WeaponUpgradePath.PathB : WeaponUpgradePath.PathA;
    }

    public bool TryGetGuaranteedPath(WeaponData weapon, out WeaponUpgradePath path)
    {
        path = WeaponUpgradePath.None;
        if (weapon == null)
            return false;
        return _guaranteedPath.TryGetValue(weapon.WeaponId, out path) && path != WeaponUpgradePath.None;
    }

    public bool WasAdvancedRejected(WeaponData weapon) =>
        weapon != null && _advancedRejected.TryGetValue(weapon.WeaponId, out bool rejected) && rejected;

    private int GetWeaponSlotIndex(WeaponData weapon)
    {
        IReadOnlyList<IWeaponBehaviour> equipped = _weaponManager.GetEquippedWeapons();
        for (int i = 0; i < equipped.Count; i++)
        {
            if (equipped[i]?.Runtime?.Data == weapon)
                return i + 1;
        }

        return equipped.Count + 1;
    }
}
