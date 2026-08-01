public sealed class NullWeaponPresentationSink : IWeaponPresentationSink
{
    public static NullWeaponPresentationSink Instance { get; } = new();

    private NullWeaponPresentationSink()
    {
    }

    public void Emit(in WeaponPresentationContext context)
    {
    }

    public WeaponPresentationLoopHandle BeginLoop(in WeaponPresentationContext context)
    {
        return default;
    }

    public void UpdateLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context)
    {
    }

    public void EndLoop(WeaponPresentationLoopHandle handle, in WeaponPresentationContext context)
    {
    }
}
