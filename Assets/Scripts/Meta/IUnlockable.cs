/// <summary>
/// Cualquier ScriptableObject de contenido (arma, ítem pasivo, etc.) que pueda estar
/// bloqueado por el sistema de meta-progresión hasta que se cumpla un <see cref="UnlockRequirement"/>.
/// </summary>
public interface IUnlockable
{
    string UnlockId { get; }
    bool UnlockedFromStart { get; }
    UnlockRequirement Requirement { get; }
}
