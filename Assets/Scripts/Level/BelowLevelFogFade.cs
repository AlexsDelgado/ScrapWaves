using UnityEngine;

/// <summary>
/// Niebla de piso: más opaca al mirar desde arriba; al cruzar el plano (solo jugador)
/// hace fade in/out suave en lugar de aparecer/desaparecer de golpe.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshRenderer))]
public class BelowLevelFogFade : MonoBehaviour
{
    private static readonly int VisibilityId = Shader.PropertyToID("_Visibility");

    [SerializeField, Min(0.05f), Tooltip("Segundos para fade completo al cruzar el plano.")]
    private float _fadeDuration = 0.75f;

    [SerializeField, Tooltip("Holgura en Y: por encima de planoY - bias se considera 'arriba' (fog visible).")]
    private float _aboveBias = 0.35f;

    [SerializeField, Tooltip("Si true, crea un BoxCollider trigger (solo responde al jugador).")]
    private bool _usePlayerTrigger = true;

    [SerializeField, Min(0.1f), Tooltip("Grosor del volumen trigger alrededor del plano.")]
    private float _triggerThickness = 4f;

    private MeshRenderer _renderer;
    private MaterialPropertyBlock _mpb;
    private float _visibility = 1f;
    private float _targetVisibility = 1f;

    private void Awake()
    {
        _renderer = GetComponent<MeshRenderer>();
        _mpb = new MaterialPropertyBlock();
        EnsureTrigger();
        ApplyVisibility(true);
    }

    private void OnEnable()
    {
        ResolveTargetFromPlayerHeight();
        ApplyVisibility(true);
    }

    private void Update()
    {
        ResolveTargetFromPlayerHeight();

        if (_fadeDuration <= 0.0001f)
            _visibility = _targetVisibility;
        else
            _visibility = Mathf.MoveTowards(_visibility, _targetVisibility, Time.deltaTime / _fadeDuration);

        ApplyVisibility(false);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!_usePlayerTrigger || !IsPlayerCollider(other))
            return;
        // Al cruzar el volumen, fuerza una re-evaluación inmediata del target (el Update suaviza).
        ResolveTargetFromPlayerHeight();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!_usePlayerTrigger || !IsPlayerCollider(other))
            return;
        ResolveTargetFromPlayerHeight();
    }

    private void ResolveTargetFromPlayerHeight()
    {
        Transform player = PlayerMovement.PlayerTransform;
        if (player == null)
        {
            _targetVisibility = 1f;
            return;
        }

        // Visible cuando el jugador está por encima del plano (mirando el piso inferior).
        // Al caer debajo, fade out; al volver a subir, fade in.
        float planeY = transform.position.y;
        bool above = player.position.y >= planeY - _aboveBias;
        _targetVisibility = above ? 1f : 0f;
    }

    private void ApplyVisibility(bool force)
    {
        if (_renderer == null)
            return;

        if (!force && Mathf.Approximately(_visibility, _targetVisibility) && _visibility <= 0.001f)
        {
            if (_renderer.enabled)
                _renderer.enabled = false;
            return;
        }

        if (!_renderer.enabled && _visibility > 0.001f)
            _renderer.enabled = true;

        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetFloat(VisibilityId, _visibility);
        _renderer.SetPropertyBlock(_mpb);
    }

    private void EnsureTrigger()
    {
        if (!_usePlayerTrigger)
            return;

        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null)
            box = gameObject.AddComponent<BoxCollider>();

        box.isTrigger = true;
        // Quad unitario: thickness en eje local Z (tras rotación 90°, es el eje vertical del mundo).
        float thicknessLocal = _triggerThickness / Mathf.Max(0.001f, transform.lossyScale.z);
        box.size = new Vector3(1f, 1f, Mathf.Max(0.05f, thicknessLocal));
        box.center = Vector3.zero;
    }

    private static bool IsPlayerCollider(Collider other)
    {
        if (other == null)
            return false;

        Transform player = PlayerMovement.PlayerTransform;
        if (player == null)
            return false;

        return other.transform == player
            || other.transform.IsChildOf(player)
            || other.GetComponentInParent<PlayerMovement>() != null;
    }
}
