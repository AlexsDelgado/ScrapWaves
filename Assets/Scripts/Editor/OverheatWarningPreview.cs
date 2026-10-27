#if UNITY_EDITOR
using System;
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

/// <summary>Renders saved warning/HUD objects in an isolated editor preview; never saves a scene.</summary>
public static class OverheatWarningPreview
{
    private const int Width = 1920;
    private const int Height = 1080;

    [MenuItem("ScrapWaves/UI/Capture Overheat Warning")]
    public static void Capture()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Capture the authored warning outside Play Mode.");
        if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            throw new InvalidOperationException("Warning capture requires graphics; omit -nographics.");

        string output = Environment.GetEnvironmentVariable("SCRAPWAVES_OVERHEAT_PREVIEW_OUTPUT")
            ?? Path.Combine(Path.GetTempPath(), "ScrapWavesOverheatWarning");
        Directory.CreateDirectory(output);
        Scene previous = SceneManager.GetActiveScene();
        Scene source = SceneManager.GetSceneByPath(UpperLevelOverheatAuthoring.ScenePath);
        bool opened = !source.IsValid() || !source.isLoaded;
        if (opened) source = EditorSceneManager.OpenScene(UpperLevelOverheatAuthoring.ScenePath, OpenSceneMode.Additive);
        Scene preview = EditorSceneManager.NewPreviewScene();
        RenderTexture target = null;
        RenderTexture previousTarget = RenderTexture.active;
        try
        {
            OverheatWarningHud saved = Find<OverheatWarningHud>(source).Single();
            Canvas sourceCanvas = saved.GetComponentsInParent<Canvas>(true).LastOrDefault();
            if (sourceCanvas == null) throw new InvalidOperationException("Saved warning has no Canvas ancestor.");
            GameObject hud = Object.Instantiate(sourceCanvas.gameObject);
            SceneManager.MoveGameObjectToScene(hud, preview);
            hud.name = sourceCanvas.gameObject.name;
            // Leave uGUI/TMP graphics and layout components enabled; silence game presenters only.
            foreach (MonoBehaviour behaviour in hud.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour is not UIBehaviour) behaviour.enabled = false;
            foreach (Animator animator in hud.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            hud.SetActive(true);

            Camera camera = new GameObject("Warning preview camera", typeof(Camera)).GetComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject, preview);
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(preview);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.055f, .075f, .065f);
            camera.orthographic = true;
            camera.orthographicSize = 5f;
            camera.transform.position = new Vector3(0f, 0f, -10f);
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            camera.targetTexture = target;
            foreach (Canvas canvas in hud.GetComponentsInChildren<Canvas>(true))
            {
                canvas.enabled = true;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
            }

            OverheatWarningHud warning = hud.GetComponentInChildren<OverheatWarningHud>(true);
            var fields = new SerializedObject(warning);
            GameObject root = fields.FindProperty("_root").objectReferenceValue as GameObject;
            TextMeshProUGUI text = fields.FindProperty("_text").objectReferenceValue as TextMeshProUGUI;
            TextMeshProUGUI repeat = fields.FindProperty("_repeatText").objectReferenceValue as TextMeshProUGUI;
            if (root == null || text == null || repeat == null || !root.transform.IsChildOf(hud.transform))
                throw new InvalidOperationException("The saved warning references are incomplete or outside its HUD.");
            if (root.GetComponent<RectMask2D>() == null)
                throw new InvalidOperationException("The saved warning must clip its scrolling labels.");
            // The live presenter hides the objective during preparation. Mirror only that view state.
            OverheatObjectiveHud objective = hud.GetComponentInChildren<OverheatObjectiveHud>(true);
            if (objective != null)
            {
                var objectiveFields = new SerializedObject(objective);
                var objectiveRoot = objectiveFields.FindProperty("_root").objectReferenceValue as GameObject;
                if (objectiveRoot != null) objectiveRoot.SetActive(false);
            }
            for (Transform ancestor = root.transform; ancestor != null && ancestor.IsChildOf(hud.transform); ancestor = ancestor.parent)
                ancestor.gameObject.SetActive(true);
            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
                if (graphic.raycastTarget) throw new InvalidOperationException("Warning blocks input: " + graphic.name);

