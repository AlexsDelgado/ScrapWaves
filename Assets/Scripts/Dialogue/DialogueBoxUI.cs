using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Panel de diálogo de jefes (arte en Assets/Art/UI/Dialogue, armado por DialogueUiBuilder). Escribe cada
/// página letra por letra, la deja unos segundos y se cierra solo; no pausa el juego y se congela con él.
/// </summary>
[DisallowMultipleComponent]
public class DialogueBoxUI : MonoBehaviour
{
    [SerializeField] private CanvasGroup _group;
    [SerializeField] private RectTransform _panel;
    [SerializeField] private Image _portrait;
    [SerializeField] private TextMeshProUGUI _speakerName;
    [SerializeField] private TextMeshProUGUI _text;
    [SerializeField] private Image _signal;
    [Tooltip("Fuente 2D para los pitidos de voz. Vacío = se crea una al iniciar.")]
    [SerializeField] private AudioSource _voiceSource;

    [Header("Ritmo")]
    [SerializeField, Min(1f)] private float _charactersPerSecond = 45f;
    [SerializeField, Min(0.01f)] private float _fadeSeconds = 0.18f;
    [SerializeField, Min(0f)] private float _slideDistance = 24f;
    [Tooltip("Espera base al terminar una página, más un extra por carácter.")]
    [SerializeField, Min(0f)] private float _holdBase = 1.6f;
    [SerializeField, Min(0f)] private float _holdPerCharacter = 0.035f;
    [SerializeField] private Vector2 _holdClamp = new(2.4f, 7f);
    [SerializeField, Min(0.05f)] private float _signalBlinkSeconds = 0.14f;

    private Vector2 _restPosition;
    private Coroutine _routine;

    public bool IsShowing => _routine != null;

    private void Awake()
    {
        if (_panel != null)
            _restPosition = _panel.anchoredPosition;
        if (_voiceSource == null)
        {
            _voiceSource = gameObject.AddComponent<AudioSource>();
            _voiceSource.playOnAwake = false;
            _voiceSource.spatialBlend = 0f;
        }
        HideImmediate();
    }

    private void OnDisable()
    {
        _routine = null;
        HideImmediate();
    }

    /// <summary>Muestra la entrada completa y llama a <paramref name="onFinished"/> al cerrarse.</summary>
    public void Play(DialogueEntry entry, bool revealed, Action onFinished)
    {
        if (_routine != null)
            StopCoroutine(_routine);
        _routine = StartCoroutine(PlayRoutine(entry, revealed, onFinished));
    }

    public void HideImmediate()
    {
        if (_group != null)
        {
            _group.alpha = 0f;
            _group.blocksRaycasts = false;
            _group.interactable = false;
        }
        if (_panel != null)
            _panel.anchoredPosition = _restPosition;
        SetSignal(false, Color.white);
    }

    // El diálogo corre en tiempo de juego: se frena con la pausa, los menús y el hit-stop.
    private static float Dt() => GameplayPause.IsUiPaused ? 0f : Time.deltaTime;

    private IEnumerator PlayRoutine(DialogueEntry entry, bool revealed, Action onFinished)
    {
        DialogueSpeaker speaker = entry.Speaker;
        if (_portrait != null)
        {
            _portrait.sprite = speaker != null ? speaker.PortraitFor(revealed) : null;
            _portrait.enabled = _portrait.sprite != null;
        }
        if (_speakerName != null)
        {
            _speakerName.text = speaker != null ? speaker.NameFor(revealed) : string.Empty;
            _speakerName.color = speaker != null ? speaker.NameColor : Color.white;
        }
        Color signalColor = speaker != null ? speaker.SignalColor : Color.white;
        _text.text = string.Empty;

        yield return Fade(0f, 1f);
        foreach (string page in entry.Pages)
        {
            if (string.IsNullOrWhiteSpace(page))
                continue;
            yield return Type(page, signalColor, speaker);
            float hold = entry.HoldSeconds > 0f
                ? entry.HoldSeconds
                : Mathf.Clamp(_holdBase + page.Length * _holdPerCharacter, _holdClamp.x, _holdClamp.y);
            for (float t = 0f; t < hold; t += Dt())
                yield return null;
        }
        yield return Fade(1f, 0f);

        HideImmediate();
        _routine = null;
        onFinished?.Invoke();
    }

    private IEnumerator Type(string page, Color signalColor, DialogueSpeaker speaker)
    {
        _text.text = page;
        _text.maxVisibleCharacters = 0;
        _text.ForceMeshUpdate();
        int total = _text.textInfo.characterCount;
        float shown = 0f, blink = 0f;
        int spoken = 0;
        int every = speaker != null ? Mathf.Max(1, speaker.VoiceEveryCharacters) : 1;
        while (_text.maxVisibleCharacters < total)
        {
            float dt = Dt();
            shown += dt * _charactersPerSecond;
            int before = _text.maxVisibleCharacters;
            _text.maxVisibleCharacters = Mathf.Min(total, Mathf.FloorToInt(shown));
            // Un pitido por frame como mucho: con framerate bajo se revelan varias letras juntas.
            bool speak = false;
            for (int i = before; i < _text.maxVisibleCharacters; i++)
                if (!char.IsWhiteSpace(_text.textInfo.characterInfo[i].character) && spoken++ % every == 0)
                    speak = true;
            if (speak)
                Speak(speaker);
            blink += dt;
            SetSignal(Mathf.FloorToInt(blink / _signalBlinkSeconds) % 2 == 0, signalColor);
            yield return null;
        }
        SetSignal(true, signalColor * 0.6f);
    }

    private void Speak(DialogueSpeaker speaker)
    {
        if (speaker == null || !speaker.HasVoice || _voiceSource == null)
            return;
        _voiceSource.pitch = speaker.PickVoicePitch();
        _voiceSource.PlayOneShot(speaker.PickVoiceBlip(), speaker.VoiceVolume * AudioManager.EffectiveSfxVolume);
    }

    private IEnumerator Fade(float from, float to)
    {
        for (float t = 0f; t < _fadeSeconds; t += Dt())
        {
            Apply(Mathf.SmoothStep(from, to, t / _fadeSeconds));
            yield return null;
        }
        Apply(to);
    }

    private void Apply(float visibility)
    {
        if (_group != null)
            _group.alpha = visibility;
        if (_panel != null)
            _panel.anchoredPosition = _restPosition + Vector2.up * (_slideDistance * (1f - visibility));
    }

    private void SetSignal(bool on, Color color)
    {
        if (_signal == null)
            return;
        color.a = on ? 1f : 0.15f;
        _signal.color = color;
    }

#if UNITY_EDITOR
    /// <summary>Lo llama DialogueUiBuilder al armar el panel.</summary>
    public void Wire(CanvasGroup group, RectTransform panel, Image portrait, TextMeshProUGUI speakerName,
        TextMeshProUGUI text, Image signal)
    {
        _group = group;
        _panel = panel;
        _portrait = portrait;
        _speakerName = speakerName;
        _text = text;
        _signal = signal;
    }
#endif
}
