using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public sealed class AnimatedWeaponSocket
{
    public WeaponType Type;
    [Tooltip("Bone child with the final artifact position, rotation and scale. Its local transform is the attachment offset.")]
    public Transform Socket;
}

[DisallowMultipleComponent]
public sealed class PlayerWeaponMountController : MonoBehaviour
{
    [SerializeField] private Transform _mainFirePoint;
    [SerializeField] private WearableWeaponMountCatalog _catalog;
    [SerializeField, Tooltip("Per-player animated attachment overrides. Unmapped weapons use the shared catalog's original placement.")]
    private AnimatedWeaponSocket[] _animatedSockets = System.Array.Empty<AnimatedWeaponSocket>();
    private readonly Dictionary<WeaponType, AutomaticWeaponMount> _mounts = new();
    private readonly Dictionary<IWeaponBehaviour, AutomaticWeaponMount> _equipped = new();
    private readonly HashSet<IWeaponBehaviour> _automatic = new();

    public Transform MainFirePoint => _mainFirePoint;

    public void ConfigureAnimatedSockets(AnimatedWeaponSocket[] sockets)
    {
        _animatedSockets = sockets ?? System.Array.Empty<AnimatedWeaponSocket>();
        // Reconfiguration preserves live muzzle objects and their behaviour bindings,
        // so an in-flight burst is not cancelled just by changing attachment offsets.
        foreach (KeyValuePair<WeaponType, AutomaticWeaponMount> pair in _mounts)
        {
            if (_catalog != null && _catalog.TryGet(pair.Key, out WearableWeaponMountDefinition definition)
                && TryResolveAttachment(definition, out Transform anchor, out bool animated))
                PlaceMount(pair.Value.transform, anchor, definition, animated);
        }
    }

    public Transform GetAnimatedSocket(WeaponType type)
    {
        foreach (AnimatedWeaponSocket binding in _animatedSockets)
        {
            if (binding != null && binding.Type == type && binding.Socket != null)
                return binding.Socket;
        }
        return null;
    }

    public void Initialize(Transform mainFirePoint)
    {
        if (mainFirePoint != null)
            _mainFirePoint = mainFirePoint;
        if (_catalog == null)
            _catalog = Resources.Load<WearableWeaponMountCatalog>("WearableWeaponMounts");
    }

    public void AddWeapon(IWeaponBehaviour weapon, bool manual)
    {
        if (weapon?.Runtime?.Data == null || _equipped.ContainsKey(weapon))
            return;
        Initialize(_mainFirePoint);
        WeaponType type = weapon.Runtime.Data.WeaponType;
        if (!_mounts.TryGetValue(type, out AutomaticWeaponMount mount))
        {
            mount = CreateMount(type);
            if (mount == null)
                return;
            _mounts.Add(type, mount);
        }
        _equipped.Add(weapon, mount);
        if (manual)
            SetManualWeapon(weapon);
        else
        {
            ApplyMode(weapon, mount, true);
            RefreshMount(mount);
        }
    }

    public void SetManualWeapon(IWeaponBehaviour weapon)
    {
        if (weapon == null || !_equipped.ContainsKey(weapon))
            return;
        foreach (KeyValuePair<IWeaponBehaviour, AutomaticWeaponMount> pair in _equipped)
            ApplyMode(pair.Key, pair.Value, pair.Key != weapon);
        RefreshMounts();
    }

    // Sandbox controls may change a runtime mode directly, outside the normal cycle.
    public void RefreshWeaponModes()
    {
        foreach (KeyValuePair<IWeaponBehaviour, AutomaticWeaponMount> pair in _equipped)
            ApplyMode(pair.Key, pair.Value, pair.Key.Runtime.State == WeaponState.Automatic);
        RefreshMounts();
    }

    public void RemoveWeapon(IWeaponBehaviour weapon)
    {
        if (weapon == null || !_equipped.TryGetValue(weapon, out AutomaticWeaponMount mount))
            return;
        BindOrigin(weapon, _mainFirePoint != null ? _mainFirePoint : transform, null);
        _equipped.Remove(weapon);
        _automatic.Remove(weapon);
        if (_equipped.ContainsValue(mount))
        {
            RefreshMount(mount);
            return;
        }
        _mounts.Remove(weapon.Runtime.Data.WeaponType);
        DestroyMount(mount);
    }

    public void ClearWeapons()
    {
        foreach (IWeaponBehaviour weapon in _equipped.Keys)
            BindOrigin(weapon, _mainFirePoint != null ? _mainFirePoint : transform, null);
        _equipped.Clear();
        _automatic.Clear();
        foreach (AutomaticWeaponMount mount in _mounts.Values)
            DestroyMount(mount);
        _mounts.Clear();
    }

