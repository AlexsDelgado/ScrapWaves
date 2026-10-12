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
    public bool HasReferences => _group != null && _title != null && _body != null && _footer != null && _advance != null && _controlGroups != null && _controlGroups.Length == 5;
    public bool IsVisible => _group != null && _group.alpha > 0;
    public void Show(TutorialPage page)
    {
        if (!HasReferences || page == null) { Debug.LogError("Tutorial requires its authored HUD and page.", this); return; }
        _title.text = page.Title;
        _body.text = page.Text;
        _footer.text = string.Empty;
        _advance.SetActive(page.AdvanceWithE);
        for (int i = 0; i < _controlGroups.Length; i++) if (_controlGroups[i] != null) _controlGroups[i].SetActive(i == (int)page.Controls);
        _group.alpha = 1;
        _group.interactable = false;
        _group.blocksRaycasts = false;
    }
    public void SetProgress(string text) { if (_footer != null) _footer.text = text; }
    public void Hide() { if (_group != null) { _group.alpha = 0; _group.blocksRaycasts = false; _group.interactable = false; } }
    private void Awake() => Hide();
}
