using System.Collections.Generic;
using UnityEngine;

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

    public static string GetDisplayName(MaterialType type) => type switch
    {
        MaterialType.SheetMetal => "Sheet Metal",
        MaterialType.MetalPipe => "Metal Pipe",
        MaterialType.Gears => "Gears",
        MaterialType.JellifiedFuel => "Jellified Fuel",
        MaterialType.PlasticExplosive => "Plastic Explosive",
        MaterialType.Wiring => "Wiring",
        _ => type.ToString()
    };

    public static Color GetUiColor(MaterialType type) => type switch
    {
        MaterialType.SheetMetal => new Color(0.72f, 0.76f, 0.82f, 1f),
        MaterialType.MetalPipe => new Color(0.52f, 0.58f, 0.64f, 1f),
        MaterialType.Gears => new Color(0.86f, 0.64f, 0.22f, 1f),
        MaterialType.JellifiedFuel => new Color(0.28f, 0.82f, 0.42f, 1f),
        MaterialType.PlasticExplosive => new Color(0.94f, 0.34f, 0.24f, 1f),
        MaterialType.Wiring => new Color(0.95f, 0.78f, 0.18f, 1f),
        _ => new Color(0.55f, 0.58f, 0.62f, 1f)
    };
}
