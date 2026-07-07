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
    private TextMeshProUGUI _inventoryText;
    private RectTransform _cardsRow;
    private readonly List<Button> _buttons = new();
    private float _previousTimeScale = 1f;
    private Action _onClosed;

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
        _previousTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        EnsureUi();
        _titleText.text = "Crafting Station";
        _inventoryText.text = BuildInventoryText(inventory);
        BuildCards(crafting, inventory);
        _canvas.gameObject.SetActive(true);
    }

    private void Hide()
    {
        if (_canvas != null)
            _canvas.gameObject.SetActive(false);
        Time.timeScale = _previousTimeScale > 0f ? _previousTimeScale : 1f;
        _onClosed?.Invoke();
        _onClosed = null;
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
                    AddCard($"Advanced: {data.DisplayName}", BuildCostText(crafting.GetAdvancedTinkeringCost(data)),
                        () => PresentAdvancedChoice(crafting, data));
                }
                else
                {
                    AddCard($"Upgrade {data.DisplayName} → {next}",
                        BuildCostText(crafting.GetUpgradeCost(data, runtime.SelectedPath, next)),
                        () =>
                        {
                            crafting.TryUpgradeWeapon(data, next);
                            Refresh(crafting, inventory);
                        });
                }
            }
        }

        if (weaponManager != null && weaponManager.CanAddWeapon())
        {
            int slot = equipped.Count + 1;
            AddCard($"Tinker arma slot {slot}", BuildCostText(crafting.GetTinkeringCost(slot)), () =>
            {
                crafting.TryTinkerRandomWeapon();
                Refresh(crafting, inventory);
            });
        }

        AddCard("Cerrar", string.Empty, Hide);
    }

    private void PresentAdvancedChoice(WeaponCraftingService crafting, WeaponData weapon)
    {
        var options = new List<LevelUpChoiceOption>
        {
            new(weapon.PathA?.PathName ?? "Path A", "Aceptar path A"),
            new(weapon.PathB?.PathName ?? "Path B", "Aceptar path B"),
            new("Rechazar", "+50% costo, garantiza path alternativo")
        };

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
        int selected = -1;
        yield return choiceUi.PresentCoroutine("Advanced Tinkering", options, index => selected = index);

        if (selected == 0)
            crafting.TryAdvancedTinkering(weapon, WeaponUpgradePath.PathA, true);
        else if (selected == 1)
            crafting.TryAdvancedTinkering(weapon, WeaponUpgradePath.PathB, true);
        else if (selected == 2)
            crafting.TryAdvancedTinkering(weapon, WeaponUpgradePath.PathA, false);

        Refresh(crafting, FindAnyObjectByType<MaterialInventory>());
    }

    private void Refresh(WeaponCraftingService crafting, MaterialInventory inventory)
    {
        _inventoryText.text = BuildInventoryText(inventory);
        BuildCards(crafting, inventory);
    }

    private static string BuildInventoryText(MaterialInventory inventory)
    {
        if (inventory == null)
            return string.Empty;

        var sb = new StringBuilder("Materiales: ");
        foreach (MaterialType type in Enum.GetValues(typeof(MaterialType)))
            sb.Append(type).Append('=').Append(inventory.GetAmount(type)).Append(' ');
        return sb.ToString();
    }

    private static string BuildCostText(IReadOnlyList<MaterialCost> costs)
    {
        if (costs == null || costs.Count == 0)
            return "Gratis";
        var sb = new StringBuilder();
        for (int i = 0; i < costs.Count; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(costs[i].Material).Append(' ').Append(costs[i].Amount);
        }
        return sb.ToString();
    }

    private void AddCard(string title, string description, Action onClick)
    {
        Button btn = CreateCard(title, description);
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

    private Button CreateCard(string title, string description)
    {
        var cardGo = new GameObject("CraftCard", typeof(RectTransform));
        cardGo.transform.SetParent(_cardsRow, false);
        cardGo.GetComponent<RectTransform>().sizeDelta = new Vector2(_cardWidth, _cardHeight);
        var bg = cardGo.AddComponent<Image>();
        bg.sprite = HudUiFactory.WhiteSprite;
        bg.color = HudUiFactory.BorderColor;
        var btn = cardGo.AddComponent<Button>();

        var titleText = CreateLabel(cardGo.transform, title, 18, FontStyles.Bold, new Vector2(0f, 0.55f), new Vector2(1f, 1f));
        CreateLabel(cardGo.transform, description, 14, FontStyles.Normal, new Vector2(0f, 0f), new Vector2(1f, 0.55f));
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

        _titleText = CreateLabel(panel.transform, "Crafting", 30, FontStyles.Bold, new Vector2(0f, 0.8f), new Vector2(1f, 1f));
        _titleText.alignment = TextAlignmentOptions.Center;
        _inventoryText = CreateLabel(panel.transform, string.Empty, 16, FontStyles.Normal, new Vector2(0f, 0.72f), new Vector2(1f, 0.8f));
        _inventoryText.alignment = TextAlignmentOptions.Center;

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
