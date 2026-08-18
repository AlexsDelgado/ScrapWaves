using UnityEngine;

/// <summary>
/// Flecha guía flotante en el mundo (placeholder geométrico: un chevron armado con 2 primitivas,
/// sin arte final). A diferencia de <see cref="OffscreenObjectiveIndicators"/> (que solo aparece
/// cuando el objetivo está fuera de pantalla, pegada al borde), esta flecha se ancla siempre en un
/// punto fijo del viewport frente a la cámara — queda visible en todo momento mientras está activa —
/// y rota en el plano de cámara para apuntar hacia la proyección en pantalla del objetivo, con la
/// misma matemática (WorldToScreenPoint + Atan2) que el indicador offscreen.
/// </summary>
public class GuideArrow : MonoBehaviour
{
    [SerializeField] private Camera _camera;

    [SerializeField, Min(0.1f), Tooltip("Distancia desde la cámara a la que flota la flecha.")]
    private float _distanceFromCamera = 4f;

    [SerializeField, Tooltip("Punto del viewport (0-1) donde se ancla la flecha (x=0.5 centro, y=0.68 arriba-centro).")]
    private Vector2 _viewportAnchor = new Vector2(0.5f, 0.68f);

    [SerializeField] private Color _color = new Color(0.15f, 0.55f, 1f, 1f);

    [SerializeField, Min(0f)] private float _pulseSpeed = 4f;
    [SerializeField, Min(0f)] private float _pulseScaleAmount = 0.15f;
    [SerializeField, Min(0f)] private float _baseScale = 0.6f;

    private Transform _target;
    private Transform _visual;
    private Material _material;

    /// <summary>Objetivo actual, o null si la flecha está oculta.</summary>
    public Transform Target => _target;

    private void Awake()
    {
        if (_camera == null)
            _camera = Camera.main;

        BuildVisual();
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
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        _target = null;
        gameObject.SetActive(false);
    }

    private void LateUpdate()
    {
        if (_target == null)
            return;

        if (_camera == null)
            _camera = Camera.main;
        if (_camera == null)
            return;

        Vector3 anchorWorld = _camera.ViewportToWorldPoint(new Vector3(_viewportAnchor.x, _viewportAnchor.y, _distanceFromCamera));
        transform.position = anchorWorld;

        Vector3 toCamera = _camera.transform.position - transform.position;
        if (toCamera.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(-toCamera.normalized, _camera.transform.up);

        float angle = ComputeScreenAngleToTarget();
        if (_visual != null)
        {
            _visual.localRotation = Quaternion.Euler(0f, 0f, angle);
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * _pulseSpeed) * _pulseScaleAmount;
            _visual.localScale = Vector3.one * _baseScale * pulse;
        }
    }

    /// <summary>
    /// Ángulo (grados, convención Z de Unity) para que el chevron —que en reposo apunta "arriba"—
    /// quede mirando hacia la proyección en pantalla del objetivo. Si el objetivo está detrás de la
    /// cámara, compensa el espejado de <see cref="Camera.WorldToScreenPoint"/> en vez de ocultarse
    /// (esta flecha debe ser visible siempre mientras esté activa, a diferencia del indicador offscreen).
    /// </summary>
    private float ComputeScreenAngleToTarget()
    {
        Vector3 targetWorld = _target.position + Vector3.up;
        Vector3 screen3 = _camera.WorldToScreenPoint(targetWorld);
        Vector2 screen = new Vector2(screen3.x, screen3.y);

        if (screen3.z < 0f)
        {
            screen.x = Screen.width - screen.x;
            screen.y = Screen.height - screen.y;
        }

        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        Vector2 dir = screen - screenCenter;
        if (dir.sqrMagnitude < 0.0001f)
            return 0f;

        dir.Normalize();
        return Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
    }

    private void BuildVisual()
    {
        var visualGo = new GameObject("Visual");
        visualGo.transform.SetParent(transform, false);
        _visual = visualGo.transform;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Standard");

        _material = new Material(shader);
        if (_material.HasProperty("_BaseColor"))
            _material.SetColor("_BaseColor", _color);
        if (_material.HasProperty("_Color"))
            _material.SetColor("_Color", _color);
        if (_material.HasProperty("_EmissionColor"))
        {
            _material.EnableKeyword("_EMISSION");
            _material.SetColor("_EmissionColor", _color * 2f);
        }

        // Chevron "^" armado con dos brazos fitos, apuntando "arriba" (+Y local) en reposo.
        CreateArm(-0.22f, -32f);
        CreateArm(0.22f, 32f);
    }

    private void CreateArm(float xOffset, float zRotationDegrees)
    {
        GameObject arm = GameObject.CreatePrimitive(PrimitiveType.Cube);
        arm.name = "Arm";
        arm.transform.SetParent(_visual, false);
        arm.transform.localPosition = new Vector3(xOffset, 0f, 0f);
        arm.transform.localRotation = Quaternion.Euler(0f, 0f, zRotationDegrees);
        arm.transform.localScale = new Vector3(0.16f, 0.8f, 0.12f);

        Collider col = arm.GetComponent<Collider>();
        if (col != null)
            Destroy(col);

        MeshRenderer renderer = arm.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = _material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }
}
