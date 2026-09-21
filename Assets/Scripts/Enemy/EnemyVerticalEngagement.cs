using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Engagement vertical asimétrico: si el enemigo queda claramente DEBAJO del jugador,
/// entra en sleep (sin chase / daño planar / auto-target) y tras un tiempo vuelve al pool.
/// Si está arriba o en el mismo piso, permanece engaged (puede caer hacia el jugador).
/// </summary>
[DisallowMultipleComponent]
public class EnemyVerticalEngagement : MonoBehaviour, IEnemySpawnLifecycle
{
    private static readonly HashSet<int> s_DisengagedIds = new(256);

    private const float GroundProbeUp = 2f;
    private const float GroundProbeDistance = 60f;
    private static int s_GroundMask = -1;

    private static int GroundMask
    {
        get
        {
            if (s_GroundMask == -1)
                s_GroundMask = LayerMask.GetMask("Terrain", "Default");
            return s_GroundMask;
        }
    }

    [SerializeField, Min(0.5f), Tooltip("Si player.y - enemy.y supera esto, el enemigo está demasiado abajo. Pisos ~50u → usar ~40.")]
    private float _belowThreshold = 40f;

    [SerializeField, Min(0.5f), Tooltip("Segundos en sleep debajo antes de entrar en dormancia (congelado y oculto, NO se borra).")]
    private float _dormancyAfterSeconds = 10f;

    [SerializeField, Tooltip("Si true, no aplica (p. ej. Stalker/GigaWorm, elites y bosses de objetivo).")]
    private bool _exempt;

    private float _sleepElapsed;
    private bool _disengaged;
    private int _instanceId;
    private bool _defaultExempt;
    private bool _defaultExemptCached;

    private bool _dormant;
    private EnemyBehaviorBase[] _behaviors;
    private bool[] _behaviorsWereEnabled;
    private SimpleFollow _simpleFollow;
    private bool _simpleFollowWasEnabled;
    private EnemyFollow _enemyFollow;
    private bool _enemyFollowWasEnabled;
    private Rigidbody _rigidbody;
    private bool _rigidbodyWasKinematic;
    private CharacterController _characterController;
    private bool _characterControllerWasEnabled;
    private Collider[] _colliders;
    private bool[] _collidersWereEnabled;
    private Renderer[] _renderers;
    private bool[] _renderersWereEnabled;
    private EnemyHitFeedback _hitFeedback;
    private bool _hitFeedbackWasEnabled;

    public bool IsVerticallyDisengaged => _disengaged;

    /// <summary>Congelado y oculto por altura, pero presente en el mundo.</summary>
    public bool IsDormant => _dormant;

    public bool IsExempt => _exempt;

    /// <summary>
    /// Marca al enemigo como objetivo (elite / boss): nunca se duerme ni se despawnea por altura.
    /// Lo llaman los spawners de objetivos justo después de obtener la instancia. Se revierte solo
    /// al devolver la instancia al pool, vía <see cref="ResetEngagement"/>.
    /// </summary>
    public void SetExempt(bool exempt)
    {
        CacheDefaultExempt();
        _exempt = exempt;
        if (exempt)
            ClearDisengaged();
    }

    public static bool IsDisengaged(Transform t)
    {
        return t != null && s_DisengagedIds.Contains(t.GetInstanceID());
    }

    public static bool IsDisengaged(Component c)
    {
        return c != null && s_DisengagedIds.Contains(c.transform.GetInstanceID());
    }

    /// <summary>Asegura el componente salvo bosses con sense propio (GigaWorm).</summary>
    public static EnemyVerticalEngagement EnsureOn(GameObject go)
    {
        if (go == null)
            return null;

        if (go.GetComponent<GigaWormBehavior>() != null)
            return null;

        EnemyVerticalEngagement existing = go.GetComponent<EnemyVerticalEngagement>();
        if (existing != null)
            return existing;

        return go.AddComponent<EnemyVerticalEngagement>();
    }

    private void Awake()
    {
        _instanceId = transform.GetInstanceID();
        if (GetComponent<GigaWormBehavior>() != null)
            _exempt = true;

        CacheDefaultExempt();
    }

    /// <summary>
    /// Guarda el valor de fábrica una sola vez, antes de que ningún spawner llame a
    /// <see cref="SetExempt"/>. Sin esto, la primera marca de elite quedaría pegada a la
    /// instancia pooleada para siempre.
    /// </summary>
    private void CacheDefaultExempt()
    {
        if (_defaultExemptCached)
            return;

        _defaultExempt = _exempt;
        _defaultExemptCached = true;
    }

