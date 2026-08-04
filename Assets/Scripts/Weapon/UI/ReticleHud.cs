using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

[DisallowMultipleComponent]
public class ReticleHud : MonoBehaviour
{
    private const int CircleTextureSize = 128;
    private const int MortarRingSegments = 64;

    [Header("Sources")]
    [SerializeField] private WeaponManager _weaponManager;
    [SerializeField] private ReticleAimProvider _aimProvider;

    [Header("Shared")]
    [SerializeField] private bool _visibleOnStart = true;
    [SerializeField, Min(1f)] private float _lineThickness = 3f;
    [SerializeField] private Color _lineColor = new Color(1f, 1f, 1f, 0.95f);
    [SerializeField] private Color _shadowColor = new Color(0f, 0f, 0f, 0.65f);
    [SerializeField] private Vector2 _shadowOffset = new Vector2(2f, -2f);
    [SerializeField] private int _sortingOrder = 650;
    [SerializeField] private Color _weakPointFlashColor = new Color(1f, 0f, 0f, 1f);
    [SerializeField, Min(0.01f)] private float _weakPointFlashDuration = 0.18f;

    [Header("Blade / Flamethrower")]
    [SerializeField] private Vector2 _wideBracketFrameSize = new Vector2(250f, 70f);
    [SerializeField, Min(1f)] private float _wideBracketArmLength = 54f;

    [Header("Cannon / Launcher")]
    [SerializeField, Min(8f)] private float _circleDiameter = 34f;
    [SerializeField, Min(1f)] private float _circleLineThickness = 3f;
    [SerializeField, Min(1f)] private float _centerDotDiameter = 5f;

    [Header("Mortar")]
    [SerializeField] private Vector2 _mortarVSize = new Vector2(30f, 18f);
    [SerializeField, Min(1f)] private float _mortarVArmLength = 20f;
    [SerializeField, Min(0.01f)] private float _mortarLandingRingRadius = 0.38f;
    [SerializeField, Min(0.005f)] private float _mortarInnerRingWidth = 0.07f;
    [SerializeField, Min(0.005f)] private float _mortarOuterRingWidth = 0.05f;
    [SerializeField, Min(0f)] private float _mortarSurfaceOffset = 0.04f;
    [SerializeField, Min(0.01f)] private float _mortarPredictionInterval = 0.04f;
    [SerializeField] private Color _mortarLandingColor = new Color(1f, 1f, 1f, 0.95f);
    [SerializeField] private Color _mortarBlastColor = new Color(1f, 0.45f, 0.05f, 0.7f);

    [Header("Rocket Active")]
    [SerializeField] private Vector2 _rocketMinimumFrameSize = new Vector2(180f, 100f);
    [SerializeField] private Vector2 _rocketMaximumFrameSize = new Vector2(1344f, 756f);
    [SerializeField, Min(1f)] private float _rocketCornerArmLength = 96f;
    [SerializeField, Min(0.1f)] private float _rocketFrameEaseSpeed = 9f;

    private GameObject _canvasRoot;
    private RectTransform _wideBracketRoot;
    private RectTransform _circleDotRoot;
    private RectTransform _mortarVRoot;
    private RectTransform _rocketFrame;

    private WeaponTestingSandboxManager _sandbox;
    private GameObject _mortarMarkerRoot;
    private LineRenderer _mortarLandingRing;
    private LineRenderer _mortarBlastRing;
    private Transform _mortarCenterDot;
    private Material _mortarLineMaterial;
    private Material _mortarDotMaterial;
    private float _mortarPredictionTimer;
    private MortarLandingIndicatorVfx _authoredMortarMarker;
    private GameObject _authoredMortarMarkerPrefab;
    private readonly RaycastHit[] _mortarPresentationSupportHits = new RaycastHit[16];

