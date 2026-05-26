using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Al subir de nivel, ofrece 2–3 <see cref="Upgrade"/> aleatorios y aplica el elegido sobre <see cref="PlayerStats"/>.
/// UI con <see cref="Button"/> (ratón); compatible con Input System (InputSystemUIInputModule).
/// </summary>
[RequireComponent(typeof(PlayerXP))]
[RequireComponent(typeof(PlayerStats))]
[DisallowMultipleComponent]
public class UpgradeManager : MonoBehaviour
{
    [SerializeField, Range(2, 3), Tooltip("Cuántas opciones mostrar por subida de nivel.")]
    private int _choicesOffered = 3;

    [SerializeField, Tooltip("Pool de mejoras posibles (arrastra aquí los ScriptableObjects Upgrade).")]
    private List<Upgrade> _upgradePool = new List<Upgrade>();

    [SerializeField, Tooltip("Pausa el juego mientras se elige (Time.timeScale = 0).")]
    private bool _pauseWhileChoosing = true;

    [SerializeField, Min(200), Tooltip("Ancho de cada botón de opción.")]
    private float _buttonWidth = 260f;

    [SerializeField, Min(28), Tooltip("Alto de cada botón.")]
    private float _buttonHeight = 48f;

    [SerializeField, Tooltip("Si está vacío, se crea un Canvas en runtime la primera vez.")]
    private Canvas _canvasOverride;

    [SerializeField, Tooltip("Cámara orbital del jugador. Si está vacío, se busca ThirdPersonCamera en la escena.")]
    private ThirdPersonCamera _thirdPersonCamera;

    [SerializeField, Tooltip("Cámara de apuntado. Si está vacío, se busca camera_Aim en la escena.")]
    private camera_Aim _aimCamera;

    private PlayerXP _playerXp;
    private PlayerStats _playerStats;

    private readonly Queue<List<Upgrade>> _pendingOffers = new Queue<List<Upgrade>>();
    private List<Upgrade> _currentOffer;
    private bool _isChoosing;
    private float _previousTimeScale = 1f;

    private Canvas _canvas;
    private RectTransform _buttonsRow;
    private TextMeshProUGUI _titleText;
    private readonly List<Button> _spawnedButtons = new List<Button>();

    private ThirdPersonCamera _resolvedCamera;
    private camera_Aim _resolvedAimCamera;

    private void Awake()
    {
        _playerXp = GetComponent<PlayerXP>();
        _playerStats = GetComponent<PlayerStats>();
    }

    private void OnEnable()
    {
        _playerXp.OnLevelUp += HandleLevelUp;
    }

    private void OnDisable()
    {
        _playerXp.OnLevelUp -= HandleLevelUp;
        HideUpgradeUi();
    }

    private void HandleLevelUp(int newLevel)
    {
        List<Upgrade> offer = BuildRandomOffer();
        if (offer.Count == 0)
        {
            Debug.LogWarning("UpgradeManager: _upgradePool está vacío. Añade activos Upgrade.", this);
            return;
        }

        _pendingOffers.Enqueue(offer);
        if (!_isChoosing)
            BeginChoosing();
    }

    private List<Upgrade> BuildRandomOffer()
    {
        var result = new List<Upgrade>();
        if (_upgradePool == null || _upgradePool.Count == 0)
            return result;

        var available = new List<Upgrade>();
        for (int i = 0; i < _upgradePool.Count; i++)
        {
            if (_upgradePool[i] != null)
                available.Add(_upgradePool[i]);
        }

        if (available.Count == 0)
            return result;

        int want = Mathf.Clamp(_choicesOffered, 2, 3);
        want = Mathf.Min(want, available.Count);

        for (int i = 0; i < want; i++)
        {
            int idx = Random.Range(0, available.Count);
            result.Add(available[idx]);
            available.RemoveAt(idx);
        }

        return result;
    }

    private void BeginChoosing()
    {
        if (_pendingOffers.Count == 0)
            return;

        _currentOffer = _pendingOffers.Dequeue();
        _isChoosing = true;

        SetUpgradeCameraBlocked(true);

        if (_pauseWhileChoosing)
        {
            _previousTimeScale = Time.timeScale;
            Time.timeScale = 0f;
        }

        EnsureUiExists();
        RefreshUpgradeButtons();
        _canvas.gameObject.SetActive(true);
    }

    private void EnsureUiExists()
    {
        if (_canvasOverride != null)
        {
            _canvas = _canvasOverride;
            CacheRowIfNeeded();
            return;
        }

        if (_canvas != null)
            return;

        EnsureEventSystemWithInputSystemUi();

        var canvasGo = new GameObject("UpgradeChoiceCanvas", typeof(RectTransform));
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 5000;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        canvasGo.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(canvasGo.transform, false);
        var panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;
        var panelImg = panel.AddComponent<Image>();
        panelImg.color = new Color(0f, 0f, 0f, 0.55f);

        var titleGo = new GameObject("Title", typeof(RectTransform));
        titleGo.transform.SetParent(panel.transform, false);
        var titleRt = titleGo.GetComponent<RectTransform>();
        titleRt.anchorMin = new Vector2(0.5f, 0.65f);
        titleRt.anchorMax = new Vector2(0.5f, 0.65f);
        titleRt.pivot = new Vector2(0.5f, 0.5f);
        titleRt.sizeDelta = new Vector2(800f, 56f);
        titleRt.anchoredPosition = Vector2.zero;
        _titleText = titleGo.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(_titleText);
        _titleText.fontSize = 28f;
        _titleText.alignment = TextAlignmentOptions.Center;
        _titleText.color = Color.white;
        _titleText.text = "Elige una mejora";

        var rowGo = new GameObject("ButtonsRow", typeof(RectTransform));
        rowGo.transform.SetParent(panel.transform, false);
        _buttonsRow = rowGo.GetComponent<RectTransform>();
        _buttonsRow.anchorMin = new Vector2(0.5f, 0.42f);
        _buttonsRow.anchorMax = new Vector2(0.5f, 0.42f);
        _buttonsRow.pivot = new Vector2(0.5f, 0.5f);
        _buttonsRow.sizeDelta = new Vector2(1200f, _buttonHeight + 16f);
        _buttonsRow.anchoredPosition = Vector2.zero;

        var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 16f;
        layout.childControlWidth = false;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = true;

        _canvas.gameObject.SetActive(false);
    }

