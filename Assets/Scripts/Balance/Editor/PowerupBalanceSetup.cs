using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Crea (si faltan) el asset PowerupBalanceProfile con los valores originales y el GameObject PowerupBalance
/// en GameplayScene con su hub apuntando al asset. Es idempotente: correrlo de nuevo no duplica nada.
/// Menú: ScrapWaves/Balance/Setup Powerup Balance. En batch: -executeMethod PowerupBalanceSetup.Setup.
/// </summary>
public static class PowerupBalanceSetup
{
    private const string ProfilePath = "Assets/Data/Balance/PowerupBalanceProfile.asset";
    private const string ScenePath = "Assets/Scenes/GameplayScene.unity";
    private const string ObjectName = "PowerupBalance";

    [MenuItem("ScrapWaves/Balance/Setup Powerup Balance")]
    public static void Setup()
    {
        LoadOrCreateProfile();

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        // Se carga después de abrir la escena: al cambiar de escena Unity descarga los assets sin uso y dejaría la referencia nula.
        var profile = AssetDatabase.LoadAssetAtPath<PowerupBalanceProfile>(ProfilePath);
        PowerupBalanceHub hub = Object.FindAnyObjectByType<PowerupBalanceHub>(FindObjectsInactive.Include);
        if (hub == null)
        {
            var go = new GameObject(ObjectName);
            hub = go.AddComponent<PowerupBalanceHub>();
            Debug.Log($"PowerupBalanceSetup: creado el GameObject '{ObjectName}'.");
        }

        var serialized = new SerializedObject(hub);
        serialized.FindProperty("_profile").objectReferenceValue = profile;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        EditorSceneManager.MarkSceneDirty(hub.gameObject.scene);
        EditorSceneManager.SaveScene(hub.gameObject.scene);
        Debug.Log($"PowerupBalanceSetup OK: hub '{hub.gameObject.name}' -> {AssetDatabase.GetAssetPath(profile)}");
    }

    private static void LoadOrCreateProfile()
    {
        if (AssetDatabase.LoadAssetAtPath<PowerupBalanceProfile>(ProfilePath) != null)
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(ProfilePath));
        var profile = ScriptableObject.CreateInstance<PowerupBalanceProfile>();
        AssetDatabase.CreateAsset(profile, ProfilePath);
        AssetDatabase.SaveAssets();
        Debug.Log($"PowerupBalanceSetup: creado {ProfilePath} con los valores originales.");
    }
}
