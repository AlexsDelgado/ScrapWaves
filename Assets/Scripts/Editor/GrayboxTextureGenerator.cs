using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Escribe albedos tileables para los cinco materiales base del graybox y los asigna en Map.
/// </summary>
public static class GrayboxTextureGenerator
{
    private const int Size = 1024;
    private const string TextureFolder = "Assets/Art/Level/Graybox/Textures";
    private const string MaterialFolder = "Assets/Art/Level/Graybox";
    private const string ScenePath = "Assets/Scenes/GameplayScene.unity";

    private static readonly Color32 Rust = new(107, 83, 68, 255);
    private static readonly Color32 RustDark = new(62, 42, 34, 255);
    private static readonly Color32 RustLight = new(138, 117, 100, 255);
    private static readonly Color32 PaintGreen = new(112, 124, 86, 255);
    private static readonly Color32 PaintYellow = new(176, 138, 48, 255);
    private static readonly Color32 PaintOrange = new(184, 102, 42, 255);
    private static readonly Color32 PaintRed = new(168, 48, 36, 255);
    private static readonly Color32 Hazard = new(214, 168, 36, 255);
    private static readonly Color32 Cloth = new(74, 63, 56, 255);
    private static readonly Color32 ClothDark = new(42, 34, 30, 255);
    private static readonly Color32 ClothLight = new(108, 94, 82, 255);
    private static readonly Color32 Cable = new(42, 46, 40, 255);
    private static readonly Color32 CableLight = new(78, 84, 72, 255);
    private static readonly Color32 Plate = new(110, 115, 112, 255);
    private static readonly Color32 Seam = new(58, 61, 59, 255);
    private static readonly Color32 Bolt = new(150, 154, 148, 255);

