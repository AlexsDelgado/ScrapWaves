using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Adds only the demo controls to the existing designer-authored title scene.</summary>
public static class DemoLeaderboardAuthoring
{
    private const string ScenePath = "Assets/Scenes/TitleScreen.unity";
    private static readonly Color Coal = new(.035f, .043f, .039f, .96f);
    private static readonly Color Steel = new(.122f, .145f, .133f, 1f);
    private static readonly Color Bone = new(.949f, .961f, .922f, 1f);
    private static readonly Color Muted = new(.678f, .741f, .69f, 1f);
    private static readonly Color Green = new(.659f, .78f, .561f, 1f);
    private static readonly Color Rust = new(.851f, .416f, .196f, 1f);

    [MenuItem("Tools/ScrapWaves/Demo Leaderboard/Add Authored UI")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Author the leaderboard outside Play Mode.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Unsaved scene work is present. No scene was changed.");
        SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath);
            if (All<DemoLeaderboardSettings>(scene).Any())
            {
                Validate(scene);
                Debug.Log("Demo leaderboard already authored; existing designer changes preserved.");
                return;
            }
            // Existing designer choices may predate the validator. Do not "repair" unrelated UI.
            string[] priorErrors = TitleScreenAuthoringValidator.ValidateScene(scene).Errors
                .Where(error => !error.StartsWith("Demo leaderboard:", StringComparison.Ordinal)).ToArray();
            TitleScreenController controller = All<TitleScreenController>(scene).Single();
            TitleScreenScreenStack stack = All<TitleScreenScreenStack>(scene).Single();
            Transform title = Named(scene, "TitleRoot");
            Transform safe = Named(scene, "SafeArea");
            TMP_FontAsset font = controller.PlayButton.GetComponentInChildren<TMP_Text>(true).font;

            var settingsRoot = new GameObject("Developer - Demo Leaderboard");
            var settings = settingsRoot.AddComponent<DemoLeaderboardSettings>();

            RectTransform board = Rect("DemoLeaderboardRoot", title.parent);
            board.anchorMin = Vector2.zero;
            board.anchorMax = new Vector2(.7f, 0f);
            board.pivot = Vector2.zero;
            board.anchoredPosition = new Vector2(46f, 42f);
            board.sizeDelta = new Vector2(-467.2f, 444f);
            var view = board.gameObject.AddComponent<DemoLeaderboardView>();
            var viewFields = new SerializedObject(view);
            viewFields.FindProperty("_root").objectReferenceValue = board.gameObject;
            BuildTable(board, font, "MostKills", "MOST KILLS", "KILLS", false,
                viewFields.FindProperty("_mostKillsRows"));
            BuildTable(board, font, "LongestSurvival", "TIME SURVIVED", "TIME", true,
                viewFields.FindProperty("_longestSurvivalRows"));
            viewFields.ApplyModifiedPropertiesWithoutUndo();