    private void CacheRowIfNeeded()
    {
        if (_buttonsRow != null)
            return;

        if (_canvas == null)
            return;

        var row = _canvas.transform.Find("Panel/ButtonsRow");
        if (row != null)
            _buttonsRow = row as RectTransform;

        var title = _canvas.transform.Find("Panel/Title");
        if (title != null)
            _titleText = title.GetComponent<TextMeshProUGUI>();
    }

    private static void EnsureEventSystemWithInputSystemUi()
    {
        EventSystem existing = Object.FindFirstObjectByType<EventSystem>();
        if (existing != null)
        {
            StandaloneInputModule legacy = existing.GetComponent<StandaloneInputModule>();
            if (legacy != null)
                Object.Destroy(legacy);

            if (existing.GetComponent<InputSystemUIInputModule>() == null)
                existing.gameObject.AddComponent<InputSystemUIInputModule>();
            return;
        }

        var esGo = new GameObject("EventSystem");
        esGo.AddComponent<EventSystem>();
        esGo.AddComponent<InputSystemUIInputModule>();
    }

    private void RefreshUpgradeButtons()
    {
        if (_currentOffer == null || _currentOffer.Count == 0)
            return;

        CacheRowIfNeeded();
        if (_buttonsRow == null)
        {
            Debug.LogError("UpgradeManager: no hay fila de botones. Asigna Canvas con Panel/ButtonsRow o deja el canvas en automático.", this);
            return;
        }

        foreach (Button b in _spawnedButtons)
        {
            if (b != null)
                Destroy(b.gameObject);
        }

        _spawnedButtons.Clear();

        for (int i = 0; i < _currentOffer.Count; i++)
        {
            Upgrade u = _currentOffer[i];
            string label = u != null ? u.DisplayName : "(null)";

            var btnGo = new GameObject($"UpgradeOption_{i}", typeof(RectTransform));
            btnGo.transform.SetParent(_buttonsRow, false);

            var btnRt = btnGo.GetComponent<RectTransform>();
            btnRt.sizeDelta = new Vector2(_buttonWidth, _buttonHeight);

            var img = btnGo.AddComponent<Image>();
            img.color = new Color(0.25f, 0.28f, 0.35f, 1f);

            var btn = btnGo.AddComponent<Button>();
            var colors = btn.colors;
            colors.highlightedColor = new Color(0.35f, 0.4f, 0.5f);
            colors.pressedColor = new Color(0.2f, 0.22f, 0.28f);
            btn.colors = colors;

            var textGo = new GameObject("Label", typeof(RectTransform));
            textGo.transform.SetParent(btnGo.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(8f, 4f);
            textRt.offsetMax = new Vector2(-8f, -4f);

            var txt = textGo.AddComponent<TextMeshProUGUI>();
            TmpUiHelper.ApplyDefaultFont(txt);
            txt.fontSize = 20f;
            txt.alignment = TextAlignmentOptions.Center;
            txt.color = Color.white;
            txt.text = label;

            int captured = i;
            btn.onClick.AddListener(() => OnUpgradeButtonClicked(captured));

            _spawnedButtons.Add(btn);
        }
    }

    private void OnUpgradeButtonClicked(int index)
    {
        SelectUpgrade(index);
    }

    private void SelectUpgrade(int index)
    {
        if (_currentOffer == null || index < 0 || index >= _currentOffer.Count)
            return;

        Upgrade chosen = _currentOffer[index];
        if (chosen != null)
            _playerStats.ApplyUpgrade(chosen);

        AdvanceAfterChoice();
    }

    private void AdvanceAfterChoice()
    {
        if (_pendingOffers.Count > 0)
        {
            _currentOffer = _pendingOffers.Dequeue();
            RefreshUpgradeButtons();
            return;
        }

        HideUpgradeUi();
        _isChoosing = false;
        _currentOffer = null;

        if (_pauseWhileChoosing)
            Time.timeScale = _previousTimeScale > 0f ? _previousTimeScale : 1f;
    }

    private void HideUpgradeUi()
    {
        if (_canvas != null && _canvasOverride == null)
            _canvas.gameObject.SetActive(false);
        else if (_canvasOverride != null)
            _canvasOverride.gameObject.SetActive(false);

        SetUpgradeCameraBlocked(false);
    }

    private void SetUpgradeCameraBlocked(bool blocked)
    {
        if (_resolvedCamera == null)
        {
            _resolvedCamera = _thirdPersonCamera != null
                ? _thirdPersonCamera
                : Object.FindFirstObjectByType<ThirdPersonCamera>();
        }

        if (_resolvedAimCamera == null)
        {
            _resolvedAimCamera = _aimCamera != null
                ? _aimCamera
                : Object.FindFirstObjectByType<camera_Aim>();
        }

        _resolvedCamera?.SetLookBlockedByUi(blocked);
        _resolvedAimCamera?.SetLookBlockedByUi(blocked);
    }
}
