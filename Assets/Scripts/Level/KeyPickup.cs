using UnityEngine;

/// <summary>
/// Llave en el mundo: registra la llave en <see cref="LevelExitObjective"/> al ser recogida.
/// El movimiento e imán los gestiona <see cref="WorldPickup"/> en el mismo GameObject.
/// </summary>
[RequireComponent(typeof(WorldPickup))]
[DisallowMultipleComponent]
public class KeyPickup : MonoBehaviour, IPickable
{
    public void OnPickedUp()
    {
        if (LevelExitObjective.Instance != null)
            LevelExitObjective.Instance.RegisterKey();
        Destroy(gameObject);
    }
}
