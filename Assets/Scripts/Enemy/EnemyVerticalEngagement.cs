using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Engagement vertical asimétrico: si el enemigo queda claramente DEBAJO del jugador,
/// entra en sleep (sin chase / daño planar / auto-target) y tras un tiempo vuelve al pool.
/// Si está arriba o en el mismo piso, permanece engaged (puede caer hacia el jugador).
/// </summary>
[DisallowMultipleComponent]
public class EnemyVerticalEngagement : MonoBehaviour
{
    private static readonly HashSet<int> s_DisengagedIds = new(256);

    [SerializeField, Min(0.5f), Tooltip("Si player.y - enemy.y supera esto, el enemigo está demasiado abajo. Pisos ~50u → usar ~40.")]
    private float _belowThreshold = 40f;

    [SerializeField, Min(0.5f), Tooltip("Segundos en sleep debajo antes de despawnear al pool.")]
    private float _despawnAfterSeconds = 10f;

    [SerializeField, Tooltip("Si true, no aplica (p. ej. Stalker/GigaWorm).")]
    private bool _exempt;

    private float _sleepElapsed;
    private bool _disengaged;
    private int _instanceId;

    public bool IsVerticallyDisengaged => _disengaged;

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
    }

    private void OnEnable()
    {
        _instanceId = transform.GetInstanceID();
        ResetEngagement();
    }

    private void OnDisable()
    {
        ClearDisengaged();
    }

    private void OnDestroy()
    {
        ClearDisengaged();
    }

    public void ResetEngagement()
    {
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
            _sleepElapsed += Time.deltaTime;
            if (_sleepElapsed >= _despawnAfterSeconds)
                DespawnSelf();
            return;
        }

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

    private void DespawnSelf()
    {
        ClearDisengaged();

        if (TryGetComponent(out SwarmPooledEnemy pooled) && pooled.IsBound)
        {
            pooled.Despawn();
            return;
        }

        if (EnemyPoolRegistry.UseEnemyPool && EnemyPoolRegistry.Instance != null)
        {
            EnemyPoolRegistry.Instance.Release(gameObject);
            return;
        }

        EnemyPoolProfiler.RegisterDestroy();
        Destroy(gameObject);
    }
}
