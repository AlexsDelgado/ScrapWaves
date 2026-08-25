using TMPro;
using UnityEngine;

/// <summary>
/// Pooled view driven exclusively by CombatTextDirector. Intentionally has no
/// Update/LateUpdate coroutine or gameplay dependency.
/// </summary>
public sealed class CombatTextView : MonoBehaviour
{
    [SerializeField] private Transform _root;
    [SerializeField] private TextMeshPro _text;

    private readonly char[] _numberBuffer = new char[32];
    private CombatTextProfile _profile;
    private CombatTextMotionSettings _motion;
    private MotionEnvelope _motionEnvelope;
    private Vector3 _anchorPosition;
    private Vector3 _position;
    private Vector3 _velocity;
    private Vector3 _spawnOffset;
    private Vector3 _motionRight = Vector3.right;
    private Color _styleColor = Color.white;
    private float _renderAlpha;
    private float _age;
    private float _releaseAge;
    private float _resolvedScale;
    private float _rePunchAmount;
    private float _rePunchRemaining;
    private float _rePunchDuration;
    private int _seed;
    private bool _active;
    private bool _burnTally;
    private bool _burnReleased;
    private bool _allowLocalShake;
    private bool _reducedMotion;

    public bool IsActive => _active;
    public bool IsBurnTally => _burnTally;
    public bool IsReleased => !_burnTally || _burnReleased;
    public bool IsAnchored => _active && (_burnTally ? !_burnReleased : _age <= _motion.ConnectionDuration);
    public bool IsFading
    {
        get
        {
            if (!_active || _motion == null) return false;
            float age = _burnTally ? _releaseAge : _age;
            return (!_burnTally || _burnReleased) && age / Mathf.Max(0.01f, _motion.Lifetime) >= _motion.FadeStartNormalized;
        }
    }
    public CombatTextPriority Priority { get; private set; }

    public void Initialize(CombatTextProfile profile)
    {
        _profile = CombatTextProfile.Resolve(profile);
        _root ??= transform;
        _text ??= GetComponentInChildren<TextMeshPro>(true);
        if (_text != null)
        {
            if (_text.transform != _root)
                _text.transform.localScale = Vector3.one * _profile.WorldTextScale;
            ConfigureRenderer(_text, _profile.RendererSortingOrder);
        }
        ReleaseImmediately();
    }

    public void Play(in CombatTextPresentation presentation, Camera camera)
    {
        if (_text == null || _root == null || presentation.Motion == null)
            return;

        gameObject.SetActive(true);
        _active = true;
        _motion = presentation.Motion;
        _age = 0f;
        _releaseAge = 0f;
        _burnTally = presentation.IsBurnTally;
        _burnReleased = false;
        _allowLocalShake = presentation.AllowLocalShake;
        _reducedMotion = ReferenceEquals(_motion, _profile.ReducedMotion);
        _motionEnvelope = MotionEnvelope.Create(_motion);
        _resolvedScale = presentation.ResolvedScale;
        _rePunchAmount = 0f;
        _rePunchRemaining = 0f;
        _rePunchDuration = 0f;
        _seed = presentation.DeterministicSeed;
        Priority = presentation.Priority;
        _anchorPosition = presentation.WorldPosition;
        _motionRight = ResolveCameraRight(camera);
        float worldUnits = _profile.WorldUnitsPerMotionUnit;
        float lateralMultiplier = _reducedMotion
            ? _profile.ReducedMotionLateralMultiplier
            : 1f;
        _spawnOffset =
            _motionRight * (HashSigned(_seed ^ 0x51ed270b) * _motion.InitialJitterX * lateralMultiplier * worldUnits) +
            Vector3.up * (HashUnit(_seed ^ 0x3c6ef372) * _motion.InitialJitterY * worldUnits);
        _position = _anchorPosition + _spawnOffset;
        _velocity =
            _motionRight * (HashSigned(_seed ^ 0x7f4a7c15) * _motion.HorizontalSpeed * lateralMultiplier * worldUnits) +
            Vector3.up * (_motion.UpwardSpeed * worldUnits);
        _renderAlpha = 1f;
        SetNumber(presentation.TotalAppliedDamage, presentation.CompactLargeNumbers);
        ApplyStyle(presentation.Style);
        SetTextAlpha(1f);
        _root.position = _position;
        _root.localScale = Vector3.one * Mathf.Max(0.01f, _resolvedScale * _motion.SpawnScale);
        ApplyRenderPose(camera);
    }

