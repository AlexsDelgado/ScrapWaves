using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class CraftingUI : MonoBehaviour
{
    [SerializeField, Min(180)] private float _cardWidth = 280f;
    [SerializeField, Min(120)] private float _cardHeight = 190f;

    private Canvas _canvas;
    private TextMeshProUGUI _titleText;
    private TextMeshProUGUI _statusText;
    private RectTransform _cardsContent;
    private float _previousTimeScale = 1f;
    private Action _onClosed;
    private ThirdPersonCamera _resolvedCamera;
    private bool _isVisible;

    public bool IsVisible => _isVisible;

    public IEnumerator PresentCoroutine(WeaponCraftingService crafting, MaterialInventory inventory, Action onClosed)
    {
        if (_isVisible)
        {
            // Ya hay una sesión de crafting abierta (p. ej. otra estación disparó esto
            // antes de que el caller pudiera chequear IsVisible): no pisar _onClosed
            // de la sesión en curso, que quedaría esperando para siempre.
            onClosed?.Invoke();
            yield break;
        }

        bool done = false;
        _onClosed = () => done = true;
        Show(crafting, inventory);
        while (!done)
            yield return null;
        onClosed?.Invoke();
    }

    private void Show(WeaponCraftingService crafting, MaterialInventory inventory)
    {
        _isVisible = true;
        _previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        SetCameraBlocked(true);
        EnsureUi();
        _titleText.text = "Crafting Station";
        SetStatus(string.Empty);
        BuildCards(crafting, inventory);
        _canvas.gameObject.SetActive(true);
    }

    private void Hide()
    {
        _isVisible = false;

        if (_canvas != null)
            _canvas.gameObject.SetActive(false);
        Time.timeScale = _previousTimeScale > 0f ? _previousTimeScale : 1f;
        SetCameraBlocked(false);
        _onClosed?.Invoke();
        _onClosed = null;
    }

    private void SetCameraBlocked(bool blocked)
    {
        if (_resolvedCamera == null)
            _resolvedCamera = FindFirstObjectByType<ThirdPersonCamera>();
        _resolvedCamera?.SetLookBlockedByUi(blocked);
    }

    // ---------------------------------------------------------------- Cards

    private void BuildCards(WeaponCraftingService crafting, MaterialInventory inventory)
    {
        ClearCards();
        WeaponManager weaponManager = crafting.GetComponent<WeaponManager>();
        if (weaponManager == null)
            weaponManager = FindAnyObjectByType<WeaponManager>();

        IReadOnlyList<IWeaponBehaviour> equipped = weaponManager != null
            ? weaponManager.GetEquippedWeapons()
            : Array.Empty<IWeaponBehaviour>();

        for (int i = 0; i < equipped.Count; i++)
        {
            WeaponInstance runtime = equipped[i]?.Runtime;
            if (runtime?.Data == null)
                continue;

            WeaponData data = runtime.Data;
            if (runtime.Level < 10)
            {
                int next = runtime.Level + 1;
                if (runtime.Level == 5 && runtime.SelectedPath == WeaponUpgradePath.None)
                {
                    IReadOnlyList<MaterialCost> advCost = crafting.GetAdvancedTinkeringCost(data);
                    bool canAffordAdv = inventory != null && inventory.CanAfford(advCost);
                    AddCard(
                        $"Advanced: {data.DisplayName}",
                        BuildCostText(advCost),
                        canAffordAdv,
                        () =>
                        {
                            if (!canAffordAdv)
                            {
                                SetStatus("Materiales insuficientes para Advanced Tinkering.");
                                return;
                            }

                            PresentAdvancedChoice(crafting, data);
                        });
                }
                else
                {
                    IReadOnlyList<MaterialCost> upgradeCost = crafting.GetUpgradeCost(data, runtime.SelectedPath, next);
                    bool canAffordUpgrade = inventory != null && inventory.CanAfford(upgradeCost);
                    AddCard(
                        $"Upgrade {data.DisplayName} → {next}",
                        BuildCostText(upgradeCost),
                        canAffordUpgrade,
                        () =>
                        {
                            CraftingActionResult result = crafting.TryUpgradeWeapon(data, next);
                            SetStatus(result.Message);
                            Refresh(crafting, inventory);
                        });
                }
            }
        }

        if (weaponManager != null && weaponManager.CanAddWeapon())
        {
            int slot = equipped.Count + 1;
            IReadOnlyList<MaterialCost> tinkerCost = crafting.GetTinkeringCost(slot);
            bool canAffordTinker = inventory != null && inventory.CanAfford(tinkerCost);
            AddCard(
                $"Tinker arma slot {slot}",
                BuildCostText(tinkerCost),
                canAffordTinker,
                () =>
                {
                    CraftingActionResult result = crafting.TryTinkerRandomWeapon();
                    SetStatus(result.Message);
                    Refresh(crafting, inventory);
                });
        }
    }

    /// <summary>
    /// Submenú de Advanced Tinkering: vive en la misma ventana/canvas que el resto del
    /// crafting (mismo título, mismo grid de cards), solo cambia qué cards se muestran.
    /// No hay handoff a otra UI ni otro estilo — al elegir una ruta, vuelve directo a las
    /// cards normales de crafting.
    /// </summary>
    private void PresentAdvancedChoice(WeaponCraftingService crafting, WeaponData weapon)
    {
        ClearCards();
        _titleText.text = $"Advanced Tinkering — {weapon.DisplayName}";
        SetStatus("Elegí una ruta de mejora.");

        if (crafting.TryGetGuaranteedPath(weapon, out WeaponUpgradePath guaranteed))
        {
            string name = guaranteed == WeaponUpgradePath.PathA
                ? (weapon.PathA?.PathName ?? "Path A")
                : (weapon.PathB?.PathName ?? "Path B");
            AddCard(name, "Path garantizado tras rechazo", true,
                () => ResolveAdvancedChoice(crafting, () => crafting.TryAdvancedTinkering(weapon, guaranteed, true)));
        }
        else
        {
            AddCard(weapon.PathA?.PathName ?? "Path A", "Aceptar path A", true,
                () => ResolveAdvancedChoice(crafting, () => crafting.TryAdvancedTinkering(weapon, WeaponUpgradePath.PathA, true)));
            AddCard(weapon.PathB?.PathName ?? "Path B", "Aceptar path B", true,
                () => ResolveAdvancedChoice(crafting, () => crafting.TryAdvancedTinkering(weapon, WeaponUpgradePath.PathB, true)));
            AddCard("Rechazar", "+50% costo, garantiza path alternativo", true,
                () => ResolveAdvancedChoice(crafting, () => crafting.TryAdvancedTinkering(weapon, WeaponUpgradePath.PathA, false)));
        }
    }

    private void ResolveAdvancedChoice(WeaponCraftingService crafting, Func<CraftingActionResult> applyChoice)
    {
        CraftingActionResult result = applyChoice();
        SetStatus(result.Message);
        _titleText.text = "Crafting Station";
        Refresh(crafting, FindAnyObjectByType<MaterialInventory>());
    }

    private void Refresh(WeaponCraftingService crafting, MaterialInventory inventory)
    {
        BuildCards(crafting, inventory);
    }

    private void SetStatus(string message)
    {
        if (_statusText != null)
            _statusText.text = message ?? string.Empty;
    }

    private static string BuildCostText(IReadOnlyList<MaterialCost> costs)
    {
        if (costs == null || costs.Count == 0)
            return "Gratis";
        var sb = new StringBuilder();
        for (int i = 0; i < costs.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(MaterialCatalog.GetDisplayName(costs[i].Material)).Append(' ').Append(costs[i].Amount);
        }
        return sb.ToString();
    }

    private readonly List<GameObject> _cardObjects = new();

    private void AddCard(string title, string description, bool interactable, Action onClick)
    {
        var card = new GameObject("CraftCard", typeof(RectTransform));
        card.transform.SetParent(_cardsContent, false);

        var bg = card.AddComponent<Image>();
        bg.sprite = HudUiFactory.WhiteSprite;
        bg.color = interactable ? HudUiFactory.BorderColor : new Color(0.22f, 0.22f, 0.24f, 1f);

        var cardLayout = card.AddComponent<VerticalLayoutGroup>();
        cardLayout.padding = new RectOffset(12, 12, 12, 12);
        cardLayout.spacing = 8f;
        cardLayout.childControlWidth = true;
        cardLayout.childControlHeight = true;
        cardLayout.childForceExpandWidth = true;
        cardLayout.childForceExpandHeight = false;

        CreateCardLabel(card.transform, title, 18f, FontStyles.Bold, Color.white, 44f);

        string desc = interactable ? description : description + "\n(Sin materiales)";
        var descGo = new GameObject("Description", typeof(RectTransform));
        descGo.transform.SetParent(card.transform, false);
        var descLabel = descGo.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(descLabel);
        descLabel.fontSize = 14f;
        descLabel.color = HudUiFactory.MutedTextColor;
        descLabel.alignment = TextAlignmentOptions.Top;
        descLabel.enableWordWrapping = true;
        descLabel.text = desc;
        descGo.AddComponent<LayoutElement>().flexibleHeight = 1f;

        Button btn = card.AddComponent<Button>();
        btn.interactable = interactable;
        btn.targetGraphic = bg;
        var colors = btn.colors;
        colors.disabledColor = new Color(0.35f, 0.35f, 0.35f, 0.9f);
        btn.colors = colors;
        btn.onClick.AddListener(() => onClick?.Invoke());

        _cardObjects.Add(card);
    }

    private void ClearCards()
    {
        for (int i = 0; i < _cardObjects.Count; i++)
        {
            if (_cardObjects[i] != null)
                Destroy(_cardObjects[i]);
        }
        _cardObjects.Clear();
    }

    private static TextMeshProUGUI CreateCardLabel(Transform parent, string text, float fontSize, FontStyles style, Color color, float preferredHeight)
    {
        var go = new GameObject("Title", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var label = go.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(label);
        label.fontSize = fontSize;
        label.fontStyle = style;
        label.color = color;
        label.alignment = TextAlignmentOptions.Top;
        label.enableWordWrapping = true;
        label.text = text;
        go.AddComponent<LayoutElement>().preferredHeight = preferredHeight;
        return label;
    }

    // ---------------------------------------------------------------- Construcción de la UI

    private void EnsureUi()
    {
        if (_canvas != null)
            return;

        if (FindFirstObjectByType<EventSystem>() == null)
        {
            var es = new GameObject("EventSystem");
            es.AddComponent<EventSystem>();
            es.AddComponent<InputSystemUIInputModule>();
        }

        var canvasGo = new GameObject("CraftingCanvas", typeof(RectTransform));
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 5100;
        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        // Backdrop: opaco, cubre toda la pantalla (no debe verse el fondo del juego).
        var backdrop = new GameObject("Backdrop", typeof(RectTransform));
        backdrop.transform.SetParent(canvasGo.transform, false);
        Stretch(backdrop.GetComponent<RectTransform>());
        var backdropImg = backdrop.AddComponent<Image>();
        backdropImg.sprite = HudUiFactory.WhiteSprite;
        backdropImg.color = new Color(0.02f, 0.02f, 0.025f, 1f);

        // Window: panel centrado de tamaño fijo, todo el contenido vive adentro.
        var window = new GameObject("Window", typeof(RectTransform));
        window.transform.SetParent(canvasGo.transform, false);
        var windowRt = window.GetComponent<RectTransform>();
        windowRt.anchorMin = new Vector2(0.5f, 0.5f);
        windowRt.anchorMax = new Vector2(0.5f, 0.5f);
        windowRt.pivot = new Vector2(0.5f, 0.5f);
        windowRt.sizeDelta = new Vector2(1500f, 820f);
        var windowImg = window.AddComponent<Image>();
        windowImg.sprite = HudUiFactory.WhiteSprite;
        windowImg.color = new Color(0.08f, 0.085f, 0.095f, 1f);

        var windowLayout = window.AddComponent<VerticalLayoutGroup>();
        windowLayout.padding = new RectOffset(32, 32, 24, 24);
        windowLayout.spacing = 10f;
        windowLayout.childControlWidth = true;
        windowLayout.childControlHeight = true;
        windowLayout.childForceExpandWidth = true;
        windowLayout.childForceExpandHeight = false;

        _titleText = HudUiFactory.CreateLabel(window.transform, "Title", "Crafting Station", 30f, TextAlignmentOptions.Center);
        _titleText.fontStyle = FontStyles.Bold;
        _titleText.gameObject.AddComponent<LayoutElement>().preferredHeight = 44f;

        _statusText = HudUiFactory.CreateLabel(window.transform, "Status", string.Empty, 16f, TextAlignmentOptions.Center);
        _statusText.fontStyle = FontStyles.Italic;
        _statusText.color = new Color(1f, 0.85f, 0.4f);
        _statusText.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;

        (RectTransform cardsSection, RectTransform cardsContent) = HudUiFactory.CreateScrollSection(
            window.transform, "CardsScroll", grid: true, cellSize: new Vector2(_cardWidth, _cardHeight));
        cardsSection.GetComponent<LayoutElement>().flexibleHeight = 1f;
        _cardsContent = cardsContent;

        var bottomBar = new GameObject("BottomBar", typeof(RectTransform));
        bottomBar.transform.SetParent(window.transform, false);
        bottomBar.AddComponent<LayoutElement>().preferredHeight = 56f;
        var bottomLayout = bottomBar.AddComponent<HorizontalLayoutGroup>();
        bottomLayout.childAlignment = TextAnchor.MiddleCenter;
        bottomLayout.childControlWidth = false;
        bottomLayout.childControlHeight = false;

        Button closeBtn = HudUiFactory.CreateButton(bottomBar.transform, "Cerrar", new Vector2(220f, 48f));
        closeBtn.onClick.AddListener(Hide);

        _canvas.gameObject.SetActive(false);
    }

    private static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
