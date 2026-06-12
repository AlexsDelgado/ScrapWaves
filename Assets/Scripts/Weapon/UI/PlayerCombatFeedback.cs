using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class PlayerCombatFeedback : MonoBehaviour
{
    [SerializeField] private PlayerHealth _playerHealth;
    [SerializeField] private PlayerMovement _playerMovement;

    private Image _damageFlash;
    private Image _invulnOverlay;
    private Image _stunOverlay;
    private Image _burnIcon;
    private TextMeshProUGUI _stunLabel;
    private float _flashTimer;

    private void Awake()
    {
        ResolveRefs();
        BuildUi();
    }

    private void OnEnable()
    {
        ResolveRefs();
        if (_playerHealth != null)
            _playerHealth.OnHitDamageTaken += OnHitDamageTaken;
        if (_playerMovement != null)
            _playerMovement.OnStunned += OnStunned;
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

    private void OnHitDamageTaken() => _flashTimer = 0.15f;

    private void OnStunned()
    {
        if (_stunOverlay != null)
            _stunOverlay.color = new Color(1f, 0.9f, 0.2f, 0.35f);
    }

    private void BuildUi()
    {
        _damageFlash = CreateFullscreenImage("DamageFlash", new Color(0.9f, 0.1f, 0.08f, 0f));
        _invulnOverlay = CreateFullscreenImage("InvulnOverlay", new Color(1f, 0.35f, 0.35f, 0f));
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

    private void Update()
    {
        if (_damageFlash != null)
        {
            if (_flashTimer > 0f)
            {
                _flashTimer -= Time.deltaTime;
                float t = Mathf.Clamp01(_flashTimer / 0.15f);
                _damageFlash.color = new Color(0.9f, 0.1f, 0.08f, 0.45f * t);
            }
            else
            {
                _damageFlash.color = new Color(0.9f, 0.1f, 0.08f, 0f);
            }
        }

        if (_playerHealth != null && _invulnOverlay != null)
        {
            if (_playerHealth.IsInvulnerable)
            {
                float pulse = 0.12f + 0.1f * (0.5f + 0.5f * Mathf.Sin(Time.time * 12f));
                _invulnOverlay.color = new Color(1f, 0.4f, 0.4f, pulse);
            }
            else
            {
                _invulnOverlay.color = new Color(1f, 0.4f, 0.4f, 0f);
            }

            if (_burnIcon != null)
                _burnIcon.gameObject.SetActive(_playerHealth.IsBurning);
        }

        if (_playerMovement != null)
        {
            bool stunned = _playerMovement.IsStunned;
            if (_stunOverlay != null)
                _stunOverlay.color = stunned
                    ? new Color(1f, 0.9f, 0.2f, 0.18f + 0.08f * Mathf.Sin(Time.time * 8f))
                    : new Color(1f, 0.9f, 0.2f, 0f);
            if (_stunLabel != null)
                _stunLabel.gameObject.SetActive(stunned);
        }
    }
}