    public void Merge(in CombatTextMergePresentation merge)
    {
        if (!_active)
            return;
        Priority = merge.Priority;
        _resolvedScale = merge.ResolvedScale;
        if (!_reducedMotion || !_burnTally)
        {
            _rePunchAmount = Mathf.Max(_rePunchAmount, merge.RePunchScale);
            _rePunchDuration = Mathf.Max(0.01f, merge.RePunchDuration);
            _rePunchRemaining = _rePunchDuration;
        }
        _position += Vector3.up * (Mathf.Max(0f, merge.UpwardNudge) * _profile.WorldUnitsPerMotionUnit);
        SetNumber(merge.TotalAppliedDamage, merge.CompactLargeNumbers);
        ApplyStyle(merge.Style);
    }

    public void SetAnchorPosition(Vector3 worldPosition, bool snap)
    {
        _anchorPosition = worldPosition;
        if (snap)
            _position = worldPosition + _spawnOffset;
    }

    public void BeginRelease()
    {
        if (!_active || !_burnTally || _burnReleased)
            return;
        _burnReleased = true;
        _releaseAge = 0f;
        float lateralMultiplier = _reducedMotion
            ? _profile.ReducedMotionLateralMultiplier
            : 1f;
        float worldUnits = _profile.WorldUnitsPerMotionUnit;
        _velocity =
            _motionRight * (HashSigned(_seed ^ 0x2c1b3c6d) * _motion.HorizontalSpeed * lateralMultiplier * worldUnits) +
            Vector3.up * (_motion.UpwardSpeed * worldUnits);
    }

    /// <returns>True when the view has completed and should return to the pool.</returns>
    public bool Tick(float unscaledDeltaTime, Camera camera)
    {
        if (!_active || _motion == null)
            return true;

        float delta = Mathf.Max(0f, unscaledDeltaTime);
        _age += delta;
        if (_rePunchRemaining > 0f)
            _rePunchRemaining = Mathf.Max(0f, _rePunchRemaining - delta);

        float scaleOverLife;
        if (_burnTally && !_burnReleased)
        {
            _position = Vector3.Lerp(_position, _anchorPosition + _spawnOffset, Mathf.Clamp01(delta * 18f));
            scaleOverLife = _motionEnvelope.EvaluateIntroScale(_age);
            SetTextAlpha(1f);
        }
        else
        {
            if (_burnTally)
                _releaseAge += delta;

            if (!_burnTally && _age <= _motion.ConnectionDuration)
            {
                _position = Vector3.Lerp(_position, _anchorPosition + _spawnOffset, Mathf.Clamp01(delta * 24f));
            }
            else
            {
                _position += _velocity * delta;
                _velocity += Vector3.down *
                    (_motion.DownwardAcceleration * _profile.WorldUnitsPerMotionUnit * delta);
            }

            float lifeAge = _burnTally ? _releaseAge : _age;
            scaleOverLife = _burnTally
                ? _motionEnvelope.EvaluateReleaseScale(lifeAge)
                : _motionEnvelope.EvaluateScale(lifeAge);
            SetTextAlpha(_motionEnvelope.EvaluateAlpha(lifeAge));
        }

        float rePunch = _rePunchRemaining > 0f
            ? _rePunchAmount * (_rePunchRemaining / Mathf.Max(0.01f, _rePunchDuration))
            : 0f;
        _root.localScale = Vector3.one * Mathf.Max(0.01f, _resolvedScale * (scaleOverLife + rePunch));

        ApplyRenderPose(camera);

        return _burnTally
            ? _burnReleased && _releaseAge >= _motion.Lifetime
            : _age >= _motion.Lifetime;
    }

