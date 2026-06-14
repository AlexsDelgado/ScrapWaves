using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class ReticleHud : MonoBehaviour
{
    [SerializeField] private bool _visibleOnStart = true;
    [SerializeField, Min(8f)] private float _reticleSize = 34f;
    [SerializeField, Min(1f)] private float _lineLength = 9f;
    [SerializeField, Min(1f)] private float _lineThickness = 2f;
    [SerializeField, Min(0f)] private float _centerGap = 5f;
    [SerializeField] private Color _lineColor = new Color(1f, 1f, 1f, 0.92f);
    [SerializeField] private Color _shadowColor = new Color(0f, 0f, 0f, 0.55f);
    [SerializeField] private Vector2 _shadowOffset = new Vector2(1f, -1f);
    [SerializeField] private int _sortingOrder = 650;

    private GameObject _canvasRoot;
    private static Sprite s_whiteSprite;

    private void Awake()
    {
        BuildUi();
        SetVisible(_visibleOnStart);
    }

    public void SetVisible(bool visible)
    {
        if (_canvasRoot != null)
            _canvasRoot.SetActive(visible);
    }

    private void BuildUi()
    {
        _canvasRoot = new GameObject("ReticleHUD_Canvas");
        _canvasRoot.transform.SetParent(transform, false);

        int uiLayer = LayerMask.NameToLayer("UI");
        if (uiLayer >= 0)
            _canvasRoot.layer = uiLayer;

        Canvas canvas = _canvasRoot.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = _sortingOrder;

        CanvasScaler scaler = _canvasRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform canvasRt = _canvasRoot.GetComponent<RectTransform>();
        canvasRt.anchorMin = Vector2.zero;
        canvasRt.anchorMax = Vector2.one;
        canvasRt.offsetMin = Vector2.zero;
        canvasRt.offsetMax = Vector2.zero;

        GameObject reticle = new GameObject("Reticle");
        reticle.transform.SetParent(_canvasRoot.transform, false);

        RectTransform reticleRt = reticle.AddComponent<RectTransform>();
        reticleRt.anchorMin = new Vector2(0.5f, 0.5f);
        reticleRt.anchorMax = new Vector2(0.5f, 0.5f);
        reticleRt.pivot = new Vector2(0.5f, 0.5f);
        reticleRt.anchoredPosition = Vector2.zero;
        reticleRt.sizeDelta = new Vector2(_reticleSize, _reticleSize);

        CreateArm(reticle.transform, "Left", new Vector2(-(_centerGap + _lineLength * 0.5f), 0f), new Vector2(_lineLength, _lineThickness));
        CreateArm(reticle.transform, "Right", new Vector2(_centerGap + _lineLength * 0.5f, 0f), new Vector2(_lineLength, _lineThickness));
        CreateArm(reticle.transform, "Top", new Vector2(0f, _centerGap + _lineLength * 0.5f), new Vector2(_lineThickness, _lineLength));
        CreateArm(reticle.transform, "Bottom", new Vector2(0f, -(_centerGap + _lineLength * 0.5f)), new Vector2(_lineThickness, _lineLength));
    }

    private void CreateArm(Transform parent, string name, Vector2 anchoredPosition, Vector2 size)
    {
        CreateImage(parent, name + "_Shadow", anchoredPosition + _shadowOffset, size, _shadowColor);
        CreateImage(parent, name, anchoredPosition, size, _lineColor);
    }

    private void CreateImage(Transform parent, string name, Vector2 anchoredPosition, Vector2 size, Color color)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);

        RectTransform rt = go.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = size;

        Image image = go.AddComponent<Image>();
        image.sprite = GetWhiteSprite();
        image.color = color;
        image.raycastTarget = false;
    }

    private static Sprite GetWhiteSprite()
    {
        if (s_whiteSprite != null)
            return s_whiteSprite;

        Texture2D tex = Texture2D.whiteTexture;
        s_whiteSprite = Sprite.Create(
            tex,
            new Rect(0f, 0f, tex.width, tex.height),
            new Vector2(0.5f, 0.5f),
            100f);
        return s_whiteSprite;
    }
}
