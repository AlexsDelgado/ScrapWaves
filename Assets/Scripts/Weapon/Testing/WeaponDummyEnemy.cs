using System.Collections.Generic;
using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(EnemyRegistryMember))]
public sealed class WeaponDummyEnemy : MonoBehaviour, IDamageable, IWeaponEnemyMetadata
{
    [SerializeField, Min(1)] private int _maxHealth = 500;
    [SerializeField, Range(0f, 0.95f)] private float _damageResistance;
    [SerializeField] private WeaponEnemyKind _enemyKind = WeaponEnemyKind.Normal;
    [SerializeField] private bool _canMove;
    [SerializeField] private WeaponSandboxMovementPattern _movementPattern = WeaponSandboxMovementPattern.None;
    [SerializeField, Min(0f)] private float _movementSpeed = 2f;
    [SerializeField, Min(0f)] private float _weakPointDamageMultiplier = 1.5f;

    private readonly List<StatusEntry> _statuses = new();
    private int _currentHealth;
    private float _lastDamageReceived;
    private float _spawnTime;
    private float _wanderTimer;
    private Vector3 _spawnPosition;
    private Vector3 _movementCenter;
    private Vector3 _wanderDirection;
    private Transform _player;
    private WeaponTestMetrics _metrics;
    private EnemyKnockbackReceiver _knockbackReceiver;
    private TextMeshPro _label;

    public int CurrentHealth => _currentHealth;
    public int MaxHealth => _maxHealth;
    public float LastDamageReceived => _lastDamageReceived;
    public WeaponSandboxMovementPattern MovementPattern => _movementPattern;
    public bool CanMove => _canMove;
    public WeaponEnemyKind WeaponEnemyKind => _enemyKind;
    public bool CountsAsEliteOrBoss => _enemyKind == WeaponEnemyKind.Elite || _enemyKind == WeaponEnemyKind.Boss;

    public event System.Action<WeaponDummyEnemy> Died;

    private void Awake()
    {
        _knockbackReceiver = GetComponent<EnemyKnockbackReceiver>();
        if (_knockbackReceiver == null)
            _knockbackReceiver = gameObject.AddComponent<EnemyKnockbackReceiver>();

        _currentHealth = _maxHealth;
        _spawnPosition = transform.position;
        _movementCenter = _spawnPosition;
        EnsureLabel();
        EnsureWeakPoint();
    }

    private void OnEnable()
    {
        _spawnTime = Time.time;
        if (_currentHealth <= 0)
            _currentHealth = _maxHealth;
    }

    private void Update()
    {
        TickStatuses(Time.deltaTime);
        RefreshBurnStatusDisplay();
        RefreshLabel();
    }

    private void FixedUpdate()
    {
        Vector3 knockback = _knockbackReceiver != null ? _knockbackReceiver.ConsumeDisplacement(Time.fixedDeltaTime) : Vector3.zero;
        if (knockback.sqrMagnitude > 0.000001f)
        {
            transform.position += knockback;
            _metrics?.RecordKnockbackDistance(knockback.magnitude);
        }

        if (_canMove)
            ApplyMovement(Time.fixedDeltaTime);
    }

    public void Configure(
        int health,
        float movementSpeed,
        WeaponEnemyKind enemyKind,
        bool canMove,
        WeaponSandboxMovementPattern movementPattern,
        Transform player,
        WeaponTestMetrics metrics)
    {
        _maxHealth = Mathf.Max(1, health);
        _currentHealth = _maxHealth;
        _movementSpeed = Mathf.Max(0f, movementSpeed);
        _enemyKind = enemyKind;
        _canMove = canMove && movementPattern != WeaponSandboxMovementPattern.None;
        _movementPattern = movementPattern;
        _player = player;
        _metrics = metrics;
        _spawnPosition = transform.position;
        _movementCenter = transform.position;
        _lastDamageReceived = 0f;
        _spawnTime = Time.time;
        _statuses.Clear();
        name = $"{_enemyKind} Weapon Dummy";
        gameObject.SetActive(true);
        RefreshLabel();
    }

    public void Respawn()
    {
        _currentHealth = _maxHealth;
        _lastDamageReceived = 0f;
        transform.position = _spawnPosition;
        _statuses.Clear();
        _spawnTime = Time.time;
        gameObject.SetActive(true);
        RefreshLabel();
    }

    public bool ApplyDamage(int amount)
    {
        return ApplyDamageInternal(amount, 1f);
    }

    public bool ApplyWeakPointDamage(int amount)
    {
        return ApplyDamageInternal(amount, _weakPointDamageMultiplier);
    }

    public void ApplyStatus(string statusName, float duration)
    {
        if (string.IsNullOrWhiteSpace(statusName) || duration <= 0f)
            return;

        for (int i = 0; i < _statuses.Count; i++)
        {
            if (_statuses[i].Name != statusName)
                continue;

            _statuses[i] = new StatusEntry(statusName, Mathf.Max(_statuses[i].RemainingDuration, duration));
            return;
        }

        _statuses.Add(new StatusEntry(statusName, duration));
        _metrics?.RecordStatusEffectApplied();
    }

    private bool ApplyDamageInternal(int amount, float weakPointMultiplier)
    {
        if (amount <= 0 || _currentHealth <= 0)
            return false;

        int finalDamage = Mathf.Max(1, Mathf.RoundToInt(amount * Mathf.Max(0f, weakPointMultiplier) * (1f - _damageResistance)));
        _currentHealth = Mathf.Max(0, _currentHealth - finalDamage);
        _lastDamageReceived = finalDamage;
        _metrics?.RecordDamage(finalDamage);

        if (_currentHealth > 0)
            return true;

        _metrics?.RecordKill(Time.time - _spawnTime);
        Died?.Invoke(this);
        gameObject.SetActive(false);
        return true;
    }

