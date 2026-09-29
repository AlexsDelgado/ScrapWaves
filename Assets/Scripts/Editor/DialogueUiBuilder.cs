#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Arma el panel de diálogo de jefes dentro de <c>GameplayHud V2</c>, arriba al centro, con el arte de
/// Assets/Art/UI/Dialogue (ver finish_dialog.py) y las ventanas de ArtSource/UI/V2B/HudLayout.json. También
/// crea, si faltan, el hablante y las entradas de ejemplo del Stalker para que los game designers partan de ahí.
/// </summary>
public static class DialogueUiBuilder
{
    private const string PrefabPath = "Assets/Prefabs/UI/GameplayHud V2.prefab";
    private const string ArtFolder = "Assets/Art/UI/Dialogue";
    private const string LayoutPath = "ArtSource/UI/V2B/HudLayout.json";
    private const string DataFolder = "Assets/ScriptableObjects/Dialogue";
    private const string BoxName = "DialogueBox";

    /// <summary>Mismas unidades de canvas por píxel de render que el HUD (HudArtApplier.Scale).</summary>
    private const float Scale = 0.45f;
    private const float TopOffset = 24f;

    [System.Serializable] private class RectEntry { public string name; public int[] rect; }
    [System.Serializable] private class Piece { public string name; public int[] size; public RectEntry[] rects; }
    [System.Serializable] private class Layout { public Piece[] pieces; }

    [MenuItem("ScrapWaves/UI/Build Dialogue Box In GameplayHud V2")]
    public static void Build()
    {
        ConfigureImporters();
        Piece piece = LoadPiece();
        DialogueSet set = EnsureSampleData();

        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform canvas = root.transform.Find("GameplayHudCanvas");
            Transform old = canvas.Find(BoxName);
            if (old != null)
                Object.DestroyImmediate(old.gameObject);

            var box = new GameObject(BoxName, typeof(RectTransform)).GetComponent<RectTransform>();
            box.SetParent(canvas, false);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 1f);
            box.anchoredPosition = new Vector2(0f, -TopOffset);
            box.sizeDelta = new Vector2(piece.size[0], piece.size[1]) * Scale;

            RectTransform panel = Child(box, "Panel");
            HudUiWire.StretchFull(panel);
            var group = panel.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.blocksRaycasts = group.interactable = false;
            AddImage(panel, Art("DialogPanel"));

            Image portrait = AddImage(Place(panel, "Portrait", Rect(piece, "Portrait")), Art("Portrait_Stalker_Hidden"), true);
            TextMeshProUGUI speaker = AddText(Place(panel, "SpeakerName", Rect(piece, "Speaker"), 10f, 0f), 20f,
                TextAlignmentOptions.MidlineLeft, FontStyles.Bold | FontStyles.UpperCase, false, "???");
            TextMeshProUGUI text = AddText(Place(panel, "Text", Rect(piece, "Text"), 14f, 8f), 21f,
                TextAlignmentOptions.TopLeft, FontStyles.Bold, true, string.Empty);
            Image signal = AddImage(Place(panel, "Signal", Rect(piece, "Signal"), -6f, -6f), Art("DialogSignal"));

