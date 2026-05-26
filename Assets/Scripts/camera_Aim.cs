using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Cámara de apuntado en tercera persona: ratón controla yaw/pitch; el jugador mira hacia adelante en horizontal.
/// </summary>
[DefaultExecutionOrder(-150)]
public class camera_Aim : MonoBehaviour
{
    [SerializeField] private Transform _followTarget;

    [SerializeField, Tooltip("Pivote de órbita relativo al jugador (altura de hombros).")]
    private Vector3 _pivotOffset = new Vector3(0f, 1.2f, 0f);

    [SerializeField, Tooltip("Offset local de la cámara (Z negativo = detrás).")]
    private Vector3 _cameraOffset = new Vector3(0f, 0.35f, -4.5f);

    [SerializeField] private float _horizontalSensitivity = 2.2f;
    [SerializeField] private float _verticalSensitivity = 2.2f;
    [SerializeField] private bool _invertVertical;
    [SerializeField] private float _minPitch = -40f;
    [SerializeField] private float _maxPitch = 55f;
    [SerializeField] private bool _lockCursorOnPlay = true;

    [SerializeField, Tooltip("Distancia del rayo de mira si no golpea geometría.")]
    private float _aimRayDistance = 300f;

    [SerializeField] private LayerMask _aimRaycastMask = ~0;

    [Header("Mira")]
    [SerializeField] private bool _showCrosshair = true;
    [SerializeField, Min(2f)] private float _crosshairSize = 14f;
    [SerializeField, Min(1f)] private float _crosshairThickness = 2f;
    [SerializeField, Min(0f)] private float _crosshairGap = 4f;
    [SerializeField] private Color _crosshairColor = new Color(1f, 1f, 1f, 0.92f);

    private float _yaw;
    private float _pitch;
    private bool _lookBlockedByUi;
    private GameObject _crosshairRoot;
    private static Sprite s_whiteSprite;

    public Vector3 AimForward { get; private set; } = Vector3.forward;

    public Ray ScreenCenterRay
    {
        get
        {
            Camera cam = GetViewCamera();
            return cam != null
                ? cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f))
                : new Ray(transform.position, transform.forward);
        }
    }

    private void Start()
    {
        if (_followTarget != null)
            _yaw = _followTarget.eulerAngles.y;
        else
            _yaw = transform.eulerAngles.y;

        _pitch = 12f;

        if (_showCrosshair)
            BuildCrosshair();

        ApplyCursorLock();
    }

    public void SetLookBlockedByUi(bool blocked)
    {
        if (blocked == _lookBlockedByUi)
            return;

        _lookBlockedByUi = blocked;
        ApplyCursorLock();

        if (_crosshairRoot != null)
            _crosshairRoot.SetActive(!blocked);
    }

    private void ApplyCursorLock()
    {
        if (_lookBlockedByUi)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            return;
        }

        if (_lockCursorOnPlay)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void Update()
    {
        if (_followTarget == null)
            return;

        ApplyMouseAim();
        ApplyCameraTransform();
        ApplyPlayerFacing();
    }

    /// <summary>Dirección desde un origen (p. ej. spawn del arma) hacia el punto bajo la mira.</summary>
    public Vector3 GetAimDirectionFrom(Vector3 origin)
    {
        Ray ray = ScreenCenterRay;
        Vector3 aimPoint;

        if (Physics.Raycast(ray, out RaycastHit hit, _aimRayDistance, _aimRaycastMask, QueryTriggerInteraction.Ignore))
            aimPoint = hit.point;
        else
            aimPoint = ray.GetPoint(_aimRayDistance);

        Vector3 toTarget = aimPoint - origin;
        if (toTarget.sqrMagnitude > 0.0001f)
            return toTarget.normalized;

        return ray.direction.sqrMagnitude > 0.0001f ? ray.direction.normalized : transform.forward;
    }

    private void ApplyMouseAim()
    {
        Mouse mouse = Mouse.current;
        if (_lookBlockedByUi || mouse == null)
            return;

        Vector2 delta = mouse.delta.ReadValue();
        _yaw += delta.x * _horizontalSensitivity;
        float verticalSign = _invertVertical ? -1f : 1f;
        _pitch += verticalSign * delta.y * _verticalSensitivity;
        _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
    }

    private void ApplyCameraTransform()
    {
        Quaternion viewRot = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 pivot = _followTarget.position + _pivotOffset;

        transform.rotation = viewRot;
        transform.position = pivot + viewRot * _cameraOffset;
    }

    private void ApplyPlayerFacing()
    {
        Vector3 flatForward = transform.forward;
        flatForward.y = 0f;
        if (flatForward.sqrMagnitude < 0.0001f)
            flatForward = _followTarget.forward;

        AimForward = flatForward.normalized;
        _followTarget.rotation = Quaternion.LookRotation(AimForward, Vector3.up);
    }

    private void BuildCrosshair()
    {
        var canvasGo = new GameObject("AimCrosshair_Canvas");
        canvasGo.transform.SetParent(transform, false);

        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0)
            canvasGo.layer = uiLayer;

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 600;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var canvasRt = canvasGo.GetComponent<RectTransform>();
        canvasRt.anchorMin = Vector2.zero;
        canvasRt.anchorMax = Vector2.one;
        canvasRt.offsetMin = Vector2.zero;
        canvasRt.offsetMax = Vector2.zero;

        _crosshairRoot = new GameObject("Crosshair");
        _crosshairRoot.transform.SetParent(canvasGo.transform, false);

        var rootRt = _crosshairRoot.AddComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0.5f, 0.5f);
        rootRt.anchorMax = new Vector2(0.5f, 0.5f);
        rootRt.pivot = new Vector2(0.5f, 0.5f);
        rootRt.anchoredPosition = Vector2.zero;

        float arm = _crosshairSize * 0.5f;
        float halfGap = _crosshairGap * 0.5f;

        CreateCrosshairBar(_crosshairRoot.transform, "Top", new Vector2(0f, halfGap + arm * 0.5f), new Vector2(_crosshairThickness, arm));
        CreateCrosshairBar(_crosshairRoot.transform, "Bottom", new Vector2(0f, -(halfGap + arm * 0.5f)), new Vector2(_crosshairThickness, arm));
        CreateCrosshairBar(_crosshairRoot.transform, "Left", new Vector2(-(halfGap + arm * 0.5f), 0f), new Vector2(arm, _crosshairThickness));
        CreateCrosshairBar(_crosshairRoot.transform, "Right", new Vector2(halfGap + arm * 0.5f, 0f), new Vector2(arm, _crosshairThickness));
    }

    private void CreateCrosshairBar(Transform parent, string name, Vector2 anchoredPos, Vector2 size)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = size;

        var img = go.AddComponent<Image>();
        img.sprite = GetWhiteSprite();
        img.color = _crosshairColor;
        img.raycastTarget = false;
    }

    private Camera GetViewCamera()
    {
        Camera cam = GetComponent<Camera>();
        if (cam != null)
            return cam;

        cam = Camera.main;
        return cam != null ? cam : Camera.current;
    }

    private static Sprite GetWhiteSprite()
    {
        if (s_whiteSprite != null)
            return s_whiteSprite;

        var tex = Texture2D.whiteTexture;
        s_whiteSprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        return s_whiteSprite;
    }
}
