using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Utilidad TEMPORAL de QA: alterna "vida infinita" del jugador con Numpad 0.
/// Mientras está activa, mantiene al jugador con la vida al máximo (y lo revive si
/// muriera), útil para probar edge cases de spawners sin morir.
///
/// No persiste nada; solo actúa en runtime. Pensado para un GameObject temporal en
/// <c>SampleScene</c> que se puede borrar antes de release.
/// </summary>
[DisallowMultipleComponent]
public class DebugInfiniteHealth : MonoBehaviour
{
    [SerializeField, Tooltip("Vacío = FindAnyObjectByType<PlayerHealth>().")]
    private PlayerHealth _playerHealth;

    [SerializeField, Tooltip("Empezar con vida infinita activada.")]
    private bool _startEnabled;

    [SerializeField, Tooltip("Mostrar un cartel en pantalla cuando está activo.")]
    private bool _showOverlay = true;

    private bool _active;

    private void Awake()
    {
        _active = _startEnabled;
        if (_playerHealth == null)
            _playerHealth = FindAnyObjectByType<PlayerHealth>(FindObjectsInactive.Include);
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current.numpad0Key.wasPressedThisFrame)
        {
            _active = !_active;
            Debug.Log($"[DebugInfiniteHealth] Vida infinita: {(_active ? "ON" : "OFF")}");
        }
    }

    private void LateUpdate()
    {
        if (!_active)
            return;

        if (_playerHealth == null)
        {
            _playerHealth = FindAnyObjectByType<PlayerHealth>(FindObjectsInactive.Include);
            if (_playerHealth == null)
                return;
        }

        if (!_playerHealth.IsAlive || _playerHealth.CurrentHealth < _playerHealth.MaxHealth)
            _playerHealth.FullHeal();
    }

    private void OnGUI()
    {
        if (!_active || !_showOverlay)
            return;

        var style = new GUIStyle(GUI.skin.box)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = Color.cyan }
        };

        const float w = 260f;
        const float h = 34f;
        GUI.Box(new Rect((Screen.width - w) * 0.5f, Screen.height - h - 12f, w, h),
            "VIDA INFINITA: ON  (Numpad 0)", style);
    }
}
