using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>Binds choices to an existing, editor-authored menu hierarchy.</summary>
[DisallowMultipleComponent]
public sealed class ChoiceMenuView : MonoBehaviour
{
    public Canvas Canvas;
    public TMP_Text TitleText;
    public TMP_Text SubtitleText;
    public TMP_Text FooterText;
    public ChoiceCardView[] Cards;

    public bool CanPresent(int count)
    {
        if (Canvas == null || TitleText == null || SubtitleText == null || FooterText == null
            || Cards == null || count <= 0 || count > Cards.Length)
            return false;

        for (int i = 0; i < count; i++)
        {
            if (Cards[i] == null || !Cards[i].IsConfigured)
                return false;
        }
        return true;
    }

    public void Show(string title, string subtitle, string footer,
        IReadOnlyList<LevelUpChoiceOption> options, RunMenuContent content, Action<int> onSelected)
    {
        if (title != null) TitleText.text = title;
        if (subtitle != null) SubtitleText.text = subtitle;
        if (footer != null) FooterText.text = footer;
        for (int i = 0; i < Cards.Length; i++)
        {
            ChoiceCardView card = Cards[i];
            if (card == null)
                continue;

            bool used = i < options.Count;
            card.gameObject.SetActive(used);
            if (!used)
            {
                card.Clear();
                continue;
            }

            LevelUpChoiceOption option = options[i];
            WeaponMenuCopy copy = option.Weapon != null && content != null ? content.Find(option.Weapon) : null;
            string description = copy != null ? copy.Description : option.Description;
            int index = i;
            card.Bind(option.DisplayName, copy?.Summary, description, option.Icon, () => onSelected?.Invoke(index));
        }
        Canvas.gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (Cards != null)
        {
            foreach (ChoiceCardView card in Cards)
                card?.Clear();
        }
        if (Canvas != null)
            Canvas.gameObject.SetActive(false);
    }
}
