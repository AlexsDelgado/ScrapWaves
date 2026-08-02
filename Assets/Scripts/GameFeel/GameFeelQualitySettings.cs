using UnityEngine;

[CreateAssetMenu(fileName = "GameFeelQuality", menuName = "ScrapWaves/Game Feel/Quality Settings")]
public sealed class GameFeelQualitySettings : ScriptableObject
{
    [Header("Secondary effect multipliers")]
    [SerializeField, Range(0f, 1f)] private float _lowParticleMultiplier = 0.35f;
    [SerializeField, Range(0f, 1f)] private float _mediumParticleMultiplier = 0.7f;
    [SerializeField, Range(0f, 1f)] private float _highParticleMultiplier = 1f;

    [Header("Optional layers")]
    [SerializeField] private bool _lowQualityLights;
    [SerializeField] private bool _mediumQualityLights = true;
    [SerializeField] private bool _highQualityLights = true;
    [SerializeField, Min(0f)] private float _lowDecalLifetimeMultiplier = 0.35f;
    [SerializeField, Min(0f)] private float _mediumDecalLifetimeMultiplier = 0.7f;
    [SerializeField, Min(0f)] private float _highDecalLifetimeMultiplier = 1f;

    public float GetParticleMultiplier(GameFeelQualityLevel quality)
    {
        return quality switch
        {
            GameFeelQualityLevel.Low => _lowParticleMultiplier,
            GameFeelQualityLevel.Medium => _mediumParticleMultiplier,
            _ => _highParticleMultiplier
        };
    }

    public float GetDecalLifetimeMultiplier(GameFeelQualityLevel quality)
    {
        return quality switch
        {
            GameFeelQualityLevel.Low => _lowDecalLifetimeMultiplier,
            GameFeelQualityLevel.Medium => _mediumDecalLifetimeMultiplier,
            _ => _highDecalLifetimeMultiplier
        };
    }

    public bool AllowLights(GameFeelQualityLevel quality)
    {
        return quality switch
        {
            GameFeelQualityLevel.Low => _lowQualityLights,
            GameFeelQualityLevel.Medium => _mediumQualityLights,
            _ => _highQualityLights
        };
    }

    private void OnValidate()
    {
        _lowParticleMultiplier = Mathf.Clamp01(_lowParticleMultiplier);
        _mediumParticleMultiplier = Mathf.Clamp(_mediumParticleMultiplier, _lowParticleMultiplier, 1f);
        _highParticleMultiplier = Mathf.Clamp(_highParticleMultiplier, _mediumParticleMultiplier, 1f);
        _lowDecalLifetimeMultiplier = Mathf.Max(0f, _lowDecalLifetimeMultiplier);
        _mediumDecalLifetimeMultiplier = Mathf.Max(0f, _mediumDecalLifetimeMultiplier);
        _highDecalLifetimeMultiplier = Mathf.Max(0f, _highDecalLifetimeMultiplier);
    }
}
