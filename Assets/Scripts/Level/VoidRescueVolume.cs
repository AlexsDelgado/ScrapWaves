using UnityEngine;

/// <summary>
/// Red de seguridad bajo el mapa: si el jugador cae al vacío (hueco de colisión, malla mal curvada, tunneling u otro bug)
/// y toca este trigger, lo devuelve al último punto donde estuvo parado de forma estable, sin velocidad y con unos
/// segundos de protección. Si todavía no estuvo parado en ningún lado, lo apoya sobre el suelo bajo su posición inicial.
/// Cada rescate deja un warning con dónde cayó, para poder encontrar y arreglar la colisión que falló.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class VoidRescueVolume : MonoBehaviour
{
    [Header("Punto seguro")]
    [SerializeField, Min(0f), Tooltip("Segundos seguidos apoyado en el suelo antes de que ese lugar cuente como punto seguro (evita guardar un borde por el que se resbala).")]
    private float _stableGroundSeconds = 0.35f;

    [SerializeField, Min(0.01f), Tooltip("Cada cuántos segundos como máximo se actualiza el punto seguro.")]
    private float _sampleInterval = 0.2f;

    [Header("Rescate")]
    [SerializeField, Min(0f), Tooltip("Metros sobre el suelo en los que se coloca al jugador, para que no aparezca incrustado.")]
    private float _liftAboveGround = 0.4f;

    [SerializeField, Min(0f), Tooltip("Segundos de protección después de ser rescatado. 0 = ninguna.")]
    private float _protectionSeconds = 1f;

    [SerializeField, Tooltip("Capas que cuentan como suelo al validar el destino. Vacío = Default + Terrain.")]
    private LayerMask _groundMask;

    private const float MaxGroundGapSeconds = 0.15f;

    private PlayerMovement _player;
    private Rigidbody _playerBody;
    private bool _hasStart;
    private Vector3 _start;
    private bool _hasSafe;
    private Vector3 _lastSafe;
    private float _groundedSeconds;
    private float _airSeconds;
    private float _nextSampleTime;
    private float _nextRescueTime;

    /// <summary>Cuántas veces rescató al jugador en esta partida.</summary>
    public int RescueCount { get; private set; }

    private int GroundMask => _groundMask.value != 0 ? _groundMask.value : LayerMask.GetMask("Default", "Terrain");

    private void Reset() => GetComponent<BoxCollider>().isTrigger = true;

    private void OnValidate()
    {
        if (TryGetComponent(out BoxCollider box))
            box.isTrigger = true;
    }

    private void Update()
    {
        if (!ResolvePlayer())
            return;

        if (!_hasStart)
        {
            _start = _player.transform.position;
            _hasStart = true;
        }

        if (GameplayPause.IsUiPaused || Time.timeScale <= 0f || EntranceCinematic.StartupHeld)
        {
            _groundedSeconds = 0f;
            return;
        }

        // El detector de suelo del jugador puede perder el apoyo uno o dos frames sobre terreno irregular: un hueco corto
        // no reinicia la cuenta, uno largo (salto, caída, slide) sí.
        bool stable = _player.IsGroundedOnSurface && !_player.IsSliding && !_player.IsDashing && !_player.IsLaunching;
        if (stable)
        {
            _groundedSeconds += Time.deltaTime;
            _airSeconds = 0f;
        }
        else
        {
            _airSeconds += Time.deltaTime;
            if (_airSeconds > MaxGroundGapSeconds)
                _groundedSeconds = 0f;
        }

        if (stable && _groundedSeconds >= _stableGroundSeconds && Time.time >= _nextSampleTime)
        {
            _lastSafe = _player.transform.position;
            _hasSafe = true;
            _nextSampleTime = Time.time + _sampleInterval;
        }
    }

    private bool ResolvePlayer()
    {
        if (_player != null)
            return true;

        Transform playerTransform = PlayerMovement.PlayerTransform;
        if (playerTransform != null)
        {
            _player = playerTransform.GetComponent<PlayerMovement>();
            _playerBody = playerTransform.GetComponent<Rigidbody>();
        }

        return _player != null;
    }

    private void OnTriggerEnter(Collider other) => TryRescue(other);

    // Por si el rescate no pudo sacarlo del volumen en el primer intento.
    private void OnTriggerStay(Collider other) => TryRescue(other);

    private void TryRescue(Collider other)
    {
        if (Time.time < _nextRescueTime || EntranceCinematic.StartupHeld)
            return;

        // Enemigos, proyectiles y pickups que caen también quedan dentro del volumen: se descartan sin buscar componentes.
        if (_player == null || other.attachedRigidbody != _playerBody)
            return;

        Rescue(_player);
    }

    private void Rescue(PlayerMovement player)
    {
        Vector3 from = player.transform.position;
        Vector3 destination = ResolveDestination(player, out string source);

        Rigidbody body = player.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.position = destination;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        player.transform.position = destination;
        Physics.SyncTransforms();

        PlayerHealth health = player.GetComponent<PlayerHealth>();
        if (health != null && _protectionSeconds > 0f)
            health.GrantInvulnerability(_protectionSeconds);

        RescueCount++;
        _groundedSeconds = 0f;
        _airSeconds = 0f;
        _nextRescueTime = Time.time + 0.5f;
        Debug.LogWarning(
            $"[VoidRescue] El jugador cayó bajo el mapa en {from.ToString("F1")} y fue devuelto a {destination.ToString("F1")} ({source}). " +
            "Revisar la colisión por donde cayó.",
            this);
    }

    private Vector3 ResolveDestination(PlayerMovement player, out string source)
    {
        int mask = GroundMask;
        if (_hasSafe && Physics.Raycast(_lastSafe + Vector3.up * 0.5f, Vector3.down, 3f, mask, QueryTriggerInteraction.Ignore))
        {
            source = "último punto estable";
            return _lastSafe + Vector3.up * _liftAboveGround;
        }

        if (_hasStart && Physics.Raycast(_start + Vector3.up * 5f, Vector3.down, out RaycastHit hit, 300f, mask, QueryTriggerInteraction.Ignore))
        {
            Collider body = player.GetComponent<Collider>();
            float halfHeight = body != null ? body.bounds.extents.y : 1f;
            source = "suelo bajo la posición inicial";
            return hit.point + Vector3.up * (halfHeight + _liftAboveGround);
        }

        source = "posición inicial";
        return _hasStart ? _start : player.transform.position;
    }

    private void OnDrawGizmos()
    {
        if (!TryGetComponent(out BoxCollider box))
            return;

        Matrix4x4 previous = Gizmos.matrix;
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0.1f, 0.1f, 0.08f);
        Gizmos.DrawCube(box.center, box.size);
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.7f);
        Gizmos.DrawWireCube(box.center, box.size);
        Gizmos.matrix = previous;
    }
}
