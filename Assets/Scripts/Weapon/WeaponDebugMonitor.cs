using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
public class WeaponDebugMonitor : MonoBehaviour
{
    [SerializeField] private WeaponManager _weaponManager;
    [SerializeField] private bool _showOverlay;
    [SerializeField] private bool _logStateChanges = true;

    private int _lastIndex = -1;
    private WeaponState _lastState = WeaponState.Automatic;
    private float _lastAmmo = -999f;

    // Caches weapon manager reference if missing in inspector.
    private void Awake()
    {
        if (_weaponManager == null)
            _weaponManager = GetComponent<WeaponManager>();
    }

    // Tracks key weapon runtime changes useful during manual testing.
    private void Update()
    {
        if (_weaponManager == null)
            return;

        WeaponInstance manual = _weaponManager.GetCurrentManualWeapon();
        if (manual == null)
            return;

        int index = _weaponManager.GetCurrentManualWeaponIndex();

        if (_logStateChanges && index != _lastIndex)
            Debug.Log($"[WeaponDebug] Manual index changed: {_lastIndex} -> {index}", this);

        if (_logStateChanges && manual.State != _lastState)
            Debug.Log($"[WeaponDebug] State changed: {_lastState} -> {manual.State}", this);

        if (_logStateChanges && IsAbilityPressed())
            Debug.Log($"[WeaponDebug] Ability used on {manual.Data.DisplayName}. Ammo before spend: {manual.CurrentAmmo:0.0}", this);

        if (_logStateChanges && manual.CurrentAmmo > 0f && _lastAmmo > 0f && manual.CurrentAmmo < _lastAmmo)
            Debug.Log($"[WeaponDebug] Ammo consumed: {_lastAmmo:0.0} -> {manual.CurrentAmmo:0.0}", this);

        _lastIndex = index;
        _lastState = manual.State;
        _lastAmmo = manual.CurrentAmmo;
    }


    // Reads ability press state from active input backend.
    private bool IsAbilityPressed()
    {
#if ENABLE_INPUT_SYSTEM
        return Keyboard.current != null && Keyboard.current.qKey.wasPressedThisFrame;
#else
        return Input.GetKeyDown(KeyCode.Q);
#endif
    }

    // Draws lightweight weapon debug overlay for quick combat validation.
    private void OnGUI()
    {
        if (!_showOverlay || _weaponManager == null)
            return;

        GUI.color = Color.white;
        GUILayout.BeginArea(new Rect(20, 220, 460, 220), GUI.skin.box);
        GUILayout.Label(BuildDebugText());
        GUILayout.EndArea();
    }

    // Builds formatted text for current weapon manager runtime state.
    private string BuildDebugText()
    {
        StringBuilder sb = new();
        var equipped = _weaponManager.GetEquippedWeapons();
        var manual = _weaponManager.GetCurrentManualWeapon();

        sb.AppendLine("WEAPON DEBUG");
        sb.AppendLine($"Equipped: {equipped.Count} / {WeaponManager.MaxWeaponSlots}");
        sb.AppendLine($"Manual Index: {_weaponManager.GetCurrentManualWeaponIndex()}");
        sb.AppendLine($"Cycle Cooldown: {_weaponManager.GetManualCooldownRemaining():0.00}s");

        if (manual == null)
        {
            sb.AppendLine("Manual Weapon: none");
            return sb.ToString();
        }

        sb.AppendLine($"Manual Weapon: {manual.Data.DisplayName} ({manual.Data.WeaponType})");
        sb.AppendLine($"State: {manual.State}");
        sb.AppendLine($"Manual Ammo: {manual.CurrentAmmo:0.0} / {WeaponMath.GetMaxManualAmmo(manual, GetComponent<PlayerStats>()):0.0}");
        sb.AppendLine($"Ability Cost: {WeaponMath.GetActiveAbilityAmmoCost(manual):0.0}");
        sb.AppendLine("Input: LMB fire, Q ability");

        return sb.ToString();
    }
}
