using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class PlayerWeaponMountController : MonoBehaviour
{
    [SerializeField] private Transform _mainFirePoint;
    [SerializeField] private AutomaticWeaponMount _rightMount;
    [SerializeField] private AutomaticWeaponMount _leftMount;
    [SerializeField] private Vector3 _rightMountOffset = new(0.58f, 1.05f, 0.05f);
    [SerializeField] private Vector3 _leftMountOffset = new(-0.58f, 1.05f, 0.05f);

    private readonly Dictionary<IWeaponBehaviour, AutomaticWeaponMount> _automaticAssignments = new();
    private IWeaponBehaviour _manualWeapon;

    public AutomaticWeaponMount RightMount => _rightMount;
    public AutomaticWeaponMount LeftMount => _leftMount;

    public void Initialize(Transform mainFirePoint)
    {
        if (mainFirePoint != null)
            _mainFirePoint = mainFirePoint;
        EnsureMounts();
    }

    public void AddWeapon(IWeaponBehaviour weapon, bool manual)
    {
        if (weapon == null)
            return;
        EnsureMounts();

        if (manual || _manualWeapon == null)
        {
            _manualWeapon = weapon;
            BindToMain(weapon);
            return;
        }

        AutomaticWeaponMount mount = GetFirstFreeMount();
        if (mount != null)
            BindToMount(weapon, mount);
    }

    public void SetManualWeapon(IWeaponBehaviour weapon)
    {
        if (weapon == null || weapon == _manualWeapon)
            return;
        EnsureMounts();

        _automaticAssignments.TryGetValue(weapon, out AutomaticWeaponMount vacatedMount);
        IWeaponBehaviour outgoingManual = _manualWeapon;

        if (vacatedMount != null)
            _automaticAssignments.Remove(weapon);

        _manualWeapon = weapon;
        BindToMain(weapon);

        if (outgoingManual != null)
        {
            AutomaticWeaponMount destination = vacatedMount != null ? vacatedMount : GetFirstFreeMount();
            if (destination != null)
                BindToMount(outgoingManual, destination);
        }
    }

    public void RemoveWeapon(IWeaponBehaviour weapon)
    {
        if (weapon == null)
            return;

        if (_manualWeapon == weapon)
        {
            _manualWeapon = null;
            return;
        }

        if (_automaticAssignments.TryGetValue(weapon, out AutomaticWeaponMount mount))
        {
            _automaticAssignments.Remove(weapon);
            mount?.Bind(null);
        }
    }

    public void ClearWeapons()
    {
        foreach (KeyValuePair<IWeaponBehaviour, AutomaticWeaponMount> pair in _automaticAssignments)
            pair.Value?.Bind(null);
        _automaticAssignments.Clear();
        _manualWeapon = null;
        _rightMount?.Bind(null);
        _leftMount?.Bind(null);
    }

    public AutomaticWeaponMount GetAssignedMount(IWeaponBehaviour weapon)
    {
        return weapon != null && _automaticAssignments.TryGetValue(weapon, out AutomaticWeaponMount mount)
            ? mount
            : null;
    }

    private void BindToMain(IWeaponBehaviour weapon)
    {
        if (weapon is IWeaponFireOriginReceiver receiver)
            receiver.SetFireOrigin(new WeaponFireOriginBinding(_mainFirePoint != null ? _mainFirePoint : transform));
    }

    private void BindToMount(IWeaponBehaviour weapon, AutomaticWeaponMount mount)
    {
        if (mount == null)
            return;
        _automaticAssignments[weapon] = mount;
        mount.Bind(weapon.Runtime);
        if (weapon is IWeaponFireOriginReceiver receiver)
            receiver.SetFireOrigin(new WeaponFireOriginBinding(mount.Muzzle, mount));
    }

    private AutomaticWeaponMount GetFirstFreeMount()
    {
        bool rightUsed = _automaticAssignments.ContainsValue(_rightMount);
        if (!rightUsed)
            return _rightMount;
        bool leftUsed = _automaticAssignments.ContainsValue(_leftMount);
        return leftUsed ? null : _leftMount;
    }

    private void EnsureMounts()
    {
        if (_rightMount == null)
            _rightMount = CreatePlaceholderMount("Automatic Mount Right", _rightMountOffset);
        if (_leftMount == null)
            _leftMount = CreatePlaceholderMount("Automatic Mount Left", _leftMountOffset);
    }

    private AutomaticWeaponMount CreatePlaceholderMount(string mountName, Vector3 localOffset)
    {
        GameObject root = new(mountName);
        root.transform.SetParent(transform, false);
        root.transform.localPosition = localOffset;

        GameObject pivotObject = new("Aim Pivot");
        pivotObject.transform.SetParent(root.transform, false);

        GameObject visualRoot = new("Placeholder Visual");
        visualRoot.transform.SetParent(pivotObject.transform, false);

        GameObject baseMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
        baseMesh.name = "Mount Body";
        baseMesh.transform.SetParent(visualRoot.transform, false);
        baseMesh.transform.localScale = new Vector3(0.34f, 0.24f, 0.42f);
        RemoveCollider(baseMesh);

        GameObject recoilRoot = new("Recoil Root");
        recoilRoot.transform.SetParent(pivotObject.transform, false);

        GameObject barrel = GameObject.CreatePrimitive(PrimitiveType.Cube);
        barrel.name = "Barrel";
        barrel.transform.SetParent(recoilRoot.transform, false);
        barrel.transform.localPosition = new Vector3(0f, 0f, 0.38f);
        barrel.transform.localScale = new Vector3(0.12f, 0.12f, 0.62f);
        RemoveCollider(barrel);

        GameObject muzzleObject = new("Muzzle");
        muzzleObject.transform.SetParent(recoilRoot.transform, false);
        muzzleObject.transform.localPosition = new Vector3(0f, 0f, 0.72f);

        AutomaticWeaponMount mount = root.AddComponent<AutomaticWeaponMount>();
        mount.Configure(transform, pivotObject.transform, recoilRoot.transform, muzzleObject.transform, visualRoot);
        mount.Bind(null);
        return mount;
    }

    private static void RemoveCollider(GameObject gameObject)
    {
        Collider collider = gameObject.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            if (Application.isPlaying)
                Destroy(collider);
            else
                DestroyImmediate(collider);
        }
    }
}