    public void ApplyRenderPose(Camera camera)
    {
        if (!_active || _root == null)
            return;

        Vector3 renderedPosition = _position;
        if (_allowLocalShake && _age < _motion.LocalShakeDuration && _motion.LocalShakeAmplitude > 0f)
        {
            float fade = 1f - _age / Mathf.Max(0.01f, _motion.LocalShakeDuration);
            float amplitude = _motion.LocalShakeAmplitude * _profile.WorldUnitsPerMotionUnit * fade;
            Vector3 cameraRight = ResolveCameraRight(camera);
            Vector3 cameraUp = camera != null ? camera.transform.up : Vector3.up;
            renderedPosition += cameraRight *
                (Mathf.Sin((_age * 73f) + (_seed & 31)) * amplitude);
            renderedPosition += cameraUp *
                (Mathf.Sin((_age * 91f) + ((_seed >> 5) & 31)) * amplitude * 0.55f);
        }
        _root.position = renderedPosition;

        if (camera == null)
            return;
        Vector3 awayFromCamera = renderedPosition - camera.transform.position;
        if (awayFromCamera.sqrMagnitude > 0.0001f)
            _root.rotation = Quaternion.LookRotation(awayFromCamera, camera.transform.up);
    }

    public void ReleaseImmediately()
    {
        _active = false;
        _burnTally = false;
        _burnReleased = false;
        _reducedMotion = false;
        _motion = null;
        Priority = CombatTextPriority.Decorative;
        SetTextAlpha(0f);
        if (gameObject.activeSelf)
            gameObject.SetActive(false);
    }

    internal static CombatTextView CreateProgrammatic(Transform parent, CombatTextProfile profile, int index)
    {
        GameObject rootObject = new($"CombatTextView_{index:00}");
        Transform root = rootObject.transform;
        root.SetParent(parent, false);

        GameObject textObject = new("Value");
        textObject.transform.SetParent(root, false);
        TextMeshPro text = textObject.AddComponent<TextMeshPro>();
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.enableAutoSizing = false;
        text.richText = false;
        text.raycastTarget = false;
        textObject.transform.localScale = Vector3.one * profile.WorldTextScale;
        ConfigureRenderer(text, profile.RendererSortingOrder);

        CombatTextView view = rootObject.AddComponent<CombatTextView>();
        view._root = root;
        view._text = text;
        view.Initialize(profile);
        return view;
    }

    private void SetNumber(long value, bool compact)
    {
        int length = CombatTextFormatter.Write(value, compact, _numberBuffer);
        _text.SetCharArray(_numberBuffer, 0, length);
    }

    private void ApplyStyle(CombatTextStyleId styleId)
    {
        CombatTextStyleDefinition style = _profile.GetStyle(styleId);
        if (_profile.FontAsset != null)
            _text.font = _profile.FontAsset;
        Material sharedMaterial = style.SharedMaterial != null ? style.SharedMaterial : _profile.DefaultFontMaterial;
        if (sharedMaterial != null)
            _text.fontSharedMaterial = sharedMaterial;
        _text.fontSize = style.FontSize;
        _text.fontStyle = style.FontStyle;
        _styleColor = style.TextColor;
        SetTextAlpha(_renderAlpha);
    }

    private void SetTextAlpha(float alpha)
    {
        _renderAlpha = Mathf.Clamp01(alpha);
        if (_text == null)
            return;
        Color color = _styleColor;
        color.a *= _renderAlpha;
        _text.color = color;
    }

    private static Vector3 ResolveCameraRight(Camera camera)
    {
        if (camera == null || camera.transform.right.sqrMagnitude <= 0.0001f)
            return Vector3.right;
        return camera.transform.right.normalized;
    }

