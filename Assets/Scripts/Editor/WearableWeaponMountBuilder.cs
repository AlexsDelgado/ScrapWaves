using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>Rebuilds the authored FBX attachments and their shared runtime catalog.</summary>
public static class WearableWeaponMountBuilder
{
    private const string ArtPath = "Assets/Art/Weapons/Wearables";
    private const string PrefabPath = "Assets/Prefabs/Weapons/Wearables";
    private const string CatalogPath = "Assets/Resources/WearableWeaponMounts.asset";
    private const string OutputPath = ".utmp/wearables";

    private static readonly string[] SourceNames =
    {
        "BeltCharm(Sword)", "ScrapFeeder(Cannon)", "ACBracelet(Flame)",
        "BackPipe(Rocket)", "ShoulderChute(Mortar)"
    };

    private readonly struct Layout
    {
        public readonly string Name;
        public readonly WeaponType Type;
        public readonly Vector3 Position;
        public readonly Vector3 Rotation;
        public readonly float Height;
        public readonly string Outlet;
        public readonly Vector3 Direction;
        public readonly Vector3 AttachmentRotation;

        public Layout(string name, WeaponType type, Vector3 position, Vector3 rotation, float height, string outlet, Vector3 direction, Vector3 attachmentRotation = default)
        {
            Name = name;
            Type = type;
            Position = position;
            Rotation = rotation;
            Height = height;
            Outlet = outlet;
            Direction = direction;
            AttachmentRotation = attachmentRotation;
        }
    }

    // Centers use the actual player FBX inside player.prefab, whose feet are at y=-0.747
    // and head reaches y=1.123. The FBX's exported origin is not at its feet.
    private static readonly Layout[] Layouts =
    {
        new Layout("BeltCharm(Sword)", WeaponType.RotatingBlade, new Vector3(-0.12f, 0.12f, -0.126f), new Vector3(0f, 180f, 0f), 0.16f, "Green indicator lens", Vector3.back, new Vector3(14f, 11f, 0f)),
        new Layout("ScrapFeeder(Cannon)", WeaponType.AutomaticCannon, new Vector3(0.32f, 0.835f, 0.025f), new Vector3(0f, -90f, 0f), 0.21f, "Muzzle flange", Vector3.forward),
        new Layout("ACBracelet(Flame)", WeaponType.Flamethrower, new Vector3(-0.515f, 0.418f, 0f), new Vector3(0f, 90f, 0f), 0.086f, "Rounded ivory vent rim", Vector3.forward, new Vector3(0f, 0f, 53f)),
        new Layout("BackPipe(Rocket)", WeaponType.RocketLauncher, new Vector3(0.12f, 0.57f, -0.15f), new Vector3(0f, 180f, 0f), 0.55f, "Thick outlet lip", Vector3.up),
        new Layout("ShoulderChute(Mortar)", WeaponType.Mortar, new Vector3(-0.14f, 0.505f, -0.16f), new Vector3(0f, 180f, 0f), 0.49f, "Rolled cross top edge", Vector3.up)
    };