    private Texture2D _circleRingTexture;
    private Sprite _circleRingSprite;
    private readonly List<Image> _reticleTintImages = new();
    private readonly Dictionary<Image, Color> _reticleBaseColors = new();
    private bool _isVisible;
    private bool _sandboxLookupComplete;
    private bool _weakPointFlashActive;
    private float _weakPointFlashTimer;
    private static Sprite s_whiteSprite;

    private void Awake()
    {
        ResolveDependencies();
        BuildUi();
        BuildMortarMarker();
        SetVisible(_visibleOnStart);
    }

    private void OnEnable()
    {
        WeaponWeakPointFeedback.WeakPointHit -= HandleWeakPointHit;
        WeaponWeakPointFeedback.WeakPointHit += HandleWeakPointHit;
    }

    private void OnDisable()
    {
        WeaponWeakPointFeedback.WeakPointHit -= HandleWeakPointHit;
    }

    private void Update()
    {
        if (!_isVisible)
            return;

        ResolveDependencies();
        WeaponInstance runtime = ResolveManualWeapon();
        IWeaponBehaviour behaviour = ResolveManualBehaviour();
        if (runtime?.Data == null || behaviour == null)
        {
            ApplyMode(ReticleMode.Hidden);
            SetMortarMarkerVisible(false);
            return;
        }

        // Weapon behavior stays authoritative; the HUD only reads presentation status.
        bool rocketCharging = behaviour is IRocketReticleStatus rocketStatus
            && rocketStatus.IsTargetingActive;
        ReticleMode mode = ReticlePresentationLogic.ResolveMode(
            runtime.Data.WeaponType,
            rocketCharging);

        ApplyMode(mode);
        if (mode == ReticleMode.RocketLock && behaviour is IRocketReticleStatus rocket)
            UpdateRocketFrame(rocket);
        else
            ResetRocketFrame();

        if (mode == ReticleMode.Mortar && behaviour is IMortarReticleStatus mortar)
            UpdateMortarMarker(runtime, mortar);
        else
        {
            _mortarPredictionTimer = 0f;
            SetMortarMarkerVisible(false);
        }

        TickWeakPointFlash();
    }

    private void OnDestroy()
    {
        WeaponWeakPointFeedback.WeakPointHit -= HandleWeakPointHit;
        DestroyOwnedObject(_authoredMortarMarker != null ? _authoredMortarMarker.gameObject : null);
        DestroyOwnedObject(_mortarMarkerRoot);
        DestroyOwnedObject(_mortarLineMaterial);
        DestroyOwnedObject(_mortarDotMaterial);
        DestroyOwnedObject(_circleRingSprite);
        DestroyOwnedObject(_circleRingTexture);
    }

    public void SetVisible(bool visible)
    {
        _isVisible = visible;
        if (_canvasRoot != null)
            _canvasRoot.SetActive(visible);
        if (!visible)
            SetMortarMarkerVisible(false);
    }

    private void ResolveDependencies()
    {
        if (_weaponManager == null)
            _weaponManager = GetComponent<WeaponManager>();
        if (_aimProvider == null)
            _aimProvider = GetComponent<ReticleAimProvider>();
        if (!_sandboxLookupComplete)
        {
            _sandbox = FindAnyObjectByType<WeaponTestingSandboxManager>();
            _sandboxLookupComplete = true;
        }
    }

    private bool UsesSandbox()
    {
        return _sandbox != null
            && _sandbox.PlayerTransform == transform
            && _sandbox.CurrentManualWeapon != null;
    }

    private WeaponInstance ResolveManualWeapon()
    {
        return UsesSandbox()
            ? _sandbox.CurrentManualWeapon
            : _weaponManager != null
                ? _weaponManager.GetCurrentManualWeapon()
                : null;
    }

    private IWeaponBehaviour ResolveManualBehaviour()
    {
        return UsesSandbox()
            ? _sandbox.CurrentManualBehaviour
            : _weaponManager != null
                ? _weaponManager.GetCurrentManualBehaviour()
                : null;
    }