    private void OnEnable()
    {
        _instanceId = transform.GetInstanceID();
        ResetEngagement();
    }

    private void OnDisable()
    {
        ExitDormancy();
        ClearDisengaged();
    }

    private void OnDestroy()
    {
        EnemyDormancyRegistry.Unregister(this);
        ClearDisengaged();
    }

    public void ResetEngagement()
    {
        CacheDefaultExempt();
        _exempt = _defaultExempt;
        _sleepElapsed = 0f;
        SetDisengaged(false);
    }

    private void Update()
    {
        if (_exempt || !isActiveAndEnabled)
            return;

        Transform player = PlayerMovement.PlayerTransform;
        if (player == null)
        {
            SetDisengaged(false);
            _sleepElapsed = 0f;
            return;
        }

        float deltaBelow = player.position.y - transform.position.y;
        if (deltaBelow > _belowThreshold)
        {
            SetDisengaged(true);
            if (_dormant)
                return;

            _sleepElapsed += Time.deltaTime;
            if (_sleepElapsed >= _dormancyAfterSeconds)
                EnterDormancy();
            return;
        }

        if (_dormant)
            ExitDormancy();

        SetDisengaged(false);
        _sleepElapsed = 0f;
    }

    private void SetDisengaged(bool value)
    {
        if (_disengaged == value)
            return;

        _disengaged = value;
        if (value)
            s_DisengagedIds.Add(_instanceId);
        else
            s_DisengagedIds.Remove(_instanceId);
    }

    private void ClearDisengaged()
    {
        _disengaged = false;
        _sleepElapsed = 0f;
        s_DisengagedIds.Remove(_instanceId);
    }

    /// <summary>
    /// Congela y oculta al enemigo conservándolo en el mundo: al volver a bajar sigue ahí.
    /// Antes esto lo devolvía al pool, que borraba el piso entero y además colgaba el contador
    /// de elites porque nunca disparaba <see cref="EnemyHealth.OnDied"/>.
    /// </summary>
    public void EnterDormancy()
    {
        if (_dormant || _exempt)
            return;

        _dormant = true;

        // 1. Su Update sigue corriendo y al expirar llamaría SetMovement(true) sobre un dormido.
        //    Destruirlo primero deja que su OnDestroy restaure, y recién después congelamos.
        WeaponMovementFreezeStatus freeze = GetComponent<WeaponMovementFreezeStatus>();
        if (freeze != null)
            Destroy(freeze);

        // 2. Behaviors ANTES que los followers: el OnDisable de cada behavior llama a
        //    SetGenericMovement(true), o sea que apagarlo después volvería a prender el chase.
        _behaviors = GetComponents<EnemyBehaviorBase>();
        _behaviorsWereEnabled = new bool[_behaviors.Length];
        for (int i = 0; i < _behaviors.Length; i++)
        {
            _behaviorsWereEnabled[i] = _behaviors[i].enabled;
            _behaviors[i].enabled = false;
        }

        // 3. Followers, cacheando su estado real (puede venir deshabilitado a propósito).
        _simpleFollow = GetComponent<SimpleFollow>();
        if (_simpleFollow != null)
        {
            _simpleFollowWasEnabled = _simpleFollow.enabled;
            _simpleFollow.enabled = false;
        }

        _enemyFollow = GetComponent<EnemyFollow>();
        if (_enemyFollow != null)
        {
            _enemyFollowWasEnabled = _enemyFollow.enabled;
            _enemyFollow.ResetVerticalVelocity();
            _enemyFollow.enabled = false;
        }

        // 4. Rigidbody: sin esto sigue acumulando velocidad aunque el follower esté apagado.
        _rigidbody = GetComponent<Rigidbody>();
        if (_rigidbody != null)
        {
            _rigidbodyWasKinematic = _rigidbody.isKinematic;
            if (!_rigidbody.isKinematic)
            {
                _rigidbody.linearVelocity = Vector3.zero;
                _rigidbody.angularVelocity = Vector3.zero;
                _rigidbody.Sleep();
            }
            _rigidbody.isKinematic = true;
        }

        // 5. Apoyarlo en el piso: mientras está disengaged EnemyFollow no aplica gravedad, así que
        //    uno congelado en el aire quedaría flotando a la vista al volver a bajar.
        SnapToGround();

        // 6. Estado pendiente que se dispararía al despertar.
        GetComponent<EnemyKnockbackReceiver>()?.ClearPending();
        if (TryGetComponent(out EnemyHealth health) && health.IsInvincible)
            health.SetInvincible(false);

        SetVisualsAndCollision(false);
    }

