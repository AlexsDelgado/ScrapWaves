using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Banco de SFX sintetizados (Tools/SfxGen). Se busca por clave de familia (p. ej. "shot", "bomber_death").
/// El asset lo regenera el generador; no editar a mano. Tiene prioridad sobre <see cref="PsxSfxLibrary"/>.
/// </summary>
[CreateAssetMenu(fileName = "SynthSfxLibrary", menuName = "ScrapWaves/Audio/Synth SFX Library")]
public class SynthSfxLibrary : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public string Key;
        public AudioClip[] Clips;
    }

    public static SynthSfxLibrary Active { get; set; }

    [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

    private Dictionary<string, AudioClip[]> _lookup;
    private readonly Dictionary<string, int> _lastIndex = new();

    public bool Has(string key) => TryGetClips(key, out _);

    public bool TryGetClips(string key, out AudioClip[] clips)
    {
        if (_lookup == null)
        {
            _lookup = new Dictionary<string, AudioClip[]>(_entries.Length);
            for (int i = 0; i < _entries.Length; i++)
            {
                if (!string.IsNullOrEmpty(_entries[i].Key))
                    _lookup[_entries[i].Key] = _entries[i].Clips;
            }
        }

        if (key != null && _lookup.TryGetValue(key, out clips) && clips != null && clips.Length > 0)
            return true;

        clips = null;
        return false;
    }

    /// <summary>Toma al azar de la familia, sin repetir la anterior. Null si la clave no existe.</summary>
    public AudioClip Pick(string key)
    {
        if (!TryGetClips(key, out AudioClip[] clips))
            return null;

        int index = UnityEngine.Random.Range(0, clips.Length);
        if (clips.Length > 1 && _lastIndex.TryGetValue(key, out int previous) && index == previous)
            index = (index + 1) % clips.Length;

        _lastIndex[key] = index;
        return clips[index];
    }

    /// <summary>Las armas automáticas (secundarias) suenan a esta fracción del volumen del arma manual.</summary>
    public const float AutomaticWeaponGain = 0.25f;

    /// <summary>Ajuste fino de volumen por familia, sobre el volumen del cue. 1 = sin cambio.</summary>
    public static float KeyGain(string key)
    {
        switch (key)
        {
            case "shot": return 1.6f;
            case "cannon_ability": return 1.3f;
            default: return 1f;
        }
    }

    public static float WeaponRoleGain(WeaponPresentationCue cue)
    {
        switch (cue)
        {
            case WeaponPresentationCue.AutomaticCannonAutoBurst:
            case WeaponPresentationCue.AutomaticCannonAutoShot:
            case WeaponPresentationCue.AutomaticCannonHeadHunterAutomatic:
            case WeaponPresentationCue.RocketAutomaticLaunch:
            case WeaponPresentationCue.MortarAutomaticLaunch:
            case WeaponPresentationCue.FlamethrowerAutomaticLoop:
            case WeaponPresentationCue.FlamethrowerJellifiedAutomaticLoop:
            case WeaponPresentationCue.FlamethrowerNitrogenAutomaticLoop:
                return AutomaticWeaponGain;
            default:
                return 1f;
        }
    }

    /// <summary>
    /// Volumen para los cues de arma que no tienen uno autorado (Volume = 0, como los que crean los builders).
    /// Los cues con volumen propio lo conservan.
    /// </summary>
    public static float DefaultGain(string key)
    {
        switch (key)
        {
            case "enemy_hit": return 0.4f;
            case "flame_loop": return 0.55f;
            case "blade_slash":
            case "blade_thrust": return 0.7f;
            case "blade_dash":
            case "flame_burst":
            case "rocket_launch": return 0.8f;
            case "mortar_launch":
            case "rocket_ability":
            case "mortar_ability": return 0.9f;
            default: return 1f;
        }
    }

    /// <summary>Familia asignada a cada cue de arma. Null si el cue no tiene sonido sintetizado.</summary>
    public static string WeaponKey(WeaponPresentationCue cue)
    {
        switch (cue)
        {
            case WeaponPresentationCue.AutomaticCannonAutoBurst:
            case WeaponPresentationCue.AutomaticCannonAutoShot:
            case WeaponPresentationCue.AutomaticCannonManualVolley:
            case WeaponPresentationCue.AutomaticCannonManualShot:
            case WeaponPresentationCue.AutomaticCannonContinuousShot:
            case WeaponPresentationCue.AutomaticCannonHeadHunterAutomatic:
            case WeaponPresentationCue.AutomaticCannonHeadHunterManual:
                return "shot";
            case WeaponPresentationCue.AutomaticCannonBaseActive:
            case WeaponPresentationCue.AutomaticCannonContinuousActive:
            case WeaponPresentationCue.AutomaticCannonHeadHunterActive:
                return "cannon_ability";
            case WeaponPresentationCue.AutomaticCannonImpact:
            case WeaponPresentationCue.AutomaticCannonCriticalImpact:
            case WeaponPresentationCue.AutomaticCannonWeakPointImpact:
            case WeaponPresentationCue.AutomaticCannonKillImpact:
            case WeaponPresentationCue.RotatingBladeContactImpact:
            case WeaponPresentationCue.RotatingBladeMultiFinalImpact:
            case WeaponPresentationCue.RotatingBladeAtomicSliceImpact:
            case WeaponPresentationCue.RocketFragmentChildImpact:
            case WeaponPresentationCue.MortarGrapeshotAirburst:
            case WeaponPresentationCue.MortarGrapeshotImpact:
            case WeaponPresentationCue.MortarMultiChargedRepeat:
                return "enemy_hit";
            case WeaponPresentationCue.RocketAutomaticLaunch:
            case WeaponPresentationCue.RocketManualLaunch:
            case WeaponPresentationCue.RocketClusterLaunch:
                return "rocket_launch";
            case WeaponPresentationCue.RocketActiveLaunch:
                return "rocket_ability";
            case WeaponPresentationCue.RocketImpact:
            case WeaponPresentationCue.RocketKineticImpact:
            case WeaponPresentationCue.RocketFragmentImpact:
            case WeaponPresentationCue.RocketKillImpact:
            case WeaponPresentationCue.RocketClusterDetonation:
                return "rocket_explosion";
            case WeaponPresentationCue.MortarImpact:
            case WeaponPresentationCue.MortarMultiChargedImpact:
                return "mortar_explosion";
            case WeaponPresentationCue.MortarAutomaticLaunch:
            case WeaponPresentationCue.MortarManualLaunch:
                return "mortar_launch";
            case WeaponPresentationCue.MortarActiveBarrage:
                return "mortar_ability";
            case WeaponPresentationCue.FlamethrowerAutomaticLoop:
            case WeaponPresentationCue.FlamethrowerManualLoop:
            case WeaponPresentationCue.FlamethrowerJellifiedAutomaticLoop:
            case WeaponPresentationCue.FlamethrowerJellifiedManualLoop:
            case WeaponPresentationCue.FlamethrowerNitrogenAutomaticLoop:
            case WeaponPresentationCue.FlamethrowerNitrogenManualLoop:
                return "flame_loop";
            case WeaponPresentationCue.FlamethrowerActiveBurst:
            case WeaponPresentationCue.FlamethrowerJellifiedActive:
            case WeaponPresentationCue.FlamethrowerNitrogenActive:
                return "flame_burst";
            case WeaponPresentationCue.RotatingBladeManualSlash:
            case WeaponPresentationCue.RotatingBladeMultiSlash:
            case WeaponPresentationCue.RotatingBladeAtomicSlash:
                return "blade_slash";
            case WeaponPresentationCue.RotatingBladeActiveThrust:
            case WeaponPresentationCue.RotatingBladeMultiThrust:
                return "blade_thrust";
            case WeaponPresentationCue.RotatingBladeAtomicDash:
                return "blade_dash";
            default:
                return null;
        }
    }
}
