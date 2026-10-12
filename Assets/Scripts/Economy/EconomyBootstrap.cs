using System.IO;
using UnityEngine;

[DefaultExecutionOrder(-100)]
public class EconomyBootstrap : MonoBehaviour
{
    private const string MaterialCsvPath = "Assets/Data/Balance/balance_material_usage.csv";

    [SerializeField] private MaterialUsageBalanceSO _materialBalance;

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
    }

    private void WireCraftingServices()
    {
        WeaponCraftingService[] services = FindObjectsByType<WeaponCraftingService>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < services.Length; i++)
            services[i].SetMaterialBalance(RuntimeMaterialBalance);
    }
}
