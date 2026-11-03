using UnityEngine;

/// <summary>
/// Punto único de tuneo de los power-ups temporales para el game designer. Reparte un
/// <see cref="PowerupBalanceProfile"/> a todo el que lo consuma y expone en vivo lo que pasa mientras se balancea.
/// Como el profile es un asset, lo que se toque en Play Mode no se pierde al salir.
/// Corre antes que el resto para que la primera lectura ya vea el perfil.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-60)]
public class PowerupBalanceHub : MonoBehaviour
{
    [SerializeField, Tooltip("El asset con todas las perillas de powerups. Sin asignar, el juego usa los valores originales del código.")]
    private PowerupBalanceProfile _profile;

    public PowerupBalanceProfile Profile => _profile;

    private void Awake() => PowerupBalanceProfile.SetActive(_profile);

    private void OnDestroy() => PowerupBalanceProfile.ClearActive(_profile);

    // ---- Lectura en vivo (la consume PowerupBalanceHubEditor) ----

    private static TemporaryPowerupController Controller => TemporaryPowerupController.Instance;

    public bool HasController => Controller != null;

    /// <summary>Buff temporal activo, o null si no hay.</summary>
    public string ActiveBuffLabel
    {
        get
        {
            TemporaryPowerupController c = Controller;
            if (c == null)
                return "sin controlador (entrá a la partida)";

            string buff = c.ActiveBuff.HasValue ? $"{c.ActiveBuff.Value} ({c.BuffRemainingSeconds:0.0} s)" : "ninguno";
            string invuln = c.IsPowerupInvulnerable ? $" · Invulnerable ({c.InvulnerabilityRemainingSeconds:0.0} s)" : string.Empty;
            return buff + invuln;
        }
    }

    /// <summary>Factor de nivel 0–1 que usan los powerups ahora mismo (ya con el forzado aplicado).</summary>
    public float CurrentLevelT => Controller != null ? Controller.CurrentLevelT : 0f;

    /// <summary>Probabilidad real de drop con el Scavenging actual del jugador.</summary>
    public float EffectiveDropChance(bool eliteOrBoss)
    {
        PlayerStats stats = FindAnyObjectByType<PlayerStats>();
        float scavenging = stats != null && stats.GetDefinition(StatType.Scavenging) != null
            ? stats.GetStat(StatType.Scavenging)
            : PowerupBalanceProfile.Current.ReferenceScavenging;
        return PowerupBalanceProfile.Current.DropChance(eliteOrBoss, scavenging);
    }

    /// <summary>Aplica el powerup al jugador como si lo hubiera recogido.</summary>
    public void ApplyNow(TemporaryPowerupType type) => Controller?.Apply(type);

    /// <summary>Hace caer un powerup delante del jugador.</summary>
    public bool SpawnInFrontOfPlayer(TemporaryPowerupType type, float distance = 3f)
    {
        Transform player = PlayerMovement.PlayerTransform;
        if (player == null)
            return false;

        Vector3 position = player.position + player.forward * distance + Vector3.up * 0.5f;
        return TemporaryPowerupPool.GetInstance().TrySpawn(position, type);
    }
}
