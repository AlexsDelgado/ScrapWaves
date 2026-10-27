#if UNITY_EDITOR
using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>One-time editor authoring. Runtime warning code never creates UI.</summary>
public static class OverheatPreparationAuthoring
{
    [MenuItem("ScrapWaves/UI/Author Overheat Preparation")]
    public static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Author outside Play Mode.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Unsaved scene edits detected; no scene changed.");
        Scene scene = EditorSceneManager.OpenScene(UpperLevelOverheatAuthoring.ScenePath);
        UpperLevelVolume volume = Find<UpperLevelVolume>(scene).Single();
        PlayerUpperLevelOverheat hazard = Find<PlayerUpperLevelOverheat>(scene).Single();
        PlayerPlatformState platform = hazard.GetComponent<PlayerPlatformState>();
        OverheatManager manager = Find<OverheatManager>(scene).Single();
        Set(hazard, "_upperLevelVolume", volume);
        Set(manager, "_playerPlatformState", platform);
        EditorUtility.SetDirty(volume);

        OverheatObjectiveHud objective = Find<OverheatObjectiveHud>(scene).Single();
        OverheatWarningHud legacyPresenter = objective.GetComponent<OverheatWarningHud>();
        Transform root = objective.transform.Find("OverheatWarning");
        bool createdView = root == null;
        if (root == null)
        {
            GameObject view = new("OverheatWarning", typeof(RectTransform));
            root = view.transform;
            root.SetParent(objective.transform, false);
            var rect = (RectTransform)root;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -108f);
            rect.sizeDelta = new Vector2(1000f, 56f);
            Image background = view.AddComponent<Image>();
            background.sprite = HudUiFactory.WhiteSprite;
            background.color = new Color(.97f, .90f, .80f, .94f);
            background.raycastTarget = false;
            TextMeshProUGUI label = HudUiFactory.CreateLabel(root, "Text",
                "OVERHEAT EVENT STARTING - GET TO GROUND LEVEL", 28f, TextAlignmentOptions.Center);
            label.fontStyle = FontStyles.Bold;
            label.color = new Color(.47f, .025f, .035f, 1f);
            label.enableAutoSizing = false;
            label.fontSizeMin = 20f;
            label.fontSizeMax = 28f;
            label.margin = new Vector4(18f, 6f, 18f, 6f);
            label.raycastTarget = false;
        }
        // Keep the controller on the warning UI object itself. Its CanvasGroup hides the
        // graphics while the active controller observes the authoritative preparation state.
        OverheatWarningHud presenter = root.GetComponent<OverheatWarningHud>();
        bool migrated = presenter == null;
        if (migrated)
        {
            presenter = root.gameObject.AddComponent<OverheatWarningHud>();
            if (legacyPresenter != null) EditorUtility.CopySerialized(legacyPresenter, presenter);
        }
        if (legacyPresenter != null) Object.DestroyImmediate(legacyPresenter);
        CanvasGroup visibility = root.GetComponent<CanvasGroup>();
        if (visibility == null) visibility = root.gameObject.AddComponent<CanvasGroup>();
        visibility.alpha = 0f;
        visibility.interactable = false;
        visibility.blocksRaycasts = false;
        if (root.GetComponent<RectMask2D>() == null) root.gameObject.AddComponent<RectMask2D>();
        Set(presenter, "_overheatManager", manager);
        Set(presenter, "_playerPlatformState", platform);
        Set(presenter, "_root", root.gameObject);
        TextMeshProUGUI primary = root.Find("Text")?.GetComponent<TextMeshProUGUI>()
            ?? root.GetComponentInChildren<TextMeshProUGUI>(true);
        TextMeshProUGUI repeat = root.Find("TextRepeat")?.GetComponent<TextMeshProUGUI>();
        if (repeat == null)
        {
            repeat = Object.Instantiate(primary.gameObject, root, false).GetComponent<TextMeshProUGUI>();
            repeat.name = "TextRepeat";
        }
        Set(presenter, "_text", primary);
        Set(presenter, "_repeatText", repeat);
        Set(presenter, "_visibility", visibility);
        Set(presenter, "_motion", primary.rectTransform);
        if (createdView)
        {
            Set(presenter, "_scrollSpeed", 180f);
            Set(presenter, "_fontSize", 96f);
            Set(presenter, "_barHeight", 160f);
            Set(presenter, "_messageGap", 120f);
        }
        StyleWarning(root, presenter, createdView);
        presenter.PreviewAt(0f, false);
        visibility.alpha = 0f;
        root.gameObject.SetActive(true);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save Overheat scene wiring.");
        AssetDatabase.SaveAssets();
        Debug.Log("OVERHEAT_PREPARATION_AUTHORED: " + Path(root) + "; volume damage and shared preparation references saved.");
    }

    public static void RunAndCapture()
    {
        Run();
        OverheatWarningPreview.Capture();
    }

    private static void StyleWarning(Transform root, OverheatWarningHud presenter, bool createdView)
    {
        var rect = (RectTransform)root;
        if (createdView)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -96f);
            root.GetComponent<Image>().color = new Color(.025f, .005f, .01f, .78f);
        }
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, presenter.BarHeight);
        var label = root.GetComponentInChildren<TextMeshProUGUI>(true);
        foreach (TextMeshProUGUI text in root.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            text.fontSize = presenter.FontSize;
            text.enableAutoSizing = false;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.raycastTarget = false;
            if (!createdView) continue;
            text.color = new Color(.61f, .055f, .075f, 1f);
            text.margin = new Vector4(32f, 12f, 32f, 12f);
            text.extraPadding = true;
        }
        const string folder = "Assets/Art/UI/Authored/";
        const string materialPath = folder + "OverheatWarningText.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(label.fontSharedMaterial) { name = "OverheatWarningText" };
            material.SetColor(ShaderUtilities.ID_OutlineColor, new Color(.98f, .87f, .76f, 1f));
            material.SetFloat(ShaderUtilities.ID_OutlineWidth, .16f);
            material.EnableKeyword("OUTLINE_ON");
            AssetDatabase.CreateAsset(material, materialPath);
        }
        if (createdView)
            foreach (TextMeshProUGUI text in root.GetComponentsInChildren<TextMeshProUGUI>(true))
                text.fontSharedMaterial = material;
        foreach (string name in new[] { "TopRule", "BottomRule" })
        {
            Transform rule = root.Find(name);
            bool createdRule = rule == null;
            if (createdRule)
            {
                rule = new GameObject(name, typeof(RectTransform), typeof(Image)).transform;
                rule.SetParent(root, false);
            }
            if (!createdRule && !createdView) continue;
            var ruleRect = (RectTransform)rule;
            float y = name == "TopRule" ? 1f : 0f;
            ruleRect.anchorMin = new Vector2(0f, y);
            ruleRect.anchorMax = new Vector2(1f, y);
            ruleRect.pivot = new Vector2(.5f, y);
            ruleRect.anchoredPosition = Vector2.zero;
            ruleRect.sizeDelta = new Vector2(0f, 4f);
            var graphic = rule.GetComponent<Image>();
            graphic.sprite = HudUiFactory.WhiteSprite;
            graphic.color = new Color(.7f, .08f, .1f, 1f);
            graphic.raycastTarget = false;
        }
        const string clipPath = folder + "OverheatWarningPulse.anim";
        AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
        if (clip == null)
        {
            clip = new AnimationClip { name = "OverheatWarningPulse", frameRate = 30f };
            foreach (string rule in new[] { "TopRule", "BottomRule" })
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(rule, typeof(Image), "m_Color.a"),
                    AnimationCurve.EaseInOut(0f, .55f, .8f, 1f));
            foreach (string rule in new[] { "TopRule", "BottomRule" })
            {
                var binding = EditorCurveBinding.FloatCurve(rule, typeof(Image), "m_Color.a");
                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                curve.AddKey(new Keyframe(1.6f, .55f, 0f, 0f));
                AnimationUtility.SetEditorCurve(clip, binding, curve);
            }
            AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, clipPath);
        }
        const string controllerPath = folder + "OverheatWarningPulse.controller";
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddMotion(clip);
        }
        Animator animator = root.GetComponent<Animator>();
        if (animator == null) animator = root.gameObject.AddComponent<Animator>();
        if (animator.runtimeAnimatorController == null)
        {
            animator.runtimeAnimatorController = controller;
            animator.updateMode = AnimatorUpdateMode.Normal;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
    }

    private static T[] Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static void Set(Object target, string field, Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(field).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }
    private static void Set(Object target, string field, float value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(field).floatValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
        PrefabUtility.RecordPrefabInstancePropertyModifications(target);
    }
    private static string Path(Transform transform) => transform.parent == null ? transform.name : Path(transform.parent) + "/" + transform.name;
}
#endif
