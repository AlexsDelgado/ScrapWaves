using System.Collections.Generic;

public static class MaterialCatalog
{
    private static readonly Dictionary<MaterialType, MaterialCategory> Categories = new()
    {
        { MaterialType.SheetMetal, MaterialCategory.Common },
        { MaterialType.MetalPipe, MaterialCategory.Common },
        { MaterialType.Gears, MaterialCategory.Common },
        { MaterialType.JellifiedFuel, MaterialCategory.Rare },
        { MaterialType.PlasticExplosive, MaterialCategory.Rare },
        { MaterialType.Wiring, MaterialCategory.Rare }
    };

    private static readonly Dictionary<string, MaterialType> NameLookup = new()
    {
        { "sheet metal", MaterialType.SheetMetal },
        { "metal pipes", MaterialType.MetalPipe },
        { "metal pipe", MaterialType.MetalPipe },
        { "gears", MaterialType.Gears },
        { "jellified fuel", MaterialType.JellifiedFuel },
        { "plastic explosives", MaterialType.PlasticExplosive },
        { "plastic explosive", MaterialType.PlasticExplosive },
        { "wiring", MaterialType.Wiring }
    };

    public static MaterialCategory GetCategory(MaterialType type) => Categories[type];

    public static bool TryParse(string rawName, out MaterialType type)
    {
        type = default;
        if (string.IsNullOrWhiteSpace(rawName))
            return false;
        return NameLookup.TryGetValue(rawName.Trim().ToLowerInvariant(), out type);
    }

    public static MaterialRole ParseRole(string cell)
    {
        if (string.IsNullOrWhiteSpace(cell))
            return MaterialRole.None;

        string trimmed = cell.Trim();
        return trimmed switch
        {
            "X" => MaterialRole.Principal,
            "x" => MaterialRole.Secondary,
            "y" => MaterialRole.Tertiary,
            "XX" => MaterialRole.PrincipalExtra,
            _ => MaterialRole.None
        };
    }

    public static int GetPickupXpValue(MaterialType type) =>
        GetCategory(type) == MaterialCategory.Rare ? 12 : 4;
}
