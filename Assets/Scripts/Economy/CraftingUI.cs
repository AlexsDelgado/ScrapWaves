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
    [SerializeField, Min(180)] private float _cardWidth = 260f;
    [SerializeField, Min(120)] private float _cardHeight = 180f;

    private Canvas _canvas;
    private TextMeshProUGUI _titleText;
    private TextMeshProUGUI _statusText;
    private RectTransform _cardsRow;
    private readonly List<Button> _buttons = new();
    private float _previousTimeScale = 1f;
    private Action _onClosed;
    private ThirdPersonCamera _resolvedCamera;
    private bool _isVisible;
    private bool _awaitingAdvancedChoice;
    private LevelUpChoiceUI _activeChoiceUi;

    public bool IsVisible => _isVisible;

    public IEnumerator PresentCoroutine(WeaponCraftingService crafting, MaterialInventory inventory, Action onClosed)
    {
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
        _statusText.text = string.Empty;
        BuildCards(crafting, inventory);
        _canvas.gameObject.SetActive(true);
    }

    private void Hide()
    {
        _isVisible = false;

        // Si el path choice quedó abierto detrás/encima, cancelarlo para que no
        // quede colgado al cerrar el crafting.
        if (_awaitingAdvancedChoice && _activeChoiceUi != null && _activeChoiceUi.IsVisible)
            _activeChoiceUi.CancelSelection();

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

    private void BuildCards(WeaponCraftingService crafting, MaterialInventory inventory)
    {
        ClearButtons();
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

        AddCard("Cerrar", string.Empty, true, Hide);
    }

    private void PresentAdvancedChoice(WeaponCraftingService crafting, WeaponData weapon)
    {
        var options = new List<LevelUpChoiceOption>();
        if (crafting.TryGetGuaranteedPath(weapon, out WeaponUpgradePath guaranteed))
        {
            string name = guaranteed == WeaponUpgradePath.PathA
                ? (weapon.PathA?.PathName ?? "Path A")
                : (weapon.PathB?.PathName ?? "Path B");
            options.Add(new LevelUpChoiceOption(name, "Path garantizado tras rechazo"));
        }
        else
        {
            options.Add(new(weapon.PathA?.PathName ?? "Path A", "Aceptar path A"));
            options.Add(new(weapon.PathB?.PathName ?? "Path B", "Aceptar path B"));
            options.Add(new("Rechazar", "+50% costo, garantiza path alternativo"));
        }

        LevelUpChoiceUI choiceUi = FindAnyObjectByType<LevelUpChoiceUI>();
        if (choiceUi == null)
            return;

        StartCoroutine(AdvancedChoiceCoroutine(crafting, weapon, choiceUi, options));
    }

    private IEnumerator AdvancedChoiceCoroutine(
        WeaponCraftingService crafting,
        WeaponData weapon,
        LevelUpChoiceUI choiceUi,
        List<LevelUpChoiceOption> options)
    {
        _awaitingAdvancedChoice = true;
        _activeChoiceUi = choiceUi;

        // Ocultar crafting para que el path choice quede visible y clickeable.
        if (_canvas != null)
            _canvas.gameObject.SetActive(false);

        // CraftingCanvas usa sortingOrder 5100; el choice por defecto es 5000.
        choiceUi.EnsureSortingOrderAtLeast(5200);

        int selected = -1;
        yield return choiceUi.PresentCoroutine("Advanced Tinkering", options, index => selected = index);

        _awaitingAdvancedChoice = false;
        _activeChoiceUi = null;

        // El usuario cerró el crafting mientras elegía path: no reabrir ni aplicar.
        if (!_isVisible)
            yield break;

        Time.timeScale = 0f;
        SetCameraBlocked(true);
        if (_canvas != null)
            _canvas.gameObject.SetActive(true);

        MaterialInventory inventory = FindAnyObjectByType<MaterialInventory>();
        CraftingActionResult result;

        if (crafting.TryGetGuaranteedPath(weapon, out WeaponUpgradePath guaranteed))
        {
            if (selected == 0)
                result = crafting.TryAdvancedTinkering(weapon, guaranteed, true);
            else
                result = new CraftingActionResult(false, "Selección cancelada.");
        }
        else if (selected == 0)
            result = crafting.TryAdvancedTinkering(weapon, WeaponUpgradePath.PathA, true);
        else if (selected == 1)
            result = crafting.TryAdvancedTinkering(weapon, WeaponUpgradePath.PathB, true);
        else if (selected == 2)
            result = crafting.TryAdvancedTinkering(weapon, WeaponUpgradePath.PathA, false);
        else
            result = new CraftingActionResult(false, "Selección cancelada.");

        SetStatus(result.Message);
        Refresh(crafting, inventory);
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

    private void AddCard(string title, string description, bool interactable, Action onClick)
    {
        Button btn = CreateCard(title, description, interactable);
        btn.onClick.AddListener(() => onClick?.Invoke());
        _buttons.Add(btn);
    }

    private void ClearButtons()
    {
        for (int i = 0; i < _buttons.Count; i++)
        {
            if (_buttons[i] != null)
                Destroy(_buttons[i].gameObject);
        }
        _buttons.Clear();
    }

    private Button CreateCard(string title, string description, bool interactable)
    {
        var cardGo = new GameObject("CraftCard", typeof(RectTransform));
        cardGo.transform.SetParent(_cardsRow, false);
        cardGo.GetComponent<RectTransform>().sizeDelta = new Vector2(_cardWidth, _cardHeight);
        var bg = cardGo.AddComponent<Image>();
        bg.sprite = HudUiFactory.WhiteSprite;
        bg.color = interactable ? HudUiFactory.BorderColor : new Color(0.25f, 0.25f, 0.25f, 0.85f);
        var btn = cardGo.AddComponent<Button>();
        btn.interactable = interactable;

        var colors = btn.colors;
        colors.disabledColor = new Color(0.35f, 0.35f, 0.35f, 0.9f);
        btn.colors = colors;

        CreateLabel(cardGo.transform, title, 18, FontStyles.Bold, new Vector2(0f, 0.55f), new Vector2(1f, 1f));
        string desc = interactable ? description : description + "\n(Sin materiales)";
        CreateLabel(cardGo.transform, desc, 14, FontStyles.Normal, new Vector2(0f, 0f), new Vector2(1f, 0.55f));
        return btn;
    }

    private static TextMeshProUGUI CreateLabel(Transform parent, string text, float size, FontStyles style, Vector2 anchorMin, Vector2 anchorMax)
    {
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = new Vector2(10f, 10f);
        rt.offsetMax = new Vector2(-10f, -10f);
        var label = go.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(label);
        label.fontSize = size;
        label.fontStyle = style;
        label.alignment = TextAlignmentOptions.Top;
        label.color = Color.white;
        label.text = text;
        label.enableWordWrapping = true;
        label.raycastTarget = false;
        return label;
    }

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
        canvasGo.AddComponent<GraphicRaycaster>();

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(canvasGo.transform, false);
        var panelRt = panel.GetComponent<RectTransform>();
        panelRt.anchorMin = Vector2.zero;
        panelRt.anchorMax = Vector2.one;
        panelRt.offsetMin = Vector2.zero;
        panelRt.offsetMax = Vector2.zero;
        panel.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

        _titleText = CreateLabel(panel.transform, "Crafting", 30, FontStyles.Bold, new Vector2(0f, 0.88f), new Vector2(1f, 1f));
        _titleText.alignment = TextAlignmentOptions.Center;

        // Inventario lo muestra MaterialInventoryHUD; acá solo status + cards.
        _statusText = CreateLabel(panel.transform, string.Empty, 15, FontStyles.Italic, new Vector2(0f, 0.78f), new Vector2(1f, 0.86f));
        _statusText.alignment = TextAlignmentOptions.Center;
        _statusText.color = new Color(1f, 0.85f, 0.4f);

        var rowGo = new GameObject("CardsRow", typeof(RectTransform));
        rowGo.transform.SetParent(panel.transform, false);
        _cardsRow = rowGo.GetComponent<RectTransform>();
        _cardsRow.anchorMin = new Vector2(0.5f, 0.35f);
        _cardsRow.anchorMax = new Vector2(0.5f, 0.35f);
        _cardsRow.pivot = new Vector2(0.5f, 0.5f);
        _cardsRow.sizeDelta = new Vector2(1300f, _cardHeight + 20f);
        var layout = rowGo.AddComponent<HorizontalLayoutGroup>();
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.spacing = 16f;
        layout.childControlWidth = false;
        layout.childControlHeight = false;

        _canvas.gameObject.SetActive(false);
    }
}
