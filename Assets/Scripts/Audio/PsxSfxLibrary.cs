using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Banco de variaciones PSX. Cada arreglo es una familia (tomas 01–05 y, si existen, sets b/c).
/// Al reproducir se elige una toma al azar, sin repetir la anterior.
/// </summary>
[CreateAssetMenu(fileName = "PsxSfxLibrary", menuName = "ScrapWaves/Audio/PSX SFX Library")]
public class PsxSfxLibrary : ScriptableObject
{
    public static PsxSfxLibrary Active { get; private set; }

    [Header("Jugador")]
    [SerializeField] private AudioClip[] _shoot;
    [SerializeField] private AudioClip[] _playerHurt;
    [SerializeField] private AudioClip[] _jump;
    [SerializeField] private AudioClip[] _land;
    [SerializeField] private AudioClip[] _xp;
    [SerializeField] private AudioClip[] _levelUp;
    [SerializeField] private AudioClip[] _overheatStart;
    [SerializeField] private AudioClip[] _overheatEnd;

    [Header("Mundo")]
    [SerializeField] private AudioClip[] _enemyDeath;
    [SerializeField] private AudioClip[] _craft;
    [SerializeField] private AudioClip[] _bossSpawn;
    [SerializeField] private AudioClip[] _victory;
    [SerializeField] private AudioClip[] _defeat;

    [Header("UI")]
    [SerializeField] private AudioClip[] _uiHover;
    [SerializeField] private AudioClip[] _uiClick;
    [SerializeField] private AudioClip[] _uiConfirm;
    [SerializeField] private AudioClip[] _uiCancel;
    [SerializeField] private AudioClip[] _uiError;

    [Header("Armas")]
    [SerializeField] private AudioClip[] _cannon;
    [SerializeField] private AudioClip[] _blade;
    [SerializeField] private AudioClip[] _rocket;
    [SerializeField] private AudioClip[] _mortar;
    [SerializeField] private AudioClip[] _flameLoop;
    [SerializeField] private AudioClip[] _flameLoopAlt;
    [SerializeField] private AudioClip[] _impact;
    [SerializeField] private AudioClip[] _explosion;

    private readonly Dictionary<string, int> _lastIndex = new();

    private void OnEnable() => Active = this;

    private void OnDisable()
    {
        if (Active == this)
            Active = null;
    }

    public AudioClip PickShoot() => Pick(nameof(_shoot), _shoot);
    public AudioClip PickHurt() => Pick(nameof(_playerHurt), _playerHurt);
    public AudioClip PickJump() => Pick(nameof(_jump), _jump);
    public AudioClip PickLand() => Pick(nameof(_land), _land);
    public AudioClip PickXp() => Pick(nameof(_xp), _xp);
    public AudioClip PickLevelUp() => Pick(nameof(_levelUp), _levelUp);
    public AudioClip PickOverheatStart() => Pick(nameof(_overheatStart), _overheatStart);
    public AudioClip PickOverheatEnd() => Pick(nameof(_overheatEnd), _overheatEnd);
    public AudioClip PickEnemyDeath() => Pick(nameof(_enemyDeath), _enemyDeath);
    public AudioClip PickCraft() => Pick(nameof(_craft), _craft);
    public AudioClip PickBossSpawn() => Pick(nameof(_bossSpawn), _bossSpawn);
    public AudioClip PickVictory() => Pick(nameof(_victory), _victory);
    public AudioClip PickDefeat() => Pick(nameof(_defeat), _defeat);
    public AudioClip PickUiClick() => Pick(nameof(_uiClick), _uiClick);
    public AudioClip PickUiConfirm() => Pick(nameof(_uiConfirm), _uiConfirm);
    public AudioClip PickUiCancel() => Pick(nameof(_uiCancel), _uiCancel);
    public AudioClip PickUiError() => Pick(nameof(_uiError), _uiError);

    public AudioClip[] HoverClips => _uiHover;

    public bool TryGetWeaponClips(WeaponPresentationCue cue, out AudioClip[] clips)
    {
        switch (cue)
        {
            case WeaponPresentationCue.AutomaticCannonAutoBurst:
            case WeaponPresentationCue.AutomaticCannonAutoShot:
            case WeaponPresentationCue.AutomaticCannonManualVolley:
            case WeaponPresentationCue.AutomaticCannonManualShot:
            case WeaponPresentationCue.AutomaticCannonBaseActive:
            case WeaponPresentationCue.AutomaticCannonContinuousShot:
            case WeaponPresentationCue.AutomaticCannonContinuousActive:
            case WeaponPresentationCue.AutomaticCannonHeadHunterAutomatic:
            case WeaponPresentationCue.AutomaticCannonHeadHunterManual:
            case WeaponPresentationCue.AutomaticCannonHeadHunterActive:
                clips = _cannon;
                break;
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
                clips = _impact;
                break;
            case WeaponPresentationCue.RocketAutomaticLaunch:
            case WeaponPresentationCue.RocketManualLaunch:
            case WeaponPresentationCue.RocketActiveLaunch:
            case WeaponPresentationCue.RocketClusterLaunch:
                clips = _rocket;
                break;
            case WeaponPresentationCue.RocketImpact:
            case WeaponPresentationCue.RocketKineticImpact:
            case WeaponPresentationCue.RocketFragmentImpact:
            case WeaponPresentationCue.RocketKillImpact:
            case WeaponPresentationCue.RocketClusterDetonation:
            case WeaponPresentationCue.MortarImpact:
            case WeaponPresentationCue.MortarMultiChargedImpact:
                clips = _explosion;
                break;
            case WeaponPresentationCue.FlamethrowerAutomaticLoop:
            case WeaponPresentationCue.FlamethrowerManualLoop:
                clips = _flameLoop;
                break;
            case WeaponPresentationCue.FlamethrowerJellifiedAutomaticLoop:
            case WeaponPresentationCue.FlamethrowerJellifiedManualLoop:
            case WeaponPresentationCue.FlamethrowerNitrogenAutomaticLoop:
            case WeaponPresentationCue.FlamethrowerNitrogenManualLoop:
                clips = _flameLoopAlt;
                break;
            case WeaponPresentationCue.RotatingBladeManualSlash:
            case WeaponPresentationCue.RotatingBladeActiveThrust:
            case WeaponPresentationCue.RotatingBladeMultiSlash:
            case WeaponPresentationCue.RotatingBladeMultiThrust:
            case WeaponPresentationCue.RotatingBladeAtomicSlash:
            case WeaponPresentationCue.RotatingBladeAtomicDash:
                clips = _blade;
                break;
            case WeaponPresentationCue.MortarAutomaticLaunch:
            case WeaponPresentationCue.MortarManualLaunch:
            case WeaponPresentationCue.MortarActiveBarrage:
                clips = _mortar;
                break;
            default:
                clips = null;
                return false;
        }

        return HasAny(clips);
    }

    public AudioClip Pick(string key, AudioClip[] clips)
    {
        if (!HasAny(clips))
            return null;

        int count = clips.Length;
        int start = Random.Range(0, count);
        if (count > 1 && _lastIndex.TryGetValue(key, out int previous) && start == previous)
            start = (start + 1) % count;

        for (int offset = 0; offset < count; offset++)
        {
            int index = (start + offset) % count;
            AudioClip clip = clips[index];
            if (clip == null)
                continue;

            _lastIndex[key] = index;
            return clip;
        }

        return null;
    }

    private static bool HasAny(AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0)
            return false;

        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null)
                return true;
        }

        return false;
    }
}