            RectTransform modal = Rect("PlayerNameEntry", safe);
            Stretch(modal);
            var modalGroup = modal.gameObject.AddComponent<CanvasGroup>();
            var nameView = modal.gameObject.AddComponent<PlayerNameEntryView>();
            Image backdrop = Image("Backdrop", modal, new Color(.018f, .026f, .022f, .72f));
            Stretch(backdrop.rectTransform);
            backdrop.raycastTarget = true;
            RectTransform panel = Image("Panel", modal, Coal).rectTransform;
            Fixed(panel, new Vector2(.5f, .5f), Vector2.zero, new Vector2(650f, 426f));
            Image edge = Image("GreenTopEdge", panel, Green);
            Fixed(edge.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -3f), new Vector2(650f, 6f));
            Label("Eyebrow", panel, font, "ENTER THE SCRAPYARD", 19f, Green,
                new Vector2(0f, 1f), new Vector2(32f, -35f), new Vector2(580f, 28f));
            Label("Title", panel, font, "YOUR NAME", 42f, Bone,
                new Vector2(0f, 1f), new Vector2(32f, -83f), new Vector2(580f, 56f));
            Label("Description", panel, font, "Make your mark on the leaderboard.", 22f, Muted,
                new Vector2(0f, 1f), new Vector2(32f, -137f), new Vector2(580f, 34f));

            Image fieldBackground = Image("NameInput", panel, Steel);
            Fixed(fieldBackground.rectTransform, new Vector2(.5f, 1f), new Vector2(0f, -205f), new Vector2(586f, 64f));
            fieldBackground.raycastTarget = true;
            var input = fieldBackground.gameObject.AddComponent<TMP_InputField>();
            RectTransform viewport = Rect("TextArea", fieldBackground.transform);
            Stretch(viewport, new Vector2(16f, 8f), new Vector2(-16f, -8f));
            viewport.gameObject.AddComponent<RectMask2D>();
            TMP_Text inputText = Label("Text", viewport, font, "", 28f, Bone);
            Stretch(inputText.rectTransform);
            TMP_Text placeholder = Label("Placeholder", viewport, font, "Enter your name", 28f, Muted);
            Stretch(placeholder.rectTransform);
            input.textViewport = viewport;
            input.textComponent = (TextMeshProUGUI)inputText;
            input.placeholder = placeholder;
            input.targetGraphic = fieldBackground;
            input.characterLimit = DemoLeaderboardNames.MaximumLength;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.Standard;
            input.richText = false;
            input.customCaretColor = true;
            input.caretColor = Green;
            input.selectionColor = new Color(Green.r, Green.g, Green.b, .3f);
            input.onFocusSelectAll = true;

            TMP_Text validation = Label("Validation", panel, font, "1-16 characters", 19f, Muted,
                new Vector2(0f, 1f), new Vector2(32f, -260f), new Vector2(586f, 30f));
            Button cancel = MakeButton("CancelButton", panel, font, "CANCEL", new Vector2(-153f, -342f), Steel, Bone);
            Button start = MakeButton("StartButton", panel, font, "START", new Vector2(153f, -342f), Green, Coal);
            Navigate(input, start, start, cancel, start);
            Navigate(start, input, input, cancel, cancel);
            Navigate(cancel, input, input, start, start);
            var inputColors = input.colors;
            inputColors.selectedColor = new Color(1.6f, 1.8f, 1.4f);
            inputColors.highlightedColor = new Color(1.3f, 1.4f, 1.3f);
            inputColors.normalColor = Color.white;
            input.colors = inputColors;

            var nameFields = new SerializedObject(nameView);
            nameFields.FindProperty("_input").objectReferenceValue = input;
            nameFields.FindProperty("_validation").objectReferenceValue = validation;
            nameFields.FindProperty("_startButton").objectReferenceValue = start;
            nameFields.FindProperty("_cancelButton").objectReferenceValue = cancel;
            nameFields.ApplyModifiedPropertiesWithoutUndo();

            var stackFields = new SerializedObject(stack);
            SerializedProperty binding = stackFields.FindProperty("_playerNameEntry");
            binding.FindPropertyRelative("_state").enumValueIndex = (int)TitleScreenLocalState.PlayerNameEntry;
            binding.FindPropertyRelative("_canvasGroup").objectReferenceValue = modalGroup;
            binding.FindPropertyRelative("_animationRoot").objectReferenceValue = panel;
            binding.FindPropertyRelative("_backButton").objectReferenceValue = cancel;
            binding.FindPropertyRelative("_initialFocus").objectReferenceValue = input;
            stackFields.ApplyModifiedPropertiesWithoutUndo();
            var controllerFields = new SerializedObject(controller);
            controllerFields.FindProperty("_demoLeaderboardSettings").objectReferenceValue = settings;
            controllerFields.FindProperty("_leaderboardView").objectReferenceValue = view;
            controllerFields.FindProperty("_playerNameEntry").objectReferenceValue = nameView;
            controllerFields.ApplyModifiedPropertiesWithoutUndo();
            modalGroup.alpha = 0f;
            modalGroup.interactable = false;
            modalGroup.blocksRaycasts = false;
            modal.gameObject.SetActive(false);
            view.ShowData(new DemoLeaderboardData());
            Validate(scene);
            TitleScreenAuthoringValidator.Result result = TitleScreenAuthoringValidator.ValidateScene(scene);
            string[] newErrors = result.Errors.Except(priorErrors).ToArray();
            if (newErrors.Length > 0) throw new InvalidOperationException(string.Join("\n", newErrors));
            foreach (string existingError in priorErrors)
                Debug.LogWarning("Preserved pre-existing title validation issue: " + existingError);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("DEMO_LEADERBOARD_AUTHORED: Added two tables, name modal and developer checkbox; saved TitleScreen only.");
        }
        finally
        {
            if (previous.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(previous);
        }
    }

    private static void BuildTable(RectTransform parent, TMP_FontAsset font, string name, string title,
        string valueTitle, bool right, SerializedProperty rows)
    {
        RectTransform table = Image(name, parent, Coal).rectTransform;
        table.anchorMin = new Vector2(right ? .5f : 0f, 0f);
        table.anchorMax = new Vector2(right ? 1f : .5f, 1f);
        table.offsetMin = new Vector2(right ? 10f : 0f, 0f);
        table.offsetMax = new Vector2(right ? 0f : -10f, 0f);
        RectTransform top = Image("Accent", table, right ? Rust : Green).rectTransform;
        top.anchorMin = new Vector2(0f, 1f); top.anchorMax = Vector2.one;
        top.offsetMin = new Vector2(0f, -4f); top.offsetMax = Vector2.zero;
        TMP_Text heading = Label("Heading", table, font, title, 29f, Bone);
        TopSpan(heading.rectTransform, 16f, 64f, 18f, 18f);
        heading.fontStyle = FontStyles.Bold;
        heading.enableAutoSizing = true; heading.fontSizeMin = 22f; heading.fontSizeMax = 29f;
        TMP_Text playerHeader = Label("PlayerHeading", table, font, "PLAYER", 16f, Muted);
        TopSpan(playerHeader.rectTransform, 65f, 89f, 57f, 110f);
        TMP_Text scoreHeader = Label("ScoreHeading", table, font, valueTitle, 16f, Muted);
        TopSpan(scoreHeader.rectTransform, 65f, 89f, 0f, 18f);
        scoreHeader.alignment = TextAlignmentOptions.MidlineRight;
        rows.arraySize = 10;
        for (int i = 0; i < 10; i++)
        {
            RectTransform row = Image("Rank" + (i + 1).ToString("00"), table,
                i % 2 == 0 ? new Color(.122f, .145f, .133f, .45f) : Color.clear).rectTransform;
            TopSpan(row, 96f + i * 32f, 127f + i * 32f, 10f, 10f);
            TMP_Text rank = Label("Rank", row, font, (i + 1).ToString("00"), 21f, i < 3 ? Green : Muted);
            rank.rectTransform.anchorMin = Vector2.zero;
            rank.rectTransform.anchorMax = new Vector2(0f, 1f);
            rank.rectTransform.pivot = new Vector2(0f, .5f);
            rank.rectTransform.anchoredPosition = new Vector2(8f, 0f);
            rank.rectTransform.sizeDelta = new Vector2(34f, 0f);
            TMP_Text player = Label("Name", row, font, "-", 22f, Bone);
            Stretch(player.rectTransform, new Vector2(46f, 0f), new Vector2(-92f, 0f));
            player.overflowMode = TextOverflowModes.Ellipsis;
            TMP_Text score = Label("Value", row, font, "-", 22f, i == 0 ? Green : Bone);
            score.alignment = TextAlignmentOptions.MidlineRight;
            score.rectTransform.anchorMin = new Vector2(1f, 0f);
            score.rectTransform.anchorMax = Vector2.one;
            score.rectTransform.pivot = new Vector2(1f, .5f);
            score.rectTransform.anchoredPosition = new Vector2(-8f, 0f);
            score.rectTransform.sizeDelta = new Vector2(84f, 0f);
            score.enableAutoSizing = true; score.fontSizeMin = 13f; score.fontSizeMax = 22f;
            SerializedProperty rowFields = rows.GetArrayElementAtIndex(i);
            rowFields.FindPropertyRelative("_rank").objectReferenceValue = rank;
            rowFields.FindPropertyRelative("_name").objectReferenceValue = player;
            rowFields.FindPropertyRelative("_value").objectReferenceValue = score;
        }
    }

    public static void Validate(Scene scene)
    {
        DemoLeaderboardSettings settings = All<DemoLeaderboardSettings>(scene).Single();
        DemoLeaderboardView view = All<DemoLeaderboardView>(scene).Single();
        PlayerNameEntryView nameView = All<PlayerNameEntryView>(scene).Single();
        if (!nameView.HasRequiredReferences || !All<TitleScreenScreenStack>(scene).Single().HasPlayerNameEntryBinding)
            throw new InvalidOperationException("The authored name screen is incomplete.");
        var fields = new SerializedObject(view);
        foreach (string field in new[] { "_mostKillsRows", "_longestSurvivalRows" })
        {
            SerializedProperty rows = fields.FindProperty(field);
            if (rows.arraySize != 10) throw new InvalidOperationException("Each leaderboard requires 10 authored rows.");
            for (int i = 0; i < 10; i++)
                foreach (string member in new[] { "_rank", "_name", "_value" })
                    if (rows.GetArrayElementAtIndex(i).FindPropertyRelative(member).objectReferenceValue == null)
                        throw new InvalidOperationException("Missing authored leaderboard label.");
        }
        if (view.GetComponentsInChildren<Graphic>(true).Any(graphic => graphic.raycastTarget))
            throw new InvalidOperationException("Leaderboard graphics must not intercept menu input.");
        if (settings.gameObject.name != "Developer - Demo Leaderboard")
            throw new InvalidOperationException("Keep the demo switch easy to find in the scene hierarchy.");
    }

    // Narrow migration for the original demo tables; never moves the existing logo or action stack.
    public static void RefineTableLayout()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Unsaved scene work is present.");
        SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath);
            RectTransform root = (RectTransform)Named(scene, "DemoLeaderboardRoot");
            root.anchorMax = new Vector2(.7f, 0f);
            root.sizeDelta = new Vector2(-467.2f, 444f);
            foreach (TMP_Text label in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (label.name == "Heading")
                { label.enableAutoSizing = true; label.fontSizeMin = 22f; label.fontSizeMax = 29f; }
                if (label.name == "Name") label.rectTransform.offsetMax = new Vector2(-92f, 0f);
                if (label.name == "Value")
                { label.rectTransform.sizeDelta = new Vector2(84f, 0f); label.fontSizeMin = 13f; }
            }
            Validate(scene);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally { if (previous.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(previous); }
        DemoLeaderboardPreview.Capture();
    }

    [MenuItem("Tools/ScrapWaves/Demo Leaderboard/Clear Demo Scores")]
    public static void ClearScores()
    {
        if (!EditorUtility.DisplayDialog("Clear demo scores?",
                "Remove the local demo leaderboard only? Progression and tutorial saves will remain intact.\n\n" + DemoLeaderboardStore.DefaultPath,
                "Clear Demo Scores", "Cancel")) return;
        var store = new DemoLeaderboardStore();
        if (!store.Clear()) { Debug.LogWarning("Demo leaderboard: " + store.LastError); return; }
        foreach (DemoLeaderboardView view in Object.FindObjectsByType<DemoLeaderboardView>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            view.ShowData(new DemoLeaderboardData());
        Debug.Log("Cleared only local demo leaderboard scores.");
    }

    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static Transform Named(Scene scene, string name) => All<Transform>(scene).Single(t => t.name == name);
    private static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer; go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }
    private static Image Image(string name, Transform parent, Color color)
    {
        Image image = Rect(name, parent).gameObject.AddComponent<Image>();
        image.color = color; image.raycastTarget = false; return image;
    }
    private static TMP_Text Label(string name, Transform parent, TMP_FontAsset font, string value, float size, Color color)
    {
        var label = Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        label.font = font; label.text = value; label.fontSize = size; label.color = color;
        label.alignment = TextAlignmentOptions.MidlineLeft; label.richText = false;
        label.textWrappingMode = TextWrappingModes.NoWrap; label.raycastTarget = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
        return label;
    }
    private static TMP_Text Label(string name, Transform parent, TMP_FontAsset font, string value, float size, Color color,
        Vector2 anchor, Vector2 position, Vector2 dimensions)
    {
        TMP_Text label = Label(name, parent, font, value, size, color);
        Fixed(label.rectTransform, anchor, position, dimensions);
        label.rectTransform.pivot = new Vector2(anchor.x, .5f);
        return label;
    }
    private static Button MakeButton(string name, Transform parent, TMP_FontAsset font, string caption, Vector2 position, Color fill, Color text)
    {
        Image image = Image(name, parent, fill);
        Fixed(image.rectTransform, new Vector2(.5f, 1f), position, new Vector2(280f, 62f));
        image.raycastTarget = true;
        Button button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
        ColorBlock colors = button.colors; colors.selectedColor = new Color(1.25f, 1.25f, 1.25f);
        colors.highlightedColor = colors.selectedColor; colors.pressedColor = new Color(.75f, .75f, .75f); button.colors = colors;
        TMP_Text label = Label("Label", image.transform, font, caption, 25f, text);
        label.fontStyle = FontStyles.Bold; label.alignment = TextAlignmentOptions.Center; Stretch(label.rectTransform);
        return button;
    }
    private static void Navigate(Selectable target, Selectable up, Selectable down, Selectable left, Selectable right)
    {
        target.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnUp = up, selectOnDown = down, selectOnLeft = left, selectOnRight = right };
    }
    private static void Fixed(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    { rect.anchorMin = rect.anchorMax = anchor; rect.pivot = new Vector2(.5f, .5f); rect.anchoredPosition = position; rect.sizeDelta = size; }
    private static void Stretch(RectTransform rect, Vector2? min = null, Vector2? max = null)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = min ?? Vector2.zero; rect.offsetMax = max ?? Vector2.zero; }
    private static void TopSpan(RectTransform rect, float top, float bottom, float left, float right)
    { rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = Vector2.one; rect.offsetMin = new Vector2(left, -bottom); rect.offsetMax = new Vector2(-right, -top); }
}
