using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// HUD pequeña que muestra la cantidad de cada material del inventario
/// con iconos de color y cantidad numérica.
/// </summary>
[DisallowMultipleComponent]
public class MaterialInventoryHUD : MonoBehaviour
{
    [SerializeField] private MaterialInventory _inventory;
    [SerializeField, Tooltip("Posición anclada respecto de la esquina superior izquierda.")]
    private Vector2 _anchoredPosition = new Vector2(16f, -16f);
    [SerializeField, Min(12f)] private float _iconSize = 22f;
    [SerializeField, Min(8f)] private float _fontSize = 16f;
    [SerializeField, Tooltip("Si está activo, muestra todos los materiales; si no, solo los que tengas (>0).")]
    private bool _showEmpty = true;

    private MaterialInventoryDisplayView _display;

    private void Awake() => ResolveInventory();

    private void OnEnable()
    {
        ResolveInventory();
        EnsureUi();
        if (_inventory != null)
        {
            _inventory.OnInventoryChanged += Refresh;
            Refresh();
        }
    }

    private void OnDisable()
    {
        if (_inventory != null)
            _inventory.OnInventoryChanged -= Refresh;
    }

    private void ResolveInventory()
    {
        if (_inventory != null)
            return;
        _inventory = MaterialInventory.Instance != null
            ? MaterialInventory.Instance
            : FindAnyObjectByType<MaterialInventory>();
    }

    private void Refresh()
    {
        if (_display != null)
            _display.Refresh(_inventory);
    }

    private void EnsureUi()
    {
        if (_display != null)
            return;

        var canvasGo = new GameObject("MaterialInventoryHUDCanvas", typeof(RectTransform));
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(canvasGo.transform, false);
        var panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = new Vector2(0f, 1f);
        panelRt.anchorMax = new Vector2(0f, 1f);
        panelRt.pivot = new Vector2(0f, 1f);
        panelRt.anchoredPosition = _anchoredPosition;
        var panelImg = panel.AddComponent<Image>();
        panelImg.sprite = HudUiFactory.WhiteSprite;
        panelImg.color = new Color(0f, 0f, 0f, 0.45f);
        panelImg.raycastTarget = false;

        var panelLayout = panel.AddComponent<VerticalLayoutGroup>();
        panelLayout.padding = new RectOffset(10, 10, 8, 8);
        panelLayout.childAlignment = TextAnchor.UpperLeft;
        panelLayout.childControlWidth = false;
        panelLayout.childControlHeight = false;
        panelLayout.childForceExpandWidth = false;
        panelLayout.childForceExpandHeight = false;

        var panelFitter = panel.AddComponent<ContentSizeFitter>();
        panelFitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        panelFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        _display = MaterialInventoryDisplayView.Create(
            panel.transform,
            MaterialDisplayLayout.Vertical,
            _showEmpty,
            showNames: true,
            iconSize: _iconSize,
            fontSize: _fontSize);
    }
}
