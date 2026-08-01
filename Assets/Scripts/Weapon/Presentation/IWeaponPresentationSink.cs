public interface IWeaponPresentationSink
{
    void Emit(in WeaponPresentationContext context);

    WeaponPresentationLoopHandle BeginLoop(in WeaponPresentationContext context);

    void UpdateLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context);

    void EndLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context);
}
