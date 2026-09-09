using System;
using UnityEngine;

[Serializable]
public sealed class WearableWeaponMountDefinition
{
    public WeaponType Type;
    public GameObject Prefab;
    public string AttachmentPath = string.Empty;
    public Vector3 LocalPosition;
    public Vector3 LocalEulerAngles;
    public Vector3 LocalScale = Vector3.one;
}

/// <summary>Authored body attachments shared by gameplay and sandbox weapon controllers.</summary>
[CreateAssetMenu(menuName = "Weapons/Wearable Mount Catalog")]
public sealed class WearableWeaponMountCatalog : ScriptableObject
{
    public const string ResourceName = "WearableWeaponMounts";

    [SerializeField] private WearableWeaponMountDefinition[] _definitions = Array.Empty<WearableWeaponMountDefinition>();

    public WearableWeaponMountDefinition[] Definitions => _definitions;

    public bool TryGet(WeaponType type, out WearableWeaponMountDefinition definition)
    {
        for (int i = 0; i < _definitions.Length; i++)
        {
            if (_definitions[i] != null && _definitions[i].Type == type && _definitions[i].Prefab != null)
            {
                definition = _definitions[i];
                return true;
            }
        }

        definition = null;
        return false;
    }

#if UNITY_EDITOR
    public void SetDefinitions(WearableWeaponMountDefinition[] definitions)
    {
        _definitions = definitions ?? Array.Empty<WearableWeaponMountDefinition>();
    }
#endif
}
