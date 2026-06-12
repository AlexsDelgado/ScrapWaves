using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class OffscreenObjectiveIndicators : MonoBehaviour
{
    private const int PoolSize = 12;
    private const float EdgeMargin = 48f;

    [SerializeField] private OverheatManager _overheatManager;
    [SerializeField] private BossManager _bossManager;
    [SerializeField] private OverheatEliteWaveSpawner _eliteSpawner;
    [SerializeField] private Camera _worldCamera;

    private readonly List<ArrowUi> _pool = new(PoolSize);
    private readonly List<TargetEntry> _targets = new(PoolSize);
    private RectTransform _containerRt;

    private struct ArrowUi
    {
        public GameObject Root;
        public RectTransform Rt;
        public Image Icon;
    }

    private struct TargetEntry
    {
        public Transform Transform;
        public bool IsBoss;
    }

    private void Awake()
    {
        ResolveRefs();
        BuildUi();
    }

    private void OnEnable()
    {
        ResolveRefs();
        if (_overheatManager != null)
        {
            _overheatManager.OnOverheatStarted += OnOverheatChanged;
            _overheatManager.OnOverheatFinished += OnOverheatFinished;
        }
    }

    private void OnDisable()
    {
        if (_overheatManager != null)
        {
            _overheatManager.OnOverheatStarted -= OnOverheatChanged;
            _overheatManager.OnOverheatFinished -= OnOverheatFinished;
        }

        HideAll();
    }

    private void OnOverheatChanged() { }

    private void OnOverheatFinished(OverheatEndReason _) => HideAll();

    private void ResolveRefs()
    {
        if (_overheatManager == null)
            _overheatManager = FindAnyObjectByType<OverheatManager>();
        if (_bossManager == null)
            _bossManager = FindAnyObjectByType<BossManager>();
        if (_eliteSpawner == null)
            _eliteSpawner = FindAnyObjectByType<OverheatEliteWaveSpawner>();
        if (_worldCamera == null)
            _worldCamera = Camera.main;
    }

    private void BuildUi()
    {
        var container = new GameObject("OffscreenIndicators", typeof(RectTransform));
        container.transform.SetParent(transform, false);
        _containerRt = container.GetComponent<RectTransform>();
        _containerRt.anchorMin = Vector2.zero;
        _containerRt.anchorMax = Vector2.one;
        _containerRt.offsetMin = Vector2.zero;
        _containerRt.offsetMax = Vector2.zero;

        for (int i = 0; i < PoolSize; i++)
        {
            var go = new GameObject($"Arrow_{i}", typeof(RectTransform));
            go.transform.SetParent(container.transform, false);
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(28f, 28f);

            var img = go.AddComponent<Image>();
            img.sprite = HudUiFactory.WhiteSprite;
            img.color = new Color(1f, 0.45f, 0.2f, 0.95f);
            img.raycastTarget = false;

            go.SetActive(false);
            _pool.Add(new ArrowUi { Root = go, Rt = rt, Icon = img });
        }
    }

    private void LateUpdate()
    {
        if (_overheatManager == null || !_overheatManager.IsOverheating)
        {
            HideAll();
            return;
        }

        if (_worldCamera == null)
            _worldCamera = Camera.main;
        if (_worldCamera == null)
            return;

        CollectTargets();
        UpdateArrows();
    }

    private void CollectTargets()
    {
        _targets.Clear();

        if (_bossManager != null)
        {
            IReadOnlyList<EnemyHealth> bosses = _bossManager.ActiveBosses;
            for (int i = 0; i < bosses.Count; i++)
            {
                EnemyHealth h = bosses[i];
                if (h == null || h.CurrentHealth <= 0)
                    continue;
                _targets.Add(new TargetEntry { Transform = h.transform, IsBoss = true });
            }
        }

        if (_eliteSpawner != null)
        {
            IReadOnlyList<Transform> elites = _eliteSpawner.GetAliveEliteTransforms();
            for (int i = 0; i < elites.Count; i++)
            {
                Transform t = elites[i];
                if (t == null)
                    continue;
                _targets.Add(new TargetEntry { Transform = t, IsBoss = false });
            }
        }
    }

    private void UpdateArrows()
    {
        int shown = 0;
        Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        float halfW = Screen.width * 0.5f - EdgeMargin;
        float halfH = Screen.height * 0.5f - EdgeMargin;

        for (int i = 0; i < _targets.Count && shown < _pool.Count; i++)
        {
            TargetEntry entry = _targets[i];
            if (entry.Transform == null)
                continue;

            Vector3 world = entry.Transform.position + Vector3.up * 1.5f;
            Vector3 screen3 = _worldCamera.WorldToScreenPoint(world);
            if (screen3.z <= 0f)
                continue;

            Vector2 screen = new Vector2(screen3.x, screen3.y);
            bool onScreen = screen.x >= EdgeMargin && screen.x <= Screen.width - EdgeMargin
                && screen.y >= EdgeMargin && screen.y <= Screen.height - EdgeMargin;
            if (onScreen)
                continue;

            Vector2 dir = screen - screenCenter;
            if (dir.sqrMagnitude < 0.001f)
                dir = Vector2.up;
            dir.Normalize();

            float scale = Mathf.Min(
                halfW / Mathf.Max(0.0001f, Mathf.Abs(dir.x)),
                halfH / Mathf.Max(0.0001f, Mathf.Abs(dir.y)));
            Vector2 edgeScreen = screenCenter + dir * scale;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                _containerRt, edgeScreen, null, out Vector2 edgeLocal);

            ArrowUi arrow = _pool[shown];
            arrow.Root.SetActive(true);
            arrow.Rt.anchoredPosition = edgeLocal;
            float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
            arrow.Rt.localRotation = Quaternion.Euler(0f, 0f, angle);
            arrow.Icon.color = entry.IsBoss
                ? new Color(0.95f, 0.2f, 0.18f, 0.95f)
                : new Color(1f, 0.55f, 0.15f, 0.95f);
            shown++;
        }

        for (int i = shown; i < _pool.Count; i++)
            _pool[i].Root.SetActive(false);
    }

    private void HideAll()
    {
        for (int i = 0; i < _pool.Count; i++)
            _pool[i].Root.SetActive(false);
    }
}
