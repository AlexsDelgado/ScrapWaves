using UnityEngine;

[DisallowMultipleComponent]
public sealed class WeaponHeatOverride : MonoBehaviour
{
    [SerializeField] private HeatManager _heatManager;

    public float NormalizedHeat => _heatManager != null ? _heatManager.NormalizedHeat : 0f;
    public float CurrentHeat => _heatManager != null ? _heatManager.CurrentHeat : 0f;

    public void Bind(HeatManager heatManager)
    {
        _heatManager = heatManager;
    }

    public void SetHeatPercent(float percent)
    {
        if (_heatManager == null)
            return;

        float normalized = Mathf.Clamp01(percent / 100f);
        float heatPoints = normalized <= 0.8f
            ? _heatManager.PointsFirstSegment * (normalized / 0.8f)
            : _heatManager.PointsFirstSegment +
              _heatManager.PointsSecondSegment * ((normalized - 0.8f) / 0.2f);
        _heatManager.SetHeat(heatPoints);
    }

    public void SetNormalizedHeat(float normalized)
    {
        SetHeatPercent(Mathf.Clamp01(normalized) * 100f);
    }
}
