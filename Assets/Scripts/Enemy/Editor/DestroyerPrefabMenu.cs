#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// Menús de editor para generar los prefabs placeholder del boss Destroyer sin armar YAML a mano
/// (mismo patrón que <see cref="EconomySceneSetupMenu"/>). Ver docs/boss-destroyer.md.
/// Correr en orden: 1) Create Destroyer Prefab (crea también el misil si falta) 2) Assign Destroyer
/// As Second Boss In Scene, con cada escena (GameplayScene, SampleScene) abierta.
/// </summary>
public static class DestroyerPrefabMenu
{
    private const string BossSourcePath = "Assets/Prefabs/Boss_2.prefab";
    private const string DestroyerPrefabPath = "Assets/Prefabs/Destroyer_Boss.prefab";
    private const string MissilePrefabPath = "Assets/Prefabs/DestroyerMissile.prefab";

    [MenuItem("ScrapWaves/Enemies/Create Destroyer Missile Prefab")]
    public static GameObject CreateMissilePrefab()
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(MissilePrefabPath);
        if (existing != null)
        {
            Debug.Log($"El prefab del misil ya existe en {MissilePrefabPath}");
            return existing;
        }

        GameObject temp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        temp.name = "DestroyerMissile";
        Object.DestroyImmediate(temp.GetComponent<SphereCollider>());
        temp.transform.localScale = Vector3.one * 0.5f;

        SphereCollider collider = temp.AddComponent<SphereCollider>();
        collider.isTrigger = true;

        Rigidbody rb = temp.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = true;
        rb.interpolation = RigidbodyInterpolation.Interpolate;

        temp.AddComponent<EnemySeekingMissile>();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, MissilePrefabPath);
        Object.DestroyImmediate(temp);
        AssetDatabase.SaveAssets();
        Debug.Log($"Creado prefab de misil en {MissilePrefabPath}");
        return prefab;
    }

    [MenuItem("ScrapWaves/Enemies/Create Destroyer Prefab")]
    public static void CreateDestroyerPrefab()
    {
        GameObject bossSource = AssetDatabase.LoadAssetAtPath<GameObject>(BossSourcePath);
        if (bossSource == null)
        {
            Debug.LogError($"No se encontró el boss base en {BossSourcePath}");
            return;
        }

        GameObject missilePrefab = CreateMissilePrefab();

        // Instancia temporal a partir de Boss_2 y desconectada del prefab original: se guarda
        // con otra ruta/nombre y no modifica Boss_2.prefab.
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(bossSource);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        instance.name = "Destroyer_Boss";

        // Sin SwarmPooledEnemy: el Destroyer es un boss único, no un enemigo pooled del swarm.
        SwarmPooledEnemy pooled = instance.GetComponent<SwarmPooledEnemy>();
        if (pooled != null)
            Object.DestroyImmediate(pooled);

        EnemyFollow follow = instance.GetComponent<EnemyFollow>();
        if (follow != null)
        {
            SerializedObject followSo = new SerializedObject(follow);
            followSo.FindProperty("_moveSpeed").floatValue = 1.6f;
            followSo.ApplyModifiedPropertiesWithoutUndo();
        }

        Transform mouth = CreateChild(instance.transform, "Mouth", new Vector3(0f, 0f, 0.6f));

        Transform weakPointTransform = CreateChild(instance.transform, "WeakPoint", new Vector3(0f, 0f, 0.6f));
        SphereCollider weakPointCollider = weakPointTransform.gameObject.AddComponent<SphereCollider>();
        weakPointCollider.isTrigger = false;
        weakPointCollider.radius = 0.35f;
        DestroyerMouthWeakPoint weakPoint = weakPointTransform.gameObject.AddComponent<DestroyerMouthWeakPoint>();
        weakPointTransform.gameObject.SetActive(false);

        Transform muzzle = CreateChild(instance.transform, "MissileMuzzle", new Vector3(0f, 0.5f, 0.6f));

        DestroyerBehavior behavior = instance.AddComponent<DestroyerBehavior>();
        SerializedObject behaviorSo = new SerializedObject(behavior);
        behaviorSo.FindProperty("_missileMuzzle").objectReferenceValue = muzzle;
        behaviorSo.FindProperty("_missilePrefab").objectReferenceValue = missilePrefab;
        behaviorSo.FindProperty("_mouth").objectReferenceValue = mouth;
        behaviorSo.FindProperty("_weakPoint").objectReferenceValue = weakPoint;
        behaviorSo.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(instance, DestroyerPrefabPath);
        Object.DestroyImmediate(instance);
        AssetDatabase.SaveAssets();
        Debug.Log($"Creado prefab del Destroyer en {DestroyerPrefabPath}. Revisá posiciones de Mouth/WeakPoint/MissileMuzzle (son placeholder).");
    }

    [MenuItem("ScrapWaves/Enemies/Assign Destroyer As Second Boss In Scene")]
    public static void AssignDestroyerAsSecondBoss()
    {
        BossManager manager = Object.FindAnyObjectByType<BossManager>();
        if (manager == null)
        {
            Debug.LogError("No hay BossManager en la escena abierta.");
            return;
        }

        GameObject destroyerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DestroyerPrefabPath);
        if (destroyerPrefab == null)
        {
            Debug.LogError($"No se encontró {DestroyerPrefabPath}. Corré antes 'Create Destroyer Prefab'.");
            return;
        }

        SerializedObject managerSo = new SerializedObject(manager);
        managerSo.FindProperty("_secondBossPrefab").objectReferenceValue = destroyerPrefab;
        managerSo.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(manager);
        Debug.Log($"BossManager._secondBossPrefab = Destroyer_Boss en la escena '{manager.gameObject.scene.name}'. Recordá guardar la escena (Ctrl+S).");
    }

    private static Transform CreateChild(Transform parent, string name, Vector3 localPosition)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent, false);
        child.transform.localPosition = localPosition;
        return child.transform;
    }
}
#endif
