using System.IO;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public class EconomyBootstrap : MonoBehaviour
{
    private const string MaterialCsvPath = "Assets/Data/Balance/balance_material_usage.csv";
    private const string WeaponStatsCsvPath = "Assets/Data/Balance/balance_weapon_stats.csv";

    [SerializeField] private MaterialUsageBalanceSO _materialBalance;
    [SerializeField] private WeaponData[] _weaponDataAssets;

    private static bool _bootstrapped;
    public static MaterialUsageBalanceSO RuntimeMaterialBalance { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void AutoCreateBootstrap()
    {
        if (_bootstrapped || FindAnyObjectByType<EconomyBootstrap>() != null)
            return;

        var go = new GameObject(nameof(EconomyBootstrap));
        go.AddComponent<EconomyBootstrap>();
        DontDestroyOnLoad(go);
    }

    private void Awake()
    {
        if (_bootstrapped)
        {
            Destroy(gameObject);
            return;
        }

        _bootstrapped = true;
        DontDestroyOnLoad(gameObject);
        LoadBalanceData();
        WireCraftingServices();
    }

    private void LoadBalanceData()
    {
#if UNITY_EDITOR
        if (_materialBalance == null)
            _materialBalance = UnityEditor.AssetDatabase.LoadAssetAtPath<MaterialUsageBalanceSO>(
                "Assets/ScriptableObjects/Economy/MaterialUsageBalance.asset");
#endif

        if (_materialBalance == null)
            _materialBalance = ScriptableObject.CreateInstance<MaterialUsageBalanceSO>();

        if (_materialBalance.RoleAssignments.Count == 0 && File.Exists(MaterialCsvPath))
        {
            MaterialUsageParser.Parse(CsvReader.ReadAllRows(MaterialCsvPath), out var assignments, out var totals);
            _materialBalance.SetData(assignments, totals);
        }

        RuntimeMaterialBalance = _materialBalance;

        if (File.Exists(WeaponStatsCsvPath))
            WeaponStatsParser.ImportAll(CsvReader.ReadAllRows(WeaponStatsCsvPath), LoadWeaponAssets());
    }

    private void WireCraftingServices()
    {
        WeaponCraftingService[] services = FindObjectsByType<WeaponCraftingService>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < services.Length; i++)
            services[i].SetMaterialBalance(RuntimeMaterialBalance);
    }

    private WeaponData[] LoadWeaponAssets()
    {
        if (_weaponDataAssets != null && _weaponDataAssets.Length > 0)
            return _weaponDataAssets;

#if UNITY_EDITOR
        return new[]
        {
            UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Flamethrower.asset"),
            UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/RocketLauncher.asset"),
            UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/Mortar.asset"),
            UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/AutomaticCannon.asset"),
            UnityEditor.AssetDatabase.LoadAssetAtPath<WeaponData>("Assets/ScriptableObjects/WeaponSO/RotatingBlade.asset")
        };
#else
        return _weaponDataAssets;
#endif
    }
}
