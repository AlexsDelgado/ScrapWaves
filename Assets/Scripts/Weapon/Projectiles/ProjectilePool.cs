using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-35)]
public class ProjectilePool : MonoBehaviour
{
    [SerializeField, Tooltip("Prefab raíz con Projectile + Rigidbody + SphereCollider (trigger).")]
    private GameObject _projectilePrefab;

    [SerializeField, Tooltip("Padre de los clones; si es null, se crea un contenedor en runtime en la escena (evita el aviso de parent persistente).")]
    private Transform _container;

    [SerializeField, Tooltip("Si está activo, registra en consola qué padre usa el pool al iniciar.")]
    private bool _debugLogParent;

    [SerializeField, Min(1)] private int _initialPoolSize = 64;

    [SerializeField] private bool _allowPoolGrowth = true;

    [SerializeField, Min(1)] private int _maxPoolSize = 1024;

    [SerializeField, Min(0.05f), Tooltip("Segundos de vida al disparar desde el pool (sobrescribe el valor usado en runtime).")]
    private float _projectileLifetime = 3f;

    private readonly Queue<GameObject> _inactive = new Queue<GameObject>();
    private readonly List<GameObject> _instances = new List<GameObject>();

    private int _leasedCount;

    private Transform _runtimeParent;
    private bool _ownsRuntimeParent;

    public float ProjectileLifetime => _projectileLifetime;
    public int ActiveLeasedCount => _leasedCount;
    public int TotalPooledInstances => _instances.Count;

    /// <summary>
    /// Nunca usar <see cref="transform"/> como padre: si el pool está inactivo, Awake no corre y
    /// además una referencia al componente desde un prefab puede exponer un transform de asset.
    /// </summary>
    private Transform GetPoolParent()
    {
        if (_container != null)
            return _container;
        EnsureRuntimeParentExists();
        return _runtimeParent;
    }

    private void EnsureRuntimeParentExists()
    {
        if (_container != null || _runtimeParent != null)
            return;

        var holder = new GameObject($"[PooledProjectiles] {gameObject.name}");
        _runtimeParent = holder.transform;
        _ownsRuntimeParent = true;

        Scene targetScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();
        SceneManager.MoveGameObjectToScene(holder, targetScene);

        if (_debugLogParent)
        {
            Debug.Log(
                $"ProjectilePool: contenedor runtime '{holder.name}' → escena '{targetScene.path}' " +
                $"(pool '{gameObject.name}', scene válida={gameObject.scene.IsValid()}, instanceID={GetInstanceID()}).",
                this);
        }
    }

    private void Awake()
    {
        if (_container == null)
            EnsureRuntimeParentExists();

        if (_projectilePrefab == null)
        {
            Debug.LogError("ProjectilePool: asigna prefab de proyectil.", this);
            return;
        }

        if (_container != null && _debugLogParent)
        {
            Debug.Log(
                $"ProjectilePool: usando _container '{_container.name}' en escena '{_container.gameObject.scene.path}'.",
                this);
        }

        if (_maxPoolSize < _initialPoolSize)
            _maxPoolSize = _initialPoolSize;

        for (int i = 0; i < _initialPoolSize; i++)
            CreateInstance(enqueueInactive: true);
    }

    private void OnDestroy()
    {
        if (_ownsRuntimeParent && _runtimeParent != null)
            Destroy(_runtimeParent.gameObject);
    }

    private void OnValidate()
    {
        if (_maxPoolSize < _initialPoolSize)
            _maxPoolSize = _initialPoolSize;
    }

    public GameObject TryGet()
    {
        if (_projectilePrefab == null)
            return null;

        GameObject instance;

        if (_inactive.Count > 0)
            instance = _inactive.Dequeue();
        else if (_allowPoolGrowth && _instances.Count < _maxPoolSize)
            instance = CreateInstance(enqueueInactive: false);
        else
            return null;

        ActivateInstance(instance);
        return instance;
    }

    public void Release(GameObject instance)
    {
        if (instance == null || !instance.activeSelf)
            return;

        ProjectilePoolMember member = instance.GetComponent<ProjectilePoolMember>();
        if (member == null || !member.BelongsTo(this))
            return;

        instance.SetActive(false);
        instance.transform.SetParent(GetPoolParent(), false);
        _leasedCount--;
        _inactive.Enqueue(instance);
    }

    public bool TrySpawnProjectile(Vector3 position, Quaternion rotation, Vector3 fireDirection)
    {
        GameObject go = TryGet();
        if (go == null)
            return false;

        go.transform.SetPositionAndRotation(position, rotation);

        Projectile projectile = go.GetComponent<Projectile>();
        if (projectile == null)
        {
            Release(go);
            return false;
        }

        projectile.ConfigurePooled(_projectileLifetime);
        projectile.Launch(fireDirection);
        return true;
    }

    public bool TrySpawnProjectile(Vector3 position, Quaternion rotation, Vector3 fireDirection, int damage)
    {
        GameObject go = TryGet();
        if (go == null)
            return false;

        go.transform.SetPositionAndRotation(position, rotation);

        Projectile projectile = go.GetComponent<Projectile>();
        if (projectile == null)
        {
            Release(go);
            return false;
        }

        projectile.ConfigurePooled(_projectileLifetime, damage);
        projectile.Launch(fireDirection);
        return true;
    }

    // Spawns projectile configured with explosion radius and damage falloff.
    public bool TrySpawnExplosiveProjectile(Vector3 position, Quaternion rotation, Vector3 fireDirection, int damage, float explosionRadius, float falloff)
    {
        return TrySpawnExplosiveProjectile(position, rotation, fireDirection, damage, explosionRadius, falloff, 1f, 0f, false);
    }

    // Spawns projectile configured with explosion, speed, and optional range detonation.
    public bool TrySpawnExplosiveProjectile(
        Vector3 position,
        Quaternion rotation,
        Vector3 fireDirection,
        int damage,
        float explosionRadius,
        float falloff,
        float speedMultiplier,
        float maxTravelDistance,
        bool explodeOnMaxTravel)
    {
        GameObject go = TryGet();
        if (go == null)
            return false;

        go.transform.SetPositionAndRotation(position, rotation);

        Projectile projectile = go.GetComponent<Projectile>();
        if (projectile == null)
        {
            Release(go);
            return false;
        }

        projectile.ConfigurePooled(_projectileLifetime, damage);
        projectile.Launch(fireDirection);
        projectile.ConfigureExplosion(explosionRadius, falloff);
        projectile.ConfigureSpeedMultiplier(speedMultiplier);
        projectile.ConfigureMaxTravel(maxTravelDistance, explodeOnMaxTravel);
        return true;
    }


    private GameObject CreateInstance(bool enqueueInactive)
    {
        EnsureRuntimeParentExists();

        GameObject instance = Instantiate(_projectilePrefab);
        instance.name = $"{_projectilePrefab.name} (pool)";

        Scene targetScene = gameObject.scene.IsValid() ? gameObject.scene : SceneManager.GetActiveScene();
        SceneManager.MoveGameObjectToScene(instance, targetScene);

        instance.transform.SetParent(GetPoolParent(), false);

        ProjectilePoolMember member = instance.GetComponent<ProjectilePoolMember>();
        if (member == null)
            member = instance.AddComponent<ProjectilePoolMember>();
        member.Bind(this);

        instance.SetActive(false);
        _instances.Add(instance);

        if (enqueueInactive)
            _inactive.Enqueue(instance);

        return instance;
    }

    private void ActivateInstance(GameObject instance)
    {
        instance.SetActive(true);
        _leasedCount++;
    }
}