    private static void ConfigureRenderer(TextMeshPro text, int sortingOrder)
    {
        if (text == null)
            return;
        text.sortingOrder = sortingOrder;
        Renderer textRenderer = text.renderer;
        if (textRenderer == null)
            return;
        textRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        textRenderer.receiveShadows = false;
        textRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        textRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        textRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
    }

    private static float HashUnit(int value)
    {
        unchecked
        {
            uint hash = (uint)value;
            hash ^= hash >> 16;
            hash *= 0x7feb352d;
            hash ^= hash >> 15;
            hash *= 0x846ca68b;
            hash ^= hash >> 16;
            return (hash & 0x00ffffff) / 16777215f;
        }
    }

    private static float HashSigned(int value) => HashUnit(value) * 2f - 1f;

    /// <summary>
    /// Applies the scalar motion contract while retaining the authored curves as easing shapes.
    /// The profile variants currently share default curves, so reading their values directly
    /// would otherwise make Burn and Reduced Motion inherit the normal profile's envelope.
    /// </summary>
    private readonly struct MotionEnvelope
    {
        private readonly AnimationCurve _scaleCurve;
        private readonly AnimationCurve _alphaCurve;
        private readonly float _lifetime;
        private readonly float _settleTime;
        private readonly float _peakTime;
        private readonly float _fadeStartTime;
        private readonly float _spawnScale;
        private readonly float _popOvershoot;
        private readonly float _endScale;
        private readonly CurveSegment _scaleRise;
        private readonly CurveSegment _scaleSettle;
        private readonly CurveSegment _scaleFade;
        private readonly CurveSegment _alphaFade;

        private MotionEnvelope(
            CombatTextMotionSettings motion,
            float peakTime,
            in CurveSegment scaleRise,
            in CurveSegment scaleSettle,
            in CurveSegment scaleFade,
            in CurveSegment alphaFade)
        {
            _scaleCurve = motion.ScaleOverLife;
            _alphaCurve = motion.AlphaOverLife;
            _lifetime = Mathf.Max(0.01f, motion.Lifetime);
            _settleTime = Mathf.Clamp(motion.SettleTime, 0.01f, _lifetime);
            _peakTime = Mathf.Clamp(peakTime, 0f, _settleTime);
            _fadeStartTime = Mathf.Clamp(motion.FadeStartNormalized, 0f, 1f) * _lifetime;
            _spawnScale = motion.SpawnScale;
            _popOvershoot = motion.PopOvershoot;
            _endScale = motion.EndScaleMultiplier;
            _scaleRise = scaleRise;
            _scaleSettle = scaleSettle;
            _scaleFade = scaleFade;
            _alphaFade = alphaFade;
        }

        public static MotionEnvelope Create(CombatTextMotionSettings motion)
        {
            AnimationCurve scale = motion.ScaleOverLife;
            int scaleCount = scale != null ? scale.length : 0;
            if (scaleCount < 2)
            {
                CurveSegment fallback = default;
                return new MotionEnvelope(motion, motion.SettleTime * 0.55f,
                    in fallback, in fallback, in fallback, in fallback);
            }

            int scaleEnd = scaleCount - 1;
            int scalePeak = 0;
            float maximumValue = scale[0].value;
            for (int i = 1; i < scaleCount; i++)
            {
                float value = scale[i].value;
                if (value > maximumValue)
                {
                    maximumValue = value;
                    scalePeak = i;
                }
            }

            int scaleSettle = Mathf.Min(scalePeak + 1, scaleEnd);
            int scaleFade = Mathf.Max(scaleSettle, scaleEnd - 1);
            CurveSegment rise = new(scale[0], scale[scalePeak]);
            CurveSegment settle = new(scale[scalePeak], scale[scaleSettle]);
            CurveSegment fade = new(scale[scaleFade], scale[scaleEnd]);

            float sourceSettleDuration = settle.EndTime - rise.StartTime;
            float peakRatio = sourceSettleDuration > 0.0001f
                ? Mathf.Clamp01((rise.EndTime - rise.StartTime) / sourceSettleDuration)
                : 0.55f;
            float peakTime = Mathf.Max(0.001f, motion.SettleTime * peakRatio);

            AnimationCurve alpha = motion.AlphaOverLife;
            int alphaCount = alpha != null ? alpha.length : 0;
            CurveSegment alphaFade;
            if (alphaCount < 2)
            {
                alphaFade = default;
            }
            else
            {
                int alphaEnd = alphaCount - 1;
                int alphaFadeStart = 0;
                float startValue = alpha[0].value;
                for (int i = 1; i < alphaEnd; i++)
                {
                    if (Mathf.Approximately(alpha[i].value, startValue))
                        alphaFadeStart = i;
                    else
                        break;
                }
                alphaFade = new CurveSegment(alpha[alphaFadeStart], alpha[alphaEnd]);
            }

            return new MotionEnvelope(
                motion,
                peakTime,
                in rise,
                in settle,
                in fade,
                in alphaFade);
        }

        public float EvaluateIntroScale(float age)
        {
            age = Mathf.Max(0f, age);
            if (age < _peakTime)
            {
                float progress = age / Mathf.Max(0.001f, _peakTime);
                return EvaluateSegment(_scaleCurve, in _scaleRise, progress, _spawnScale, _popOvershoot);
            }
            if (age < _settleTime)
            {
                float progress = (age - _peakTime) / Mathf.Max(0.001f, _settleTime - _peakTime);
                return EvaluateSegment(_scaleCurve, in _scaleSettle, progress, _popOvershoot, 1f);
            }
            return 1f;
        }

        public float EvaluateScale(float age)
        {
            if (age < _settleTime)
                return EvaluateIntroScale(age);
            return EvaluateReleaseScale(age);
        }

        public float EvaluateReleaseScale(float age)
        {
            if (age >= _lifetime)
                return _endScale;
            if (age <= _fadeStartTime)
                return 1f;
            float progress = (age - _fadeStartTime) /
                             Mathf.Max(0.001f, _lifetime - _fadeStartTime);
            return EvaluateSegment(_scaleCurve, in _scaleFade, progress, 1f, _endScale);
        }

        public float EvaluateAlpha(float age)
        {
            if (age >= _lifetime)
                return 0f;
            if (age <= _fadeStartTime)
                return 1f;
            float progress = (age - _fadeStartTime) /
                             Mathf.Max(0.001f, _lifetime - _fadeStartTime);
            return Mathf.Clamp01(EvaluateSegment(_alphaCurve, in _alphaFade, progress, 1f, 0f));
        }

        private static float EvaluateSegment(
            AnimationCurve curve,
            in CurveSegment segment,
            float progress,
            float targetStart,
            float targetEnd)
        {
            progress = Mathf.Clamp01(progress);
            if (curve == null || !segment.IsUsable)
                return Mathf.Lerp(targetStart, targetEnd, Mathf.SmoothStep(0f, 1f, progress));

            float sourceTime = Mathf.Lerp(segment.StartTime, segment.EndTime, progress);
            float sourceValue = curve.Evaluate(sourceTime);
            float sourceRange = segment.EndValue - segment.StartValue;
            float curveProgress = Mathf.Abs(sourceRange) > 0.0001f
                ? Mathf.Clamp01((sourceValue - segment.StartValue) / sourceRange)
                : progress;
            return Mathf.Lerp(targetStart, targetEnd, curveProgress);
        }

        private readonly struct CurveSegment
        {
            public readonly float StartTime;
            public readonly float EndTime;
            public readonly float StartValue;
            public readonly float EndValue;

            public bool IsUsable => EndTime - StartTime > 0.0001f;

            public CurveSegment(Keyframe start, Keyframe end)
            {
                StartTime = start.time;
                EndTime = end.time;
                StartValue = start.value;
                EndValue = end.value;
            }
        }
    }
}
