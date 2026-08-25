using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class WeaponDummySpawner : MonoBehaviour
{
    [SerializeField] private GameObject _dummyPrefab;
    [SerializeField] private Transform _player;
    [SerializeField] private WeaponTestMetrics _metrics;
    [SerializeField] private Transform _spawnParent;

    [Header("Spawn Defaults")]
    [Min(1)] public int EnemyHealth = 500;
    [Min(0f)] public float EnemyMovementSpeed = 2f;
    public WeaponEnemyKind EnemyType = WeaponEnemyKind.Normal;
    [Min(1)] public int EnemyCount = 12;
    [Min(0.25f)] public float EnemySpacing = 2f;
    public WeaponSandboxFormation CurrentFormation = WeaponSandboxFormation.Circle;
    public WeaponSandboxMovementPattern CurrentMovementPattern = WeaponSandboxMovementPattern.LeftRight;

    private readonly List<WeaponDummyEnemy> _spawned = new();

    private Vector3 _singleTargetCenter = new(0f, 0f, 14f);
    private Vector3 _groupCenter = new(18f, 0f, 14f);
    private Vector3 _movingCenter = new(-18f, 0f, 14f);
    private Vector3 _eliteBossCenter = new(0f, 0f, -16f);
    private Vector3 _knockbackLaneStart = new(18f, 0f, -18f);

    public IReadOnlyList<WeaponDummyEnemy> Spawned => _spawned;

    public void Bind(GameObject dummyPrefab, Transform player, WeaponTestMetrics metrics, Transform spawnParent = null)
    {
        _dummyPrefab = dummyPrefab;
        _player = player;
        _metrics = metrics;
        _spawnParent = spawnParent != null ? spawnParent : transform;
    }

    public void SetZoneCenters(Vector3 singleTarget, Vector3 group, Vector3 moving, Vector3 eliteBoss, Vector3 knockback)
    {
        _singleTargetCenter = singleTarget;
        _groupCenter = group;
        _movingCenter = moving;
        _eliteBossCenter = eliteBoss;
        _knockbackLaneStart = knockback;
    }

    public void SpawnSingleDummy()
    {
        ClearEnemies();
        SpawnDummy(_singleTargetCenter, EnemyType, WeaponSandboxMovementPattern.None, false);
    }

    /// <summary>Spawns one stationary target safely inside the CQB passive threshold.</summary>
    public void SpawnCloseRangeDummy()
    {
        ClearEnemies();
        SpawnDummy(GetPlayerRelativePosition(7f, 0f), EnemyType, WeaponSandboxMovementPattern.None, false);
    }

    /// <summary>Spawns one stationary target safely beyond the Sharpshooter passive threshold.</summary>
    public void SpawnLongRangeDummy()
    {
        ClearEnemies();
        SpawnDummy(GetPlayerRelativePosition(20f, 0f), EnemyType, WeaponSandboxMovementPattern.None, false);
    }

    /// <summary>Spawns close and long range targets side-by-side for passive damage comparisons.</summary>
    public void SpawnRangeComparisonDummies()
    {
        ClearEnemies();
        SpawnDummy(GetPlayerRelativePosition(7f, -2.5f), EnemyType, WeaponSandboxMovementPattern.None, false);
        SpawnDummy(GetPlayerRelativePosition(20f, 2.5f), EnemyType, WeaponSandboxMovementPattern.None, false);
    }

    public void SpawnEliteDummy()
    {
        ClearEnemies();
        SpawnDummy(_eliteBossCenter + Vector3.left * 3f, WeaponEnemyKind.Elite, WeaponSandboxMovementPattern.None, false);
    }

    public void SpawnBossDummy()
    {
        ClearEnemies();
        SpawnDummy(_eliteBossCenter + Vector3.right * 3f, WeaponEnemyKind.Boss, WeaponSandboxMovementPattern.None, false, EnemyHealth * 5);
    }

    public void SpawnEliteBossSet()
    {
        ClearEnemies();
        SpawnDummy(_eliteBossCenter + Vector3.left * 5f, WeaponEnemyKind.Normal, WeaponSandboxMovementPattern.None, false);
        SpawnDummy(_eliteBossCenter, WeaponEnemyKind.Elite, WeaponSandboxMovementPattern.None, false, Mathf.RoundToInt(EnemyHealth * 2.5f));
        SpawnDummy(_eliteBossCenter + Vector3.right * 5f, WeaponEnemyKind.Boss, WeaponSandboxMovementPattern.None, false, EnemyHealth * 6);
    }

    public void SpawnGroup()
    {
        ClearEnemies();
        SpawnFormation(_groupCenter, CurrentFormation, EnemyType, WeaponSandboxMovementPattern.None, false);
    }

    public void SpawnGroup(WeaponSandboxFormation formation)
    {
        CurrentFormation = formation;
        SpawnGroup();
    }

    public void SpawnMovingTargets()
    {
        ClearEnemies();
        SpawnFormation(_movingCenter, CurrentFormation, EnemyType, CurrentMovementPattern, true);
    }

    public void SpawnKnockbackLane()
    {
        ClearEnemies();
        int count = Mathf.Max(3, Mathf.Min(EnemyCount, 8));
        for (int i = 0; i < count; i++)
            SpawnDummy(_knockbackLaneStart + Vector3.forward * (i * EnemySpacing), EnemyType, WeaponSandboxMovementPattern.None, false);
    }

    public void RespawnCurrentTest()
    {
        for (int i = _spawned.Count - 1; i >= 0; i--)
        {
            if (_spawned[i] == null)
            {
                _spawned.RemoveAt(i);
                continue;
            }

            _spawned[i].Respawn();
        }
    }

    public void ClearEnemies()
    {
        for (int i = _spawned.Count - 1; i >= 0; i--)
        {
            if (_spawned[i] != null)
                Destroy(_spawned[i].gameObject);
        }

        _spawned.Clear();
    }

    private void SpawnFormation(
        Vector3 center,
        WeaponSandboxFormation formation,
        WeaponEnemyKind enemyKind,
        WeaponSandboxMovementPattern movementPattern,
        bool canMove)
    {
        int count = Mathf.Max(1, EnemyCount);
        float spacing = Mathf.Max(0.25f, EnemySpacing);

        for (int i = 0; i < count; i++)
        {
            Vector3 position = center + GetFormationOffset(i, count, formation, spacing);
            SpawnDummy(position, enemyKind, movementPattern, canMove);
        }
    }

    private Vector3 GetFormationOffset(int index, int count, WeaponSandboxFormation formation, float spacing)
    {
        switch (formation)
        {
            case WeaponSandboxFormation.Line:
                return Vector3.right * ((index - (count - 1) * 0.5f) * spacing);
            case WeaponSandboxFormation.PackedGroup:
                return GridOffset(index, Mathf.CeilToInt(Mathf.Sqrt(count)), spacing * 0.65f);
            case WeaponSandboxFormation.SpreadGroup:
                return GridOffset(index, Mathf.CeilToInt(Mathf.Sqrt(count)), spacing * 1.75f);
            case WeaponSandboxFormation.Random:
                Vector2 random = Random.insideUnitCircle * spacing * Mathf.Sqrt(count);
                return new Vector3(random.x, 0f, random.y);
            case WeaponSandboxFormation.Circle:
            default:
                float angle = count <= 1 ? 0f : index / (float)count * Mathf.PI * 2f;
                float radius = Mathf.Max(spacing, spacing * count / (Mathf.PI * 2f));
                return new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
        }
    }

    private static Vector3 GridOffset(int index, int columns, float spacing)
    {
        int row = index / Mathf.Max(1, columns);
        int col = index % Mathf.Max(1, columns);
        return new Vector3((col - (columns - 1) * 0.5f) * spacing, 0f, row * spacing);
    }

    private Vector3 GetPlayerRelativePosition(float forwardDistance, float lateralOffset)
    {
        Vector3 origin = _player != null ? _player.position : Vector3.zero;
        Vector3 forward = _player != null ? _player.forward : Vector3.forward;
        forward.y = 0f;
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;
        forward.Normalize();

        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 position = origin + forward * Mathf.Max(0f, forwardDistance) + right * lateralOffset;
        position.y = 0f;
        return position;
    }

    private WeaponDummyEnemy SpawnDummy(
        Vector3 position,
        WeaponEnemyKind enemyKind,
        WeaponSandboxMovementPattern movementPattern,
        bool canMove,
        int healthOverride = -1)
    {
        GameObject go;
        if (_dummyPrefab != null)
            go = Instantiate(_dummyPrefab, position, Quaternion.identity, _spawnParent != null ? _spawnParent : transform);
        else
            go = CreateRuntimeDummy(position);

        WeaponDummyEnemy dummy = go.GetComponent<WeaponDummyEnemy>();
        if (dummy == null)
            dummy = go.AddComponent<WeaponDummyEnemy>();

        int health = healthOverride > 0 ? healthOverride : EnemyHealth;
        dummy.Configure(health, EnemyMovementSpeed, enemyKind, canMove, movementPattern, _player, _metrics);
        _spawned.Add(dummy);
        return dummy;
    }

    private GameObject CreateRuntimeDummy(Vector3 position)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "Weapon Dummy";
        go.transform.position = position + Vector3.up;
        go.transform.SetParent(transform);
        Rigidbody rb = go.AddComponent<Rigidbody>();
        rb.isKinematic = true;
        rb.useGravity = false;
        go.AddComponent<EnemyRegistryMember>();
        go.AddComponent<WeaponDummyEnemy>();
        return go;
    }
}
