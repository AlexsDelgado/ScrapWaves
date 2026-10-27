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
            if (root == null || text == null || !root.transform.IsChildOf(hud.transform))
                throw new InvalidOperationException("The saved warning references are incomplete or outside its HUD.");
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
            evidence.AppendLine("Cycle seconds=" + warning.CycleSeconds + "; display budget=" + warning.DisplayBudgetSeconds + "; all warning graphics non-raycast.");
            float cycle = warning.CycleSeconds;
            CaptureFrame(false, "motion-01-entering.png", cycle * .25f);
            float right = text.rectTransform.anchoredPosition.x;
            CaptureFrame(false, "motion-02-centered.png", cycle * .5f);
            float center = text.rectTransform.anchoredPosition.x;
            CaptureFrame(false, "motion-03-exiting.png", cycle * .75f);
            float left = text.rectTransform.anchoredPosition.x;
            if (!(right > center && center > left)) throw new InvalidOperationException("Saved text must travel right to left.");
            CaptureFrame(true, "motion-04-second-cycle-ground.png", cycle * 1.5f);
            CaptureFrame(false, "motion-05-budget-expired.png", warning.DisplayBudgetSeconds);
            CanvasGroup visibility = fields.FindProperty("_visibility").objectReferenceValue as CanvasGroup;
            if (visibility == null || visibility.alpha != 0f) throw new InvalidOperationException("Warning did not hide at its saved budget.");
            warning.PreviewAt(warning.DisplayBudgetSeconds + cycle, false);
            if (visibility.alpha != 0f) throw new InvalidOperationException("Exhausted preview reappeared.");
            evidence.AppendLine("Horizontal positions=" + right + ", " + center + ", " + left + "; hidden at/after budget.");
            File.WriteAllText(Path.Combine(output, "overheat-warning-motion-preview.txt"), evidence.ToString());
            Debug.Log("OVERHEAT_WARNING_MOTION_PREVIEW: " + output + "; moving text, second cycle, both messages and budget expiry rendered; no text overflow or input blockers.");

            void CaptureFrame(bool onGround, string filename, float sampleTime)
            {
                Canvas.ForceUpdateCanvases();
                warning.PreviewAt(sampleTime, onGround);
                foreach (TMP_Text label in hud.GetComponentsInChildren<TMP_Text>(false)) label.ForceMeshUpdate();
                Canvas.ForceUpdateCanvases();
                text.ForceMeshUpdate();
                if (text.isTextOverflowing) throw new InvalidOperationException("Warning text overflows its moving rect: " + text.text);

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
                evidence.AppendLine(filename + ": " + text.text + "; fontSize=" + text.fontSize + "; rect=" + text.rectTransform.rect + "; overflow=false.");
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