            var ui = box.gameObject.AddComponent<DialogueBoxUI>();
            ui.Wire(group, panel, portrait, speaker, text, signal);
            var director = box.gameObject.AddComponent<DialogueDirector>();
            var so = new SerializedObject(director);
            so.FindProperty("_set").objectReferenceValue = set;
            so.FindProperty("_box").objectReferenceValue = ui;
            so.ApplyModifiedPropertiesWithoutUndo();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"[{nameof(DialogueUiBuilder)}] Panel de diálogo armado en {PrefabPath} con {AssetDatabase.GetAssetPath(set)}.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static void ConfigureImporters()
    {
        AssetDatabase.Refresh();
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { ArtFolder }))
        {
            if (AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid)) is not TextureImporter ti
                || ti.textureType == TextureImporterType.Sprite && !ti.mipmapEnabled)
                continue;
            ti.textureType = TextureImporterType.Sprite;
            ti.spriteImportMode = SpriteImportMode.Single;
            ti.mipmapEnabled = false;
            ti.alphaIsTransparency = true;
            ti.SaveAndReimport();
        }
    }

    private static Piece LoadPiece()
    {
        string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), LayoutPath);
        foreach (Piece p in JsonUtility.FromJson<Layout>(File.ReadAllText(path)).pieces)
            if (p.name == "DialogPanel")
                return p;
        throw new System.InvalidOperationException($"{LayoutPath} no tiene DialogPanel. Corré blender_build_hud.py --variant V2B.");
    }

    private static int[] Rect(Piece piece, string name)
    {
        foreach (RectEntry r in piece.rects)
            if (r.name == name)
                return r.rect;
        throw new System.InvalidOperationException($"DialogPanel no tiene la ventana {name}.");
    }

    private static Sprite Art(string name)
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtFolder}/{name}.png");
        if (sprite == null)
            throw new FileNotFoundException($"Falta {ArtFolder}/{name}.png. Corré finish_dialog.py.");
        return sprite;
    }

    private static RectTransform Child(Transform parent, string name)
    {
        var rt = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    /// <summary>Ubica un hijo sobre una ventana del layout (px desde arriba-izquierda), con márgenes internos.</summary>
    private static RectTransform Place(Transform parent, string name, int[] r, float padX = 0f, float padY = 0f)
    {
        RectTransform rt = Child(parent, name);
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(r[0] * Scale + padX, -(r[1] * Scale + padY));
        rt.sizeDelta = new Vector2(r[2] * Scale - 2f * padX, r[3] * Scale - 2f * padY);
        return rt;
    }

    private static Image AddImage(RectTransform rt, Sprite sprite, bool preserveAspect = false)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = sprite;
        img.preserveAspect = preserveAspect;
        img.raycastTarget = false;
        return img;
    }

    private static TextMeshProUGUI AddText(RectTransform rt, float size, TextAlignmentOptions align, FontStyles style,
        bool wrap, string text)
    {
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(tmp);
        tmp.fontSize = size;
        tmp.fontStyle = style;
        tmp.alignment = align;
        tmp.color = new Color(0.92f, 0.92f, 0.9f, 1f);
        tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        tmp.overflowMode = TextOverflowModes.Truncate;
        tmp.raycastTarget = false;
        tmp.text = text;
        return tmp;
    }

    // --- Datos de ejemplo: el Stalker del boceto de diálogo, más dos guías por inactividad. ---

    private static DialogueSet EnsureSampleData()
    {
        EnsureFolder(DataFolder);
        var stalker = LoadOrCreate<DialogueSpeaker>($"{DataFolder}/Speaker_Stalker.asset", s =>
        {
            var so = new SerializedObject(s);
            so.FindProperty("_displayName").stringValue = "Stalker";
            so.FindProperty("_hiddenName").stringValue = "???";
            so.FindProperty("_hiddenPortrait").objectReferenceValue = Art("Portrait_Stalker_Hidden");
            so.FindProperty("_revealedPortrait").objectReferenceValue = Art("Portrait_Stalker_Revealed");
            so.ApplyModifiedPropertiesWithoutUndo();
        });
        AssignVoiceIfMissing(stalker, "Stalker");

        DialogueEntry Entry(string file, DialogueTrigger trigger, string page, int occurrence = 0, string boss = null,
            float seconds = 30f, bool reveals = false, int priority = 0, bool once = true, float delay = 0f,
            bool craftingGuide = false) =>
            LoadOrCreate<DialogueEntry>($"{DataFolder}/{file}.asset", e =>
            {
                var so = new SerializedObject(e);
                so.FindProperty("_speaker").objectReferenceValue = stalker;
                SerializedProperty pages = so.FindProperty("_pages");
                pages.arraySize = 1;
                pages.GetArrayElementAtIndex(0).stringValue = page;
                so.FindProperty("_trigger").enumValueIndex = (int)trigger;
                so.FindProperty("_occurrence").intValue = occurrence;
                so.FindProperty("_bossFilter").stringValue = boss ?? string.Empty;
                so.FindProperty("_seconds").floatValue = seconds;
                so.FindProperty("_revealsSpeaker").boolValue = reveals;
                so.FindProperty("_priority").intValue = priority;
                so.FindProperty("_oncePerRun").boolValue = once;
                so.FindProperty("_delay").floatValue = delay;
                so.FindProperty("_showsCraftingGuide").boolValue = craftingGuide;
                so.ApplyModifiedPropertiesWithoutUndo();
            });

        DialogueEntry[] entries =
        {
            Entry("Dialogue_Stalker_RunStart", DialogueTrigger.RunStart,
                "You want MY battery? An unworthy little GRUB like you doesn't DESERVE MY ATTENTION.", delay: 1.5f),
            Entry("Dialogue_Stalker_FirstOverheat", DialogueTrigger.OverheatFinished,
                "You think you're the shit for that? Don't make me laugh... Barely more than a piece of scrap for me to eat.",
                occurrence: 1),
            Entry("Dialogue_Stalker_Spawn", DialogueTrigger.BossSpawned,
                "FINE... YOU WANT MY BATTERY? COME AND GET IT.", boss: "Stalker", reveals: true, priority: 10),
            Entry("Dialogue_Stalker_Guide_NoKills", DialogueTrigger.NoKillsFor,
                "Hiding already? The scrap won't melt itself, grub. Go break something.", seconds: 25f, once: false),
            Entry("Dialogue_Stalker_Guide_Crafting", DialogueTrigger.NoCraftingFor,
                "All that junk in your pockets and you still haven't upgraded a single weapon? The crafting station is RIGHT THERE, grub.",
                seconds: 60f, once: false, craftingGuide: true),
        };

        var set = LoadOrCreate<DialogueSet>($"{DataFolder}/DialogueSet_GameplayScene.asset", s =>
        {
            var so = new SerializedObject(s);
            SerializedProperty list = so.FindProperty("_entries");
            list.arraySize = entries.Length;
            for (int i = 0; i < entries.Length; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = entries[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        });
        AssetDatabase.SaveAssets();
        return set;
    }

    private const string VoiceFolder = "Assets/Audio/Dialogue";

    /// <summary>Carga los pitidos Voice_&lt;voz&gt;_N.wav (make_voice_blips.py) si el hablante todavía no tiene voz.</summary>
    private static void AssignVoiceIfMissing(DialogueSpeaker speaker, string voice)
    {
        var so = new SerializedObject(speaker);
        SerializedProperty clips = so.FindProperty("_voiceBlips");
        if (clips.arraySize > 0)
            return;
        string[] guids = AssetDatabase.FindAssets($"Voice_{voice}_ t:AudioClip", new[] { VoiceFolder });
        clips.arraySize = guids.Length;
        for (int i = 0; i < guids.Length; i++)
            clips.GetArrayElementAtIndex(i).objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guids[i]));
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(speaker);
    }

    /// <summary>Carga el asset o lo crea con <paramref name="init"/>; nunca pisa uno que ya existe.</summary>
    private static T LoadOrCreate<T>(string path, System.Action<T> init) where T : ScriptableObject
    {
        var asset = AssetDatabase.LoadAssetAtPath<T>(path);
        if (asset != null)
            return asset;
        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        init(asset);
        EditorUtility.SetDirty(asset);
        return asset;
    }

    private static void EnsureFolder(string folder)
    {
        string current = "Assets";
        foreach (string part in folder.Split('/')[1..])
        {
            string next = $"{current}/{part}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, part);
            current = next;
        }
    }
}
#endif
