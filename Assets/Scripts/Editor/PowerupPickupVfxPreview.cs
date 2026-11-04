using UnityEditor;
using UnityEngine;

/// <summary>Visual-only checks; never heals the player or damages enemies.</summary>
public sealed class PowerupPickupVfxPreview : EditorWindow
{
    [MenuItem("ScrapWaves/Powerups/Preview Full Heal and Nuke")]
    public static void Open() => GetWindow<PowerupPickupVfxPreview>("Pickup VFX");

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Vista visual en Play. No modifica vida, escudos, municion ni enemigos.", MessageType.Info);
        using (new EditorGUI.DisabledScope(!EditorApplication.isPlaying))
        {
            if (GUILayout.Button("Full Heal - pulso verde")) Play(false);
            if (GUILayout.Button("Nuke - onda expansiva")) Play(true);
        }
        if (!EditorApplication.isPlaying) EditorGUILayout.LabelField("Entra en Play para probar los efectos.");
    }
    private static void Play(bool nuke)
    {
        var player = Object.FindAnyObjectByType<PlayerHealth>();
        if (player == null) return;
        var effect = PowerupPickupVfx.Ensure(player.gameObject);
        if (nuke) effect.PlayNuke(player.transform.position, 20f);
        else effect.PlayFullHeal();
    }
}
