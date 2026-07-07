using UnityEngine;

/// <summary>
/// HUD ligero para contadores de pool/spawn (QA / baseline profiling).
/// </summary>
[DisallowMultipleComponent]
public class EnemyPoolProfilerHud : MonoBehaviour
{
    [SerializeField] private bool _visible = true;
    [SerializeField] private float _refreshInterval = 0.5f;

    private float _nextRefresh;

    private void Update()
    {
        if (!_visible)
            return;

        if (Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + _refreshInterval;
            EnemyPoolProfiler.RefreshInactiveEnemyCount();
        }
    }

    private void OnGUI()
    {
        if (!_visible || QaPanels.Active != QaPanelKind.Qa)
            return;

        const float w = 280f;
        Rect rect = new Rect(Screen.width - w - 10f, 10f, w, 120f);
        GUILayout.BeginArea(rect, GUI.skin.box);
        GUILayout.Label("<b>Enemy Pool Profiler</b>", RichLabel());
        GUILayout.Label($"Registry active: {EnemyPoolRegistry.Instance?.TotalLeased ?? 0}");
        GUILayout.Label($"EnemyRegistry: {EnemyPoolProfiler.RegistryActiveCount}");
        GUILayout.Label($"Inactive in scene: {EnemyPoolProfiler.InactiveEnemyObjects}");
        GUILayout.Label($"Pool get/release: {EnemyPoolProfiler.PoolGetCount}/{EnemyPoolProfiler.PoolReleaseCount}");
        GUILayout.Label($"Instantiate/Destroy: {EnemyPoolProfiler.InstantiateCount}/{EnemyPoolProfiler.DestroyCount}");
        GUILayout.EndArea();
    }

    private static GUIStyle s_RichLabel;

    private static GUIStyle RichLabel()
    {
        if (s_RichLabel == null)
            s_RichLabel = new GUIStyle(GUI.skin.label) { richText = true };
        return s_RichLabel;
    }
}
