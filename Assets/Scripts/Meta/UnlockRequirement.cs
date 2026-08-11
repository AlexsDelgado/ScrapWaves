using System;
using UnityEngine;

/// <summary>
/// Condición para desbloquear un <see cref="IUnlockable"/>. Ambos campos son opcionales:
/// solo logro, solo precio, ambos, o ninguno (en cuyo caso nunca se puede comprar y debería
/// usarse <see cref="IUnlockable.UnlockedFromStart"/> en su lugar).
/// </summary>
[Serializable]
public class UnlockRequirement
{
    public AchievementDefinition RequiredAchievement;
    [Min(0)] public int ScrapPrice;
}
