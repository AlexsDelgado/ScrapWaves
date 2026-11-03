using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// Comprueba en GameplayScene que cada VisualEffect colgado del Player (hijo del objeto con PlayerMovement) quede enlazado a un powerup por
/// <see cref="PlayerPowerupVfx"/> y que ningún buff con VFX se quede sin efectos.
/// Menú: ScrapWaves/Powerups/Validate VFX Links. En batch: -executeMethod PowerupVfxValidator.Validate.
/// </summary>
public static class PowerupVfxValidator
{
    private const string ScenePath = "Assets/Scenes/GameplayScene.unity";

    [MenuItem("ScrapWaves/Powerups/Validate VFX Links")]
    public static void Validate()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        try
        {
            // SpecMetaBootstrap agrega TemporaryPowerupController (y éste a PlayerPowerupVfx) al objeto del PlayerMovement.
            PlayerMovement player = Object.FindAnyObjectByType<PlayerMovement>(FindObjectsInactive.Include);
            if (player == null)
            {
                Debug.LogError("PowerupVfx: no hay PlayerMovement en GameplayScene.");
                return;
            }

            var found = new Dictionary<string, List<string>>();
            var unmatched = new List<string>();
            foreach (VisualEffect effect in player.GetComponentsInChildren<VisualEffect>(true))
            {
                string asset = effect.visualEffectAsset != null ? effect.visualEffectAsset.name : "(sin asset)";
                string expected = asset switch
                {
                    "Vfx_Damage" => nameof(TemporaryPowerupType.ExtraDamage),
                    "Vfx_Speed" => nameof(TemporaryPowerupType.ExtraSpeed),
                    "Vfx_Scavening" or "Vfx_Scavenging" => nameof(TemporaryPowerupType.ExtraScavenging),
                    _ => null,
                };

                if (expected == null)
                {
                    unmatched.Add($"{effect.gameObject.name} ({asset})");
                    continue;
                }

                if (!found.TryGetValue(expected, out List<string> list))
                    found[expected] = list = new List<string>();
                list.Add(effect.gameObject.name);
            }

            var report = new List<string>();
            foreach (KeyValuePair<string, List<string>> pair in found)
                report.Add($"{pair.Key}: {string.Join(", ", pair.Value)}");
            foreach (string type in new[] { "ExtraDamage", "ExtraSpeed", "ExtraScavenging" })
            {
                if (!found.ContainsKey(type))
                    report.Add($"FALTA VFX para {type}");
            }

            if (unmatched.Count > 0)
                report.Add("sin powerup asociado: " + string.Join(", ", unmatched));

            bool ok = found.Count == 3 && unmatched.Count == 0;
            string message = "PowerupVfx " + (ok ? "OK" : "con problemas") + "\n" + string.Join("\n", report);
            if (ok) Debug.Log(message);
            else Debug.LogWarning(message);
        }
        finally
        {
            EditorSceneManager.CloseScene(scene, true);
        }
    }
}
