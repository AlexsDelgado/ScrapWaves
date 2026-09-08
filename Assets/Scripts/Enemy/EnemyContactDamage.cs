using UnityEngine;

public class EnemyContactDamage : MonoBehaviour
{
    [SerializeField, Min(1)] private int _contactDamage = 5;

    [SerializeField, Min(0f), Tooltip("Knockback pequeño al jugador en cada hit de contacto.")]
    private float _contactPushForce = 3f;

    [SerializeField, Tooltip("Enable console logs to debug contact damage.")]
    private bool _logDebug;

    public void TryApplyContactDamage(PlayerHealth player)
    {
        if (player == null || !player.IsAlive)
            return;

        if (player.IsInvulnerable)
            return;

        int damage = EnemyOutgoingDamageScale.ScaleFrom(this, _contactDamage);
        player.TakeDamage(damage);

        if (_contactPushForce > 0f)
            PlayerCombatHooks.TryPush(transform.position, _contactPushForce);

        if (_logDebug)
            Debug.Log($"[EnemyContactDamage] Daño={damage} (base={_contactDamage}) HP jugador={player.CurrentHealth}/{player.MaxHealth}", this);
    }
}
