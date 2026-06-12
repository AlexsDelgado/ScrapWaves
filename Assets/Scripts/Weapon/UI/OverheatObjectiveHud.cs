using TMPro;
using UnityEngine;

[DisallowMultipleComponent]
public class OverheatObjectiveHud : MonoBehaviour
{
    [SerializeField] private OverheatManager _overheatManager;
    [SerializeField] private BossManager _bossManager;
    [SerializeField] private OverheatEliteWaveSpawner _eliteSpawner;

    private TextMeshProUGUI _objectiveText;
    private GameObject _root;

    private void Awake()
    {
        ResolveRefs();
        if (!TryWireFromHierarchy())
            BuildUi();
        Refresh();
    }

    private void OnEnable()
    {
        ResolveRefs();
        Subscribe(true);
        Refresh();
    }

    private void OnDisable() => Subscribe(false);

    private void ResolveRefs()
    {
        if (_overheatManager == null)
            _overheatManager = FindAnyObjectByType<OverheatManager>();
        if (_bossManager == null)
            _bossManager = FindAnyObjectByType<BossManager>();
        if (_eliteSpawner == null)
            _eliteSpawner = FindAnyObjectByType<OverheatEliteWaveSpawner>();
    }

    private void Subscribe(bool subscribe)
    {
        if (_overheatManager != null)
        {
            if (subscribe)
            {
                _overheatManager.OnOverheatStarted += Refresh;
                _overheatManager.OnOverheatFinished += OnOverheatFinished;
            }
            else
            {
                _overheatManager.OnOverheatStarted -= Refresh;
                _overheatManager.OnOverheatFinished -= OnOverheatFinished;
            }
        }

        if (_bossManager != null)
        {
            if (subscribe)
                _bossManager.OnActiveBossesChanged += Refresh;
            else
                _bossManager.OnActiveBossesChanged -= Refresh;
        }

        if (_eliteSpawner != null)
        {
            if (subscribe)
                _eliteSpawner.OnEliteWaveProgressChanged += Refresh;
            else
                _eliteSpawner.OnEliteWaveProgressChanged -= Refresh;
        }
    }

    private void OnOverheatFinished(OverheatEndReason _) => Refresh();

    private bool TryWireFromHierarchy()
    {
        Transform objective = transform.Find("OverheatObjective");
        if (objective == null)
            return false;

        _root = objective.gameObject;
        _objectiveText = HudUiWire.FindTmp(objective, "Text");
        return _objectiveText != null;
    }

    private void BuildUi()
    {
        _root = new GameObject("OverheatObjective", typeof(RectTransform));
        _root.transform.SetParent(transform, false);
        var rootRt = _root.GetComponent<RectTransform>();
        rootRt.anchorMin = new Vector2(0.5f, 1f);
        rootRt.anchorMax = new Vector2(0.5f, 1f);
        rootRt.pivot = new Vector2(0.5f, 1f);
        rootRt.anchoredPosition = new Vector2(0f, -108f);
        rootRt.sizeDelta = new Vector2(720f, 48f);

        _objectiveText = HudUiFactory.CreateLabel(_root.transform, "Text", string.Empty, 28f, TextAlignmentOptions.Center);
        _objectiveText.fontStyle = FontStyles.Bold;
        _objectiveText.color = new Color(1f, 0.55f, 0.2f, 1f);
    }

    private void Refresh()
    {
        if (_objectiveText == null)
            return;

        bool show = _overheatManager != null && _overheatManager.IsOverheating;
        _root.SetActive(show);
        if (!show)
            return;

        if (_bossManager != null && _bossManager.HasActiveBosses)
        {
            int alive = _bossManager.ActiveBosses.Count;
            _objectiveText.text = alive > 1
                ? $"Bosses: {alive}"
                : "Derrotá al boss";
            return;
        }

        if (_eliteSpawner != null && _eliteSpawner.IsEliteWaveActive)
        {
            int defeated = Mathf.Max(0, _eliteSpawner.EliteWaveTotal - _eliteSpawner.ElitesRemaining);
            _objectiveText.text = $"Élites: {defeated}/{_eliteSpawner.EliteWaveTotal}";
            return;
        }

        _objectiveText.text = "Completá el objetivo";
    }
}
