using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class BossHealthBarHud : MonoBehaviour
{
    [SerializeField] private BossManager _bossManager;

    private GameObject _root;
    private Image _fill;
    private TextMeshProUGUI _nameText;
    private EnemyHealth _trackedBoss;

    private void Awake()
    {
        if (_bossManager == null)
            _bossManager = FindAnyObjectByType<BossManager>();
        BuildUi();
        RefreshTarget();
    }

    private void OnEnable()
    {
        if (_bossManager == null)
            _bossManager = FindAnyObjectByType<BossManager>();
        if (_bossManager != null)
            _bossManager.OnActiveBossesChanged += RefreshTarget;
        RefreshTarget();
    }

    private void OnDisable()
    {
        if (_bossManager != null)
            _bossManager.OnActiveBossesChanged -= RefreshTarget;
        UnsubscribeBoss();
    }

    private void Update()
    {
        if (_trackedBoss == null || _trackedBoss.CurrentHealth <= 0)
        {
            RefreshTarget();
            return;
        }

        if (_fill != null && _trackedBoss.MaxHealth > 0)
            _fill.fillAmount = (float)_trackedBoss.CurrentHealth / _trackedBoss.MaxHealth;
    }

    private void BuildUi()
    {
        _root = new GameObject("BossHealthBar", typeof(RectTransform));
        _root.transform.SetParent(transform, false);
        var rootRt = _root.GetComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0.5f, 1f);
        rootRt.anchorMax = new Vector2(0.5f, 1f);
        rootRt.pivot = new Vector2(0.5f, 1f);
        rootRt.anchoredPosition = new Vector2(0f, -24f);
        rootRt.sizeDelta = new Vector2(1152f, 56f);

        _nameText = HudUiFactory.CreateLabel(_root.transform, "BossName", "BOSS", 18f, TextAlignmentOptions.Center);
        var nameRt = _nameText.GetComponent<RectTransform>();
        nameRt.anchorMin = new Vector2(0f, 1f);
        nameRt.anchorMax = new Vector2(1f, 1f);
        nameRt.pivot = new Vector2(0.5f, 1f);
        nameRt.anchoredPosition = Vector2.zero;
        nameRt.sizeDelta = new Vector2(0f, 22f);
        _nameText.fontStyle = FontStyles.Bold;
        _nameText.color = new Color(1f, 0.4f, 0.35f, 1f);

        var barRoot = new GameObject("Bar", typeof(RectTransform));
        barRoot.transform.SetParent(_root.transform, false);
        var barRt = barRoot.GetComponent<RectTransform>();
        barRt.anchorMin = new Vector2(0.5f, 0f);
        barRt.anchorMax = new Vector2(0.5f, 0f);
        barRt.pivot = new Vector2(0.5f, 0f);
        barRt.anchoredPosition = new Vector2(0f, 4f);
        barRt.sizeDelta = new Vector2(1152f, 28f);

        (_, _fill) = HudUiFactory.CreateHorizontalBar(barRoot.transform, "HealthFill", new Vector2(1152f, 28f), new Color(0.85f, 0.2f, 0.18f, 1f));
        _root.SetActive(false);
    }

    private void RefreshTarget()
    {
        UnsubscribeBoss();
        _trackedBoss = _bossManager != null ? _bossManager.PrimaryBoss : null;

        bool show = _trackedBoss != null && _trackedBoss.CurrentHealth > 0;
        _root.SetActive(show);
        if (!show)
            return;

        if (_nameText != null)
            _nameText.text = _trackedBoss.gameObject.name.Replace("(Clone)", string.Empty).Trim();

        if (_fill != null && _trackedBoss.MaxHealth > 0)
            _fill.fillAmount = (float)_trackedBoss.CurrentHealth / _trackedBoss.MaxHealth;
    }

    private void UnsubscribeBoss()
    {
        _trackedBoss = null;
    }
}
