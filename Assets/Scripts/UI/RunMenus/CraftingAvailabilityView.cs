using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Separate authored overlay: never changes weapon selection or creates UI.</summary>
public sealed class CraftingAvailabilityView : MonoBehaviour
{
    public Image Glow;
    public TMP_Text Marker;
    public Color GlowColor = new(0.35f, 0.9f, 0.48f, 0.045f);
    public Image[] OutlineEdges;
    public Color OutlineColor = new(0.35f, 0.9f, 0.48f, 0.65f);
    [Min(0f)] public float PulseSpeed = 2.4f;
    [Range(0f, 1f)] public float PulseDepth = 0.72f;
    private bool _available;
    private void Update()
    {
        if (_available) RefreshPulse(Time.unscaledTime);
    }
    // Explicit timestamp also lets the editor preview sample the actual pulse deterministically.
    public void RefreshPulse(float unscaledTime)
    {
        float opacity = 1f - PulseDepth * (0.5f + 0.5f * Mathf.Sin(unscaledTime * PulseSpeed));
        Color color = GlowColor;
        color.a *= opacity;
        Glow.color = color;
        Color outline = OutlineColor;
        outline.a *= opacity;
        if (OutlineEdges != null)
            foreach (Image edge in OutlineEdges)
                if (edge != null) edge.color = outline;
    }
    public void Bind(CraftingActionKind? action)
    {
        bool available = _available = action.HasValue;
        Glow.gameObject.SetActive(available);
        Marker.gameObject.SetActive(available);
        if (!available) return;
        RefreshPulse(Time.unscaledTime);
        Marker.text = action == CraftingActionKind.TinkerNewWeapon ? "+" : "↑";
    }
}
