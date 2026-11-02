using System;
using System.Collections.Generic;

public enum WeaponGeometryPurpose { Targeting, ProjectilePath, Hitbox, Explosion }
public enum WeaponGeometryShape { Line, Circle, Sphere, PlanarCone, SphericalCone, Capsule, Arc, SweptCircle }
public enum WeaponGeometryAnchor { Muzzle, Owner }
public enum WeaponGeometryDirection { Aim, OwnerForward, HorizontalAim }

/// <summary>
/// A gameplay dimension observed through the weapon's own getters. Distances are
/// world metres after player/heat modifiers; angles are full angles in degrees.
/// A guide is a measurement, not a predicted target or an action simulation.
/// </summary>
public readonly struct WeaponGeometryGuide
{
    public readonly WeaponStatMode Mode;
    public readonly WeaponGeometryPurpose Purpose;
    public readonly WeaponGeometryShape Shape;
    public readonly WeaponGeometryAnchor Anchor;
    public readonly WeaponGeometryDirection Direction;
    public readonly float ForwardOffsetMeters;
    public readonly float RangeMeters;
    public readonly float RadiusMeters;
    public readonly float FullAngleDegrees;
    public readonly float ArcHeightMeters;

    public WeaponGeometryGuide(WeaponStatMode mode, WeaponGeometryPurpose purpose,
        WeaponGeometryShape shape, WeaponGeometryAnchor anchor,
        float rangeMeters = 0f, float radiusMeters = 0f, float fullAngleDegrees = 0f,
        float forwardOffsetMeters = 0f, float arcHeightMeters = 0f,
        WeaponGeometryDirection direction = WeaponGeometryDirection.Aim)
    {
        Mode = mode; Purpose = purpose; Shape = shape; Anchor = anchor; Direction = direction;
        RangeMeters = rangeMeters; RadiusMeters = radiusMeters; FullAngleDegrees = fullAngleDegrees;
        ForwardOffsetMeters = forwardOffsetMeters; ArcHeightMeters = arcHeightMeters;
    }
}

/// <summary>Immutable, observation-only geometry. Unsupported dimensions are omitted.</summary>
public sealed class WeaponGeometrySnapshot
{
    public IReadOnlyList<WeaponGeometryGuide> Guides { get; }

    public WeaponGeometrySnapshot(params WeaponGeometryGuide[] guides)
    {
        Guides = Array.AsReadOnly(guides == null ? Array.Empty<WeaponGeometryGuide>()
            : (WeaponGeometryGuide[])guides.Clone());
    }
}
