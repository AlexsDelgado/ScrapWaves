#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Renders a copy of the saved menu. Sample scores never touch the leaderboard store.</summary>
public static class DemoLeaderboardPreview
{
    private const string ScenePath = "Assets/Scenes/TitleScreen.unity";

    [MenuItem("Tools/ScrapWaves/Demo Leaderboard/Capture Menu Previews")]
    public static void Capture()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Capture the authored leaderboard outside Play Mode.");
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            throw new InvalidOperationException("Leaderboard capture requires graphics; omit -nographics.");

        string output = Environment.GetEnvironmentVariable("SCRAPWAVES_LEADERBOARD_PREVIEW_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) output = ".utmp/demo-leaderboard-20261031/previews";
        Directory.CreateDirectory(output);
        Scene previous = SceneManager.GetActiveScene();
        Scene source = SceneManager.GetSceneByPath(ScenePath);
        if (source.IsValid() && source.isLoaded && source.isDirty)
            throw new InvalidOperationException("The title scene has unsaved edits. Save it before capturing the saved menu.");
        bool opened = !source.IsValid() || !source.isLoaded;
        if (opened) source = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture previousTarget = RenderTexture.active;
        var temporaryMaterials = new List<Material>();
        var evidence = new StringBuilder();
        try
        {
            Canvas savedCanvas = Find<Canvas>(source).Single(canvas => canvas.name == "MainMenuCanvas");
            GameObject menu = Object.Instantiate(savedCanvas.gameObject);
            menu.name = savedCanvas.name;
            SceneManager.MoveGameObjectToScene(menu, preview);
            foreach (MonoBehaviour behaviour in menu.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour is not UIBehaviour) behaviour.enabled = false;
            foreach (Animator animator in menu.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            foreach (ParticleSystem particles in menu.GetComponentsInChildren<ParticleSystem>(true))
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            menu.SetActive(true);

            Canvas canvas = menu.GetComponent<Canvas>();
            CanvasScaler scaler = menu.GetComponent<CanvasScaler>();
            if (scaler != null) scaler.enabled = false;
            foreach (Graphic graphic in menu.GetComponentsInChildren<Graphic>(true))
            {
                Material authored = graphic.material;
                if (authored == null || !authored.HasProperty("_UnscaledTime")) continue;
                Material material = new(authored) { hideFlags = HideFlags.HideAndDontSave };
                material.SetFloat("_UnscaledTime", 0f);
                if (material.HasProperty("_MotionScale")) material.SetFloat("_MotionScale", 0f);
                graphic.material = material;
                temporaryMaterials.Add(material);
            }

            RectTransform main = Named(menu, "MainMenuScreen");
            RectTransform title = Named(menu, "TitleRoot");
            RectTransform destinations = Named(menu, "MenuRoot");
            RectTransform leaderboard = Named(menu, "DemoLeaderboardRoot");
            RectTransform nameScreen = Named(menu, "PlayerNameEntry");
            DemoLeaderboardView view = menu.GetComponentInChildren<DemoLeaderboardView>(true);
            if (view == null || !view.HasRequiredReferences)
                throw new InvalidOperationException("The saved leaderboard view is missing its twenty authored rows.");
            CanvasGroup mainGroup = main.GetComponent<CanvasGroup>();
            CanvasGroup nameGroup = nameScreen.GetComponent<CanvasGroup>();
            if (mainGroup == null || nameGroup == null)
                throw new InvalidOperationException("The saved menu and name screen require CanvasGroups.");
            foreach (string hidden in new[] { "ObjectivesScreen", "SettingsScreen", "QuitConfirmation", "DeveloperRoot" })
                Named(menu, hidden).gameObject.SetActive(false);
            main.gameObject.SetActive(true);
            foreach (CanvasGroup group in main.GetComponentsInChildren<CanvasGroup>(true))
                group.alpha = 1f;
            view.SetVisible(true);
            nameScreen.gameObject.SetActive(false);

            Camera camera = new GameObject("Leaderboard preview camera", typeof(Camera)).GetComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject, preview);
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(preview);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.035f, .043f, .039f);
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            foreach (Canvas childCanvas in menu.GetComponentsInChildren<Canvas>(true))
            {
                childCanvas.enabled = true;
                childCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                childCanvas.worldCamera = camera;
                childCanvas.planeDistance = 1f;
            }

            evidence.AppendLine($"Captured {DateTime.UtcNow:O}; Unity {Application.unityVersion}.");
            evidence.AppendLine("Source: " + ScenePath + "; copied authored MainMenuCanvas rendered by Unity into a RenderTexture.");
            evidence.AppendLine("Scores and the entered name are preview samples only. No leaderboard store was loaded or written.");
            evidence.AppendLine("No scene or asset was saved. Runtime presenters are disabled; these captures verify layout, not input or gameplay.");
            evidence.AppendLine("The saved CanvasScaler reference resolution and screen-match rule are applied to each render target.");

            view.ShowData(new DemoLeaderboardData());
            Frame("menu-empty-1080.png", 1920, 1080, false);
            view.ShowData(SampleScores());
            Frame("menu-scores-1080.png", 1920, 1080, false);
            TMP_InputField input = nameScreen.GetComponentInChildren<TMP_InputField>(true);
            if (input == null) throw new InvalidOperationException("The saved name screen has no authored input field.");
            input.SetTextWithoutNotify("SCRAP QUEEN");
            PlayerNameEntryView nameView = nameScreen.GetComponent<PlayerNameEntryView>();
            if (nameView != null)
            {
                var nameFields = new SerializedObject(nameView);
                Button start = nameFields.FindProperty("_startButton").objectReferenceValue as Button;
                if (start != null) start.interactable = true;
            }
            Frame("name-entry-1080.png", 1920, 1080, true);
            Frame("menu-scores-720.png", 1280, 720, false);
            Frame("menu-scores-16x10.png", 1600, 1000, false);
            Frame("menu-scores-4x3.png", 1440, 1080, false);
            DemoLeaderboardData stress = SampleScores();
            stress.MostKills[0].Name = "WWWWWWWWWWWWWWWW";
            stress.MostKills[0].Kills = int.MaxValue;
            stress.LongestSurvival[0].Name = "WWWWWWWWWWWWWWWW";
            stress.LongestSurvival[0].SurvivalSeconds = int.MaxValue;
            view.ShowData(stress);
            Frame("menu-stress-4x3.png", 1440, 1080, false);
            evidence.AppendLine("Stress frame uses a 16-character wide name and int.MaxValue scores; it is intentionally synthetic.");
            File.WriteAllText(Path.Combine(output, "preview-evidence.txt"), evidence.ToString());
            Debug.Log("DEMO_LEADERBOARD_PREVIEWS: " + Path.GetFullPath(output));

            void Frame(string file, int width, int height, bool showName)
            {
                nameScreen.gameObject.SetActive(showName);
                nameGroup.alpha = showName ? 1f : 0f;
                mainGroup.alpha = showName ? .16f : 1f;
                RenderTexture target = new(width, height, 24, RenderTextureFormat.ARGB32);
                Texture2D pixels = null;
                try
                {
                    target.Create();
                    camera.targetTexture = target;
                    camera.aspect = width / (float)height;
                    canvas.scaleFactor = ResolveScale(scaler, width, height);
                    canvas.referencePixelsPerUnit = scaler != null ? scaler.referencePixelsPerUnit : 100f;
                    Canvas.ForceUpdateCanvases();
                    LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)menu.transform);
                    foreach (TMP_Text text in menu.GetComponentsInChildren<TMP_Text>(false)) text.ForceMeshUpdate();
                    Canvas.ForceUpdateCanvases();
                    camera.Render();
                    RenderTexture.active = target;
                    pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
                    pixels.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                    pixels.Apply();
                    File.WriteAllBytes(Path.Combine(output, file), pixels.EncodeToPNG());

                    Rect boardRect = ScreenRect(leaderboard, camera);
                    Rect titleRect = ScreenRect(title, camera);
                    Rect menuRect = ScreenRect(destinations, camera);
                    bool withinScreen = boardRect.xMin >= -1f && boardRect.yMin >= -1f &&
                                        boardRect.xMax <= width + 1f && boardRect.yMax <= height + 1f;
                    evidence.AppendLine($"{file}: {width}x{height}; scale={canvas.scaleFactor:0.###}; board={boardRect}; " +
                        $"within viewport={withinScreen}; overlaps logo={boardRect.Overlaps(titleRect)}; overlaps actions={boardRect.Overlaps(menuRect)}.");
                    ReportRows(view, evidence);
                    if (showName)
                        evidence.AppendLine("Name screen visible; menu alpha=0.16; input displays sample name SCRAP QUEEN. Focus/typing require runtime validation.");
                }
                finally
                {
                    RenderTexture.active = previousTarget;
                    camera.targetTexture = null;
                    if (pixels != null) Object.DestroyImmediate(pixels);
                    target.Release();
                    Object.DestroyImmediate(target);
                }
            }
        }
        finally
        {
            RenderTexture.active = previousTarget;
            EditorSceneManager.ClosePreviewScene(preview);
            foreach (Material material in temporaryMaterials) if (material != null) Object.DestroyImmediate(material);
            if (opened) EditorSceneManager.CloseScene(source, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    private static float ResolveScale(CanvasScaler scaler, int width, int height)
    {
        if (scaler == null) return 1f;
        if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize) return scaler.scaleFactor;
        if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
            throw new InvalidOperationException("This preview supports the authored pixel or screen-size scaling modes.");
        Vector2 reference = scaler.referenceResolution;
        float x = width / reference.x;
        float y = height / reference.y;
        return scaler.screenMatchMode switch
        {
            CanvasScaler.ScreenMatchMode.Expand => Mathf.Min(x, y),
            CanvasScaler.ScreenMatchMode.Shrink => Mathf.Max(x, y),
            _ => Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(x, 2f), Mathf.Log(y, 2f), scaler.matchWidthOrHeight))
        };
    }

    private static void ReportRows(DemoLeaderboardView view, StringBuilder evidence)
    {
        var serialized = new SerializedObject(view);
        foreach (string field in new[] { "_mostKillsRows", "_longestSurvivalRows" })
        {
            SerializedProperty rows = serialized.FindProperty(field);
            int nameOverflows = 0;
            int valueOverflows = 0;
            float smallestNameSize = float.MaxValue;
            for (int index = 0; index < rows.arraySize; index++)
            {
                SerializedProperty row = rows.GetArrayElementAtIndex(index);
                TMP_Text name = row.FindPropertyRelative("_name").objectReferenceValue as TMP_Text;
                TMP_Text value = row.FindPropertyRelative("_value").objectReferenceValue as TMP_Text;
                if (name == null || value == null) throw new InvalidOperationException("Preview found an unassigned row.");
                if (name.isTextOverflowing) nameOverflows++;
                if (value.isTextOverflowing) valueOverflows++;
                smallestNameSize = Mathf.Min(smallestNameSize, name.fontSize);
            }
            evidence.AppendLine($"  {field}: rows={rows.arraySize}; minimum name font={smallestNameSize:0.##}; " +
                $"name overflows={nameOverflows} (authored ellipsis may be intentional); score overflows={valueOverflows}.");
        }
    }

    private static DemoLeaderboardData SampleScores()
    {
        string[] names = { "SCRAP QUEEN", "BOLT", "RUSTY", "TIN CAN", "WRENCH", "ASH", "GEARHEAD", "SPARK", "SCRAP COLLECTOR!", "ROOKIE" };
        var data = new DemoLeaderboardData();
        for (int index = 0; index < names.Length; index++)
        {
            data.MostKills.Add(new DemoLeaderboardEntry
            {
                RunId = "preview-kills-" + index, Name = names[index], Kills = 2387 - index * 173,
                SurvivalSeconds = 1092 - index * 49, CompletedOrder = index + 1, Victory = index % 2 == 0
            });
            data.LongestSurvival.Add(new DemoLeaderboardEntry
            {
                RunId = "preview-time-" + index, Name = names[(index + 3) % names.Length], Kills = 2045 - index * 109,
                SurvivalSeconds = 1694 - index * 93, CompletedOrder = index + 11, Victory = index % 2 != 0
            });
        }
        return data;
    }

    private static Rect ScreenRect(RectTransform rect, Camera camera)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector2 minimum = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 maximum = new(float.NegativeInfinity, float.NegativeInfinity);
        foreach (Vector3 corner in corners)
        {
            Vector2 point = camera.WorldToScreenPoint(corner);
            minimum = Vector2.Min(minimum, point);
            maximum = Vector2.Max(maximum, point);
        }
        return Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
    }

    private static RectTransform Named(GameObject root, string name) => root.GetComponentsInChildren<RectTransform>(true)
        .SingleOrDefault(transform => transform.name == name) ?? throw new InvalidOperationException("Missing authored menu object: " + name);

    private static T[] Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
}
#endif
