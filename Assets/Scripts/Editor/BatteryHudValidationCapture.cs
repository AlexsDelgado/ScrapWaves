#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Renders and measures the authored battery HUD without entering gameplay or saving its scene.</summary>
public static class BatteryHudValidationCapture
{
    private const string ScenePath = "Assets/Scenes/GameplayScene.unity";
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [Serializable] private sealed class Report
    {
        public string UnityVersion;
        public string Scene = ScenePath;
        public string Method = "Production GameplayScene opened as an isolated preview. Saved main-camera pose and authored HUD are rendered by Unity. Root canvases temporarily use ScreenSpaceCamera; original CanvasScaler rules are applied to each target size.";
        public string Limitations = "Controlled Edit Mode UI states, not recorded gameplay. Measurements temporarily show boss health bar and dialogue together to stress simultaneous layout; PNGs hide those two overlays to show a normal HUD state. Objective counts are assigned without emitting gameplay events. No GameManager, save or leaderboard method is invoked; no scene or asset is saved.";
        public Vector3 CameraPosition;
        public Vector3 CameraAngles;
        public float CameraFov;
        public string PanelHierarchy;
        public Vector2 Anchor;
        public Vector2 Offset;
        public Vector2 Size;
        public Color Background;
        public List<Frame> Frames = new();
    }

    [Serializable] private sealed class Frame
    {
        public int Width;
        public int Height;
        public int Batteries;
        public string DoorState;
        public string Text;
        public float CanvasScale;
        public float FontSize;
        public int Lines;
        public Rect PanelScreenBounds;
        public Rect TextScreenBounds;
        public Rect BossScreenBounds;
        public Rect DialogueScreenBounds;
        public bool PanelWithinViewport;
        public bool GlyphsWithinTextRect;
        public bool TextOverflow;
        public bool OverlapsBoss;
        public bool OverlapsDialogue;
        public string Image;
    }

    [MenuItem("Tools/ScrapWaves/Capture Battery HUD Validation")]
    public static void Capture()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Capture the battery HUD outside Play Mode.");
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            throw new InvalidOperationException("Battery HUD capture requires graphics; omit -nographics.");

