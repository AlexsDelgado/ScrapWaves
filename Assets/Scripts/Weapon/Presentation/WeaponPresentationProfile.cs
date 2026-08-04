using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "WeaponPresentation", menuName = "ScrapWaves/Weapon Presentation Profile")]
public sealed class WeaponPresentationProfile : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private WeaponType _weaponType;
    [SerializeField] private Gradient _displayColor = CreateDefaultDisplayGradient();
    [SerializeField] private GameFeelQualityLevel _defaultQuality = GameFeelQualityLevel.High;

    [Header("Semantic routing")]
    [SerializeField] private List<WeaponFeedbackBinding> _feedbackBindings = new();

    [Header("Authored cues")]
    [SerializeField] private List<WeaponPresentationCueData> _cues = new();

    [Header("Projectile archetypes")]
    [SerializeField] private List<ProjectileArchetypePresentation> _projectileArchetypes = new();

    [Header("Shared responses")]
    [SerializeField] private WeaponHeatPresentationSettings _heat = new();
    [SerializeField] private WeaponDensitySettings _density = new();
    [SerializeField] private AutomaticCannonPresentationSettings _automaticCannon = new();
    [SerializeField] private FlamethrowerPresentationSettings _flamethrower = new();
    [SerializeField] private RotatingBladePresentationSettings _rotatingBlade = new();
    [SerializeField] private MortarPresentationSettings _mortar = new();
    [SerializeField] private GameFeelQualitySettings _qualitySettings;

    [Header("Accessibility fallbacks")]
    [SerializeField] private Color _reducedFlashColor = new(1f, 0.58f, 0.16f, 0.55f);
    [SerializeField, Range(0f, 1f)] private float _reducedFlashIntensity = 0.35f;

    private Dictionary<WeaponPresentationCue, WeaponPresentationCueData> _cueLookup;
    private Dictionary<ProjectilePresentationArchetypeId, ProjectileArchetypePresentation> _projectileLookup;
    private bool _cacheReady;
    private bool _hasDuplicateCues;

    public WeaponType WeaponType => _weaponType;
    public Gradient DisplayColor => _displayColor;
    public GameFeelQualityLevel DefaultQuality => _defaultQuality;
    public IReadOnlyList<WeaponFeedbackBinding> FeedbackBindings => _feedbackBindings;
    public IReadOnlyList<WeaponPresentationCueData> Cues => _cues;
    public IReadOnlyList<ProjectileArchetypePresentation> ProjectileArchetypes => _projectileArchetypes;
    public WeaponHeatPresentationSettings Heat => _heat;
    public WeaponDensitySettings Density => _density;
    public AutomaticCannonPresentationSettings AutomaticCannon => _automaticCannon;
    public FlamethrowerPresentationSettings Flamethrower => _flamethrower;
    public RotatingBladePresentationSettings RotatingBlade => _rotatingBlade;
    public MortarPresentationSettings Mortar => _mortar;
    public GameFeelQualitySettings QualitySettings => _qualitySettings;
    public Color ReducedFlashColor => _reducedFlashColor;
    public float ReducedFlashIntensity => _reducedFlashIntensity;
    public bool HasDuplicateCues
    {
        get
        {
            EnsureCache();
            return _hasDuplicateCues;
        }
    }

    public bool TryGetCueData(WeaponPresentationCue cue, out WeaponPresentationCueData cueData)
    {
        EnsureCache();
        if (cue == WeaponPresentationCue.None)
        {
            cueData = null;
            return false;
        }

        return _cueLookup.TryGetValue(cue, out cueData);
    }

    public bool TryResolveCue(
        WeaponFeedbackEvent feedbackEvent,
        in WeaponFeedbackContext context,
        out WeaponPresentationCueData cueData)
    {
        cueData = null;
        EnsureCache();
        WeaponFeedbackBinding bestBinding = null;
        int bestSpecificity = -1;
        for (int i = 0; i < _feedbackBindings.Count; i++)
        {
            WeaponFeedbackBinding binding = _feedbackBindings[i];
            if (binding == null || binding.Cue == WeaponPresentationCue.None ||
                !binding.Matches(feedbackEvent, in context))
            {
                continue;
            }

            if (binding.Specificity <= bestSpecificity)
                continue;
            bestBinding = binding;
            bestSpecificity = binding.Specificity;
        }

        return bestBinding != null && _cueLookup.TryGetValue(bestBinding.Cue, out cueData);
    }

    public bool TryGetProjectileArchetype(
        ProjectilePresentationArchetypeId archetype,
        out ProjectileArchetypePresentation presentation)
    {
        EnsureCache();
        return _projectileLookup.TryGetValue(archetype, out presentation);
    }

    public void RebuildCache()
    {
        _cueLookup ??= new Dictionary<WeaponPresentationCue, WeaponPresentationCueData>();
        _projectileLookup ??= new Dictionary<ProjectilePresentationArchetypeId, ProjectileArchetypePresentation>();
        _cueLookup.Clear();
        _projectileLookup.Clear();
        _hasDuplicateCues = false;

        _cues ??= new List<WeaponPresentationCueData>();
        _feedbackBindings ??= new List<WeaponFeedbackBinding>();
        _projectileArchetypes ??= new List<ProjectileArchetypePresentation>();
        _displayColor ??= CreateDefaultDisplayGradient();
        _heat ??= new WeaponHeatPresentationSettings();
        _density ??= new WeaponDensitySettings();
        _automaticCannon ??= new AutomaticCannonPresentationSettings();
        _flamethrower ??= new FlamethrowerPresentationSettings();
        _rotatingBlade ??= new RotatingBladePresentationSettings();
        _mortar ??= new MortarPresentationSettings();
        _heat.Sanitize();
        _density.Sanitize();
        _automaticCannon.Sanitize();
        _flamethrower.Sanitize();
        _rotatingBlade.Sanitize();
        _mortar.Sanitize();
        _reducedFlashIntensity = Mathf.Clamp01(_reducedFlashIntensity);
        for (int i = 0; i < _cues.Count; i++)
        {
            WeaponPresentationCueData cueData = _cues[i];
            if (cueData == null)
                continue;

            cueData.Sanitize();
            if (cueData.Cue == WeaponPresentationCue.None)
                continue;

            if (!_cueLookup.TryAdd(cueData.Cue, cueData))
                _hasDuplicateCues = true;
        }

        for (int i = 0; i < _projectileArchetypes.Count; i++)
        {
            ProjectileArchetypePresentation archetype = _projectileArchetypes[i];
            if (archetype == null)
                continue;
            archetype.Sanitize();
            _projectileLookup.TryAdd(archetype.Archetype, archetype);
        }

        _cacheReady = true;
    }

    private void OnEnable()
    {
        _cacheReady = false;
        EnsureCache();
    }

    private void OnValidate()
    {
        _cacheReady = false;
        EnsureCache();
    }

    private void EnsureCache()
    {
        if (!_cacheReady || _cueLookup == null)
            RebuildCache();
    }

    private static Gradient CreateDefaultDisplayGradient()
    {
        Gradient gradient = new();
        gradient.SetKeys(
            new[]
            {
                new GradientColorKey(new Color(1f, 0.3f, 0.03f), 0f),
                new GradientColorKey(new Color(1f, 0.82f, 0.2f), 1f)
            },
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            });
        return gradient;
    }
}