    /// <summary>Restaura exactamente el estado previo a <see cref="EnterDormancy"/>.</summary>
    public void ExitDormancy()
    {
        if (!_dormant)
            return;

        _dormant = false;

        SetVisualsAndCollision(true);

        if (_rigidbody != null)
            _rigidbody.isKinematic = _rigidbodyWasKinematic;

        if (_enemyFollow != null)
            _enemyFollow.ResetVerticalVelocity();

        // Con behavior: se re-habilita SOLO el behavior. Su OnEnable resetea el estado y llama a
        // SetGenericMovement(true), que respeta los flags *InitiallyEnabled. Tocar los followers
        // a mano acá pisaría esa decisión.
        bool hasBehavior = _behaviors != null && _behaviors.Length > 0;
        if (hasBehavior)
        {
            for (int i = 0; i < _behaviors.Length; i++)
            {
                if (_behaviors[i] != null)
                    _behaviors[i].enabled = _behaviorsWereEnabled[i];
            }
        }
        else
        {
            if (_simpleFollow != null)
                _simpleFollow.enabled = _simpleFollowWasEnabled;
            if (_enemyFollow != null)
                _enemyFollow.enabled = _enemyFollowWasEnabled;
        }

        _behaviors = null;
        _behaviorsWereEnabled = null;
    }

    private void SetVisualsAndCollision(bool active)
    {
        if (!active)
        {
            _hitFeedback = GetComponent<EnemyHitFeedback>();
            if (_hitFeedback != null)
            {
                _hitFeedbackWasEnabled = _hitFeedback.enabled;
                // Su OnDisable hace RestoreVisuals(); tiene que correr ANTES de cachear renderers,
                // porque crea renderers hijos de flash que no queremos dejar prendidos.
                _hitFeedback.enabled = false;
            }

            _characterController = GetComponent<CharacterController>();
            if (_characterController != null)
            {
                _characterControllerWasEnabled = _characterController.enabled;
                _characterController.enabled = false;
            }

            _colliders = GetComponentsInChildren<Collider>(true);
            _collidersWereEnabled = new bool[_colliders.Length];
            for (int i = 0; i < _colliders.Length; i++)
            {
                _collidersWereEnabled[i] = _colliders[i].enabled;
                _colliders[i].enabled = false;
            }

            _renderers = GetComponentsInChildren<Renderer>(true);
            _renderersWereEnabled = new bool[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                _renderersWereEnabled[i] = _renderers[i].enabled;
                _renderers[i].enabled = false;
            }

            EnemyDormancyRegistry.Register(this);
            return;
        }

        EnemyDormancyRegistry.Unregister(this);

        if (_renderers != null)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (_renderers[i] != null)
                    _renderers[i].enabled = _renderersWereEnabled[i];
            }
            _renderers = null;
            _renderersWereEnabled = null;
        }

        if (_colliders != null)
        {
            for (int i = 0; i < _colliders.Length; i++)
            {
                if (_colliders[i] != null)
                    _colliders[i].enabled = _collidersWereEnabled[i];
            }
            _colliders = null;
            _collidersWereEnabled = null;
        }

        if (_characterController != null)
            _characterController.enabled = _characterControllerWasEnabled;

        if (_hitFeedback != null)
            _hitFeedback.enabled = _hitFeedbackWasEnabled;
    }

    private void SnapToGround()
    {
        Vector3 origin = transform.position + Vector3.up * GroundProbeUp;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, GroundProbeDistance, GroundMask, QueryTriggerInteraction.Ignore))
            transform.position = new Vector3(transform.position.x, hit.point.y, transform.position.z);
    }

    // ---- IEnemySpawnLifecycle ----
    // Obligatorio: SetActive(false)/(true) NO restaura los .enabled por componente, así que
    // liberar al pool un enemigo todavía dormido devolvería una instancia invisible, sin
    // colliders y sin movimiento para siempre. OnPoolDespawn corre antes del SetActive(false).

    public void OnPoolSpawn()
    {
        ExitDormancy();
        ResetEngagement();
    }

    public void OnPoolDespawn()
    {
        ExitDormancy();
    }
}
