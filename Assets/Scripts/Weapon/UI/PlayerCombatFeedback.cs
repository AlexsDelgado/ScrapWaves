using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class PlayerCombatFeedback : MonoBehaviour
{
    public static event Action<PlayerCombatFeedback> BecameAvailable;

    [SerializeField] private PlayerHealth _playerHealth;
    [SerializeField] private PlayerMovement _playerMovement;

    [Header("Authored UI")]
    [SerializeField] private Image _damageFlash;
    [SerializeField] private Image _invulnOverlay;
    [SerializeField] private Image _stunOverlay;
    [SerializeField] private Image _burnIcon;
    [SerializeField] private TextMeshProUGUI _stunLabel;
    private Color _damageTint = new(0.9f, 0.1f, 0.08f);
    private Color _invulnTint = new(1f, 0.4f, 0.4f);
    private Color _stunTint = new(1f, 0.9f, 0.2f);
    private float _flashTimer;
    private bool _reducedMotion;
    private bool _screenFlashEnabled = true;

    private void Awake()
    {
        ResolveRefs();
        TryWireFromHierarchy();
        if (_damageFlash != null) _damageTint = _damageFlash.color;
        if (_invulnOverlay != null) _invulnTint = _invulnOverlay.color;
        if (_stunOverlay != null) _stunTint = _stunOverlay.color;
    }

    private void OnEnable()
    {
        ResolveRefs();
        if (_playerHealth != null)
            _playerHealth.OnHitDamageTaken += OnHitDamageTaken;
        if (_playerMovement != null)
            _playerMovement.OnStunned += OnStunned;
        BecameAvailable?.Invoke(this);
    }

    private void OnDisable()
    {
        if (_playerHealth != null)
            _playerHealth.OnHitDamageTaken -= OnHitDamageTaken;
        if (_playerMovement != null)
            _playerMovement.OnStunned -= OnStunned;
    }

    private void ResolveRefs()
    {
        if (_playerHealth == null)
            _playerHealth = FindAnyObjectByType<PlayerHealth>();
        if (_playerMovement == null)
            _playerMovement = FindAnyObjectByType<PlayerMovement>();
    }

    private void OnHitDamageTaken()
    {
        if (_screenFlashEnabled)
            _flashTimer = 0.15f;
    }

    public void ApplyUserFeedbackPreferences(bool reducedMotion, bool screenFlash)
    {
        _reducedMotion = reducedMotion;
        _screenFlashEnabled = screenFlash;
        if (_screenFlashEnabled)
            return;

        _flashTimer = 0f;
        if (_damageFlash != null)
            _damageFlash.color = WithAlpha(_damageTint, 0f);
    }

    private void OnStunned()
    {
        if (_stunOverlay != null)
        {
            float alpha = _reducedMotion || !_screenFlashEnabled ? 0.18f : 0.35f;
            _stunOverlay.color = WithAlpha(_stunTint, alpha);
        }
    }

    private bool TryWireFromHierarchy()
    {
        if (_damageFlash == null) _damageFlash = HudUiWire.FindImage(transform, "DamageFlash");
        if (_invulnOverlay == null) _invulnOverlay = HudUiWire.FindImage(transform, "InvulnOverlay");
        if (_stunOverlay == null) _stunOverlay = HudUiWire.FindImage(transform, "StunOverlay");
        if (_burnIcon == null) _burnIcon = HudUiWire.FindImage(transform, "BurnIcon");
        if (_stunLabel == null) _stunLabel = HudUiWire.FindTmp(transform, "StunLabel");
        return _damageFlash != null && _invulnOverlay != null && _stunOverlay != null && _burnIcon != null && _stunLabel != null;
    }

#if UNITY_EDITOR
    public void AuthorUi()
    {
        if (Application.isPlaying)
            throw new InvalidOperationException("Author player feedback UI outside Play Mode.");
        if (TryWireFromHierarchy())
            return;
        if (_damageFlash != null || _invulnOverlay != null || _stunOverlay != null || _burnIcon != null || _stunLabel != null)
            throw new InvalidOperationException("The existing player feedback hierarchy is incomplete; repair its references before authoring.");
        BuildUi();
    }

    private void BuildUi()
    {
        _damageFlash = CreateFullscreenImage("DamageFlash", new Color(0.9f, 0.1f, 0.08f, 0f));
        _invulnOverlay = CreateFullscreenImage("InvulnOverlay", new Color(1f, 0.4f, 0.4f, 0f));
        _stunOverlay = CreateFullscreenImage("StunOverlay", new Color(1f, 0.9f, 0.2f, 0f));

        var burnGo = new GameObject("BurnIcon", typeof(RectTransform));
        burnGo.transform.SetParent(transform, false);
        var burnRt = burnGo.GetComponent<RectTransform>();
        burnRt.anchorMin = new Vector2(0f, 1f);
        burnRt.anchorMax = new Vector2(0f, 1f);
        burnRt.pivot = new Vector2(0f, 1f);
        burnRt.anchoredPosition = new Vector2(24f, -24f);
        burnRt.sizeDelta = new Vector2(40f, 40f);
        _burnIcon = burnGo.AddComponent<Image>();
        _burnIcon.sprite = HudUiFactory.WhiteSprite;
        _burnIcon.color = new Color(1f, 0.45f, 0.1f, 0.95f);
        _burnIcon.raycastTarget = false;
        burnGo.SetActive(false);

        _stunLabel = HudUiFactory.CreateLabel(transform, "StunLabel", "ATURDIDO", 36f, TextAlignmentOptions.Center);
        var stunRt = _stunLabel.GetComponent<RectTransform>();
        stunRt.anchorMin = new Vector2(0.5f, 0.5f);
        stunRt.anchorMax = new Vector2(0.5f, 0.5f);
        stunRt.pivot = new Vector2(0.5f, 0.5f);
        stunRt.anchoredPosition = new Vector2(0f, 120f);
        stunRt.sizeDelta = new Vector2(400f, 48f);
        _stunLabel.fontStyle = FontStyles.Bold;
        _stunLabel.color = new Color(1f, 0.85f, 0.2f, 1f);
        _stunLabel.gameObject.SetActive(false);
    }

    private Image CreateFullscreenImage(string name, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(transform, false);
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.sprite = HudUiFactory.WhiteSprite;
        img.color = color;
        img.raycastTarget = false;
        return img;
    }
#endif

    private static Color WithAlpha(Color color, float alpha)
    {
        color.a = alpha;
        return color;
    }

    private void Update()
    {
        if (_damageFlash != null)
        {
            if (_screenFlashEnabled && _flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                float t = Mathf.Clamp01(_flashTimer / 0.15f);
                _damageFlash.color = WithAlpha(_damageTint, 0.45f * t);
            }
            else
            {
                _damageFlash.color = WithAlpha(_damageTint, 0f);
            }
        }

        if (_playerHealth != null && _invulnOverlay != null)
        {
            if (_playerHealth.IsInvulnerable)
            {
                float pulse = _reducedMotion || !_screenFlashEnabled
                    ? 0.14f
                    : 0.12f + 0.1f * (0.5f + 0.5f * Mathf.Sin(Time.time * 12f));
                _invulnOverlay.color = WithAlpha(_invulnTint, pulse);
            }
            else
            {
                _invulnOverlay.color = WithAlpha(_invulnTint, 0f);
            }

            if (_burnIcon != null)
                _burnIcon.gameObject.SetActive(_playerHealth.IsBurning);
        }

        if (_playerMovement != null)
        {
            bool stunned = _playerMovement.IsStunned;
            if (_stunOverlay != null)
            {
                float stunAlpha = _reducedMotion || !_screenFlashEnabled
                    ? 0.18f
                    : 0.18f + 0.08f * Mathf.Sin(Time.time * 8f);
                _stunOverlay.color = stunned
                    ? WithAlpha(_stunTint, stunAlpha)
                    : WithAlpha(_stunTint, 0f);
            }
            if (_stunLabel != null)
                _stunLabel.gameObject.SetActive(stunned);
        }
    }
}
