using UnityEngine;

public interface IWeaponAimSink
{
    void AimAt(Transform target, Vector3 fallbackWorldPoint);
    void AimAlong(Vector3 worldDirection);
    void ClearAim();
}

public readonly struct WeaponFireOriginBinding
{
    public Transform Muzzle { get; }
    public IWeaponAimSink AimSink { get; }

    public WeaponFireOriginBinding(Transform muzzle, IWeaponAimSink aimSink = null)
    {
        Muzzle = muzzle;
        AimSink = aimSink;
    }

    public bool IsValid => Muzzle != null;

    public void AimAt(Transform target, Vector3 fallbackWorldPoint)
    {
        AimSink?.AimAt(target, fallbackWorldPoint);
    }

    public void AimAlong(Vector3 worldDirection)
    {
        AimSink?.AimAlong(worldDirection);
    }

    public void ClearAim()
    {
        AimSink?.ClearAim();
    }
}

public interface IWeaponFireOriginReceiver
{
    WeaponFireOriginBinding FireOrigin { get; }
    void SetFireOrigin(WeaponFireOriginBinding fireOrigin);
}
