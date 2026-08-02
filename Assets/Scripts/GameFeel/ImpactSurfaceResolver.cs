using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ImpactSurfaceMetadata : MonoBehaviour
{
    [SerializeField] private ImpactSurfaceType _surfaceType = ImpactSurfaceType.Default;
    public ImpactSurfaceType SurfaceType => _surfaceType;
}

public static class ImpactSurfaceResolver
{
    public static ImpactSurfaceType Resolve(Collider collider, IDamageable damageable = null)
    {
        if (collider == null)
            return damageable != null ? ImpactSurfaceType.EnemyOrganic : ImpactSurfaceType.Default;

        ImpactSurfaceMetadata metadata = collider.GetComponentInParent<ImpactSurfaceMetadata>();
        if (metadata != null)
            return metadata.SurfaceType;

        if (damageable != null || collider.GetComponentInParent<IDamageable>() != null)
            return ResolveEnemySurface(collider);

        int terrainLayer = LayerMask.NameToLayer("Terrain");
        if (terrainLayer >= 0 && collider.gameObject.layer == terrainLayer)
            return ImpactSurfaceType.GroundDirt;

        string physicsMaterialName = collider.sharedMaterial != null ? collider.sharedMaterial.name : string.Empty;
        if (ContainsAny(physicsMaterialName, "metal", "steel", "iron", "scrap"))
            return ImpactSurfaceType.EnvironmentMetal;
        if (ContainsAny(physicsMaterialName, "stone", "concrete", "rock"))
            return ImpactSurfaceType.StoneConcrete;
        if (ContainsAny(physicsMaterialName, "dirt", "ground", "soil", "sand"))
            return ImpactSurfaceType.GroundDirt;

        Renderer renderer = collider.GetComponentInParent<Renderer>();
        if (renderer != null && renderer.sharedMaterial != null)
        {
            string materialName = renderer.sharedMaterial.name;
            if (ContainsAny(materialName, "metal", "steel", "iron", "scrap"))
                return ImpactSurfaceType.EnvironmentMetal;
            if (ContainsAny(materialName, "stone", "concrete", "rock"))
                return ImpactSurfaceType.StoneConcrete;
            if (ContainsAny(materialName, "dirt", "ground", "soil", "sand"))
                return ImpactSurfaceType.GroundDirt;
        }

        return ImpactSurfaceType.Default;
    }

    private static ImpactSurfaceType ResolveEnemySurface(Collider collider)
    {
        Renderer renderer = collider.GetComponentInParent<Renderer>();
        string materialName = renderer != null && renderer.sharedMaterial != null
            ? renderer.sharedMaterial.name
            : collider.transform.root.name;
        return ContainsAny(materialName, "metal", "steel", "iron", "scrap", "drone", "robot")
            ? ImpactSurfaceType.EnemyMetal
            : ImpactSurfaceType.EnemyOrganic;
    }

    private static bool ContainsAny(string value, params string[] needles)
    {
        if (string.IsNullOrEmpty(value))
            return false;

        for (int i = 0; i < needles.Length; i++)
        {
            if (value.Contains(needles[i], StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
