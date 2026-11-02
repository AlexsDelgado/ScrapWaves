#if UNITY_EDITOR
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Refreshes mesh debris capability without resetting scene instances or their flow tuning.</summary>
public static class GeyserDebrisAuthoring
{
    private const string MeshFolder = "Assets/Art/Enemies/Stalker/ScrapBurst/Meshes/";

    [MenuItem("ScrapWaves/Level/Refresh Jump Pad Scrap Meshes Only")]
    public static void RefreshPrefabs()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Refresh outside Play Mode.");
        foreach (string name in new[] { "TrashGeyser_Visual", "HotAirGeyser_Visual", "FanJumpPad", "VentJumpPad" })
        {
            string path = GeyserModelAuthoring.Folder + "/Prefabs/" + name + ".prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                Configure(root.GetComponentInChildren<GeyserVfx>(true));
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        Debug.Log("GEYSER_SCRAP_REFRESHED: existing worm meshes; all four prefabs support Trash overrides; scene settings retained.");
    }

    public static void Configure(GeyserVfx vfx)
    {
        var settings = new SerializedObject(vfx);
        var updraft = (ParticleSystem)settings.FindProperty("_updraft").objectReferenceValue;
        var debris = (ParticleSystem)settings.FindProperty("_debris").objectReferenceValue;
        if (debris == null)
        {
            debris = new GameObject("LightTumblingDebris", typeof(ParticleSystem)).GetComponent<ParticleSystem>();
            debris.transform.SetParent(updraft.transform.parent, false);
            debris.transform.localPosition = updraft.transform.localPosition;
            debris.transform.localRotation = updraft.transform.localRotation;
            settings.FindProperty("_debris").objectReferenceValue = debris;
        }
        debris.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        debris.useAutoRandomSeed = false; debris.randomSeed = 1752;
        var meshes = new[] { "Cog", "Bolt", "Nail", "Nut" }.Select(name => AssetDatabase.LoadAssetAtPath<Mesh>(MeshFolder + name + ".asset")).ToArray();
        if (meshes.Any(mesh => mesh == null)) throw new InvalidOperationException("Existing worm scrap meshes are required.");
        var renderer = debris.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Mesh; renderer.SetMeshes(meshes);
        renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(GeyserModelAuthoring.Folder + "/Materials/Geyser_BareSteel.mat");
        renderer.enableGPUInstancing = true;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; renderer.receiveShadows = false;
        var main = debris.main; main.prewarm = true; main.startLifetime = new ParticleSystem.MinMaxCurve(2f,3f);
        main.startColor = Color.white; main.startRotation3D = true; main.gravityModifier = 0;
        main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(-Mathf.PI, Mathf.PI);
        var shape = debris.shape; shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Cone;
        var colors = debris.colorOverLifetime; colors.enabled = false;
        // Opaque scrap retains its existing material; shrink it away instead of relying on ignored alpha.
        var size = debris.sizeOverLifetime; size.enabled = true; size.separateAxes = false;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0,1), new Keyframe(.8f,1), new Keyframe(1,0)));
        // Migrate the previous stock default only. Preserve deliberate prefab size overrides.
        if (Mathf.Approximately(settings.FindProperty("_debrisSize").floatValue, .36f))
            settings.FindProperty("_debrisSize").floatValue = .9f;
        settings.ApplyModifiedPropertiesWithoutUndo();
        vfx.ApplyTuning();
    }
}
#endif