        string output = Environment.GetEnvironmentVariable("SCRAPWAVES_BATTERY_HUD_PREVIEW_OUTPUT")
            ?? Path.GetFullPath(".utmp/battery-hud/preview");
        Directory.CreateDirectory(output);
        Scene previous = SceneManager.GetActiveScene();
        Scene scene = EditorSceneManager.OpenPreviewScene(ScenePath);
        RenderTexture target = null;
        RenderTexture previousTarget = RenderTexture.active;
        try
        {
            GameObject[] roots = scene.GetRootGameObjects();
            LevelExitHud hud = Find<LevelExitHud>(roots).Single();
            LevelExitObjective objective = Find<LevelExitObjective>(roots).Single();
            ExitDoor door = Find<ExitDoor>(roots).Single();
            GameObject panelObject = Get<GameObject>(hud, "_root");
            TextMeshProUGUI label = Get<TextMeshProUGUI>(hud, "_statusText");
            if (panelObject == null || label == null)
                throw new InvalidOperationException("The saved battery HUD is missing authored references.");
            RectTransform panel = panelObject.GetComponent<RectTransform>();
            Image background = panel.GetComponent<Image>();
            if (background == null)
                throw new InvalidOperationException("The saved battery panel needs its authored background Image.");
            Set(hud, "_exitObjective", objective);
            Set(hud, "_exitDoor", door);

            // Preserve graphics/layout, but prevent presenters from replacing the deliberate capture state.
            foreach (MonoBehaviour behaviour in Find<MonoBehaviour>(roots))
                if (behaviour != null && behaviour is not UIBehaviour) behaviour.enabled = false;
            foreach (Animator animator in Find<Animator>(roots)) animator.enabled = false;

            Camera savedCamera = Find<Camera>(roots).Single(c => c.CompareTag("MainCamera"));
            Camera camera = new GameObject("Battery HUD review camera", typeof(Camera)).GetComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
            camera.CopyFrom(savedCamera);
            camera.enabled = false;
            camera.transform.SetPositionAndRotation(savedCamera.transform.position, savedCamera.transform.rotation);
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.rect = new Rect(0f, 0f, 1f, 1f);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;

            Canvas[] canvases = Find<Canvas>(roots).Where(c => c.renderMode != RenderMode.WorldSpace).ToArray();
            foreach (Canvas canvas in canvases)
            {
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
                if (scaler != null) scaler.enabled = false;
            }
            Canvas panelCanvas = panel.GetComponentsInParent<Canvas>(true).Last();
            RectTransform boss = Find<RectTransform>(roots).SingleOrDefault(r => r.name == "BossHealthBar");
            RectTransform dialogue = Find<RectTransform>(roots).FirstOrDefault(r => r.name == "DialogueBox");
            ShowForLayout(panel);
            ShowForLayout(boss);
            ShowForLayout(dialogue);

            var report = new Report
            {
                UnityVersion = Application.unityVersion,
                CameraPosition = savedCamera.transform.position,
                CameraAngles = savedCamera.transform.eulerAngles,
                CameraFov = savedCamera.fieldOfView,
                PanelHierarchy = Hierarchy(panel),
                Anchor = panel.anchorMin,
                Offset = panel.anchoredPosition,
                Size = panel.sizeDelta,
                Background = background.color
            };

            foreach (Vector2Int size in new[] { new Vector2Int(1920, 1080), new Vector2Int(1280, 720),
                new Vector2Int(1920, 1200), new Vector2Int(2560, 1080), new Vector2Int(1280, 960) })
            {
                if (target != null) { camera.targetTexture = null; target.Release(); Object.DestroyImmediate(target); }
                target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32);
                target.Create();
                camera.targetTexture = target;
                camera.aspect = size.x / (float)size.y;
                foreach (Canvas canvas in canvases.Where(c => c.isRootCanvas))
                {
                    CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
                    canvas.scaleFactor = ResolveScale(scaler, size.x, size.y);
                    canvas.referencePixelsPerUnit = scaler != null ? scaler.referencePixelsPerUnit : 100f;
                }

                CaptureState(0, ExitDoorState.Locked, 0f, true);
                CaptureState(1, ExitDoorState.Locked, 0f, true);
                CaptureState(2, ExitDoorState.AwaitingActivation, 0f, false);
                CaptureState(2, ExitDoorState.Charging, .75f, false);
                CaptureState(2, ExitDoorState.Ready, 1f, false);

                void CaptureState(int count, ExitDoorState state, float progress, bool saveImage)
                {
                    Set(objective, "_keysCollected", count);
                    Set(door, "_state", state);
                    Set(door, "_chargeRemaining", Get<float>(door, "_chargeDurationSeconds") * (1f - progress));
                    typeof(LevelExitHud).GetMethod("Refresh", Hidden).Invoke(hud, null);
                    if (!panelObject.activeSelf)
                        throw new InvalidOperationException("Normal battery capture unexpectedly hid the HUD.");
                    if (count == 0 && label.text != "Find a way to escape")
                        throw new InvalidOperationException("Zero-battery prompt differs from the required exact text: " + label.text);
                    Canvas.ForceUpdateCanvases();
                    foreach (Canvas canvas in canvases.Where(c => c.isRootCanvas))
                        LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvas.transform);
                    foreach (TMP_Text text in Find<TMP_Text>(roots).Where(t => t.gameObject.activeInHierarchy))
                        text.ForceMeshUpdate();
                    Canvas.ForceUpdateCanvases();
                    label.ForceMeshUpdate();
                    // A camera render initializes native Canvas dimensions for the first target.
                    // Measure the layout that is actually drawn, not the previous Game View size.
                    camera.Render();
                    Rect panelBounds = ScreenRect(panel, camera);
                    Rect bossBounds = boss != null ? ScreenRect(boss, camera) : default;
                    Rect dialogueBounds = dialogue != null ? ScreenRect(dialogue, camera) : default;
                    string file = saveImage ? $"batteries-{count}-{size.x}x{size.y}.png" : null;
                    var frame = new Frame
                    {
                        Width = size.x, Height = size.y, Batteries = count, DoorState = state.ToString(), Text = label.text,
                        CanvasScale = panelCanvas.scaleFactor, FontSize = label.fontSize, Lines = label.textInfo.lineCount,
                        PanelScreenBounds = panelBounds, TextScreenBounds = ScreenRect(label.rectTransform, camera),
                        BossScreenBounds = bossBounds, DialogueScreenBounds = dialogueBounds,
                        PanelWithinViewport = Inside(panelBounds, new Rect(0f, 0f, size.x, size.y)),
                        GlyphsWithinTextRect = GlyphsFit(label), TextOverflow = label.isTextOverflowing,
                        OverlapsBoss = boss != null && panelBounds.Overlaps(bossBounds),
                        OverlapsDialogue = dialogue != null && panelBounds.Overlaps(dialogueBounds), Image = file
                    };
                    report.Frames.Add(frame);
                    if (saveImage)
                    {
                        bool bossWasActive = boss != null && boss.gameObject.activeSelf;
                        bool dialogueWasActive = dialogue != null && dialogue.gameObject.activeSelf;
                        try
                        {
                            if (boss != null) boss.gameObject.SetActive(false);
                            if (dialogue != null) dialogue.gameObject.SetActive(false);
                            Canvas.ForceUpdateCanvases();
                            camera.Render();
                            RenderTexture.active = target;
                            var pixels = new Texture2D(size.x, size.y, TextureFormat.RGB24, false);
                            pixels.ReadPixels(new Rect(0, 0, size.x, size.y), 0, 0);
                            pixels.Apply();
                            File.WriteAllBytes(Path.Combine(output, file), pixels.EncodeToPNG());
                            Object.DestroyImmediate(pixels);
                            RenderTexture.active = previousTarget;
                        }
                        finally
                        {
                            if (boss != null) boss.gameObject.SetActive(bossWasActive);
                            if (dialogue != null) dialogue.gameObject.SetActive(dialogueWasActive);
                        }
                    }
                }
            }

            File.WriteAllText(Path.Combine(output, "validation.json"), JsonUtility.ToJson(report, true));
            Frame[] failed = report.Frames.Where(f => !f.PanelWithinViewport || !f.GlyphsWithinTextRect
                || f.TextOverflow || f.OverlapsBoss || f.OverlapsDialogue).ToArray();
            if (failed.Length > 0)
                throw new InvalidOperationException("Battery HUD layout failed " + failed.Length + " captured states; inspect validation.json and PNGs.");
            Debug.Log("BATTERY_HUD_CAPTURE_COMPLETE " + output + " — " + report.Frames.Count + " layout states passed.");
        }
        finally
        {
            RenderTexture.active = previousTarget;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            foreach (CompactorDoorPresentation presentation in Find<CompactorDoorPresentation>(scene.GetRootGameObjects()))
                presentation.Dispose();
            EditorSceneManager.ClosePreviewScene(scene);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    private static bool GlyphsFit(TMP_Text label)
    {
        Rect rect = label.rectTransform.rect;
        const float tolerance = 1f;
        for (int i = 0; i < label.textInfo.characterCount; i++)
        {
            TMP_CharacterInfo character = label.textInfo.characterInfo[i];
            if (!character.isVisible) continue;
            if (character.bottomLeft.x < rect.xMin - tolerance || character.topRight.x > rect.xMax + tolerance
                || character.bottomLeft.y < rect.yMin - tolerance || character.topRight.y > rect.yMax + tolerance)
                return false;
        }
        return true;
    }

    private static bool Inside(Rect inner, Rect outer) => inner.xMin >= outer.xMin - 1f
        && inner.yMin >= outer.yMin - 1f && inner.xMax <= outer.xMax + 1f && inner.yMax <= outer.yMax + 1f;

    private static Rect ScreenRect(RectTransform rect, Camera camera)
    {
        var corners = new Vector3[4];
        rect.GetWorldCorners(corners);
        Vector2 min = new(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 max = new(float.NegativeInfinity, float.NegativeInfinity);
        foreach (Vector3 corner in corners)
        {
            Vector3 point = camera.WorldToScreenPoint(corner);
            min = Vector2.Min(min, point); max = Vector2.Max(max, point);
        }
        return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
    }

    private static float ResolveScale(CanvasScaler scaler, int width, int height)
    {
        if (scaler == null) return 1f;
        if (scaler.uiScaleMode == CanvasScaler.ScaleMode.ConstantPixelSize) return scaler.scaleFactor;
        if (scaler.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize)
            throw new InvalidOperationException("Capture supports the saved pixel or screen-size canvas scaling modes.");
        float x = width / scaler.referenceResolution.x;
        float y = height / scaler.referenceResolution.y;
        return scaler.screenMatchMode switch
        {
            CanvasScaler.ScreenMatchMode.Expand => Mathf.Min(x, y),
            CanvasScaler.ScreenMatchMode.Shrink => Mathf.Max(x, y),
            _ => Mathf.Pow(2f, Mathf.Lerp(Mathf.Log(x, 2f), Mathf.Log(y, 2f), scaler.matchWidthOrHeight))
        };
    }

    private static void ShowForLayout(RectTransform rect)
    {
        if (rect == null) return;
        for (Transform ancestor = rect; ancestor != null; ancestor = ancestor.parent)
            ancestor.gameObject.SetActive(true);
        foreach (CanvasGroup group in rect.GetComponentsInParent<CanvasGroup>(true)) group.alpha = 1f;
        foreach (CanvasGroup group in rect.GetComponentsInChildren<CanvasGroup>(true)) group.alpha = 1f;
    }

    private static T[] Find<T>(GameObject[] roots) where T : Component => roots.SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
    private static string Hierarchy(Transform transform) => transform.parent == null ? transform.name : Hierarchy(transform.parent) + "/" + transform.name;
    private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Hidden).GetValue(target);
    private static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target, value);
}
#endif
