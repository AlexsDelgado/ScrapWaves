using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class GameplayHudRoot : MonoBehaviour
{
    [SerializeField] private int _sortingOrder = 600;
    [SerializeField, Tooltip("Si está vacío, se construye la jerarquía placeholder en Awake.")]
    private Transform _playerBarsContent;

    private void Awake()
    {
        Canvas existing = GetComponentInChildren<Canvas>(true);
        if (existing != null)
        {
            existing.sortingOrder = _sortingOrder;
            return;
        }

        Debug.LogWarning($"[{nameof(GameplayHudRoot)}] Sin Canvas en prefab. Ejecutá ScrapWaves → UI → Build GameplayHud Prefab.", this);
    }
}
