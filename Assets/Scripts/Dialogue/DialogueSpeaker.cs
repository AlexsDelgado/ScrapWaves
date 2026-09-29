using UnityEngine;

/// <summary>
/// Quién habla en el diálogo de guía (un jefe, p. ej. el Stalker). Hasta que una entrada lo revela en el run,
/// el panel muestra <see cref="HiddenName"/> y <see cref="HiddenPortrait"/>.
/// </summary>
[CreateAssetMenu(menuName = "ScrapWaves/Dialogue/Speaker", fileName = "Speaker_")]
public class DialogueSpeaker : ScriptableObject
{
    [SerializeField] private string _displayName = "Stalker";
    [Tooltip("Nombre mientras está oculto.")]
    [SerializeField] private string _hiddenName = "???";
    [SerializeField] private Sprite _hiddenPortrait;
    [SerializeField] private Sprite _revealedPortrait;
    [SerializeField] private Color _nameColor = new(0.85f, 0.75f, 0.19f, 1f);
    [Tooltip("Color del LED de señal mientras habla.")]
    [SerializeField] private Color _signalColor = new(1f, 0.2f, 0.15f, 1f);

    [Header("Voz (pitidos mientras escribe)")]
    [Tooltip("Se elige uno al azar en cada pitido. Vacío = mudo. Se generan con ArtSource/Tools/make_voice_blips.py.")]
    [SerializeField] private AudioClip[] _voiceBlips = System.Array.Empty<AudioClip>();
    [Tooltip("Suena un pitido cada tantas letras visibles (los espacios no cuentan).")]
    [SerializeField, Min(1)] private int _voiceEveryCharacters = 2;
    [SerializeField] private Vector2 _voicePitchRange = new(0.92f, 1.08f);
    [SerializeField, Range(0f, 1f)] private float _voiceVolume = 0.5f;

    public string DisplayName => _displayName;
    public string HiddenName => _hiddenName;
    public Sprite HiddenPortrait => _hiddenPortrait;
    public Sprite RevealedPortrait => _revealedPortrait;
    public Color NameColor => _nameColor;
    public Color SignalColor => _signalColor;
    public int VoiceEveryCharacters => _voiceEveryCharacters;
    public float VoiceVolume => _voiceVolume;
    public bool HasVoice => _voiceBlips != null && _voiceBlips.Length > 0;

    public AudioClip PickVoiceBlip() => HasVoice ? _voiceBlips[Random.Range(0, _voiceBlips.Length)] : null;

    public float PickVoicePitch() => Random.Range(_voicePitchRange.x, _voicePitchRange.y);

    public string NameFor(bool revealed) => revealed || string.IsNullOrEmpty(_hiddenName) ? _displayName : _hiddenName;

    public Sprite PortraitFor(bool revealed) =>
        revealed ? (_revealedPortrait != null ? _revealedPortrait : _hiddenPortrait)
                 : (_hiddenPortrait != null ? _hiddenPortrait : _revealedPortrait);
}
