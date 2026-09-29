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

    [Header("Slot vacío (vacío = cuadrado liso)")]
    [SerializeField] private Sprite _emptyHead;
    [SerializeField] private Sprite _emptyCore;
    [SerializeField] private Sprite _emptyArm;
    [SerializeField] private Sprite _emptyLeg;

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

            Image icon = ResolveSlotIcon(slotRoot);
            TextMeshProUGUI badge = HudUiWire.FindTmp(slotRoot, "Level");
            if (icon == null)
                return false;

            icon.preserveAspect = true;

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

    /// <summary>
    /// CreateIconSlot nests Icon/Icon; paint the leaf so the outer border frame does not get the sprite.
    /// </summary>
    private static Image ResolveSlotIcon(Transform slotRoot)
    {
        Transform nested = slotRoot.Find("Icon/Icon");
        if (nested != null)
        {
            Image nestedImage = nested.GetComponent<Image>();
            if (nestedImage != null)
                return nestedImage;
        }

        return HudUiWire.FindImage(slotRoot, "Icon");
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
                Sprite empty = EmptySprite(slotUi.Slot);
                slotUi.Icon.sprite = empty != null ? empty : HudUiFactory.WhiteSprite;
                slotUi.Icon.color = empty != null ? Color.white : HudUiFactory.EmptySlotColor;
                SetBadge(slotUi.LevelBadge, string.Empty);
                continue;
            }

            Sprite icon = instance.Data.Icon;
            slotUi.Icon.sprite = icon != null ? icon : HudUiFactory.WhiteSprite;
            slotUi.Icon.color = icon != null ? Color.white : HudUiFactory.GetPlaceholderColor(SlotToPlaceholder(slotUi.Slot));
            SetBadge(slotUi.LevelBadge, instance.Level > 0 ? instance.Level.ToString() : string.Empty);
        }
    }

    /// <summary>Si el texto vive dentro de una chapita (Badge), se oculta la chapita entera cuando no hay nivel.</summary>
    private static void SetBadge(TextMeshProUGUI badge, string text)
    {
        if (badge == null)
            return;

        badge.text = text;
        Transform plate = badge.transform.parent;
        if (plate != null && plate.name == "Badge")
            plate.gameObject.SetActive(!string.IsNullOrEmpty(text));
    }

    private Sprite EmptySprite(PassiveItemSlot slot) => slot switch
    {
        PassiveItemSlot.Head => _emptyHead,
        PassiveItemSlot.Core => _emptyCore,
        PassiveItemSlot.Arm => _emptyArm,
        PassiveItemSlot.Leg => _emptyLeg,
        _ => null
    };

    private static HudPlaceholderKind SlotToPlaceholder(PassiveItemSlot slot) => slot switch
    {
        PassiveItemSlot.Head => HudPlaceholderKind.Head,
        PassiveItemSlot.Core => HudPlaceholderKind.Core,
        PassiveItemSlot.Arm => HudPlaceholderKind.Arm,
        PassiveItemSlot.Leg => HudPlaceholderKind.Leg,
        _ => HudPlaceholderKind.None
    };
}