    public AutomaticWeaponMount GetAssignedMount(IWeaponBehaviour weapon) =>
        weapon != null && _automatic.Contains(weapon) ? GetEquippedMount(weapon) : null;

    public AutomaticWeaponMount GetEquippedMount(IWeaponBehaviour weapon) =>
        weapon != null && _equipped.TryGetValue(weapon, out AutomaticWeaponMount mount) ? mount : null;

    private void LateUpdate() => RefreshWeaponModes();

    private void ApplyMode(IWeaponBehaviour weapon, AutomaticWeaponMount mount, bool automatic)
    {
        if (automatic)
            _automatic.Add(weapon);
        else
            _automatic.Remove(weapon);
        BindOrigin(weapon, automatic ? mount.Muzzle : (_mainFirePoint != null ? _mainFirePoint : transform),
            automatic ? mount : null);
    }

    private static void BindOrigin(IWeaponBehaviour weapon, Transform muzzle, IWeaponAimSink aimSink)
    {
        if (weapon is IWeaponFireOriginReceiver receiver &&
            (receiver.FireOrigin.Muzzle != muzzle || receiver.FireOrigin.AimSink != aimSink))
            receiver.SetFireOrigin(new WeaponFireOriginBinding(muzzle, aimSink));
    }

    private void RefreshMounts()
    {
        foreach (AutomaticWeaponMount mount in _mounts.Values)
            RefreshMount(mount);
    }

    private void RefreshMount(AutomaticWeaponMount mount)
    {
        WeaponInstance representative = null;
        bool automatic = false;
        // Duplicate sandbox slots share one physical attachment. Its light is on
        // while any copy is automatic, without intersecting duplicate models.
        foreach (KeyValuePair<IWeaponBehaviour, AutomaticWeaponMount> pair in _equipped)
        {
            if (pair.Value != mount)
                continue;
            representative ??= pair.Key.Runtime;
            if (_automatic.Contains(pair.Key))
            {
                representative = pair.Key.Runtime;
                automatic = true;
                break;
            }
        }
        if (mount.Weapon != representative)
            mount.Bind(representative);
        mount.SetAutomatic(automatic);
    }

    private AutomaticWeaponMount CreateMount(WeaponType type)
    {
        if (_catalog == null || !_catalog.TryGet(type, out WearableWeaponMountDefinition definition)
            || definition.Prefab == null)
        {
            Debug.LogError($"No wearable fire point configured for {type}.", this);
            return null;
        }
        if (!TryResolveAttachment(definition, out Transform anchor, out bool animated))
        {
            Debug.LogError($"Wearable attachment '{definition.AttachmentPath}' was not found for {type}.", this);
            return null;
        }
        GameObject instance = Instantiate(definition.Prefab, anchor, false);
        instance.name = $"{type} Wearable Fire Point";
        PlaceMount(instance.transform, anchor, definition, animated);
        AutomaticWeaponMount mount = instance.GetComponent<AutomaticWeaponMount>();
        if (mount == null)
        {
            Debug.LogError($"Wearable prefab for {type} is missing AutomaticWeaponMount.", this);
            instance.SetActive(false);
            if (Application.isPlaying) Destroy(instance); else DestroyImmediate(instance);
            return null;
        }
        mount.SetOwner(transform);
        return mount;
    }

    private bool TryResolveAttachment(WearableWeaponMountDefinition definition, out Transform anchor, out bool animated)
    {
        anchor = GetAnimatedSocket(definition.Type);
        animated = anchor != null;
        if (!animated)
            anchor = string.IsNullOrEmpty(definition.AttachmentPath)
                ? transform : transform.Find(definition.AttachmentPath);
        return anchor != null;
    }

    private static void PlaceMount(Transform mount, Transform anchor, WearableWeaponMountDefinition definition, bool animated)
    {
        mount.SetParent(anchor, false);
        // Animated sockets carry the full calibrated bind-pose offset. Reapplying
        // the root-relative catalog offset here would displace the artifact twice.
        mount.localPosition = animated ? Vector3.zero : definition.LocalPosition;
        mount.localRotation = animated ? Quaternion.identity : Quaternion.Euler(definition.LocalEulerAngles);
        mount.localScale = animated ? Vector3.one : definition.LocalScale;
    }

    private static void DestroyMount(AutomaticWeaponMount mount)
    {
        if (mount == null)
            return;
        mount.Bind(null);
        mount.gameObject.SetActive(false);
        if (Application.isPlaying) Destroy(mount.gameObject); else DestroyImmediate(mount.gameObject);
    }
}
