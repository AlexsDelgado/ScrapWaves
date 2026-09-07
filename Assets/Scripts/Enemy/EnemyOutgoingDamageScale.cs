using UnityEngine;

/// <summary>
/// Multiplicador de daño saliente cacheado al spawn (misma curva que velocidad: hasta x1.35).
/// </summary>
[DisallowMultipleComponent]
public class EnemyOutgoingDamageScale : MonoBehaviour
{
    private float _multiplier = 1f;

    public float Multiplier => _multiplier;

    public void ConfigureForSpawn(float multiplier)
    {
        _multiplier = Mathf.Max(0.1f, multiplier);
    }

    public void ResetForDespawn()
    {
        _multiplier = 1f;
    }

    public int Scale(int baseDamage)
    {
        if (baseDamage <= 0)
            return 0;
        return Mathf.Max(1, Mathf.RoundToInt(baseDamage * _multiplier));
    }

    public static int ScaleFrom(Component source, int baseDamage)
    {
        if (baseDamage <= 0)
            return 0;

        if (source != null)
        {
            EnemyOutgoingDamageScale scale = source.GetComponentInParent<EnemyOutgoingDamageScale>();
            if (scale != null)
                return scale.Scale(baseDamage);
        }

        DifficultyManager difficulty = DifficultyManager.Instance;
        if (difficulty != null)
            return Mathf.Max(1, Mathf.RoundToInt(baseDamage * difficulty.GetEnemyDamageMultiplier()));

        return baseDamage;
    }
}