    /// <summary>Un archivo por llamada, para no bloquear el editor con las seis texturas juntas.</summary>
    public static string GenerateStep(int step)
    {
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "Art/Level/Graybox/Textures"));
        switch (step)
        {
            case 0:
                WriteAlbedo("GB_MetalOxido_Albedo.png", PaintMetal);
                return "metal-albedo";
            case 1:
                WriteLinear("GB_MetalOxido_Metallic.png", PaintMetalMask);
                return "metal-mask";
            case 2:
                WriteAlbedo("GB_PlasticoReciclado_Albedo.png", PaintPlastic);
                return "plastic";
            case 3:
                WriteAlbedo("GB_TelaRota_Albedo.png", PaintCloth);
                return "cloth";
            case 4:
                WriteAlbedo("GB_Cables_Albedo.png", PaintCables);
                return "cables";
            case 5:
                WriteAlbedo("GB_Mecanica_Albedo.png", PaintMechanical);
                return "mechanical";
            default:
                AssignToMaterialsAndScene();
                return "assigned";
        }
    }

    [MenuItem("Tools/Graybox/Generate Tileable Textures")]
    public static void GenerateAndAssign()
    {
        Directory.CreateDirectory(Path.Combine(Application.dataPath, "Art/Level/Graybox/Textures"));

        WriteAlbedo("GB_MetalOxido_Albedo.png", PaintMetal);
        WriteLinear("GB_MetalOxido_Metallic.png", PaintMetalMask);
        WriteAlbedo("GB_PlasticoReciclado_Albedo.png", PaintPlastic);
        WriteAlbedo("GB_TelaRota_Albedo.png", PaintCloth);
        WriteAlbedo("GB_Cables_Albedo.png", PaintCables);
        WriteAlbedo("GB_Mecanica_Albedo.png", PaintMechanical);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        AssignToMaterialsAndScene();
    }

    private static void AssignToMaterialsAndScene()
    {
        AssignMaterial("GB_MetalOxido", "GB_MetalOxido_Albedo.png", "GB_MetalOxido_Metallic.png");
        AssignMaterial("GB_PlasticoReciclado", "GB_PlasticoReciclado_Albedo.png", null);
        AssignMaterial("GB_TelaRota", "GB_TelaRota_Albedo.png", null);
        AssignMaterial("GB_Cables", "GB_Cables_Albedo.png", null);
        AssignMaterial("GB_Mecanica", "GB_Mecanica_Albedo.png", null);

        AssignSceneTiling();
        Debug.Log("Texturas tileables del graybox generadas y asignadas.");
    }

    private static void WriteAlbedo(string fileName, System.Func<float, float, Color32> paint)
    {
        WriteTexture(fileName, paint, sRgb: true);
    }

    private static void WriteLinear(string fileName, System.Func<float, float, Color32> paint)
    {
        WriteTexture(fileName, paint, sRgb: false);
    }

    private static void WriteTexture(string fileName, System.Func<float, float, Color32> paint, bool sRgb)
    {
        var pixels = new Color32[Size * Size];
        for (int y = 0; y < Size; y++)
        {
            float v = y / (float)Size;
            for (int x = 0; x < Size; x++)
            {
                float u = x / (float)Size;
                pixels[y * Size + x] = paint(u, v);
            }
        }

        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
        texture.SetPixels32(pixels);
        texture.Apply(false, false);
        string assetPath = TextureFolder + "/" + fileName;
        File.WriteAllBytes(Path.Combine(Application.dataPath, "Art/Level/Graybox/Textures/" + fileName), texture.EncodeToPNG());
        Object.DestroyImmediate(texture);

        AssetDatabase.ImportAsset(assetPath);
        var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        importer.textureType = TextureImporterType.Default;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = true;
        importer.sRGBTexture = sRgb;
        importer.maxTextureSize = Size;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    private static void AssignMaterial(string materialName, string albedoFile, string metallicFile)
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/" + materialName + ".mat");
        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "/" + albedoFile);
        if (material == null || albedo == null)
            throw new System.InvalidOperationException("Falta " + materialName + " o " + albedoFile);

        material.SetTexture("_BaseMap", albedo);
        material.SetTexture("_MainTex", albedo);
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_Color", Color.white);
        material.SetTextureScale("_BaseMap", Vector2.one);
        material.SetTextureScale("_MainTex", Vector2.one);

        if (!string.IsNullOrEmpty(metallicFile))
        {
            var mask = AssetDatabase.LoadAssetAtPath<Texture2D>(TextureFolder + "/" + metallicFile);
            material.SetTexture("_MetallicGlossMap", mask);
            material.SetTextureScale("_MetallicGlossMap", Vector2.one);
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.SetFloat("_Metallic", 1f);
            material.SetFloat("_Smoothness", 1f);
        }

        EditorUtility.SetDirty(material);
    }

    private static void AssignSceneTiling()
    {
        Scene scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath)
            scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        var map = GameObject.Find("Map");
        if (map == null)
            throw new System.InvalidOperationException("No está el objeto Map.");

        for (int i = 0; i < map.transform.childCount; i++)
        {
            var child = map.transform.GetChild(i);
            if (!child.TryGetComponent(out Renderer renderer) || renderer.sharedMaterial == null)
                continue;

            string name = renderer.sharedMaterial.name;
            if (!TryTileSettings(name, out float meters, out float maxRepeats, out bool metallic))
            {
                if (child.TryGetComponent(out GrayboxWorldTiling leftover))
                    Object.DestroyImmediate(leftover);
                renderer.SetPropertyBlock(null);
                continue;
            }

            var tiling = child.GetComponent<GrayboxWorldTiling>();
            if (tiling == null)
                tiling = Undo.AddComponent<GrayboxWorldTiling>(child.gameObject);
            Undo.RecordObject(tiling, "Graybox texture tiling");
            tiling.Configure(meters, maxRepeats, metallic);
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    private static bool TryTileSettings(string materialName, out float metersPerTile, out float maxRepeats, out bool tileMetallic)
    {
        tileMetallic = false;
        switch (materialName)
        {
            case "GB_MetalOxido":
                metersPerTile = 48f;
                maxRepeats = 4f;
                tileMetallic = true;
                return true;
            case "GB_PlasticoReciclado":
            case "GB_Mecanica":
                metersPerTile = 48f;
                maxRepeats = 4f;
                return true;
            case "GB_TelaRota":
            case "GB_Cables":
                metersPerTile = 16f;
                maxRepeats = 6f;
                return true;
            default:
                metersPerTile = 0f;
                maxRepeats = 1f;
                return false;
        }
    }

    private struct PlateSample
    {
        public Color32 Albedo;
        public float Metallic;
        public float Smoothness;
    }

    private static PlateSample SamplePaintedPlate(float u, float v, int seed, bool allowRed)
    {
        const int cells = 2;
        float x = u * cells;
        float y = v * cells;
        int cx = Mathf.FloorToInt(x);
        int cy = Mathf.FloorToInt(y);
        float fu = x - cx;
        float fv = y - cy;
        int wx = Mod(cx, cells);
        int wy = Mod(cy, cells);
        float pick = Hash01(wx, wy, seed);

        Color32 paint = PickPlateColor(pick, allowRed);
        float shade = Seamless(u, v, 3f, seed * 0.17f);
        Color32 color = Lerp(paint, RustDark, shade * 0.16f);

        float chip = Fbm(u, v, 5f, 2, seed * 0.31f);
        float edge = Mathf.Min(Mathf.Min(fu, fv), Mathf.Min(1f - fu, 1f - fv));
        bool eaten = chip > 0.74f || (edge < 0.12f && chip > 0.58f);
        if (eaten)
            color = Lerp(RustDark, Rust, chip);

        bool triangle = allowRed && pick > 0.86f && InsideTriangle(fu, fv);
        bool stripe = pick > 0.58f && pick <= 0.7f && fu > 0.14f && fu < 0.86f && fv > 0.4f && fv < 0.5f;
        if (triangle || stripe)
            color = Hazard;

        bool seam = fu < 0.035f || fv < 0.035f;
        bool rivet = (fu < 0.028f && fv > 0.08f && fv < 0.92f && IsRivet(fv))
            || (fv < 0.028f && fu > 0.08f && fu < 0.92f && IsRivet(fu));
        if (seam)
            color = Seam;
        if (rivet)
            color = Bolt;

        float scratchBand = Mathf.Abs(Mathf.Sin((v * 6f + shade) * Mathf.PI * 2f));
        if (!seam && !rivet && scratchBand < 0.05f && Seamless(u, v, 4f, seed + 1.2f) > 0.62f)
            color = Lerp(color, RustLight, 0.55f);

        float metallic = eaten ? 0.62f : 0.16f;
        float smoothness = eaten ? 0.2f : 0.34f;
        if (seam)
        {
            metallic = 0.84f;
            smoothness = 0.42f;
        }
        if (rivet)
        {
            metallic = 0.92f;
            smoothness = 0.6f;
        }

        return new PlateSample { Albedo = color, Metallic = metallic, Smoothness = smoothness };
    }

    private static Color32 PickPlateColor(float pick, bool allowRed)
    {
        if (pick < 0.3f)
            return PaintGreen;
        if (pick < 0.52f)
            return PaintYellow;
        if (pick < 0.74f)
            return PaintOrange;
        if (allowRed && pick > 0.86f)
            return PaintRed;
        return Rust;
    }

    private static bool InsideTriangle(float fu, float fv)
    {
        float nx = (fu - 0.28f) / 0.44f;
        float ny = (fv - 0.32f) / 0.4f;
        return ny >= 0f && ny <= 1f && nx >= ny * 0.5f && nx <= 1f - ny * 0.5f;
    }

    private static bool IsRivet(float along)
    {
        float local = Frac(along * 5f);
        return local > 0.38f && local < 0.62f;
    }

    private static Color32 PaintMetal(float u, float v) => SamplePaintedPlate(u, v, 11, true).Albedo;

    private static Color32 PaintMetalMask(float u, float v)
    {
        PlateSample plate = SamplePaintedPlate(u, v, 11, true);
        return new Color32((byte)(plate.Metallic * 255f), 0, 0, (byte)(plate.Smoothness * 255f));
    }

    private static Color32 PaintPlastic(float u, float v) => SamplePaintedPlate(u, v, 47, false).Albedo;

    private static Color32 PaintCloth(float u, float v)
    {
        float threadU = Mathf.Abs(Mathf.Sin(u * 8f * Mathf.PI * 2f));
        float threadV = Mathf.Abs(Mathf.Sin(v * 8f * Mathf.PI * 2f));
        float weave = Mathf.Max(threadU, threadV * 0.85f);
        float hole = Fbm(u, v, 3f, 2, 7.7f);
        Color32 color = Lerp(ClothDark, ClothLight, weave);
        if (hole > 0.62f)
            color = Lerp(color, ClothDark, Mathf.InverseLerp(0.62f, 0.92f, hole));
        return Lerp(color, Cloth, 0.2f);
    }

    private static Color32 PaintCables(float u, float v)
    {
        float ridge = Mathf.Abs(Mathf.Sin(v * 4f * Mathf.PI * 2f));
        float wobble = Seamless(u, v, 3f, 6.2f);
        float along = Seamless(u, v, 6f, 2.8f);
        Color32 color = Lerp(Cable, CableLight, Mathf.Pow(ridge, 1.6f) * 0.85f);
        return Lerp(color, Cable, wobble * 0.15f + along * 0.08f);
    }

    private static Color32 PaintMechanical(float u, float v)
    {
        const float cells = 3f;
        float fu = Frac(u * cells);
        float fv = Frac(v * cells);
        bool seam = fu < 0.06f || fv < 0.06f;
        float boltU = Mathf.Abs(fu - 0.16f);
        float boltV = Mathf.Abs(fv - 0.16f);
        bool bolt = boltU < 0.07f && boltV < 0.07f;
        float grain = Seamless(u, v, 4f, 11.2f);

        if (bolt)
            return Lerp(Bolt, Plate, grain * 0.2f);
        if (seam)
            return Seam;
        return Lerp(Plate, new Color32(96, 100, 98, 255), grain * 0.35f);
    }

    /// <summary>Ruido periódico en UV 0-1: la grilla cierra en los bordes, así que la textura se puede repetir.</summary>
    private static float Seamless(float u, float v, float frequency, float seed)
    {
        int cells = Mathf.Max(1, Mathf.RoundToInt(frequency));
        int seedInt = Mathf.Abs(Mathf.RoundToInt(seed * 100f));
        float x = u * cells;
        float y = v * cells;
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        float tx = x - x0;
        float ty = y - y0;
        tx = tx * tx * (3f - 2f * tx);
        ty = ty * ty * (3f - 2f * ty);
        int x1 = Mod(x0 + 1, cells);
        int y1 = Mod(y0 + 1, cells);
        x0 = Mod(x0, cells);
        y0 = Mod(y0, cells);
        float n00 = Hash01(x0, y0, seedInt);
        float n10 = Hash01(x1, y0, seedInt);
        float n01 = Hash01(x0, y1, seedInt);
        float n11 = Hash01(x1, y1, seedInt);
        return Mathf.Lerp(Mathf.Lerp(n00, n10, tx), Mathf.Lerp(n01, n11, tx), ty);
    }

    private static int Mod(int value, int cells)
    {
        int wrapped = value % cells;
        return wrapped < 0 ? wrapped + cells : wrapped;
    }

    private static float Hash01(int x, int y, int seed)
    {
        uint n = (uint)(x * 374761393 + y * 668265263 + seed * 1442695041);
        n = (n ^ (n >> 13)) * 1274126177u;
        return (n & 0x00FFFFFFu) / 16777215f;
    }

    private static float Fbm(float u, float v, float frequency, int octaves, float seed)
    {
        float sum = 0f;
        float weight = 0f;
        float amplitude = 1f;
        float freq = frequency;
        for (int i = 0; i < octaves; i++)
        {
            sum += Seamless(u, v, freq, seed + i * 17.3f) * amplitude;
            weight += amplitude;
            amplitude *= 0.5f;
            freq *= 2f;
        }

        return weight > 0f ? sum / weight : 0f;
    }

    private static float Frac(float value) => value - Mathf.Floor(value);

    private static Color32 Lerp(Color32 a, Color32 b, float t)
    {
        t = Mathf.Clamp01(t);
        return new Color32(
            (byte)Mathf.Lerp(a.r, b.r, t),
            (byte)Mathf.Lerp(a.g, b.g, t),
            (byte)Mathf.Lerp(a.b, b.b, t),
            255);
    }
}