    [MenuItem("Tools/ScrapWaves/Build Wearable Weapon Mounts")]
    public static void Build()
    {
        EnsureFolder(ArtPath + "/Materials");
        EnsureFolder(PrefabPath);
        EnsureFolder("Assets/Resources");
        Directory.CreateDirectory(OutputPath);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

        WearableWeaponMountDefinition[] definitions = new WearableWeaponMountDefinition[Layouts.Length];
        for (int i = 0; i < Layouts.Length; i++)
        {
            Layout layout = Layouts[i];
            string sourcePath = $"{ArtPath}/{layout.Name}.fbx";
            ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(sourcePath);
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.addCollider = false;
            importer.SaveAndReimport();

            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            GameObject root = new GameObject(layout.Type + " Wearable Fire Point");
            try
            {
                GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(source);
                model.name = layout.Name;
                model.transform.SetParent(root.transform, false);
                model.transform.localRotation = Quaternion.Euler(layout.Rotation);
                if (layout.Type == WeaponType.Flamethrower)
                    model.transform.localRotation = Quaternion.AngleAxis(180f, Vector3.forward) * model.transform.localRotation;
                Bounds rawBounds = GetBounds(model);
                model.transform.localScale *= layout.Height / rawBounds.size.y;
                model.transform.localPosition -= GetBounds(model).center;
                ConvertMaterials(model);

                Bounds bounds = GetBounds(model);
                Renderer outlet = model.GetComponentsInChildren<Renderer>(true).Single(r => r.name == layout.Outlet);
                Vector3 muzzlePosition = outlet.bounds.center;
                if (layout.Type == WeaponType.AutomaticCannon || layout.Type == WeaponType.Flamethrower)
                    muzzlePosition.z = outlet.bounds.max.z + 0.015f;
                else if (layout.Type == WeaponType.RocketLauncher)
                    muzzlePosition.y = outlet.bounds.max.y + 0.015f;
                else if (layout.Type == WeaponType.Mortar)
                    muzzlePosition = new Vector3(bounds.center.x, bounds.max.y + 0.015f, bounds.center.z);
                else
                    muzzlePosition.z = bounds.min.z - 0.01f;

                Transform muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(root.transform, false);
                muzzle.localPosition = muzzlePosition;
                muzzle.localRotation = Quaternion.LookRotation(layout.Direction, layout.Direction == Vector3.up ? Vector3.forward : Vector3.up);
                AutomaticWeaponMount mount = root.AddComponent<AutomaticWeaponMount>();
                mount.Configure(null, null, null, muzzle, model);
                mount.ConfigureWearable();
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabPath}/{layout.Type}Wearable.prefab");
                definitions[i] = new WearableWeaponMountDefinition
                {
                    Type = layout.Type,
                    Prefab = prefab,
                    LocalPosition = layout.Position,
                    LocalEulerAngles = layout.AttachmentRotation,
                    LocalScale = Vector3.one
                };
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        WearableWeaponMountCatalog catalog = AssetDatabase.LoadAssetAtPath<WearableWeaponMountCatalog>(CatalogPath);
        if (catalog == null)
        {
            catalog = ScriptableObject.CreateInstance<WearableWeaponMountCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
        }
        catalog.SetDefinitions(definitions);
        EditorUtility.SetDirty(catalog);
        GameObject player = PrefabUtility.LoadPrefabContents("Assets/Prefabs/player.prefab");
        try
        {
            SerializedObject controller = new SerializedObject(player.GetComponent<PlayerWeaponMountController>());
            controller.FindProperty("_catalog").objectReferenceValue = catalog;
            controller.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(player, "Assets/Prefabs/player.prefab");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(player);
        }
        AssetDatabase.SaveAssets();
        Preview();
        Debug.Log("Five wearable fire points rebuilt and player catalog assigned.");
    }

    [MenuItem("Tools/ScrapWaves/Render Wearable Weapon Mount Preview")]
    public static void Preview()
    {
        WearableWeaponMountCatalog catalog = AssetDatabase.LoadAssetAtPath<WearableWeaponMountCatalog>(CatalogPath);
        if (catalog == null)
            throw new InvalidOperationException("Rebuild the wearable fire points first.");

        Scene previewScene = EditorSceneManager.NewPreviewScene();
        AmbientMode previousAmbientMode = RenderSettings.ambientMode;
        Color previousAmbientLight = RenderSettings.ambientLight;
        bool previousFog = RenderSettings.fog;
        WeaponData[] previewData = new WeaponData[catalog.Definitions.Length];
        try
        {
            // The exact nested FBX and local offset from the production player prefab.
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/player.prefab");
            Transform sourceModel = playerPrefab.transform.Find("Player (1)");
            GameObject player = UnityEngine.Object.Instantiate(sourceModel.gameObject);
            SceneManager.MoveGameObjectToScene(player, previewScene);
            player.name = "Player model (actual prefab transform)";
            player.transform.SetPositionAndRotation(sourceModel.localPosition, sourceModel.localRotation);
            player.transform.localScale = sourceModel.localScale;

            StringBuilder report = new StringBuilder();
            Bounds bodyBounds = GetBounds(player);
            report.AppendLine($"Player model: min={bodyBounds.min:F4}, max={bodyBounds.max:F4}, size={bodyBounds.size:F4}");
            GameObject[] models = new GameObject[catalog.Definitions.Length];
            for (int i = 0; i < catalog.Definitions.Length; i++)
            {
                WearableWeaponMountDefinition definition = catalog.Definitions[i];
                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(definition.Prefab, previewScene);
                instance.transform.SetPositionAndRotation(definition.LocalPosition, Quaternion.Euler(definition.LocalEulerAngles));
                instance.transform.localScale = definition.LocalScale;
                models[i] = instance;
                previewData[i] = ScriptableObject.CreateInstance<WeaponData>();
                previewData[i].WeaponType = definition.Type;
                instance.GetComponent<AutomaticWeaponMount>().Bind(new WeaponInstance { Data = previewData[i], State = WeaponState.Automatic });
                Bounds bounds = GetBounds(instance);
                Transform muzzle = instance.GetComponent<AutomaticWeaponMount>().Muzzle;
                report.AppendLine($"{definition.Type}: center={bounds.center:F4}, size={bounds.size:F4}, min={bounds.min:F4}, max={bounds.max:F4}, muzzle={muzzle.position:F4}, height/player={bounds.size.y / bodyBounds.size.y:P1}");
            }
            for (int i = 0; i < models.Length; i++)
                for (int j = i + 1; j < models.Length; j++)
                    if (GetBounds(models[i]).Intersects(GetBounds(models[j])))
                        throw new InvalidOperationException($"Wearables overlap: {models[i].name} and {models[j].name}.");
            report.AppendLine("All 10 pairwise full-renderer AABB overlap checks passed.");

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.58f, 0.62f, 0.68f);
            RenderSettings.fog = false;
            CreateLight("Key", new Vector3(35f, -30f, 0f), 1.8f, previewScene);
            CreateLight("Rear", new Vector3(25f, 150f, 0f), 1.3f, previewScene);
            Camera camera = new GameObject("Wearables preview camera").AddComponent<Camera>();
            SceneManager.MoveGameObjectToScene(camera.gameObject, previewScene);
            camera.scene = previewScene;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.065f, 0.085f, 0.11f);
            camera.orthographic = true;
            camera.orthographicSize = 1.22f;
            camera.nearClipPlane = 0.01f;
            camera.farClipPlane = 30f;
            camera.allowHDR = true;
            Vector3 target = new Vector3(0f, 0.16f, 0f);
            RenderView(camera, target, new Vector3(0f, 0.8f, -5f), "wearables-rear.png");
            RenderView(camera, target, new Vector3(3.5f, 1.4f, -4f), "wearables-rear-three-quarter.png");
            RenderView(camera, target, new Vector3(-3.5f, 1.4f, -4f), "wearables-left-rear.png");
            RenderView(camera, target, new Vector3(0f, 0.8f, 5f), "wearables-front.png");
            RenderView(camera, target, new Vector3(-5f, 0.65f, 0f), "wearables-left.png");
            RenderView(camera, target, new Vector3(5f, 0.65f, 0f), "wearables-right.png");
            foreach (GameObject model in models)
            {
                AutomaticWeaponMount mount = model.GetComponent<AutomaticWeaponMount>();
                mount.Weapon.State = WeaponState.Manual;
                mount.SetAutomatic(false);
            }
            RenderView(camera, target, new Vector3(0f, 0.8f, -5f), "wearables-manual-rear.png");
            RenderView(camera, target, new Vector3(-3.5f, 1.4f, -4f), "wearables-manual-left-rear.png");
            RenderView(camera, target, new Vector3(0f, 0.8f, 5f), "wearables-manual-front.png");
            File.WriteAllText($"{OutputPath}/layout-report.txt", report.ToString());
            Debug.Log(report.ToString());
        }
        finally
        {
            RenderSettings.ambientMode = previousAmbientMode;
            RenderSettings.ambientLight = previousAmbientLight;
            RenderSettings.fog = previousFog;
            EditorSceneManager.ClosePreviewScene(previewScene);
            foreach (WeaponData data in previewData)
                if (data != null)
                    UnityEngine.Object.DestroyImmediate(data);
        }
    }

    private static void ConvertMaterials(GameObject model)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            throw new InvalidOperationException("URP Lit shader is unavailable.");
        foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material source = materials[i];
                if (source == null)
                    continue;
                bool indicator = source.name.IndexOf("green indicator", StringComparison.OrdinalIgnoreCase) >= 0;
                string name = indicator ? "AutoIndicator_Green" : source.name.Replace("Plain ", "Wearable_").Replace(" ", "_");
                string path = $"{ArtPath}/Materials/{name}.mat";
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader) { name = name };
                    AssetDatabase.CreateAsset(material, path);
                }
                material.shader = shader;
                Color color = source.HasProperty("_BaseColor") ? source.GetColor("_BaseColor") : source.color;
                material.SetColor("_BaseColor", color);
                material.SetFloat("_Metallic", source.name.Contains("steel") ? 0.65f : source.name.Contains("metal") ? 0.4f : 0f);
                material.SetFloat("_Smoothness", 0.38f);
                material.SetColor("_EmissionColor", indicator ? color * 1.5f : Color.black);
                // Imported thin armor panels have a back-facing exterior polygon. Their
                // native Blender material is double sided, so retain that appearance in URP.
                bool thinPanel = source.name.Contains("muted blue") || source.name.Contains("oxide red") || source.name.Contains("interior metal");
                material.SetFloat("_Cull", thinPanel ? (float)CullMode.Off : (float)CullMode.Back);
                material.doubleSidedGI = thinPanel;
                // URP's material upgrader derives the emission keyword from this flag.
                // Leaving the default EmissiveIsBlack flag clears _EMISSION on reimport.
                material.globalIlluminationFlags = indicator ? MaterialGlobalIlluminationFlags.BakedEmissive : MaterialGlobalIlluminationFlags.EmissiveIsBlack;
                if (indicator)
                    material.EnableKeyword("_EMISSION");
                else
                    material.DisableKeyword("_EMISSION");
                EditorUtility.SetDirty(material);
                materials[i] = material;
            }
            renderer.sharedMaterials = materials;
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }
    }

    private static void RenderView(Camera camera, Vector3 target, Vector3 offset, string filename)
    {
        camera.transform.position = target + offset;
        camera.transform.LookAt(target);
        RenderTexture texture = new RenderTexture(1000, 1000, 24, RenderTextureFormat.ARGB32);
        RenderTexture previous = RenderTexture.active;
        Texture2D image = new Texture2D(1000, 1000, TextureFormat.RGB24, false);
        try
        {
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            image.ReadPixels(new Rect(0, 0, 1000, 1000), 0, 0);
            image.Apply();
            File.WriteAllBytes($"{OutputPath}/{filename}", image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void CreateLight(string name, Vector3 rotation, float intensity, Scene previewScene)
    {
        Light light = new GameObject(name).AddComponent<Light>();
        SceneManager.MoveGameObjectToScene(light.gameObject, previewScene);
        light.type = LightType.Directional;
        light.intensity = intensity;
        light.transform.rotation = Quaternion.Euler(rotation);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        string parent = path.Substring(0, path.LastIndexOf('/'));
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
    }

    public static void InspectSources()
    {
        Directory.CreateDirectory(OutputPath);
        AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
        StringBuilder report = new StringBuilder();
        foreach (string name in SourceNames.Concat(new[] { "Player (1)" }))
        {
            string path = name == "Player (1)" ? "Assets/Prefabs/player.prefab" : $"{ArtPath}/{name}.fbx";
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            GameObject instance = UnityEngine.Object.Instantiate(source);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            report.AppendLine(name + ": " + GetBounds(instance));
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
                report.AppendLine($"  {renderer.name}: {renderer.bounds}; materials={string.Join(",", renderer.sharedMaterials.Select(m => m == null ? "null" : m.name))}");
            UnityEngine.Object.DestroyImmediate(instance);
        }
        File.WriteAllText($"{OutputPath}/unity-source-bounds.txt", report.ToString());
        Debug.Log(report.ToString());
    }

    public static Bounds GetBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            throw new InvalidOperationException($"No mesh renderers on {root.name}.");
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }
}
