#if UNITY_EDITOR
using System;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Builds the authored, shared assets used by the pooled combat-text runtime.
/// The generated view deliberately contains no layout, raycaster, particle,
/// or per-instance material components.
/// </summary>
public static class CombatTextAssetBuilder
{
    private const string ProfilePath = "Assets/ScriptableObjects/GameFeel/CombatTextProfile.asset";
    private const string PrefabPath = "Assets/GameFeel/Prefabs/CombatText/CombatTextView.prefab";
    private const string PlayerPrefabPath = "Assets/Prefabs/player.prefab";
    private const string LiberationSansPath =
        "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

    [MenuItem("Tools/ScrapWaves/Game Feel/Rebuild Combat Text Assets")]
    public static void BuildFromMenu()
    {
        BuildAll();
        Debug.Log("Combat-text profile, pooled view prefab, and player reference rebuilt.");
    }

    public static void BuildBatch()
    {
        BuildAll();
    }

    private static void BuildAll()
    {
        EnsureFolders();
        AssetDatabase.Refresh();

        TMP_FontAsset font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(LiberationSansPath);
        Material sharedFontMaterial = FindSharedFontMaterial(font);
        if (font == null)
            Debug.LogWarning($"Combat text could not find the shared TMP font at '{LiberationSansPath}'.");

        CombatTextProfile profile = EnsureProfile();
        CombatTextView viewPrefab = BuildViewPrefab(font, sharedFontMaterial);
        ConfigureProfile(profile, viewPrefab, font, sharedFontMaterial);
        AssignPlayerProfile(profile);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void EnsureFolders()
    {
        EnsureFolder("Assets", "ScriptableObjects");
        EnsureFolder("Assets/ScriptableObjects", "GameFeel");
        EnsureFolder("Assets", "GameFeel");
        EnsureFolder("Assets/GameFeel", "Prefabs");
        EnsureFolder("Assets/GameFeel/Prefabs", "CombatText");
    }

    private static void EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path))
            AssetDatabase.CreateFolder(parent, child);
    }

    private static CombatTextProfile EnsureProfile()
    {
        CombatTextProfile profile = AssetDatabase.LoadAssetAtPath<CombatTextProfile>(ProfilePath);
        if (profile != null)
            return profile;

        profile = ScriptableObject.CreateInstance<CombatTextProfile>();
        profile.name = "CombatTextProfile";
        profile.Sanitize();
        AssetDatabase.CreateAsset(profile, ProfilePath);
        return profile;
    }

    private static void ConfigureProfile(
        CombatTextProfile profile,
        CombatTextView viewPrefab,
        TMP_FontAsset font,
        Material sharedFontMaterial)
    {
        profile.ViewPrefab = viewPrefab;
        profile.FontAsset = font;
        profile.DefaultFontMaterial = sharedFontMaterial;
        profile.Sanitize();
        EditorUtility.SetDirty(profile);
    }

    private static CombatTextView BuildViewPrefab(TMP_FontAsset font, Material sharedFontMaterial)
    {
        GameObject rootObject = new(
            "CombatTextView",
            typeof(RectTransform),
            typeof(CanvasGroup),
            typeof(CombatTextView));

        try
        {
            RectTransform root = (RectTransform)rootObject.transform;
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(190f, 72f);

            CanvasGroup canvasGroup = rootObject.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            Image backing = CreateImage(
                root,
                "Backing",
                new Vector2(116f, 42f),
                Vector2.zero,
                0f,
                new Color(0.025f, 0.018f, 0.012f, 0.54f));

            TMP_Text value = CreateValueText(root, font, sharedFontMaterial);
            Image burn = CreateImage(
                root,
                "BurnAccent",
                new Vector2(9f, 19f),
                new Vector2(-62f, 0f),
                24f,
                new Color(1f, 0.30f, 0.05f, 0.92f));
            Image jellified = CreateImage(
                root,
                "JellifiedAccent",
                new Vector2(16f, 9f),
                new Vector2(-62f, -10f),
                0f,
                new Color(0.34f, 0.92f, 0.24f, 0.92f));
            Image critical = CreateImage(
                root,
                "CriticalAccent",
                new Vector2(34f, 4f),
                new Vector2(0f, 27f),
                0f,
                new Color(1f, 0.42f, 0.04f, 0.92f));
            Image weakPoint = CreateImage(
                root,
                "WeakPointAccent",
                new Vector2(10f, 10f),
                new Vector2(64f, 0f),
                45f,
                new Color(0.35f, 0.95f, 1f, 0.92f));
            Image kill = CreateImage(
                root,
                "KillAccent",
                new Vector2(16f, 6f),
                new Vector2(66f, -16f),
                -28f,
                new Color(1f, 0.88f, 0.45f, 0.92f));

            burn.gameObject.SetActive(false);
            jellified.gameObject.SetActive(false);
            critical.gameObject.SetActive(false);
            weakPoint.gameObject.SetActive(false);
            kill.gameObject.SetActive(false);

            CombatTextView view = rootObject.GetComponent<CombatTextView>();
            SerializedObject serializedView = new(view);
            serializedView.Update();
            SetReference(serializedView, "_root", root);
            SetReference(serializedView, "_text", value);
            SetReference(serializedView, "_canvasGroup", canvasGroup);
            SetReference(serializedView, "_backing", backing);
            SetReference(serializedView, "_burnAccent", burn);
            SetReference(serializedView, "_jellifiedAccent", jellified);
            SetReference(serializedView, "_criticalAccent", critical);
            SetReference(serializedView, "_weakPointAccent", weakPoint);
            SetReference(serializedView, "_killAccent", kill);
            serializedView.ApplyModifiedPropertiesWithoutUndo();

            rootObject.SetActive(false);
            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(rootObject, PrefabPath);
            if (savedPrefab == null)
                throw new InvalidOperationException($"Could not save combat-text view prefab at '{PrefabPath}'.");
            return savedPrefab.GetComponent<CombatTextView>();
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(rootObject);
        }
    }

    private static TMP_Text CreateValueText(
        RectTransform parent,
        TMP_FontAsset font,
        Material sharedFontMaterial)
    {
        GameObject textObject = new("Value", typeof(RectTransform), typeof(TextMeshProUGUI));
        RectTransform rect = (RectTransform)textObject.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(8f, 4f);
        rect.offsetMax = new Vector2(-8f, -4f);

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.text = "9999";
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.fontSize = 38f;
        text.fontStyle = FontStyles.Bold;
        text.color = new Color(1f, 0.93f, 0.78f, 1f);
        if (font != null)
            text.font = font;
        if (sharedFontMaterial != null)
            text.fontSharedMaterial = sharedFontMaterial;
        return text;
    }

    private static Image CreateImage(
        RectTransform parent,
        string name,
        Vector2 size,
        Vector2 position,
        float rotation,
        Color color)
    {
        GameObject imageObject = new(name, typeof(RectTransform), typeof(Image));
        RectTransform rect = (RectTransform)imageObject.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        rect.localRotation = Quaternion.Euler(0f, 0f, rotation);

        Image image = imageObject.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Material FindSharedFontMaterial(TMP_FontAsset font)
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(LiberationSansPath);
        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is Material material && material.name == "LiberationSans SDF Material")
                return material;
        }

        for (int i = 0; i < assets.Length; i++)
        {
            if (assets[i] is Material material)
                return material;
        }

        return font != null ? font.material : null;
    }

    private static void AssignPlayerProfile(CombatTextProfile profile)
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath) == null)
            throw new InvalidOperationException($"Player prefab not found at '{PlayerPrefabPath}'.");

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            WeaponPresentationController controller =
                root.GetComponentInChildren<WeaponPresentationController>(true);
            if (controller == null)
                throw new InvalidOperationException(
                    $"Player prefab '{PlayerPrefabPath}' has no {nameof(WeaponPresentationController)}.");

            SerializedObject serializedController = new(controller);
            serializedController.Update();
            SerializedProperty profileProperty = serializedController.FindProperty("_combatTextProfile");
            if (profileProperty == null)
                throw new MissingFieldException(
                    nameof(WeaponPresentationController),
                    "_combatTextProfile");

            if (profileProperty.objectReferenceValue == profile)
                return;

            profileProperty.objectReferenceValue = profile;
            serializedController.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void SetReference(
        SerializedObject serializedObject,
        string propertyName,
        UnityEngine.Object value)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null)
            throw new MissingFieldException(serializedObject.targetObject.GetType().Name, propertyName);
        property.objectReferenceValue = value;
    }
}
#endif
