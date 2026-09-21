using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class CraftingWeaponSlotView : MonoBehaviour
{
    public Button Button;
    public Image Background;
    public Image Border;
    public Image Icon;
    public TMP_Text NameText;
    public TMP_Text LevelText;
    public Color NormalColor = new(0.067f, 0.078f, 0.075f, 1f);
    public Color SelectedColor = new(0.122f, 0.145f, 0.133f, 1f);

    public void Bind(WeaponInstance weapon, bool selected, bool interactable)
    {
        bool occupied = weapon?.Data != null;
        NameText.text = occupied ? weapon.Data.DisplayName : "Empty slot";
        LevelText.text = occupied ? $"LV {weapon.Level}" : "+ Tinker";
        Icon.sprite = occupied ? WeaponUiIcons.Resolve(weapon.Data, selected: true) : null;
        Icon.gameObject.SetActive(Icon.sprite != null);
        Icon.color = Color.white;
        Background.color = selected ? SelectedColor : NormalColor;
        Border.gameObject.SetActive(selected);
        Button.interactable = interactable;
    }
}
