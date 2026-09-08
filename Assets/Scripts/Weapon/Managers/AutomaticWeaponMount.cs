using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class AutomaticWeaponMount : MonoBehaviour, IWeaponAimSink
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    [SerializeField] private Transform _aimPivot;
    [SerializeField] private Transform _recoilRoot;
    [SerializeField] private Transform _muzzle;
    [SerializeField] private GameObject _visualRoot;
    [SerializeField] private bool _wearable;
    [SerializeField, Min(1f)] private float _turnSpeedDegrees = 720f;
    [SerializeField, Min(0f)] private float _recoilDistance = 0.07f;
    [SerializeField, Min(0.01f)] private float _recoilRecoverySpeed = 1.4f;

    private Transform _owner;
    private Transform _target;
    private Vector3 _fallbackPoint;
    private Vector3 _desiredDirection;
    private bool _hasAim;
    private Vector3 _recoilBasePosition;
    private float _recoil;
    private WeaponInstance _weapon;
    private bool _automatic;
    private bool _indicatorStateApplied;
    private readonly List<IndicatorSlot> _indicators = new();
    private MaterialPropertyBlock _indicatorBlock;

    private readonly struct IndicatorSlot
    {
        public readonly Renderer Renderer;
        public readonly int Index;
        public readonly Color Color;
        public readonly Color Emission;

        public IndicatorSlot(Renderer renderer, int index, Material material)
        {
            Renderer = renderer;
            Index = index;
            Color = material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : material.color;
            Emission = material.HasProperty(EmissionColorId) ? material.GetColor(EmissionColorId) : Color;
        }
    }

    public Transform Muzzle => _muzzle != null ? _muzzle : transform;
    public WeaponInstance Weapon => _weapon;
    public bool IsAutomatic => _automatic;

    public void SetOwner(Transform owner) => _owner = owner;

    public void ConfigureWearable()
    {
        _wearable = true;
        CacheIndicators();
    }

    public void Configure(Transform owner, Transform aimPivot, Transform recoilRoot, Transform muzzle, GameObject visualRoot)
    {
        _owner = owner;
        _aimPivot = aimPivot;
        _recoilRoot = recoilRoot;
        _muzzle = muzzle;
        _visualRoot = visualRoot;
        CacheRecoilPosition();
    }

    public void Bind(WeaponInstance weapon)
    {
        _weapon = weapon;
        bool visible = weapon?.Data != null && (_wearable || weapon.Data.WeaponType != WeaponType.RotatingBlade);
        if (_visualRoot != null)
            _visualRoot.SetActive(visible);
        if (_recoilRoot != null)
            _recoilRoot.gameObject.SetActive(visible);
        if (visible && !_wearable)
            ApplyWeaponColor(ResolveWeaponColor(weapon.Data.WeaponType));
        SetAutomatic(visible && weapon.State == WeaponState.Automatic);
        ClearAim();
    }

    public void SetAutomatic(bool automatic)
    {
        automatic &= _weapon?.Data != null;
        if (_automatic == automatic && _indicatorStateApplied)
            return;
        _automatic = automatic;
        if (!_wearable)
            return;
        if (_indicators.Count == 0)
            CacheIndicators();
        _indicatorBlock ??= new MaterialPropertyBlock();
        foreach (IndicatorSlot indicator in _indicators)
        {
            if (indicator.Renderer == null)
                continue;
            _indicatorBlock.Clear();
            indicator.Renderer.GetPropertyBlock(_indicatorBlock, indicator.Index);
            Color color = automatic ? indicator.Color : new Color(0.018f, 0.035f, 0.018f, indicator.Color.a);
            _indicatorBlock.SetColor(BaseColorId, color);
            _indicatorBlock.SetColor(ColorId, color);
            _indicatorBlock.SetColor(EmissionColorId, automatic ? indicator.Emission : Color.black);
            indicator.Renderer.SetPropertyBlock(_indicatorBlock, indicator.Index);
        }
        _indicatorStateApplied = true;
    }

    public void AimAt(Transform target, Vector3 fallbackWorldPoint)
    {
        _target = target;
        _fallbackPoint = target == null ? fallbackWorldPoint : Vector3.zero;
        _hasAim = true;
    }

    public void AimAlong(Vector3 worldDirection)
    {
        _target = null;
        _desiredDirection = worldDirection;
        _hasAim = worldDirection.sqrMagnitude > 0.0001f;
    }

    public void ClearAim()
    {
        _target = null;
        _fallbackPoint = Vector3.zero;
        _desiredDirection = Vector3.zero;
        _hasAim = false;
    }

    public void RequestRecoil(float intensity)
    {
        if (_wearable)
            return;
        _recoil = Mathf.Max(_recoil, _recoilDistance * Mathf.Clamp01(intensity));
    }

    private void Awake()
    {
        if (_owner == null)
            _owner = transform.root;
        CacheRecoilPosition();
        if (_wearable)
            CacheIndicators();
    }

    private void LateUpdate()
    {
        TickAim();
        TickRecoil();
    }

    private void TickAim()
    {
        // Body attachments stay fixed, so aiming cannot swing a mesh into another
        // attachment or into the player. Weapon logic computes the shot direction.
        if (_wearable)
            return;
        if (_aimPivot == null)
            return;

        Vector3 direction;
        if (_target != null)
            direction = EnemyRegistry.GetAimPoint(_target) - _aimPivot.position;
        else if (_hasAim && _fallbackPoint != Vector3.zero)
            direction = _fallbackPoint - _aimPivot.position;
        else if (_hasAim)
            direction = _desiredDirection;
        else
            direction = _owner != null ? _owner.forward : transform.forward;

        if (direction.sqrMagnitude <= 0.0001f)
            return;

        Quaternion desired = Quaternion.LookRotation(direction.normalized, Vector3.up);
        _aimPivot.rotation = Quaternion.RotateTowards(
            _aimPivot.rotation,
            desired,
            _turnSpeedDegrees * Time.deltaTime);
    }

    private void TickRecoil()
    {
        if (_wearable)
            return;
        if (_recoilRoot == null)
            return;
        _recoil = Mathf.MoveTowards(_recoil, 0f, _recoilRecoverySpeed * Time.unscaledDeltaTime);
        _recoilRoot.localPosition = _recoilBasePosition + Vector3.back * _recoil;
    }

    private void CacheRecoilPosition()
    {
        if (_recoilRoot != null)
            _recoilBasePosition = _recoilRoot.localPosition;
    }

    private void CacheIndicators()
    {
        _indicators.Clear();
        _indicatorStateApplied = false;
        Transform root = _visualRoot != null ? _visualRoot.transform : transform;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int index = 0; index < materials.Length; index++)
            {
                Material material = materials[index];
                if (material != null && material.name.StartsWith("AutoIndicator", System.StringComparison.Ordinal))
                    _indicators.Add(new IndicatorSlot(renderer, index, material));
            }
        }
    }

    private void ApplyWeaponColor(Color color)
    {
        if (_aimPivot == null)
            return;
        Renderer[] renderers = _aimPivot.GetComponentsInChildren<Renderer>(true);
        MaterialPropertyBlock block = new();
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].GetPropertyBlock(block);
            block.SetColor(BaseColorId, color);
            block.SetColor(ColorId, color);
            renderers[i].SetPropertyBlock(block);
        }
    }

    private static Color ResolveWeaponColor(WeaponType type)
    {
        return type switch
        {
            WeaponType.AutomaticCannon => new Color(0.95f, 0.48f, 0.12f),
            WeaponType.RocketLauncher => new Color(0.82f, 0.22f, 0.12f),
            WeaponType.Mortar => new Color(0.45f, 0.72f, 0.42f),
            WeaponType.Flamethrower => new Color(1f, 0.72f, 0.12f),
            _ => Color.gray
        };
    }
}
