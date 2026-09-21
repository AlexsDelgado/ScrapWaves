using System;
using UnityEngine;

[CreateAssetMenu(fileName = "AchievementUiIconCatalog", menuName = "ScrapWaves/UI/Achievement UI Icon Catalog")]
public sealed class AchievementUiIconCatalog : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public string AchievementId;
        public Sprite Icon;
    }

    [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

    public Sprite Resolve(string achievementId)
    {
        if (string.IsNullOrEmpty(achievementId) || _entries == null)
            return null;

        for (int i = 0; i < _entries.Length; i++)
        {
            if (!string.Equals(_entries[i].AchievementId, achievementId, StringComparison.Ordinal))
                continue;
            return _entries[i].Icon;
        }

        return null;
    }

    public bool TryGet(string achievementId, out Sprite icon)
    {
        icon = Resolve(achievementId);
        return icon != null;
    }
}

/// <summary>Loads authored achievement icons from Resources and applies them to definitions.</summary>
public static class AchievementUiIcons
{
    private const string ResourcesPath = "UI/AchievementUiIconCatalog";
    private static AchievementUiIconCatalog s_catalog;

    public static AchievementUiIconCatalog Catalog
    {
        get
        {
            if (s_catalog == null)
                s_catalog = Resources.Load<AchievementUiIconCatalog>(ResourcesPath);
            return s_catalog;
        }
    }

    public static Sprite Resolve(string achievementId)
    {
        AchievementUiIconCatalog catalog = Catalog;
        return catalog != null ? catalog.Resolve(achievementId) : null;
    }

    public static Sprite Resolve(AchievementDefinition definition)
    {
        if (definition == null)
            return null;
        if (definition.Icon != null)
            return definition.Icon;
        return Resolve(definition.AchievementId);
    }

    public static void ApplyTo(AchievementDefinition definition)
    {
        if (definition == null || definition.Icon != null)
            return;

        Sprite icon = Resolve(definition.AchievementId);
        if (icon != null)
            definition.SetIcon(icon);
    }

    public static void ApplyToAll(System.Collections.Generic.IReadOnlyList<AchievementDefinition> definitions)
    {
        if (definitions == null)
            return;
        for (int i = 0; i < definitions.Count; i++)
            ApplyTo(definitions[i]);
    }

#if UNITY_EDITOR
    public static void SetCatalogForTests(AchievementUiIconCatalog catalog) => s_catalog = catalog;
#endif
}
