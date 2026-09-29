using UnityEngine;

/// <summary>
/// Tecla 3D ("E") que flota sobre la crafting station. Crece al entrar en el radio de interacción y se achica
/// al alejarse, al abrir la estación o con el juego en pausa. Siempre mira a la cámara y se mece un poco.
/// Modelo: ArtSource/UI/V2B/scripts/interact_key.py → Assets/Art/Props/InteractKey/.
/// </summary>
[DisallowMultipleComponent]
public class InteractPrompt : MonoBehaviour
{
    [SerializeField] private CraftingStation _station;
    [Tooltip("Hijo con el modelo; se escala entre 0 y su escala original.")]
    [SerializeField] private Transform _visual;
    [Tooltip("Multiplica el radio de interacción de la estación para mostrar el prompt un poco antes.")]
    [SerializeField, Min(0.1f)] private float _radiusMultiplier = 1.15f;
    [SerializeField, Min(0.1f)] private float _showSpeed = 10f;
    [SerializeField, Min(0f)] private float _bobHeight = 0.08f;
    [SerializeField, Min(0f)] private float _bobSpeed = 2.2f;
    [Tooltip("Pulso de tamaño para llamar la atención (0 = sin pulso).")]
    [SerializeField, Range(0f, 0.3f)] private float _pulse = 0.06f;

    private Vector3 _visualScale = Vector3.one;
    private Vector3 _visualPosition;
    private float _shown;
    private Camera _camera;

    private void Awake()
    {
        if (_station == null)
            _station = GetComponentInParent<CraftingStation>();
        if (_visual == null && transform.childCount > 0)
            _visual = transform.GetChild(0);
        if (_visual != null)
        {
            _visualScale = _visual.localScale;
            _visualPosition = _visual.localPosition;
            _visual.localScale = Vector3.zero;
            _visual.gameObject.SetActive(false);
        }
    }

    private void LateUpdate()
    {
        if (_visual == null)
            return;

        float target = ShouldShow() ? 1f : 0f;
        _shown = Mathf.MoveTowards(_shown, target, Time.unscaledDeltaTime * _showSpeed);
        bool visible = _shown > 0.001f;
        if (_visual.gameObject.activeSelf != visible)
            _visual.gameObject.SetActive(visible);
        if (!visible)
            return;

        float t = Time.unscaledTime;
        float eased = Mathf.SmoothStep(0f, 1f, _shown);
        _visual.localScale = _visualScale * (eased * (1f + _pulse * Mathf.Sin(t * _bobSpeed * 2f)));
        _visual.localPosition = _visualPosition + Vector3.up * (_bobHeight * Mathf.Sin(t * _bobSpeed));
        FaceCamera();
    }

    private bool ShouldShow()
    {
        if (_station == null || _station.IsOpen || GameplayPause.IsUiPaused)
            return false;
        if (GameManager.Instance != null && !GameManager.Instance.IsPlaying)
            return false;
        Transform player = PlayerMovement.PlayerTransform;
        return player != null
            && Vector3.Distance(player.position, _station.InteractionPosition) <= _station.InteractionRadius * _radiusMultiplier;
    }

    // El frente del modelo mira a -Z local: con +Z alejándose de la cámara, la tecla queda de cara al jugador.
    private void FaceCamera()
    {
        if (_camera == null || !_camera.isActiveAndEnabled)
            _camera = Camera.main;
        if (_camera == null)
            return;
        Vector3 away = transform.position - _camera.transform.position;
        if (away.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(away, Vector3.up);
    }
}
