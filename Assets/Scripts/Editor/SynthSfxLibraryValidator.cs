using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Comprueba que SynthSfxLibrary tenga todas las claves que el juego pide y que ningún clip esté roto.
/// Menú: ScrapWaves/Audio/Validate Synth Library. También sirve con -executeMethod en batch.
/// </summary>
public static class SynthSfxLibraryValidator
{
    private const string AssetPath = "Assets/Audio/Synth/SynthSfxLibrary.asset";

    private static readonly string[] RequiredKeys =
    {
        "shot", "jump", "land", "hurt", "xp", "levelup", "overheat_start", "overheat_end", "overheat_warning",
        "enemy_hit", "enemy_death", "dash", "slide",
        "rocket_launch", "rocket_explosion", "mortar_launch", "mortar_explosion", "blade_slash", "blade_thrust",
        "blade_dash", "flame_loop", "flame_burst", "cannon_ability", "rocket_ability", "mortar_ability",
        "bomber_spawn", "bomber_death", "charger_spawn", "charger_death", "destroyer_spawn", "destroyer_death",
        "flyer_spawn", "flyer_death", "worm_spawn", "worm_death", "slime_spawn", "slime_death",
    };

    [MenuItem("ScrapWaves/Audio/Validate Synth Library")]
    public static void Validate()
    {
        SynthSfxLibrary library = AssetDatabase.LoadAssetAtPath<SynthSfxLibrary>(AssetPath);
        if (library == null)
        {
            Debug.LogError($"SynthSfxLibrary: no se pudo cargar {AssetPath}");
            return;
        }

        var problems = new List<string>();
        int clipCount = 0;
        foreach (string key in RequiredKeys)
        {
            if (!library.TryGetClips(key, out AudioClip[] clips))
            {
                problems.Add($"falta la clave '{key}'");
                continue;
            }

            foreach (AudioClip clip in clips)
            {
                if (clip == null) problems.Add($"'{key}' tiene un clip nulo");
                else clipCount++;
            }
        }

        // Cada cue de arma con sonido sintetizado debe apuntar a una clave existente.
        foreach (WeaponPresentationCue cue in System.Enum.GetValues(typeof(WeaponPresentationCue)))
        {
            string key = SynthSfxLibrary.WeaponKey(cue);
            if (key != null && !library.Has(key))
                problems.Add($"el cue {cue} apunta a la clave inexistente '{key}'");
        }

        if (problems.Count == 0)
            Debug.Log($"SynthSfxLibrary OK: {RequiredKeys.Length} claves, {clipCount} clips.");
        else
            Debug.LogError("SynthSfxLibrary con problemas:\n" + string.Join("\n", problems));
    }
}