    private Transform ResolveProjectileSpawn()
    {
        if (UsesSandbox())
            return _sandbox.ProjectileSpawn != null ? _sandbox.ProjectileSpawn : transform;
        return _weaponManager != null ? _weaponManager.GetProjectileSpawn() : transform;
    }

    private void BuildUi()
    {
        _canvasRoot = new GameObject("ReticleHUD_Canvas");
        _canvasRoot.transform.SetParent(transform, false);

        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0)
            _canvasRoot.layer = uiLayer;

        Canvas canvas = _canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = _sortingOrder;

        CanvasScaler scaler = _canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform canvasRt = _canvasRoot.GetComponent<RectTransform>();
        canvasRt.anchorMin = Vector2.zero;
        canvasRt.anchorMax = Vector2.one;
        canvasRt.offsetMin = Vector2.zero;
        canvasRt.offsetMax = Vector2.zero;

        BuildWideBracketReticle();
        BuildCircleDotReticle();
        BuildMortarVReticle();
        BuildRocketLockReticle();
    }

    private void BuildWideBracketReticle()
    {
        _wideBracketRoot = CreateCenteredRoot("WideBracketReticle", _wideBracketFrameSize);
        float halfWidth = _wideBracketFrameSize.x * 0.5f;
        float halfHeight = _wideBracketFrameSize.y * 0.5f;
        float arm = Mathf.Min(_wideBracketArmLength, _wideBracketFrameSize.x * 0.45f);

        CreateStyledLine(_wideBracketRoot, "LeftVertical", new Vector2(-halfWidth, 0f), new Vector2(_lineThickness, _wideBracketFrameSize.y));
        CreateStyledLine(_wideBracketRoot, "LeftTop", new Vector2(-halfWidth + arm * 0.5f, halfHeight), new Vector2(arm, _lineThickness));
        CreateStyledLine(_wideBracketRoot, "LeftBottom", new Vector2(-halfWidth + arm * 0.5f, -halfHeight), new Vector2(arm, _lineThickness));

        CreateStyledLine(_wideBracketRoot, "RightVertical", new Vector2(halfWidth, 0f), new Vector2(_lineThickness, _wideBracketFrameSize.y));
        CreateStyledLine(_wideBracketRoot, "RightTop", new Vector2(halfWidth - arm * 0.5f, halfHeight), new Vector2(arm, _lineThickness));
        CreateStyledLine(_wideBracketRoot, "RightBottom", new Vector2(halfWidth - arm * 0.5f, -halfHeight), new Vector2(arm, _lineThickness));
    }

    private void BuildCircleDotReticle()
    {
        _circleDotRoot = CreateCenteredRoot(
            "CircleDotReticle",
            new Vector2(_circleDiameter, _circleDiameter));

        _circleRingSprite = CreateRingSprite();
        CreateImage(
            _circleDotRoot,
            "CircleShadow",
            _shadowOffset,
            new Vector2(_circleDiameter, _circleDiameter),
            _shadowColor,
            _circleRingSprite);
        CreateImage(
            _circleDotRoot,
            "Circle",
            Vector2.zero,
            new Vector2(_circleDiameter, _circleDiameter),
            _lineColor,
            _circleRingSprite,
            tintWithWeakPointFlash: true);

        CreateImage(
            _circleDotRoot,
            "DotShadow",
            _shadowOffset,
            Vector2.one * _centerDotDiameter,
            _shadowColor,
            GetWhiteSprite());
        CreateImage(
            _circleDotRoot,
            "Dot",
            Vector2.zero,
            Vector2.one * _centerDotDiameter,
            _lineColor,
            GetWhiteSprite(),
            tintWithWeakPointFlash: true);
    }

    private void BuildMortarVReticle()
    {
        _mortarVRoot = CreateCenteredRoot("MortarVReticle", _mortarVSize);
        float horizontalOffset = _mortarVSize.x * 0.22f;
        float verticalOffset = _mortarVSize.y * 0.1f;

        CreateStyledLine(
            _mortarVRoot,
            "MortarVLeft",
            new Vector2(-horizontalOffset, verticalOffset),
            new Vector2(_mortarVArmLength, _lineThickness),
            -42f);
        CreateStyledLine(
            _mortarVRoot,
            "MortarVRight",
            new Vector2(horizontalOffset, verticalOffset),
            new Vector2(_mortarVArmLength, _lineThickness),
            42f);
    }

    private void BuildRocketLockReticle()
    {
        _rocketFrame = CreateCenteredRoot("RocketLockReticle", _rocketMinimumFrameSize);
        CreateRocketCorner("TopLeft", new Vector2(0f, 1f), 1f, -1f);
        CreateRocketCorner("TopRight", new Vector2(1f, 1f), -1f, -1f);
        CreateRocketCorner("BottomLeft", new Vector2(0f, 0f), 1f, 1f);
        CreateRocketCorner("BottomRight", new Vector2(1f, 0f), -1f, 1f);
    }

    private void CreateRocketCorner(string name, Vector2 anchor, float horizontalDirection, float verticalDirection)
    {
        float halfArm = _rocketCornerArmLength * 0.5f;
        Vector2 horizontalPosition = new Vector2(horizontalDirection * halfArm, 0f);
        Vector2 verticalPosition = new Vector2(0f, verticalDirection * halfArm);

        CreateStyledAnchoredLine(
            _rocketFrame,
            name + "Horizontal",
            anchor,
            horizontalPosition,
            new Vector2(_rocketCornerArmLength, _lineThickness));
        CreateStyledAnchoredLine(
            _rocketFrame,
            name + "Vertical",
            anchor,
            verticalPosition,
            new Vector2(_lineThickness, _rocketCornerArmLength));
    }

    private void ApplyMode(ReticleMode mode)
    {
        SetRootActive(_wideBracketRoot, mode == ReticleMode.WideBrackets);
        SetRootActive(_circleDotRoot, mode == ReticleMode.CircleDot);
        SetRootActive(_mortarVRoot, mode == ReticleMode.Mortar);
        SetRootActive(_rocketFrame, mode == ReticleMode.RocketLock);
    }

    private void UpdateRocketFrame(IRocketReticleStatus rocket)
    {
        float progress = ReticlePresentationLogic.GetRocketLockProgress(
            rocket.CurrentRocketLocks,
            rocket.InitialRocketLocks,
            rocket.MaximumRocketLocks);
        Vector2 targetSize = Vector2.Lerp(
            _rocketMinimumFrameSize,
            _rocketMaximumFrameSize,
            progress);
        float interpolation = 1f - Mathf.Exp(
            -Mathf.Max(0.1f, _rocketFrameEaseSpeed) * Time.unscaledDeltaTime);
        _rocketFrame.sizeDelta = Vector2.Lerp(
            _rocketFrame.sizeDelta,
            targetSize,
            interpolation);
    }

    private void ResetRocketFrame()
    {
        if (_rocketFrame != null)
            _rocketFrame.sizeDelta = _rocketMinimumFrameSize;
    }

    private void BuildMortarMarker()
    {
        _mortarMarkerRoot = new GameObject("MortarLandingMarker");
        _mortarLineMaterial = CreateLineMaterial();
        _mortarDotMaterial = CreateUnlitMaterial(_mortarLandingColor);

        _mortarLandingRing = CreateWorldRing(
            _mortarMarkerRoot.transform,
            "LandingRing",
            _mortarInnerRingWidth,
            _mortarLandingColor);
        _mortarBlastRing = CreateWorldRing(
            _mortarMarkerRoot.transform,
            "BlastRing",
            _mortarOuterRingWidth,
            _mortarBlastColor);

        GameObject centerDot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        centerDot.name = "LandingPoint";
        centerDot.transform.SetParent(_mortarMarkerRoot.transform, false);
        centerDot.transform.localScale = Vector3.one * 0.13f;
        Collider dotCollider = centerDot.GetComponent<Collider>();
        if (dotCollider != null)
            Destroy(dotCollider);
        Renderer renderer = centerDot.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = _mortarDotMaterial;
        _mortarCenterDot = centerDot.transform;

        SetMortarMarkerVisible(false);
    }

    private void UpdateMortarMarker(WeaponInstance runtime, IMortarReticleStatus mortar)
    {
        EnsureAuthoredMortarMarker(runtime);
        if (_aimProvider == null)
        {
            SetMortarMarkerVisible(false);
            return;
        }

        // Physics prediction is intentionally throttled while the screen-space V stays responsive.
        _mortarPredictionTimer -= Time.unscaledDeltaTime;
        if (_mortarPredictionTimer > 0f)
            return;
        _mortarPredictionTimer = Mathf.Max(0.01f, _mortarPredictionInterval);

        Transform spawn = ResolveProjectileSpawn();
        if (spawn == null
            || !_aimProvider.TryGetAimDirection(spawn.position, runtime.Data.BaseRange, out Vector3 aimDirection)
            || !_aimProvider.TryGetMortarTerrainImpact(
                spawn.position,
                aimDirection,
                runtime.Data.BaseRange,
                mortar.ArcHeight,
                mortar.ShellCollisionRadius,
                mortar.ManualTravelTime,
                out RaycastHit terrainHit))
        {
            SetMortarMarkerVisible(false);
            return;
        }

        MortarPresentationSurface.Resolve(
            terrainHit,
            mortar.ManualExplosionRadius,
            transform,
            _mortarPresentationSupportHits,
            out Vector3 presentationPoint,
            out Vector3 normal);
        Vector3 markerPosition = presentationPoint + normal * _mortarSurfaceOffset;
        if (_authoredMortarMarker != null)
        {
            _authoredMortarMarker.gameObject.SetActive(true);
            _authoredMortarMarker.Configure(
                markerPosition,
                normal,
                Mathf.Max(_mortarLandingRingRadius, mortar.ManualExplosionRadius),
                mortar.ManualTravelTime,
                runtime.HasAdvancedPath ? runtime.SelectedPath : WeaponUpgradePath.None);
            if (_mortarMarkerRoot != null)
                _mortarMarkerRoot.SetActive(false);
            return;
        }

        _mortarMarkerRoot.transform.position = markerPosition;
        _mortarCenterDot.position = markerPosition;

        UpdateWorldRing(_mortarLandingRing, markerPosition, normal, _mortarLandingRingRadius);
        UpdateWorldRing(
            _mortarBlastRing,
            markerPosition,
            normal,
            Mathf.Max(_mortarLandingRingRadius, mortar.ManualExplosionRadius));
        SetMortarMarkerVisible(true);
    }

    private LineRenderer CreateWorldRing(
        Transform parent,
        string name,
        float width,
        Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        LineRenderer line = go.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = true;
        line.positionCount = MortarRingSegments;
        line.widthMultiplier = width;
        line.numCapVertices = 2;
        line.numCornerVertices = 2;
        line.sharedMaterial = _mortarLineMaterial;
        line.startColor = color;
        line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }

    private static void UpdateWorldRing(
        LineRenderer line,
        Vector3 center,
        Vector3 normal,
        float radius)
    {
        // Build an orthonormal basis so the rings lie flat on sloped terrain.
        Vector3 tangent = Vector3.Cross(normal, Vector3.forward);
        if (tangent.sqrMagnitude <= 0.0001f)
            tangent = Vector3.Cross(normal, Vector3.right);
        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(normal, tangent).normalized;

        float safeRadius = Mathf.Max(0.01f, radius);
        for (int i = 0; i < MortarRingSegments; i++)
        {
            float angle = i / (float)MortarRingSegments * Mathf.PI * 2f;
            Vector3 offset = tangent * Mathf.Cos(angle) * safeRadius
                + bitangent * Mathf.Sin(angle) * safeRadius;
            line.SetPosition(i, center + offset);
        }
    }

    private void SetMortarMarkerVisible(bool visible)
    {
        if (_authoredMortarMarker != null && _authoredMortarMarker.gameObject.activeSelf != visible)
            _authoredMortarMarker.gameObject.SetActive(visible);
        bool showLegacy = visible && _authoredMortarMarker == null;
        if (_mortarMarkerRoot != null && _mortarMarkerRoot.activeSelf != showLegacy)
            _mortarMarkerRoot.SetActive(showLegacy);
    }

    private void EnsureAuthoredMortarMarker(WeaponInstance runtime)
    {
        GameObject prefab = runtime?.Data?.PresentationProfile?.Mortar?.LandingIndicatorPrefab;
        if (prefab == _authoredMortarMarkerPrefab)
            return;

        if (_authoredMortarMarker != null)
            DestroyOwnedObject(_authoredMortarMarker.gameObject);
        _authoredMortarMarker = null;
        _authoredMortarMarkerPrefab = prefab;
        if (prefab == null)
            return;

        GameObject instance = Instantiate(prefab);
        instance.name = "Mortar Authored Landing Prediction";
        _authoredMortarMarker = instance.GetComponent<MortarLandingIndicatorVfx>();
        if (_authoredMortarMarker == null)
        {
            DestroyOwnedObject(instance);
            _authoredMortarMarkerPrefab = null;
            return;
        }
        instance.SetActive(false);
    }

    private RectTransform CreateCenteredRoot(string name, Vector2 size)
    {
        GameObject root = new GameObject(name);
        root.transform.SetParent(_canvasRoot.transform, false);

        RectTransform rect = root.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = size;
        return rect;
    }

    private void CreateStyledLine(
        Transform parent,
        string name,
        Vector2 anchoredPosition,
        Vector2 size,
        float rotation = 0f)
    {
        CreateImage(
            parent,
            name + "_Shadow",
            anchoredPosition + _shadowOffset,
            size,
            _shadowColor,
            GetWhiteSprite(),
            rotation);
        CreateImage(
            parent,
            name,
            anchoredPosition,
            size,
            _lineColor,
            GetWhiteSprite(),
            rotation,
            tintWithWeakPointFlash: true);
    }

    private void CreateStyledAnchoredLine(
        Transform parent,
        string name,
        Vector2 anchor,
        Vector2 anchoredPosition,
        Vector2 size)
    {
        CreateAnchoredImage(
            parent,
            name + "_Shadow",
            anchor,
            anchoredPosition + _shadowOffset,
            size,
            _shadowColor);
        CreateAnchoredImage(
            parent,
            name,
            anchor,
            anchoredPosition,
            size,
            _lineColor,
            tintWithWeakPointFlash: true);
    }

    private static void SetRootActive(RectTransform root, bool active)
    {
        if (root != null && root.gameObject.activeSelf != active)
            root.gameObject.SetActive(active);
    }

    private Image CreateImage(
        Transform parent,
        string name,
        Vector2 anchoredPosition,
        Vector2 size,
        Color color,
        Sprite sprite,
        float rotation = 0f,
        bool tintWithWeakPointFlash = false)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
        rect.localRotation = Quaternion.Euler(0f, 0f, rotation);

        Image image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = color;
        image.raycastTarget = false;
        RegisterReticleTintImage(image, color, tintWithWeakPointFlash);
        return image;
    }

    private Image CreateAnchoredImage(
        Transform parent,
        string name,
        Vector2 anchor,
        Vector2 anchoredPosition,
        Vector2 size,
        Color color,
        bool tintWithWeakPointFlash = false)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        RectTransform rect = go.AddComponent<RectTransform>();
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;

        Image image = go.AddComponent<Image>();
        image.sprite = GetWhiteSprite();
        image.color = color;
        image.raycastTarget = false;
        RegisterReticleTintImage(image, color, tintWithWeakPointFlash);
        return image;
    }

    private void RegisterReticleTintImage(Image image, Color baseColor, bool tintWithWeakPointFlash)
    {
        if (!tintWithWeakPointFlash || image == null)
            return;

        _reticleTintImages.Add(image);
        _reticleBaseColors[image] = baseColor;
    }

    private void HandleWeakPointHit()
    {
        _weakPointFlashActive = true;
        _weakPointFlashTimer = Mathf.Max(0.01f, _weakPointFlashDuration);
        ApplyWeakPointFlashColor();
    }

    private void TickWeakPointFlash()
    {
        if (!_weakPointFlashActive)
            return;

        _weakPointFlashTimer -= Time.unscaledDeltaTime;
        if (_weakPointFlashTimer > 0f)
        {
            ApplyWeakPointFlashColor();
            return;
        }

        _weakPointFlashActive = false;
        RestoreReticleTintColors();
    }

    private void ApplyWeakPointFlashColor()
    {
        for (int i = 0; i < _reticleTintImages.Count; i++)
        {
            Image image = _reticleTintImages[i];
            if (image != null)
                image.color = _weakPointFlashColor;
        }
    }

    private void RestoreReticleTintColors()
    {
        for (int i = 0; i < _reticleTintImages.Count; i++)
        {
            Image image = _reticleTintImages[i];
            if (image != null && _reticleBaseColors.TryGetValue(image, out Color baseColor))
                image.color = baseColor;
        }
    }

    private Sprite CreateRingSprite()
    {
        // Generate a crisp circular outline without requiring a project texture asset.
        _circleRingTexture = new Texture2D(
            CircleTextureSize,
            CircleTextureSize,
            TextureFormat.RGBA32,
            false)
        {
            name = "ReticleCircleRing",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.HideAndDontSave
        };

        Color[] pixels = new Color[CircleTextureSize * CircleTextureSize];
        float outerRadius = CircleTextureSize * 0.48f;
        float thicknessRatio = _circleLineThickness / Mathf.Max(1f, _circleDiameter);
        float innerRadius = outerRadius - CircleTextureSize * Mathf.Clamp(thicknessRatio, 0.02f, 0.3f);
        Vector2 center = Vector2.one * ((CircleTextureSize - 1) * 0.5f);

        for (int y = 0; y < CircleTextureSize; y++)
        {
            for (int x = 0; x < CircleTextureSize; x++)
            {
                float distance = Vector2.Distance(new Vector2(x, y), center);
                float outerAlpha = 1f - Mathf.Clamp01(distance - outerRadius + 1f);
                float innerAlpha = Mathf.Clamp01(distance - innerRadius + 1f);
                pixels[y * CircleTextureSize + x] = new Color(1f, 1f, 1f, outerAlpha * innerAlpha);
            }
        }

        _circleRingTexture.SetPixels(pixels);
        _circleRingTexture.Apply(false, true);
        return Sprite.Create(
            _circleRingTexture,
            new Rect(0f, 0f, CircleTextureSize, CircleTextureSize),
            new Vector2(0.5f, 0.5f),
            CircleTextureSize);
    }

    private static Sprite GetWhiteSprite()
    {
        if (s_whiteSprite != null)
            return s_whiteSprite;

        Texture2D texture = Texture2D.whiteTexture;
        s_whiteSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            100f);
        return s_whiteSprite;
    }

    private static Material CreateUnlitMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        Material material = new Material(shader)
        {
            color = color,
            hideFlags = HideFlags.HideAndDontSave
        };
        return material;
    }

    private static Material CreateLineMaterial()
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        return new Material(shader)
        {
            color = Color.white,
            hideFlags = HideFlags.HideAndDontSave
        };
    }

    private static void DestroyOwnedObject(Object ownedObject)
    {
        if (ownedObject == null)
            return;

        if (Application.isPlaying)
            Destroy(ownedObject);
        else
            DestroyImmediate(ownedObject);
    }
}
