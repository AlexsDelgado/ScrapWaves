using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-20)]
public class RunStartWeaponChoice : MonoBehaviour
{
    [SerializeField] private WeaponManager _weaponManager;
    [SerializeField] private LevelUpChoiceUI _choiceUi;
    [SerializeField] private List<WeaponData> _weaponPool = new();

    private bool _presented;

    private void Awake()
    {
        if (_weaponManager == null)
            _weaponManager = GetComponent<WeaponManager>();
        if (_choiceUi == null)
            _choiceUi = GetComponent<LevelUpChoiceUI>();
    }

    private void Start()
    {
        if (_presented || _weaponManager == null || _choiceUi == null || _weaponPool.Count == 0)
            return;

        StartCoroutine(PresentInitialChoiceCoroutine());
    }

    private IEnumerator PresentInitialChoiceCoroutine()
    {
        _presented = true;

        // Espera un frame para que todos los Start() se ejecuten primero (en
        // particular ThirdPersonCamera.Start(), que bloquea/oculta el cursor).
        // Si presentamos antes, la cámara re-bloquea el cursor después de que la
        // UI lo liberó y el mouse queda oculto durante la selección.
        yield return null;

        List<WeaponData> offer = PickTwoRandomWeapons();
        if (offer.Count == 0)
            yield break;

        var options = new List<LevelUpChoiceOption>(offer.Count);
        for (int i = 0; i < offer.Count; i++)
        {
            WeaponData data = offer[i];
            options.Add(new LevelUpChoiceOption(data.DisplayName, "Primera arma gratis", data.Icon, HudPlaceholderKind.Weapon));
        }

        int selected = -1;
        yield return _choiceUi.PresentCoroutine("Elige tu primera arma", options, index => selected = index);
        if (selected < 0 || selected >= offer.Count)
            selected = 0;

        // Solo el arma elegida: limpia cualquier starter residual y equipa una.
        _weaponManager.ClearEquippedWeapons();
        _weaponManager.AddWeapon(offer[selected]);
    }

    private List<WeaponData> PickTwoRandomWeapons()
    {
        var pool = new List<WeaponData>();
        for (int i = 0; i < _weaponPool.Count; i++)
        {
            WeaponData data = _weaponPool[i];
            if (data != null && (SaveManager.Instance == null || SaveManager.Instance.IsUnlocked(data)))
                pool.Add(data);
        }

        var result = new List<WeaponData>();
        while (result.Count < 2 && pool.Count > 0)
        {
            int index = Random.Range(0, pool.Count);
            result.Add(pool[index]);
            pool.RemoveAt(index);
        }

        return result;
    }
}
