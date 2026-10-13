using TMPro;
using UnityEngine;

/// <summary>Binds only saved UI. Every panel, glyph and text field is editor authored.</summary>
public sealed class TutorialHudView : MonoBehaviour
{
    [SerializeField] private CanvasGroup _group;
    [SerializeField] private TMP_Text _title;
    [SerializeField] private TMP_Text _body;
    [SerializeField] private TMP_Text _footer;
    [SerializeField] private GameObject _advance;
    [SerializeField] private GameObject[] _controlGroups;
    [SerializeField] private CanvasGroup _craftingGroup;
    [SerializeField] private TMP_Text _craftingTitle;
    [SerializeField] private TMP_Text _craftingBody;
    [SerializeField] private CraftingUI _craftingUi;
    private bool _shown;
    public bool HasReferences => _group != null && _title != null && _body != null && _footer != null && _advance != null && _controlGroups != null && _controlGroups.Length == 5
        && _craftingGroup != null && _craftingTitle != null && _craftingBody != null && _craftingUi != null;
    public bool IsVisible => (_group != null && _group.alpha > 0) || (_craftingGroup != null && _craftingGroup.alpha > 0);
    public void Show(TutorialPage page)
    {
        if (!HasReferences || page == null) { Debug.LogError("Tutorial requires its authored HUD and page.", this); return; }
        _title.text = page.Title;
        _body.text = page.Text;
        _craftingTitle.text = page.Title;
        _craftingBody.text = page.Text;
        _footer.text = string.Empty;
        _advance.SetActive(page.AdvanceWithE);
        for (int i = 0; i < _controlGroups.Length; i++) if (_controlGroups[i] != null) _controlGroups[i].SetActive(i == (int)page.Controls);
        _shown = true;
        ApplyVisibility();
    }
    public void SetProgress(string text) { if (_footer != null) _footer.text = text; }
    public void Hide() { _shown = false; ApplyVisibility(); }
    private void LateUpdate() => ApplyVisibility();
    private void ApplyVisibility()
    {
        bool crafting = _craftingUi != null && _craftingUi.IsVisible;
        SetVisible(_group, _shown && !crafting);
        SetVisible(_craftingGroup, _shown && crafting);
    }
    private static void SetVisible(CanvasGroup group, bool visible)
    {
        if (group == null) return;
        group.alpha = visible ? 1 : 0;
        group.blocksRaycasts = false;
        group.interactable = false;
    }
    private void Awake() => Hide();
}
