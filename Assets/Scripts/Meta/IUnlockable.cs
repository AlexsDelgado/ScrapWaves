/// <summary>
/// Cualquier ScriptableObject de contenido (arma, ítem pasivo, etc.) que pueda estar
/// bloqueado por el sistema de meta-progresión hasta que se cumpla un <see cref="UnlockRequirement"/>.
/// </summary>
public interface IUnlockable
{
    string UnlockId { get; }
    /// <summary>Player-facing summary (English): what it does, how it plays, active ability.</summary>
    string Description { get; }
    bool UnlockedFromStart { get; }
    UnlockRequirement Requirement { get; }
}
