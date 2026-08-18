using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class PassiveLoadoutHud : MonoBehaviour
{
    private struct PassiveSlotUi
    {
        public PassiveItemSlot Slot;
        public int SlotIndex;
        public Image Icon;
        public TextMeshProUGUI LevelBadge;
    }

    private static readonly (PassiveItemSlot slot, int index)[] SlotLayout =
    {
        (PassiveItemSlot.Head, 0),
        (PassiveItemSlot.Core, 0),
        (PassiveItemSlot.Arm, 0),
        (PassiveItemSlot.Arm, 1),
        (PassiveItemSlot.Leg, 0),
        (PassiveItemSlot.Leg, 1)
    };

    [SerializeField] private PassiveItemManager _passiveItemManager;
    [SerializeField, Min(8f)] private float _slotSpacing = 20f;

    private PassiveSlotUi[] _passiveSlots;
    private Transform _passivesRoot;

    private void Awake()
    {
        if (_passiveItemManager == null)
            _passiveItemManager = FindAnyObjectByType<PassiveItemManager>();

        if (!TryWireFromHierarchy())
            Debug.LogWarning($"[{nameof(PassiveLoadoutHud)}] Falta jerarquía 'Passives/PassiveSlot_N' en el prefab. Ejecutá ScrapWaves → UI → Rebuild BottomStrip In Prefab.", this);

        RefreshPassiveSlots();
    }

    private void OnEnable()
    {
        if (_passiveItemManager != null)
            _passiveItemManager.OnInventoryChanged += RefreshPassiveSlots;
    }

    private void OnDisable()
    {
        if (_passiveItemManager != null)
            _passiveItemManager.OnInventoryChanged -= RefreshPassiveSlots;
    }

    private bool TryWireFromHierarchy()
    {
        _passivesRoot = transform.Find("Passives");
        if (_passivesRoot == null)
            return false;

        _passiveSlots = new PassiveSlotUi[SlotLayout.Length];
        for (int i = 0; i < SlotLayout.Length; i++)
        {
            Transform slotRoot = _passivesRoot.Find($"PassiveSlot_{i}");
            if (slotRoot == null)
                return false;

            Image icon = HudUiWire.FindImage(slotRoot, "Icon");
            TextMeshProUGUI badge = HudUiWire.FindTmp(slotRoot, "Level");
            if (icon == null)
                return false;

            (PassiveItemSlot slot, int index) = SlotLayout[i];
            _passiveSlots[i] = new PassiveSlotUi
            {
                Slot = slot,
                SlotIndex = index,
                Icon = icon,
                LevelBadge = badge
            };
        }

        return true;
    }

    private void RefreshPassiveSlots()
    {
        if (_passiveSlots == null || _passiveItemManager == null)
            return;

        for (int i = 0; i < _passiveSlots.Length; i++)
        {
            PassiveSlotUi slotUi = _passiveSlots[i];
            PassiveItemInstance instance = _passiveItemManager.Inventory.Get(slotUi.Slot, slotUi.SlotIndex);
            if (instance?.Data == null)
            {
                slotUi.Icon.sprite = HudUiFactory.WhiteSprite;
                slotUi.Icon.color = HudUiFactory.EmptySlotColor;
                if (slotUi.LevelBadge != null)
                    slotUi.LevelBadge.text = string.Empty;
                continue;
            }

            Sprite icon = instance.Data.Icon;
            slotUi.Icon.sprite = icon != null ? icon : HudUiFactory.WhiteSprite;
            slotUi.Icon.color = icon != null ? Color.white : HudUiFactory.GetPlaceholderColor(SlotToPlaceholder(slotUi.Slot));
            if (slotUi.LevelBadge != null)
                slotUi.LevelBadge.text = instance.Level > 0 ? instance.Level.ToString() : string.Empty;
        }
    }

    private static HudPlaceholderKind SlotToPlaceholder(PassiveItemSlot slot) => slot switch
    {
        PassiveItemSlot.Head => HudPlaceholderKind.Head,
        PassiveItemSlot.Core => HudPlaceholderKind.Core,
        PassiveItemSlot.Arm => HudPlaceholderKind.Arm,
        PassiveItemSlot.Leg => HudPlaceholderKind.Leg,
        _ => HudPlaceholderKind.None
    };
}
