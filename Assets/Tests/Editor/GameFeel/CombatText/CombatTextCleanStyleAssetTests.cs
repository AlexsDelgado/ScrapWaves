using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public sealed class CombatTextCleanStyleAssetTests
{
    private const string ProfilePath = "Assets/ScriptableObjects/GameFeel/CombatTextProfile.asset";
    private const string PrefabPath = "Assets/GameFeel/Prefabs/CombatText/CombatTextView.prefab";

    [Test]
    public void AuthoredAndProgrammaticViewsContainOnlyTheNumericVisual()
    {
        GameObject authoredRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Assert.That(authoredRoot, Is.Not.Null);
        Assert.That(authoredRoot.GetComponent<CombatTextView>(), Is.Not.Null);
        AssertNumberOnly(authoredRoot, "authored prefab");

        GameObject parentObject = new("Programmatic Combat Text Parent");
        try
        {
            CombatTextProfile profile = AssetDatabase.LoadAssetAtPath<CombatTextProfile>(ProfilePath);
            MethodInfo createProgrammatic = typeof(CombatTextView).GetMethod(
                "CreateProgrammatic",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(profile, Is.Not.Null);
            Assert.That(createProgrammatic, Is.Not.Null);
            CombatTextView programmatic = (CombatTextView)createProgrammatic.Invoke(
                null,
                new object[] { parentObject.transform, profile, 0 });

            Assert.That(programmatic, Is.Not.Null);
            AssertNumberOnly(programmatic.gameObject, "programmatic fallback");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(parentObject);
        }
    }

    [Test]
    public void EveryAuthoredStyleUsesOneDistinctOpaqueTextColor()
    {
        CombatTextProfile profile = AssetDatabase.LoadAssetAtPath<CombatTextProfile>(ProfilePath);
        Assert.That(profile, Is.Not.Null);

        string[] expectedColors =
        {
            "FFEDC7",
            "FF6B0A",
            "57EB3D",
            "FFD138",
            "59F2FF",
            "D966FF",
            "FF3B4D",
            "5C7CFF"
        };
        float[] expectedFontSizes = { 34f, 30f, 30f, 40f, 38f, 44f, 46f, 36f };
        float[] expectedBaseScales = { 1f, 0.90f, 0.90f, 1.06f, 1.07f, 1.10f, 1.10f, 1.04f };
        Array styleIds = Enum.GetValues(typeof(CombatTextStyleId));
        Assert.That(styleIds.Length, Is.EqualTo(expectedColors.Length));

        HashSet<string> uniqueColors = new(StringComparer.Ordinal);
        for (int i = 0; i < styleIds.Length; i++)
        {
            CombatTextStyleId styleId = (CombatTextStyleId)styleIds.GetValue(i);
            CombatTextStyleDefinition style = profile.GetStyle(styleId);
            string color = ColorUtility.ToHtmlStringRGB(style.TextColor);

            Assert.That(style.TextColor.a, Is.EqualTo(1f), $"{styleId} must remain opaque.");
            Assert.That(style.FontStyle.HasFlag(FontStyles.Bold), Is.True, $"{styleId} must remain bold.");
            Assert.That(color, Is.EqualTo(expectedColors[i]), $"Unexpected authored color for {styleId}.");
            Assert.That(style.FontSize, Is.EqualTo(expectedFontSizes[i]), $"Unexpected font size for {styleId}.");
            Assert.That(style.BaseScale, Is.EqualTo(expectedBaseScales[i]), $"Unexpected base scale for {styleId}.");
            Assert.That(uniqueColors.Add(color), Is.True, $"{styleId} duplicates another style color.");
        }
    }

    private static void AssertNumberOnly(GameObject viewObject, string source)
    {
        TMP_Text[] textVisuals = viewObject.GetComponentsInChildren<TMP_Text>(true);
        TextMeshPro[] worldTexts = viewObject.GetComponentsInChildren<TextMeshPro>(true);
        TextMeshProUGUI[] uiTexts = viewObject.GetComponentsInChildren<TextMeshProUGUI>(true);
        Graphic[] graphics = viewObject.GetComponentsInChildren<Graphic>(true);
        MeshRenderer[] meshRenderers = viewObject.GetComponentsInChildren<MeshRenderer>(true);
        Assert.That(
            textVisuals,
            Has.Length.EqualTo(1),
            $"The {source} must contain exactly one numeric TMP visual.");
        Assert.That(
            worldTexts,
            Has.Length.EqualTo(1),
            $"The {source} must use one spatial TextMeshPro mesh.");
        Assert.That(textVisuals[0], Is.SameAs(worldTexts[0]));
        Assert.That(uiTexts, Is.Empty, $"The {source} must not use TextMeshProUGUI.");
        Assert.That(meshRenderers, Has.Length.EqualTo(1), $"The {source} must have one MeshRenderer.");
        Assert.That(worldTexts[0].isOverlay, Is.False, $"The {source} must remain depth tested.");
        Assert.That(
            graphics,
            Has.Length.EqualTo(1),
            $"The {source} must contain no Graphic besides the world TMP base component.");
        Assert.That(graphics[0], Is.SameAs(worldTexts[0]));
        Assert.That(viewObject.GetComponentsInChildren<Canvas>(true), Is.Empty);
        Assert.That(viewObject.GetComponentsInChildren<CanvasGroup>(true), Is.Empty);
        Assert.That(viewObject.GetComponentsInChildren<CanvasRenderer>(true), Is.Empty);
        Assert.That(
            viewObject.GetComponentsInChildren<Image>(true),
            Is.Empty,
            $"The {source} must not contain backing or decorative Image components.");
    }
}
