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

    [SerializeField] private Canvas _canvas;
    [SerializeField] private RectTransform _container;
    [SerializeField] private TextMeshProUGUI[] _messageSlots;
    private Vector2[] _restPositions;
    private Color[] _restColors;
    private readonly Queue<List<StatUpgradeResult>> _pendingBatches = new();
    private bool _isShowing;

    private void Awake() => CacheMessageRestState();

    private void CacheMessageRestState()
    {
        if (_messageSlots == null) return;
        if (_restPositions != null && _restPositions.Length == _messageSlots.Length) return;
        _restPositions = new Vector2[_messageSlots.Length];
        _restColors = new Color[_messageSlots.Length];
        for (int i = 0; i < _messageSlots.Length; i++)
        {
            TextMeshProUGUI message = _messageSlots[i];
            if (message == null) continue;
            _restPositions[i] = message.rectTransform.anchoredPosition;
            _restColors[i] = message.color;
            message.gameObject.SetActive(false);
        }
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        _pendingBatches.Clear();
        _isShowing = false;
        if (_messageSlots == null || _restPositions == null) return;
        for (int i = 0; i < _messageSlots.Length; i++) ResetMessage(i);
    }

    public void Show(IReadOnlyList<StatUpgradeResult> upgrades)
    {
        if (upgrades == null || upgrades.Count == 0 || _canvas == null
            || _messageSlots == null || _messageSlots.Length == 0)
            return;

        CacheMessageRestState();

        _pendingBatches.Enqueue(new List<StatUpgradeResult>(upgrades));
        if (!_isShowing)
            StartCoroutine(ShowBatchesCoroutine());
    }

    private IEnumerator ShowBatchesCoroutine()
    {
        _isShowing = true;

        while (_pendingBatches.Count > 0)
        {
            List<StatUpgradeResult> batch = _pendingBatches.Dequeue();
            yield return ShowBatchCoroutine(batch);
        }

        _isShowing = false;
    }

    private IEnumerator ShowBatchCoroutine(List<StatUpgradeResult> batch)
    {
        for (int start = 0; start < batch.Count; start += _messageSlots.Length)
        {
            var active = new List<Coroutine>(_messageSlots.Length);
            int count = Mathf.Min(_messageSlots.Length, batch.Count - start);
            for (int i = 0; i < count; i++)
            {
                if (_messageSlots[i] == null) continue;
                StatUpgradeResult upgrade = batch[start + i];
                string label = $"++{StatDisplayNames.GetDisplayName(upgrade.StatType)}";
                active.Add(StartCoroutine(AnimateMessage(label, i)));
                yield return new WaitForSecondsRealtime(_messageSpacing);
            }

            for (int i = 0; i < active.Count; i++) yield return active[i];
        }
    }

    private IEnumerator AnimateMessage(string text, int stackIndex)
    {
        TextMeshProUGUI tmp = _messageSlots[stackIndex];
        RectTransform rt = tmp.rectTransform;
        tmp.gameObject.SetActive(true);
        tmp.text = text;

        float elapsed = 0f;
        Vector2 start = _restPositions[stackIndex];
        Color startColor = _restColors[stackIndex];

        while (elapsed < _messageDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / _messageDuration);
            rt.anchoredPosition = start + new Vector2(0f, _floatDistance * t);
            tmp.color = new Color(startColor.r, startColor.g, startColor.b, startColor.a * (1f - t));
            yield return null;
        }

        ResetMessage(stackIndex);
    }

    private void ResetMessage(int index)
    {
        TextMeshProUGUI message = _messageSlots[index];
        if (message == null) return;
        message.rectTransform.anchoredPosition = _restPositions[index];
        message.color = _restColors[index];
        message.gameObject.SetActive(false);
    }

#if UNITY_EDITOR
    public void AuthorUi(Transform uiRoot)
    {
        if (_canvas != null)
        {
            if (uiRoot != null && !_canvas.transform.IsChildOf(uiRoot))
                _canvas.transform.SetParent(uiRoot, false);
            return;
        }

        var canvasGo = new GameObject("LevelUpStatFeedbackCanvas", typeof(RectTransform));
        canvasGo.transform.SetParent(uiRoot, false);
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

        // The level-36 upgrade batch contains at most twenty messages.
        _messageSlots = new TextMeshProUGUI[20];
        for (int i = 0; i < _messageSlots.Length; i++)
        {
            var go = new GameObject($"StatUpgradeMsg_{i + 1}", typeof(RectTransform));
            go.transform.SetParent(_container, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = _anchorPosition;
            rt.anchorMax = _anchorPosition;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(360f, 32f);
            rt.anchoredPosition = new Vector2(0f, -i * 28f);
            var tmp = go.AddComponent<TextMeshProUGUI>();
            TmpUiHelper.ApplyDefaultFont(tmp);
            tmp.fontSize = _fontSize;
            tmp.alignment = TextAlignmentOptions.Left;
            tmp.color = _textColor;
            tmp.raycastTarget = false;
            tmp.text = "++Stat";
            _messageSlots[i] = tmp;
            go.SetActive(false);
        }
        UnityEditor.EditorUtility.SetDirty(this);
    }
#endif
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
        { StatType.BaseFireInterval, "Fire Interval" },
        { StatType.LongRangeDamageMultiplier, "Long Range Damage" },
        { StatType.CloseRangeDamageMultiplier, "Close Range Damage" },
        { StatType.ShieldCharges, "Shield Charges" },
        { StatType.ShieldRechargeDelay, "Shield Recharge Delay" },
        { StatType.HealthRegenerationDelayReduction, "Regen Delay Reduction" }
    };

    public static string GetDisplayName(StatType statType) =>
        Names.TryGetValue(statType, out string name) ? name : statType.ToString();
}