            Canvas.ForceUpdateCanvases();
            var evidence = new StringBuilder();
            evidence.AppendLine("Saved scene: " + source.path);
            evidence.AppendLine("Saved warning/controller: " + Hierarchy(saved.transform));
            evidence.AppendLine("1920 x 1080 editor renders of saved UI; no full gameplay verification.");
            evidence.AppendLine("Scroll speed=" + warning.ScrollSpeed + "; font size=" + warning.FontSize +
                "; bar height=" + warning.BarHeight + "; all warning graphics non-raycast.");
            evidence.AppendLine("Two authored labels repeat without a display budget. Real lifetime follows manager preparation and is covered by lifecycle tests.");
            CaptureFrame(false, "motion-01-upstairs-start.png", 0f);
            float right = text.rectTransform.anchoredPosition.x;
            CaptureFrame(false, "motion-02-upstairs-one-second.png", 1f);
            float center = text.rectTransform.anchoredPosition.x;
            CaptureFrame(false, "motion-03-upstairs-two-seconds.png", 2f);
            float left = text.rectTransform.anchoredPosition.x;
            if (!(right > center && center > left)) throw new InvalidOperationException("Saved text must travel right to left.");
            CaptureFrame(false, "motion-04-upstairs-after-one-minute.png", 60f);
            CanvasGroup visibility = fields.FindProperty("_visibility").objectReferenceValue as CanvasGroup;
            if (visibility == null || visibility.alpha != 1f) throw new InvalidOperationException("Pending upstairs preview must not expire.");
            CaptureFrame(true, "motion-05-ground-message-start.png", 0f);
            OverheatManager manager = fields.FindProperty("_overheatManager").objectReferenceValue as OverheatManager;
            float preparation = manager != null ? manager.ConfiguredPreparationDuration : 0f;
            CaptureFrame(true, "motion-06-ground-message-before-encounter.png", Mathf.Max(0f, preparation - .1f));
            CaptureFrame(true, "motion-07-encounter-hidden.png", -1f);
            if (visibility.alpha != 0f) throw new InvalidOperationException("Explicit hidden preview remained visible.");
            evidence.AppendLine("Horizontal positions=" + right + ", " + center + ", " + left +
                "; upstairs still visible at 60 seconds; ground samples use manager duration=" + preparation + ".");
            File.WriteAllText(Path.Combine(output, "overheat-warning-motion-preview.txt"), evidence.ToString());
            Debug.Log("OVERHEAT_WARNING_MOTION_PREVIEW: " + output + "; continuous upstairs marquee, larger clipped text and ground preparation message rendered; no input blockers.");

            void CaptureFrame(bool onGround, string filename, float sampleTime)
            {
                Canvas.ForceUpdateCanvases();
                warning.PreviewAt(sampleTime, onGround);
                foreach (TMP_Text label in hud.GetComponentsInChildren<TMP_Text>(false)) label.ForceMeshUpdate();
                Canvas.ForceUpdateCanvases();
                text.ForceMeshUpdate();
                if (text.enableAutoSizing || repeat.enableAutoSizing)
                    throw new InvalidOperationException("Warning labels must retain their readable authored font size.");
                if (text.textWrappingMode != TextWrappingModes.NoWrap || repeat.textWrappingMode != TextWrappingModes.NoWrap)
                    throw new InvalidOperationException("Warning labels must remain on one scrolling line.");
                if (sampleTime >= 0f && text.preferredWidth <= ((RectTransform)root.transform).rect.width)
                    throw new InvalidOperationException("The default warning message should be wider than its viewport.");

                camera.Render();
                RenderTexture.active = target;
                Texture2D image = new(Width, Height, TextureFormat.RGB24, false);
                try
                {
                    image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
                    image.Apply();
                    File.WriteAllBytes(Path.Combine(output, filename), image.EncodeToPNG());
                }
                finally { Object.DestroyImmediate(image); RenderTexture.active = previousTarget; }
                evidence.AppendLine(filename + ": " + text.text + "; fontSize=" + text.fontSize +
                    "; rect=" + text.rectTransform.rect + "; preferred width=" + text.preferredWidth +
                    "; viewport=" + ((RectTransform)root.transform).rect + "; intentionally clipped, auto-size=false.");
            }
        }
        finally
        {
            RenderTexture.active = previousTarget;
            if (target != null) { target.Release(); Object.DestroyImmediate(target); }
            EditorSceneManager.ClosePreviewScene(preview);
            if (opened) EditorSceneManager.CloseScene(source, true);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
    }

    private static T[] Find<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static string Hierarchy(Transform value) => value.parent == null ? value.name : Hierarchy(value.parent) + "/" + value.name;
}
#endif
