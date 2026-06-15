using System;
using UnityEngine;

/// <summary>
/// Progreso de llaves del nivel. Los bosses dropean pickups que llaman <see cref="RegisterKey"/>.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-40)]
public class LevelExitObjective : MonoBehaviour
{
    public static LevelExitObjective Instance { get; private set; }

    [SerializeField, Min(1), Tooltip("Cuántas llaves hay que recoger para desbloquear la salida.")]
    private int _keysRequired = 2;

    private int _keysCollected;

    public int KeysCollected => _keysCollected;
    public int KeysRequired => _keysRequired;
    public bool AllKeysCollected => _keysCollected >= _keysRequired;

    public event Action<int, int> OnKeyProgressChanged;
    public event Action OnAllKeysCollected;

    private void OnEnable()
    {
        Instance = this;
    }

    private void OnDisable()
    {
        if (Instance == this)
            Instance = null;
    }

    public bool RegisterKey()
    {
        if (AllKeysCollected)
            return false;

        _keysCollected++;
        OnKeyProgressChanged?.Invoke(_keysCollected, _keysRequired);

        if (AllKeysCollected)
            OnAllKeysCollected?.Invoke();

        return true;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_keysRequired < 1)
            _keysRequired = 1;
    }
#endif
}
