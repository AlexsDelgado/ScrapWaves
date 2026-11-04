using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Mide el mapa de GameplayScene y coloca el volumen VoidRescue debajo de su geometría más baja.
/// Es idempotente: si el objeto ya existe, lo reubica. Menú: ScrapWaves/Level/Setup Void Rescue.
/// En batch: -executeMethod VoidRescueSetup.Setup (deja el reporte de medidas en el log).
/// </summary>
public static class VoidRescueSetup
{
    private const string ScenePath = "Assets/Scenes/GameplayScene.unity";
    private const string ObjectName = "VoidRescue";
    private const float MarginBelowMap = 15f;
    private const float VolumeHeight = 600f;
    private const float HorizontalMargin = 400f;

    [MenuItem("ScrapWaves/Level/Setup Void Rescue")]
    public static void Setup()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        VoidRescueVolume existing = Object.FindAnyObjectByType<VoidRescueVolume>(FindObjectsInactive.Include);
        GameObject go = existing != null ? existing.gameObject : new GameObject(ObjectName);

        // Se mide el suelo que realmente se ve desde arriba: rayos hacia abajo en una grilla, quedándose con la superficie
        // pisable (cara que mira hacia arriba) más baja. Medir colliders no sirve: hay bloques de aparición de cientos de
        // metros que figuran como sólidos pero no son suelo.
        Physics.SyncTransforms();
        var surfaces = new List<(float y, string name)>();
        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        const float step = 20f;
        const float startY = 1500f;
        for (float x = -700f; x <= 1000f; x += step)
        {
            for (float z = -700f; z <= 700f; z += step)
            {
                if (Physics.Raycast(new Vector3(x, startY, z), Vector3.down, out RaycastHit hit, startY + 1500f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    && hit.normal.y > 0.5f && !hit.collider.transform.IsChildOf(go.transform))
                {
                    surfaces.Add((hit.point.y, hit.collider.name));
                    minX = Mathf.Min(minX, x); maxX = Mathf.Max(maxX, x);
                    minZ = Mathf.Min(minZ, z); maxZ = Mathf.Max(maxZ, z);
                }
            }
        }

        if (surfaces.Count == 0)
        {
            Debug.LogError("VoidRescueSetup: no se encontró ninguna superficie pisable.");
            return;
        }

        surfaces.Sort((a, b) => a.y.CompareTo(b.y));
        float lowest = surfaces[0].y;
        Debug.Log($"VoidRescueSetup: {surfaces.Count} superficies pisables. Más baja y={lowest:F1}; mediana y={surfaces[surfaces.Count / 2].y:F1}; más alta y={surfaces[^1].y:F1}");
        foreach (var group in surfaces.Take(40).GroupBy(t => t.name).Take(5))
            Debug.Log($"VoidRescueSetup: bajas -> {group.Key}: {group.Count()} puntos, desde y={group.Min(t => t.y):F1}");

        var map = new Bounds(new Vector3((minX + maxX) * 0.5f, lowest, (minZ + maxZ) * 0.5f), new Vector3(maxX - minX, 0f, maxZ - minZ));
        float top = lowest - MarginBelowMap;
        if (!go.TryGetComponent(out BoxCollider box))
            box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        go.transform.position = new Vector3(map.center.x, top - VolumeHeight * 0.5f, map.center.z);
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        box.center = Vector3.zero;
        box.size = new Vector3(map.size.x + HorizontalMargin, VolumeHeight, map.size.z + HorizontalMargin);
        if (go.GetComponent<VoidRescueVolume>() == null)
            go.AddComponent<VoidRescueVolume>();

        EditorSceneManager.MarkSceneDirty(go.scene);
        EditorSceneManager.SaveScene(go.scene);
        Debug.Log($"VoidRescueSetup OK: '{go.name}' con la cara superior en y={top:F1} y tamaño {box.size.ToString("F0")}.");
    }
}
