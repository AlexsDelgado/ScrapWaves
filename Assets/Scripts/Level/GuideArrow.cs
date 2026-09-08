using UnityEngine;

/// <summary>
/// Flecha guía en el mundo usando el mesh del prefab <c>Arrow_Guide</c>.
/// Se ancla sobre la cabeza del player y apunta en 3D hacia el objetivo (incluye diferencia de altura),
/// con un pulse simple de escala mientras está visible.
/// No desactiva el GameObject raíz (el <see cref="GuideArrowController"/> vive en el mismo GO).
/// </summary>
public class GuideArrow : MonoBehaviour
{
    [SerializeField, Tooltip("Raíz visual a escalar. Vacío = este transform.")]
    private Transform _visual;

    [SerializeField, Min(0f), Tooltip("Altura sobre el player donde flota la flecha.")]
    private float _heightAbovePlayer = 2.2f;

    [SerializeField, Tooltip("Orientación local del mesh en reposo (Arrow_Guide usa -90 en X).")]
    private Vector3 _meshRestEuler = new Vector3(-90f, 0f, 0f);

    [SerializeField, Min(0f)] private float _pulseSpeed = 4f;
    [SerializeField, Min(0f)] private float _pulseScaleAmount = 0.15f;

    private Transform _target;
    private Vector3 _visualBaseLocalScale = Vector3.one;
    private Renderer[] _renderers;
    private bool _visible;

    /// <summary>Objetivo actual, o null si la flecha está oculta.</summary>
    public Transform Target => _target;

    private void Awake()
    {
        if (_visual == null)
            _visual = transform;

        _visualBaseLocalScale = _visual.localScale;
        if (_visualBaseLocalScale.sqrMagnitude < 0.0001f)
            _visualBaseLocalScale = Vector3.one * 0.3f;

        _renderers = GetComponentsInChildren<Renderer>(true);
        Hide();
    }

    /// <summary>Muestra la flecha apuntando hacia <paramref name="target"/>.</summary>
    public void Show(Transform target)
    {
        if (target == null)
        {
            Hide();
            return;
        }

        _target = target;
        SetVisible(true);
        LateUpdate();
    }

    public void Hide()
    {
        _target = null;
        SetVisible(false);
    }

    private void LateUpdate()
    {
        if (_target == null || !_visible)
            return;

        Transform player = PlayerMovement.PlayerTransform;
        if (player == null)
            return;

        transform.position = player.position + Vector3.up * _heightAbovePlayer;

        Vector3 toTarget = _target.position - transform.position;
        if (toTarget.sqrMagnitude > 0.0001f)
        {
            Vector3 dir = toTarget.normalized;
            // LookRotation es inestable si dir ≈ up/down; elegir un up auxiliar.
            Vector3 up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.99f
                ? Vector3.forward
                : Vector3.up;
            transform.rotation = Quaternion.LookRotation(dir, up) * Quaternion.Euler(_meshRestEuler);
        }

        float pulse = 1f + Mathf.Sin(Time.unscaledTime * _pulseSpeed) * _pulseScaleAmount;
        _visual.localScale = _visualBaseLocalScale * pulse;
    }

    private void SetVisible(bool visible)
    {
        _visible = visible;
        if (_renderers == null)
            return;

        for (int i = 0; i < _renderers.Length; i++)
        {
            if (_renderers[i] != null)
                _renderers[i].enabled = visible;
        }
    }
}
