using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class WeaponUiIconCatalogTests
{
    private const string CatalogPath = "Assets/Resources/UI/WeaponUiIconCatalog.asset";

    [Test]
    public void Catalog_ResolvesLockedAndSelectedPerWeaponType()
    {
        WeaponUiIconCatalog catalog = AssetDatabase.LoadAssetAtPath<WeaponUiIconCatalog>(CatalogPath);
        Assert.That(catalog, Is.Not.Null);

        Assert.That(catalog.TryGet(WeaponType.AutomaticCannon, out Sprite locked, out Sprite selected), Is.True);
        Assert.That(locked, Is.Not.Null);
        Assert.That(selected, Is.Not.Null);
        Assert.That(locked, Is.Not.SameAs(selected));

        Assert.That(catalog.TryGet(WeaponType.Mortar, out locked, out selected), Is.True);
        Assert.That(locked.name, Does.Contain("morter_locked").IgnoreCase);
        Assert.That(selected.name, Does.Contain("morter_selected").IgnoreCase);
    }

    [Test]
    public void Resolve_UsesSelectedForEquippedAndLockedForSecondary()
    {
        WeaponUiIconCatalog catalog = AssetDatabase.LoadAssetAtPath<WeaponUiIconCatalog>(CatalogPath);
        WeaponUiIcons.SetCatalogForTests(catalog);

        WeaponData cannon = AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/AutomaticCannon.asset");
        Assert.That(cannon, Is.Not.Null);

        Sprite selected = WeaponUiIcons.Resolve(cannon, selected: true);
        Sprite locked = WeaponUiIcons.Resolve(cannon, selected: false);
        Assert.That(selected, Is.Not.Null);
        Assert.That(locked, Is.Not.Null);
        Assert.That(selected, Is.Not.SameAs(locked));

        WeaponUiIcons.SetCatalogForTests(null);
    }
}
