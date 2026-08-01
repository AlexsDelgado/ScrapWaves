using UnityEngine;

public readonly struct WeaponPresentationLoopHandle
{
    public readonly int Id;

    public WeaponPresentationLoopHandle(int id)
    {
        Id = Mathf.Max(0, id);
    }

    public bool IsValid => Id > 0;
}

public readonly struct WeaponPresentationContext
{
    public readonly WeaponPresentationCue Cue;
    public readonly WeaponInstance Weapon;
    public readonly Vector3 Position;
    public readonly Vector3 Direction;
    public readonly float Intensity;
    public readonly Transform Target;
    public readonly bool IsAbility;
    public readonly bool IsCritical;
    public readonly bool IsWeakPoint;
    public readonly Transform Anchor;

    public WeaponPresentationContext(
        WeaponPresentationCue cue,
        WeaponInstance weapon,
        Vector3 position,
        Vector3 direction,
        float intensity = 1f,
        Transform target = null,
        bool isAbility = false,
        bool isCritical = false,
        bool isWeakPoint = false,
        Transform anchor = null)
    {
        Cue = cue;
        Weapon = weapon;
        Position = position;
        Direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        Intensity = Mathf.Max(0f, intensity);
        Target = target;
        IsAbility = isAbility;
        IsCritical = isCritical;
        IsWeakPoint = isWeakPoint;
        Anchor = anchor;
    }
}
