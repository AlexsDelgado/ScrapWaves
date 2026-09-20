using UnityEngine;

[RequireComponent(typeof(PlayerStats))]
public class PlayerAutoAttack : MonoBehaviour
{
    [SerializeField, Tooltip("Origen visual del disparo (hijo del jugador, delante del modelo).")]
    private Transform _firePoint;

    [SerializeField, Tooltip("Pool de proyectiles (sustituye Instantiate).")]
    private ProjectilePool _projectilePool;

    [SerializeField, Min(0.1f), Tooltip("Radio en XZ desde la posición del jugador para candidatos a objetivo.")]
    private float _detectionRange = 12f;

    [SerializeField, Min(0f), Tooltip("Si la diferencia en Y es mayor que esto, se ignora el objetivo y se busca otro a altura similar.")]
    private float _maxAimVerticalDelta = 2.5f;

    private float _fireCooldown;
    private PlayerStats _stats;
    private WeaponManager _weaponManager;
    private PlayerAnimationDriver _animationDriver;

    private void Awake()
    {
        _stats = GetComponent<PlayerStats>();
        _weaponManager = GetComponent<WeaponManager>();
        _animationDriver = GetComponent<PlayerAnimationDriver>();
    }

    private void Update()
    {
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
            return;

        if (_weaponManager != null && _weaponManager.GetEquippedWeapons().Count > 0)
            return;

        float interval = _stats.GetFireInterval();

        _fireCooldown -= Time.deltaTime;
        if (_fireCooldown > 0f)
            return;

        _fireCooldown = interval;

        if (_projectilePool == null || _firePoint == null)
            return;

        if (!EnemyRegistry.TryGetClosestOnPlaneWithinVerticalDelta(transform.position, _detectionRange, _maxAimVerticalDelta, out Transform target))
            return;

        if (_animationDriver != null && _animationDriver.isActiveAndEnabled)
            _animationDriver.EvaluatePoseForWeapons(Time.deltaTime, target.position);

        // Dirección 3D hacia el enemigo (incluye altura); el registro sigue eligiendo el más cercano en XZ.
        Vector3 aim = target.position - _firePoint.position;
        if (aim.sqrMagnitude < 0.0001f)
            return;

        Vector3 dir = aim.normalized;
        Quaternion rot = Quaternion.FromToRotation(Vector3.forward, dir);

        if (_projectilePool.TrySpawnProjectile(_firePoint.position, rot, dir, _stats.GetDamage()))
            AudioManager.TryPlayShoot();
    }
}
