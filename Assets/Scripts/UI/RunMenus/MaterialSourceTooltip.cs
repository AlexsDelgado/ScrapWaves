using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Authored hover tooltip for the crafting table: tells which enemies drop each material.
/// The panel and texts are authored in the prefab (see MaterialSourceTooltipAuthoring);
/// runtime only picks the text and positions the panel. Mouse only for now.
/// </summary>
public sealed class MaterialSourceTooltip : MonoBehaviour
{
    [Serializable]
    public struct Source
    {
        public MaterialType Type;
        [TextArea(1, 3)] public string Text;
    }

    public RectTransform Panel;
    public TMP_Text Label;
    public Source[] Sources = Array.Empty<Source>();
    [Min(0f)] public float VerticalGap = 8f;

    private void OnDisable() => Hide();

    public bool TryGetText(MaterialType type, out string text)
    {
        if (Sources != null)
        {
            for (int i = 0; i < Sources.Length; i++)
            {
                if (Sources[i].Type != type || string.IsNullOrEmpty(Sources[i].Text))
                    continue;
                text = Sources[i].Text;
                return true;
            }
        }

        text = null;
        return false;
    }

    public void Show(MaterialType type, RectTransform anchor)
    {
        if (Panel == null || Label == null || anchor == null || !TryGetText(type, out string text))
        {
            Hide();
            return;
        }

        Label.text = text;
        Panel.gameObject.SetActive(true);
        Panel.SetAsLastSibling();

        // Hang the panel under the hovered row, left-aligned with it.
        var corners = new Vector3[4];
        anchor.GetWorldCorners(corners);
        Panel.pivot = new Vector2(0f, 1f);
        Vector3 bottomLeft = corners[0];
        float gap = VerticalGap * (Panel.lossyScale.y > 0f ? Panel.lossyScale.y : 1f);
        Panel.position = new Vector3(bottomLeft.x, bottomLeft.y - gap, bottomLeft.z);
    }

    public void Hide()
    {
        if (Panel != null && Panel.gameObject.activeSelf)
            Panel.gameObject.SetActive(false);
    }
}
