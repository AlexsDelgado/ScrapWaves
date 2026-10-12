using UnityEngine;

/// <summary>Lesson-only actor. Production prefabs are never modified at runtime.</summary>
[DefaultExecutionOrder(50)]
public sealed class TutorialEnemy : MonoBehaviour
{
    [SerializeField] private bool _stationary = true;
    [SerializeField] private float _damageMultiplier = .5f;
    [SerializeField] private MaterialType _primaryMaterial;
    [SerializeField] private TutorialPickup[] _drops;
    private TutorialDirector _owner;
    private EnemyHealth _health;
    private Vector3 _spawn;
    private bool _died;
    public TutorialPickup[] Drops => _drops;
    public MaterialType PrimaryMaterial => _primaryMaterial;
    public bool IsDead => _died;
    public void Begin(TutorialDirector owner)
    {
        _owner = owner; _spawn = transform.position; _died = false;
        gameObject.SetActive(true);
    }
    private void Awake() => _health = GetComponent<EnemyHealth>();
    private void OnEnable()
    {
        if (_health == null) _health = GetComponent<EnemyHealth>();
        _health.OnDied += Died;
        if (_stationary)
        {
            foreach (var ai in GetComponentsInChildren<EnemyBehaviorBase>()) ai.enabled = false;
            foreach (var follow in GetComponentsInChildren<EnemyFollow>()) follow.enabled = false;
            foreach (var follow in GetComponentsInChildren<SimpleFollow>()) follow.enabled = false;
            var body = GetComponent<Rigidbody>(); if (body != null) body.isKinematic = true;
        }
        else GetComponent<EnemyOutgoingDamageScale>()?.ConfigureForSpawn(_damageMultiplier);
    }
    private void OnDisable() { if (_health != null) _health.OnDied -= Died; }
    private void LateUpdate()
    {
        if (_owner == null || _died || GameplayPause.IsUiPaused) return;
        if (_stationary || transform.position.y < _spawn.y - 5f) transform.position = _spawn;
    }
    private void Died()
    {
        if (_died || _owner == null) return;
        _died = true;
        _owner.NotifyKilled(this, transform.position.y < _spawn.y - 2f ? _spawn : transform.position);
    }
}
