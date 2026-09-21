using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class AchievementUiIconCatalogTests
{
    private const string CatalogPath = "Assets/Resources/UI/AchievementUiIconCatalog.asset";

    [Test]
    public void Catalog_ResolvesKnownAchievementIds()
    {
        AchievementUiIconCatalog catalog = AssetDatabase.LoadAssetAtPath<AchievementUiIconCatalog>(CatalogPath);
        Assert.That(catalog, Is.Not.Null);

        Assert.That(catalog.TryGet("first_kill", out Sprite firstKill), Is.True);
        Assert.That(firstKill, Is.Not.Null);
        Assert.That(firstKill.name, Does.Contain("first_kill").IgnoreCase);

        Assert.That(catalog.TryGet("BossHunter", out Sprite boss), Is.True);
        Assert.That(boss, Is.Not.Null);
    }

    [Test]
    public void Resolve_PrefersDefinitionIconThenCatalog()
    {
        AchievementUiIconCatalog catalog = AssetDatabase.LoadAssetAtPath<AchievementUiIconCatalog>(CatalogPath);
        AchievementUiIcons.SetCatalogForTests(catalog);

        AchievementDefinition def = ScriptableObject.CreateInstance<AchievementDefinition>();
        def.name = "first_kill";
        try
        {
            Sprite fromCatalog = AchievementUiIcons.Resolve(def);
            Assert.That(fromCatalog, Is.Not.Null);

            Sprite overrideIcon = catalog.Resolve("BossHunter");
            def.SetIcon(overrideIcon);
            Assert.That(AchievementUiIcons.Resolve(def), Is.SameAs(overrideIcon));
        }
        finally
        {
            Object.DestroyImmediate(def);
            AchievementUiIcons.SetCatalogForTests(null);
        }
    }
}