    private void ApplyMovement(float deltaTime)
    {
        Vector3 direction = Vector3.zero;
        float elapsed = Time.time - _spawnTime;

        switch (_movementPattern)
        {
            case WeaponSandboxMovementPattern.LeftRight:
                direction = Vector3.right * Mathf.Sign(Mathf.Sin(elapsed * 1.4f));
                break;
            case WeaponSandboxMovementPattern.TowardPlayer:
                direction = DirectionToPlayer();
                break;
            case WeaponSandboxMovementPattern.AwayFromPlayer:
                direction = -DirectionToPlayer();
                break;
            case WeaponSandboxMovementPattern.CircleAroundPlayer:
                Vector3 toCenter = transform.position - (_player != null ? _player.position : _movementCenter);
                toCenter.y = 0f;
                direction = toCenter.sqrMagnitude > 0.0001f ? Vector3.Cross(Vector3.up, toCenter.normalized) : Vector3.right;
                break;
            case WeaponSandboxMovementPattern.RandomWander:
                _wanderTimer -= deltaTime;
                if (_wanderTimer <= 0f)
                {
                    Vector2 random = Random.insideUnitCircle.normalized;
                    _wanderDirection = new Vector3(random.x, 0f, random.y);
                    _wanderTimer = Random.Range(0.8f, 2.2f);
                }
                direction = _wanderDirection;
                break;
        }

        direction.y = 0f;
        if (direction.sqrMagnitude > 0.0001f)
            transform.position += direction.normalized * (_movementSpeed * GetStatusSpeedMultiplier() * deltaTime);
    }

    private Vector3 DirectionToPlayer()
    {
        Vector3 target = _player != null ? _player.position : _movementCenter;
        Vector3 direction = target - transform.position;
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.zero;
    }

    private void TickStatuses(float deltaTime)
    {
        for (int i = _statuses.Count - 1; i >= 0; i--)
        {
            StatusEntry status = _statuses[i];
            status.RemainingDuration -= deltaTime;
            if (status.RemainingDuration <= 0f)
                _statuses.RemoveAt(i);
            else
                _statuses[i] = status;
        }
    }

    private void RefreshBurnStatusDisplay()
    {
        if (GetComponent<FlamethrowerBurnStatus>() != null)
            ApplyStatus("Burn", 0.25f);
    }

    private void EnsureLabel()
    {
        Transform existing = transform.Find("DummyLabel");
        if (existing != null)
        {
            _label = existing.GetComponent<TextMeshPro>();
            return;
        }

        GameObject labelGo = new GameObject("DummyLabel");
        labelGo.transform.SetParent(transform, false);
        labelGo.transform.localPosition = new Vector3(0f, 2.2f, 0f);
        _label = labelGo.AddComponent<TextMeshPro>();
        if (TMP_Settings.defaultFontAsset != null)
            _label.font = TMP_Settings.defaultFontAsset;
        _label.fontSize = 2.2f;
        _label.alignment = TextAlignmentOptions.Center;
        _label.color = Color.white;
        _label.textWrappingMode = TextWrappingModes.NoWrap;
    }

    private void EnsureWeakPoint()
    {
        if (transform.Find("WeakPoint") != null)
            return;

        GameObject weakPoint = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        weakPoint.name = "WeakPoint";
        weakPoint.transform.SetParent(transform, false);
        weakPoint.transform.localPosition = new Vector3(0f, 0.75f, -0.52f);
        weakPoint.transform.localScale = Vector3.one * 0.25f;
        Collider collider = weakPoint.GetComponent<Collider>();
        collider.isTrigger = true;
        weakPoint.AddComponent<WeaponDummyWeakPoint>().Bind(this);
    }

    private void RefreshLabel()
    {
        if (_label == null)
            return;

        if (Camera.main != null)
            _label.transform.rotation = Quaternion.LookRotation(_label.transform.position - Camera.main.transform.position);

        string statusText = _statuses.Count == 0 ? "None" : BuildStatusText();
        _label.text = $"{_enemyKind}\nHP {_currentHealth}/{_maxHealth}\nLast {_lastDamageReceived:0}\nStatus {statusText}";
    }

    private string BuildStatusText()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder(64);
        for (int i = 0; i < _statuses.Count; i++)
        {
            if (i > 0)
                sb.Append(", ");
            sb.Append(_statuses[i].Name);
        }

        return sb.ToString();
    }

    private float GetStatusSpeedMultiplier()
    {
        float multiplier = 1f;
        for (int i = 0; i < _statuses.Count; i++)
        {
            if (_statuses[i].Name == "Freeze")
                return 0f;
            if (_statuses[i].Name == "Slow" || _statuses[i].Name == "Liquid Nitrogen")
                multiplier = Mathf.Min(multiplier, 0.45f);
        }

        return multiplier;
    }

    private struct StatusEntry
    {
        public string Name;
        public float RemainingDuration;

        public StatusEntry(string name, float remainingDuration)
        {
            Name = name;
            RemainingDuration = remainingDuration;
        }
    }
}

public sealed class WeaponDummyWeakPoint : MonoBehaviour, IDamageable
{
    private WeaponDummyEnemy _dummy;

    public void Bind(WeaponDummyEnemy dummy)
    {
        _dummy = dummy;
    }

    public bool ApplyDamage(int amount)
    {
        if (_dummy == null)
            _dummy = GetComponentInParent<WeaponDummyEnemy>();

        return _dummy != null && _dummy.ApplyWeakPointDamage(amount);
    }
}
