using System.Collections.Generic;

public readonly struct MaterialCost
{
    public MaterialCost(MaterialType material, int amount)
    {
        Material = material;
        Amount = amount;
    }

    public MaterialType Material { get; }
    public int Amount { get; }
}

public static class WeaponCraftingCostCalculator
{
    private static readonly MaterialType[] AllMaterials =
    {
        MaterialType.SheetMetal,
        MaterialType.MetalPipe,
        MaterialType.Gears,
        MaterialType.JellifiedFuel,
        MaterialType.PlasticExplosive,
        MaterialType.Wiring
    };

    public static List<MaterialCost> GetUpgradeCost(
        MaterialUsageBalanceSO balance,
        WeaponType weaponType,
        WeaponUpgradePath path,
        int targetLevel)
    {
        var costs = new List<MaterialCost>();
        if (balance == null || targetLevel < 1 || targetLevel > 10)
            return costs;

        WeaponMaterialColumn column = MaterialUsageBalanceSO.GetColumnForWeapon(weaponType, path, targetLevel);
        for (int i = 0; i < AllMaterials.Length; i++)
        {
            MaterialType material = AllMaterials[i];
            MaterialRole role = balance.GetRole(column, material);
            if (role == MaterialRole.None)
                continue;

            int total = balance.GetTotalForRole(role, targetLevel);
            if (total > 0)
                costs.Add(new MaterialCost(material, total));
        }

        return costs;
    }

    public static List<MaterialCost> GetTinkeringSlotCost(int slotIndex, bool advancedRejected)
    {
        var costs = new List<MaterialCost>();
        if (slotIndex <= 1)
            return costs;

        int commonEach = slotIndex switch
        {
            2 => 5,
            3 => 15,
            _ => 0
        };

        if (commonEach <= 0)
            return costs;

        costs.Add(new MaterialCost(MaterialType.SheetMetal, commonEach));
        costs.Add(new MaterialCost(MaterialType.MetalPipe, commonEach));
        costs.Add(new MaterialCost(MaterialType.Gears, commonEach));
        return costs;
    }

    public static List<MaterialCost> GetAdvancedTinkeringCost(int slotIndex, bool rejectedOnce)
    {
        int rareEach = slotIndex switch
        {
            1 => rejectedOnce ? 8 : 5,
            2 => rejectedOnce ? 22 : 15,
            3 => rejectedOnce ? 45 : 30,
            _ => 0
        };

        var costs = new List<MaterialCost>();
        if (rareEach <= 0)
            return costs;

        costs.Add(new MaterialCost(MaterialType.JellifiedFuel, rareEach));
        costs.Add(new MaterialCost(MaterialType.PlasticExplosive, rareEach));
        costs.Add(new MaterialCost(MaterialType.Wiring, rareEach));
        return costs;
    }
}
