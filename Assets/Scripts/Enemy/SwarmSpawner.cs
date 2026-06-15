using UnityEngine;

public class SwarmSpawner : MonoBehaviour
{
    [SerializeField] private SwarmEnemyPool _pool;

    [SerializeField, Tooltip("Vacío = FindAnyObjectByType. Escala intervalo, tamaño de oleada y stats al spawn.")]
    private DifficultyManager _difficultyManager;

    [SerializeField, Min(0.05f)] private float _spawnInterval = 1.5f;

    [SerializeField, Min(1)] private int _spawnPerWave = 5;

    [SerializeField, Min(0f)] private float _minSpawnRadius = 8f;

    [SerializeField, Min(0f)] private float _maxSpawnRadius = 18f;

    [SerializeField, Min(1)] private int _maxActiveEnemies = 200;

    [SerializeField] private bool _spawnOnStart = true;

    [SerializeField, Tooltip("Desplazamiento Y del spawn respecto al jugador (solo XZ se randomiza con el anillo).")]
    private float _spawnHeightOffset;

    [Header("Spawn en suelo")]
    [SerializeField, Tooltip("Raycast principal hacia abajo (por defecto solo Terrain si el mask queda en 0 en Awake).")]
    private LayerMask _groundRaycastMask;

    [SerializeField, Tooltip("Si el principal falla, se intenta este mask (0 en Awake = Terrain + Default).")]
    private LayerMask _fallbackGroundRaycastMask;

    [SerializeField, Tooltip("Colliders sólidos para detectar penetración y corregir (0 en Awake = Terrain + Default).")]
    private LayerMask _overlapSolidMask;

    [SerializeField, Min(1f), Tooltip("Altura sobre la referencia Y desde la que se lanza el raycast hacia abajo.")]
    private float _raycastStartHeight = 48f;

    [SerializeField, Min(1f), Tooltip("Longitud máxima del raycast hacia abajo.")]
    private float _raycastMaxDistance = 220f;

    [SerializeField, Min(0f), Tooltip("Preferir superficies con |Y - referencia| <= este valor. 0 = sin preferencia.")]
    private float _maxAbsSpawnSurfaceDeltaY = 3.5f;

    [SerializeField, Min(0f), Tooltip("Separación del punto de contacto a lo largo de la normal.")]
    private float _surfaceSeparation = 0.02f;

    [SerializeField, Min(0), Tooltip("Pasos máximos de proyección si el volumen intersecta geometría.")]
    private int _maxProjectionIterations = 14;

    [SerializeField, Min(0f), Tooltip("Paso vertical por iteración al despegar de geometría.")]
    private float _resolveStepUp = 0.08f;

    [SerializeField, Min(0f), Tooltip("Paso horizontal por iteración al despegar de geometría.")]
    private float _resolveStepOut = 0.06f;

    private Transform _player;
    private float _nextSpawnTime;

    private void Awake()
    {
        if (_difficultyManager == null)
            _difficultyManager = FindAnyObjectByType<DifficultyManager>();

        if (_groundRaycastMask.value == 0)
            _groundRaycastMask = LayerMask.GetMask("Terrain");
        if (_fallbackGroundRaycastMask.value == 0)
            _fallbackGroundRaycastMask = LayerMask.GetMask("Terrain", "Default");
        if (_overlapSolidMask.value == 0)
            _overlapSolidMask = LayerMask.GetMask("Terrain", "Default");
    }

    private void Start()
    {
        float interval = EffectiveSpawnInterval();
        _nextSpawnTime = _spawnOnStart ? 0f : Time.time + interval;
    }

    private void OnValidate()
    {
        if (_maxSpawnRadius < _minSpawnRadius)
        {
            float t = _minSpawnRadius;
            _minSpawnRadius = _maxSpawnRadius;
            _maxSpawnRadius = t;
        }
    }

    private void Update()
    {
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
            return;

        if (_pool == null)
            return;

        if (_player == null)
            _player = PlayerMovement.PlayerTransform;

        if (_player == null)
            return;

        if (Time.time < _nextSpawnTime)
            return;

        _nextSpawnTime = Time.time + EffectiveSpawnInterval();
        SpawnWave();
    }

    private float EffectiveSpawnInterval()
    {
        float scale = _difficultyManager != null ? _difficultyManager.GetSpawnIntervalScale() : 1f;
        float interval = Mathf.Max(0.05f, _spawnInterval * scale);
        if (ExitSpawnPressure.IsActive && ExitSpawnPressure.SpawnRateMultiplier > 1f)
            interval /= ExitSpawnPressure.SpawnRateMultiplier;
        return interval;
    }

    private void SpawnWave()
    {
        float diffCount = _difficultyManager != null ? _difficultyManager.GetSpawnCountMultiplier() : 1f;
        int count = Mathf.Max(1, Mathf.RoundToInt(_spawnPerWave * diffCount * OverheatSwarmBoost.SpawnWaveMultiplier));
        for (int i = 0; i < count; i++)
        {
            if (_pool.ActiveLeasedCount >= _maxActiveEnemies)
                break;

            GameObject enemy = _pool.TryGet();
            if (enemy == null)
                break;

            _difficultyManager?.ApplySpawnModifiers(enemy);

            float angle = Random.Range(0f, Mathf.PI * 2f);
            float radius = Random.Range(_minSpawnRadius, _maxSpawnRadius);
            Vector3 offset = new Vector3(
                Mathf.Cos(angle) * radius,
                _spawnHeightOffset,
                Mathf.Sin(angle) * radius);

            Vector3 ringPos = _player.position + offset;

            CharacterController cc = enemy.GetComponent<CharacterController>();
            if (cc == null)
            {
                enemy.transform.SetPositionAndRotation(ringPos, Quaternion.identity);
            }
            else if (!SpawnGroundUtility.TryResolveFootPosition(
                         new Vector3(ringPos.x, 0f, ringPos.z),
                         enemy.transform,
                         cc,
                         ringPos.y,
                         _maxAbsSpawnSurfaceDeltaY,
                         _groundRaycastMask,
                         _fallbackGroundRaycastMask,
                         _overlapSolidMask,
                         _raycastStartHeight,
                         _raycastMaxDistance,
                         _surfaceSeparation,
                         _maxProjectionIterations,
                         _resolveStepUp,
                         _resolveStepOut,
                         out Vector3 foot))
            {
                _pool.Release(enemy);
                continue;
            }
            else
            {
                enemy.transform.SetPositionAndRotation(foot, Quaternion.identity);
            }

            Vector3 toPlayer = _player.position - enemy.transform.position;
            toPlayer.y = 0f;
            if (toPlayer.sqrMagnitude > 0.0001f)
                enemy.transform.rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
        }
    }
}
