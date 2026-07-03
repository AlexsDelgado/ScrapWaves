using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class LevelUpStatFeedback : MonoBehaviour
{
    [SerializeField, Min(8f)] private float _fontSize = 22f;
    [SerializeField] private Color _textColor = new(0.4f, 1f, 0.55f, 1f);
    [SerializeField] private Vector2 _anchorPosition = new(0.78f, 0.55f);
    [SerializeField, Min(0.05f)] private float _messageDuration = 1.4f;
    [SerializeField, Min(0.02f)] private float _messageSpacing = 0.08f;
    [SerializeField, Min(20f)] private float _floatDistance = 36f;

    private Canvas _canvas;
    private RectTransform _container;
    private readonly Queue<List<StatUpgradeResult>> _pendingBatches = new();
    private bool _isShowing;

    public void Show(IReadOnlyList<StatUpgradeResult> upgrades)
    {
        if (upgrades == null || upgrades.Count == 0)
            return;

        _pendingBatches.Enqueue(new List<StatUpgradeResult>(upgrades));
        if (!_isShowing)
            StartCoroutine(ShowBatchesCoroutine());
    }

    private IEnumerator ShowBatchesCoroutine()
    {
        _isShowing = true;
        EnsureUiExists();

        while (_pendingBatches.Count > 0)
        {
            List<StatUpgradeResult> batch = _pendingBatches.Dequeue();
            yield return ShowBatchCoroutine(batch);
        }

        _isShowing = false;
    }

    private IEnumerator ShowBatchCoroutine(List<StatUpgradeResult> batch)
    {
        var active = new List<Coroutine>(batch.Count);

        for (int i = 0; i < batch.Count; i++)
        {
            StatUpgradeResult upgrade = batch[i];
            string label = $"++{StatDisplayNames.GetDisplayName(upgrade.StatType)}";
            active.Add(StartCoroutine(AnimateMessage(label, i)));
            yield return new WaitForSecondsRealtime(_messageSpacing);
        }

        for (int i = 0; i < active.Count; i++)
            yield return active[i];
    }

    private IEnumerator AnimateMessage(string text, int stackIndex)
    {
        var go = new GameObject("StatUpgradeMsg", typeof(RectTransform));
        go.transform.SetParent(_container, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = _anchorPosition;
        rt.anchorMax = _anchorPosition;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(360f, 32f);
        rt.anchoredPosition = new Vector2(0f, -stackIndex * 28f);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        TmpUiHelper.ApplyDefaultFont(tmp);
        tmp.fontSize = _fontSize;
        tmp.alignment = TextAlignmentOptions.Left;
        tmp.color = _textColor;
        tmp.text = text;

        float elapsed = 0f;
        Vector2 start = rt.anchoredPosition;
        Color startColor = _textColor;

        while (elapsed < _messageDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _messageDuration);
            rt.anchoredPosition = start + new Vector2(0f, _floatDistance * t);
            tmp.color = new Color(startColor.r, startColor.g, startColor.b, 1f - t);
            yield return null;
        }

        Destroy(go);
    }

    private void EnsureUiExists()
    {
        if (_canvas != null)
            return;

        var canvasGo = new GameObject("LevelUpStatFeedbackCanvas", typeof(RectTransform));
        _canvas = canvasGo.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 4900;

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        var containerGo = new GameObject("Container", typeof(RectTransform));
        containerGo.transform.SetParent(canvasGo.transform, false);
        _container = containerGo.GetComponent<RectTransform>();
        _container.anchorMin = Vector2.zero;
        _container.anchorMax = Vector2.one;
        _container.offsetMin = Vector2.zero;
        _container.offsetMax = Vector2.zero;
    }
}

public static class StatDisplayNames
{
    private static readonly Dictionary<StatType, string> Names = new()
    {
        { StatType.MovementSpeed, "Movement Speed" },
        { StatType.JumpHeight, "Jump Height" },
        { StatType.AirJumps, "Air Jumps" },
        { StatType.DashCharges, "Dash Charges" },
        { StatType.DashSpeed, "Dash Speed" },
        { StatType.DamageMultiplier, "Damage" },
        { StatType.DamageFlat, "Flat Damage" },
        { StatType.EliteDamageMultiplier, "Elite Damage" },
        { StatType.AttackSpeedMultiplier, "Attack Speed" },
        { StatType.ProjectileAreaSize, "Projectile Area" },
        { StatType.CriticalChance, "Critical Chance" },
        { StatType.CriticalDamage, "Critical Damage" },
        { StatType.Knockback, "Knockback" },
        { StatType.AmmoMultiplier, "Ammo" },
        { StatType.AbilityDamageMultiplier, "Ability Damage" },
        { StatType.AbilityCooldownReduction, "Ability Cooldown" },
        { StatType.MaxHealth, "Max Health" },
        { StatType.HealthRegeneration, "Health Regen" },
        { StatType.Lifesteal, "Lifesteal" },
        { StatType.DamageResistance, "Damage Resistance" },
        { StatType.PickupRange, "Pickup Range" },
        { StatType.ExtraEliteChance, "Elite Chance" },
        { StatType.Scavenging, "Scavenging" },
        { StatType.DoubleDrop, "Double Drop" },
        { StatType.BaseFireInterval, "Fire Interval" }
    };

    public static string GetDisplayName(StatType statType) =>
        Names.TryGetValue(statType, out string name) ? name : statType.ToString();
}
