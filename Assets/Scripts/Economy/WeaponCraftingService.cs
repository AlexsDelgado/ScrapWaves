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
    private readonly Dictionary<string, WeaponUpgradePath> _advancedOffers = new();
    // Owned by the run's player, never written to the meta-progression save.
    private readonly HashSet<string> _tinkerDiscards = new();
    private readonly List<WeaponData> _tinkerOffer = new(2);
    private List<MaterialCost> _reservedTinkerCost;
    private int _reservedTinkerSlot;
    private bool _tinkering;
    public bool HasPendingTinkeringChoice => _reservedTinkerCost != null;

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
        int slot = GetNextAdvancedWeaponIndex();
        bool rejected = weapon != null && _advancedRejected.TryGetValue(weapon.WeaponId, out bool value) && value;
        return WeaponCraftingCostCalculator.GetAdvancedTinkeringCost(slot, rejected);
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

    // Availability queries never roll or cache offers, spend materials, or mutate progression.
    public bool CanTinkerNewWeapon() => !HasPendingTinkeringChoice && !_tinkering
        && _weaponManager != null && _inventory != null
        && _weaponManager.CanAddWeapon() && BuildUnequippedWeapons().Count >= 2
        && _inventory.CanAfford(GetTinkeringCost(_weaponManager.GetEquippedWeapons().Count + 1));

    public CraftingActionKind? GetAvailableAction(WeaponInstance weapon)
    {
        if (weapon?.Data == null || _weaponManager == null || _inventory == null
            || !_weaponManager.TryGetEquippedWeapon(weapon.Data, out WeaponInstance equipped)
            || equipped != weapon || weapon.Level >= 10) return null;
        if (weapon.Level == 5 && weapon.SelectedPath == WeaponUpgradePath.None)
        {
            WeaponUpgradePath path;
            bool eligible = TryGetGuaranteedPath(weapon.Data, out path)
                || _advancedOffers.TryGetValue(weapon.Data.WeaponId, out path);
            eligible = eligible ? IsAdvancedPathUnlocked(weapon.Data, path)
                : IsAdvancedPathUnlocked(weapon.Data, WeaponUpgradePath.PathA);
            return eligible && _inventory.CanAfford(GetAdvancedTinkeringCost(weapon.Data))
                ? CraftingActionKind.AdvancedTinkering : null;
        }
        return _inventory.CanAfford(GetUpgradeCost(weapon.Data, weapon.SelectedPath, weapon.Level + 1))
            ? CraftingActionKind.UpgradeLevel : null;
    }

    public bool HasAnyAvailableCraftingAction()
    {
        if (_weaponManager == null) return false;
        foreach (IWeaponBehaviour weapon in _weaponManager.GetEquippedWeapons())
            if (GetAvailableAction(weapon?.Runtime).HasValue) return true;
        return CanTinkerNewWeapon();
    }

    public IReadOnlyList<WeaponData> GetTinkeringOffer()
    {
        // A paid offer survives UI disable/recreation. Never replace it while awaiting a choice.
        if (HasPendingTinkeringChoice || _tinkering) return _tinkerOffer.AsReadOnly();
        if (_weaponManager == null || !_weaponManager.CanAddWeapon())
            return System.Array.Empty<WeaponData>();

        List<WeaponData> candidates = BuildUnequippedWeapons();
        if (_tinkerOffer.Count == 2 && candidates.Contains(_tinkerOffer[0]) && candidates.Contains(_tinkerOffer[1]))
            return _tinkerOffer.AsReadOnly();

        _tinkerOffer.Clear();
        if (candidates.Count < 2) return System.Array.Empty<WeaponData>();
        while (_tinkerOffer.Count < 2)
        {
            int index = Random.Range(0, candidates.Count);
            _tinkerOffer.Add(candidates[index]);
            candidates.RemoveAt(index);
        }
        return _tinkerOffer.AsReadOnly();
    }

    public CraftingActionResult TryBeginTinkering()
    {
        if (_tinkering) return new CraftingActionResult(false, "Tinkering en curso.");
        if (HasPendingTinkeringChoice) return new CraftingActionResult(true, "Elegí una de las dos armas.");
        if (!CanTinkerNewWeapon()) return new CraftingActionResult(false, "Tinkering no disponible.");
        if (GetTinkeringOffer().Count != 2) return new CraftingActionResult(false, "No hay dos armas disponibles.");
        _tinkering = true;
        try
        {
            _reservedTinkerSlot = _weaponManager.GetEquippedWeapons().Count + 1;
            var cost = new List<MaterialCost>(GetTinkeringCost(_reservedTinkerSlot));
            _reservedTinkerCost = cost;
            if (!_inventory.TrySpend(cost))
            {
                _reservedTinkerCost = null;
                return new CraftingActionResult(false, "Materiales insuficientes.");
            }
            return new CraftingActionResult(true, "Elegí una de las dos armas.");
        }
        finally { _tinkering = false; }
    }

    public CraftingActionResult TryTinkerWeapon(WeaponData chosen)
    {
        if (_tinkering || _weaponManager == null || _inventory == null)
            return new CraftingActionResult(false, "Sistema de crafting no configurado.");

        if (!_weaponManager.CanAddWeapon() && !HasPendingTinkeringChoice)
            return new CraftingActionResult(false, "Slots de arma llenos.");

        List<WeaponData> candidates = BuildUnequippedWeapons();
        if (chosen == null || _tinkerOffer.Count != 2 || !_tinkerOffer.Contains(chosen))
            return new CraftingActionResult(false, "El arma no coincide con la oferta disponible.");

        // Keep the atomic entry point for non-UI callers, with the same cached-offer validation.
        if (!HasPendingTinkeringChoice)
        {
            if (!candidates.Contains(_tinkerOffer[0]) || !candidates.Contains(_tinkerOffer[1]))
                return new CraftingActionResult(false, "La oferta ya no está disponible.");
            CraftingActionResult begin = TryBeginTinkering();
            if (!begin.Success) return begin;
            candidates = BuildUnequippedWeapons();
        }

        _tinkering = true;
        try
        {
            WeaponData discarded = _tinkerOffer[0] == chosen ? _tinkerOffer[1] : _tinkerOffer[0];
            // External equipment/unlock changes can invalidate an interrupted transaction.
            // Refund exactly once and leave the cached offer intact; no discard without an award.
            if (!_weaponManager.CanAddWeapon() || _weaponManager.GetEquippedWeapons().Count + 1 != _reservedTinkerSlot
                || !candidates.Contains(chosen) || !candidates.Contains(discarded) || !_weaponManager.AddWeapon(chosen))
            {
                List<MaterialCost> costs = _reservedTinkerCost;
                _reservedTinkerCost = null;
                foreach (MaterialCost cost in costs) _inventory.Add(cost.Material, cost.Amount);
                return new CraftingActionResult(false, "No se pudo equipar el arma.");
            }
            _reservedTinkerCost = null;
            _tinkerDiscards.Add(discarded.WeaponId);
            _tinkerOffer.Clear();
            return new CraftingActionResult(true, $"Nueva arma: {chosen.DisplayName}.");
        }
        finally { _tinkering = false; }
    }

    public bool TryGetAdvancedOffer(WeaponData weapon, out WeaponUpgradePath path)
    {
        path = WeaponUpgradePath.None;
        if (weapon == null || _weaponManager == null
            || !_weaponManager.TryGetEquippedWeapon(weapon, out WeaponInstance instance)
            || instance.Level != 5 || instance.SelectedPath != WeaponUpgradePath.None)
            return false;

        // Keep the same offer when closing/reopening the station; reopening is not a reroll.
        if (TryGetGuaranteedPath(weapon, out path))
            _advancedOffers[weapon.WeaponId] = path;
        else if (!_advancedOffers.TryGetValue(weapon.WeaponId, out path))
        {
            path = IsAdvancedPathUnlocked(weapon, WeaponUpgradePath.PathB) && Random.Range(0, 2) == 1
                ? WeaponUpgradePath.PathB : WeaponUpgradePath.PathA;
            _advancedOffers[weapon.WeaponId] = path;
        }

        return IsAdvancedPathUnlocked(weapon, path);
    }

    public bool CanRejectAdvancedOffer(WeaponData weapon)
    {
        return weapon != null && !WasAdvancedRejected(weapon)
            && _advancedOffers.TryGetValue(weapon.WeaponId, out WeaponUpgradePath offered)
            && IsAdvancedPathUnlocked(weapon, GetGuaranteedAlternatePath(weapon, offered));
    }

    private static bool IsAdvancedPathUnlocked(WeaponData weapon, WeaponUpgradePath path)
    {
        return path == WeaponUpgradePath.PathA
            || (path == WeaponUpgradePath.PathB
                && (SaveManager.Instance == null || SaveManager.Instance.IsPathUnlocked(weapon, path)));
    }

    public CraftingActionResult TryAdvancedTinkering(WeaponData weapon, WeaponUpgradePath path, bool accept)
    {
        if (weapon == null || _weaponManager == null || _inventory == null)
            return new CraftingActionResult(false, "Sistema de crafting no configurado.");

        if (!_weaponManager.TryGetEquippedWeapon(weapon, out WeaponInstance instance))
            return new CraftingActionResult(false, "Arma no equipada.");

        if (instance.Level != 5 || instance.SelectedPath != WeaponUpgradePath.None)
            return new CraftingActionResult(false, "Solo disponible en nivel 5.");

        if (!_advancedOffers.TryGetValue(weapon.WeaponId, out WeaponUpgradePath offered) || offered != path
            || !IsAdvancedPathUnlocked(weapon, path))
            return new CraftingActionResult(false, "La ruta no coincide con la oferta disponible.");

        if (!accept && !CanRejectAdvancedOffer(weapon))
            return new CraftingActionResult(false, "No hay otra ruta disponible para este intento.");

        List<MaterialCost> costs = new(GetAdvancedTinkeringCost(weapon));
        if (!_inventory.TrySpend(costs))
            return new CraftingActionResult(false, "Materiales insuficientes para Advanced Tinkering.");

        if (accept)
        {
            _weaponManager.UpgradeWeapon(instance);
            _weaponManager.ApplyUpgradePath(instance, path);
            _advancedRejected.Remove(weapon.WeaponId);
            _guaranteedPath.Remove(weapon.WeaponId);
            _advancedOffers.Remove(weapon.WeaponId);
            return new CraftingActionResult(true, $"Path {path} aplicado. Nivel {instance.Level}.");
        }

        WeaponUpgradePath alternate = GetGuaranteedAlternatePath(weapon, path);
        _advancedRejected[weapon.WeaponId] = true;
        _guaranteedPath[weapon.WeaponId] = alternate;
        _advancedOffers[weapon.WeaponId] = alternate;
        return new CraftingActionResult(true, "Oferta rechazada. Costo de re-tinkering +50%.");
    }

    public List<WeaponData> BuildUnequippedWeapons()
    {
        var list = new List<WeaponData>();
        var seen = new HashSet<string>();
        if (_weaponManager == null) return list;
        for (int i = 0; i < _weaponPool.Count; i++)
        {
            WeaponData data = _weaponPool[i];
            if (data == null)
                continue;
            if (_weaponManager.TryGetEquippedWeapon(data, out _))
                continue;
            if (SaveManager.Instance != null && !SaveManager.Instance.IsUnlocked(data))
                continue;
            if (_tinkerDiscards.Contains(data.WeaponId) || !seen.Add(data.WeaponId))
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

    private int GetNextAdvancedWeaponIndex()
    {
        int advancedCount = 0;
        if (_weaponManager == null)
            return 1;
        IReadOnlyList<IWeaponBehaviour> equipped = _weaponManager.GetEquippedWeapons();
        for (int i = 0; i < equipped.Count; i++)
        {
            WeaponInstance runtime = equipped[i]?.Runtime;
            if (runtime != null && runtime.HasAdvancedPath
                && (runtime.SelectedPath == WeaponUpgradePath.PathA || runtime.SelectedPath == WeaponUpgradePath.PathB))
                advancedCount++;
        }

        return advancedCount + 1;
    }
}
