#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Keeps graphics made by the one-time authoring tools valid after a scene reload.</summary>
public static class GameplayUiAuthoringAssets
{
    private const string Folder = "Assets/Art/UI/Authored";

    public static void PersistGraphics(Transform root)
    {
        var saved = new Dictionary<Sprite, Sprite>();
        foreach (Image graphic in root.GetComponentsInChildren<Image>(true))
        {
            Sprite source = graphic.sprite;
            if (source == null || EditorUtility.IsPersistent(source)) continue;
            // Legacy bar prefabs can carry a serialized copy of the old generated
            // white sprite without its texture. Their fill has always been solid.
            if (source.texture == null && graphic.type == Image.Type.Filled
                && graphic.GetComponentInParent<PlayerBarsHud>(true) != null)
            {
                graphic.sprite = HudUiFactory.WhiteSprite;
                EditorUtility.SetDirty(graphic);
                if (PrefabUtility.IsPartOfPrefabInstance(graphic))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(graphic);
                continue;
            }
            if (!saved.TryGetValue(source, out Sprite sprite))
            {
                try { sprite = PersistSprite(source); }
                catch (System.Exception error) { throw new System.InvalidOperationException($"Failed to save {AnimationUtility.CalculateTransformPath(graphic.transform, root)} ({graphic.type}) in {root.gameObject.scene.path}: {error.Message}", error); }
                saved.Add(source, sprite);
            }
            graphic.sprite = sprite;
            EditorUtility.SetDirty(graphic);
            if (PrefabUtility.IsPartOfPrefabInstance(graphic))
                PrefabUtility.RecordPrefabInstancePropertyModifications(graphic);
        }
    }

    private static Sprite PersistSprite(Sprite source)
    {
        Texture2D texture = source.texture;
        if (texture == null)
            throw new System.InvalidOperationException($"Cannot persist generated UI sprite {source.name}.");
        Texture2D readable = texture;
        if (!texture.isReadable)
        {
            RenderTexture previous = RenderTexture.active;
            RenderTexture temporary = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);
            try
            {
                Graphics.Blit(texture, temporary);
                RenderTexture.active = temporary;
                readable = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                readable.Apply();
            }
            finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(temporary); }
        }
        byte[] png = readable.EncodeToPNG();
        using var hash = SHA256.Create();
        string key = System.BitConverter.ToString(hash.ComputeHash(png)).Replace("-", "").Substring(0, 16);
        string settings = JsonUtility.ToJson(new SpriteSettings(source));
        string settingsKey = System.BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(settings))).Replace("-", "").Substring(0, 8);
        EnsureFolder(Folder);
        string path = $"{Folder}/Graphic_{key}_{settingsKey}.asset";
        Sprite existing = AssetDatabase.LoadAssetAtPath<Sprite>(path);
        if (existing != null)
        {
            if (readable != texture) Object.DestroyImmediate(readable);
            return existing;
        }
        Texture2D copy = Object.Instantiate(readable);
        if (readable != texture) Object.DestroyImmediate(readable);
        copy.name = "Texture";
        copy.hideFlags = HideFlags.None;
        Sprite sprite = Sprite.Create(copy, source.rect,
            new Vector2(source.pivot.x / source.rect.width, source.pivot.y / source.rect.height),
            source.pixelsPerUnit, 0, SpriteMeshType.FullRect, source.border);
        sprite.name = string.IsNullOrEmpty(source.name) ? "UI Graphic" : source.name;
        AssetDatabase.CreateAsset(sprite, path);
        AssetDatabase.AddObjectToAsset(copy, sprite);
        AssetDatabase.SaveAssetIfDirty(sprite);
        return sprite;
    }

    [System.Serializable]
    private struct SpriteSettings
    {
        public Rect rect;
        public Vector2 pivot;
        public Vector4 border;
        public float pixelsPerUnit;
        public SpriteSettings(Sprite sprite)
        { rect = sprite.rect; pivot = sprite.pivot; border = sprite.border; pixelsPerUnit = sprite.pixelsPerUnit; }
    }

    public static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
#endif
