using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEngine;

/// <summary>
/// Arma un reporte de texto (listo para copiar/pegar) con las variables de balance
/// usadas en QA: stats por tipo de enemigo (vida, daño de contacto, velocidad y los
/// campos de cada comportamiento), la configuración de la ruleta y los ajustes del
/// spawner orbital. Usa reflexión sobre los campos serializados de los prefabs, así
/// que refleja siempre los valores reales configurados en el editor.
/// </summary>
public static class QaBalanceReport
{
    // Componentes de balance que nos interesan dumpear por enemigo.
    private static readonly string[] InterestingComponents =
    {
        "EnemyHealth",
        "EnemyContactDamage",
        "SimpleFollow",
        "EnemyFollow",
        "FlyingRangedBehavior",
        "ChargerEnemyBehavior",
        "HellfireSlimeBehavior",
        "BomberDroneBehavior",
    };

    public static string Build(EnemySpawnRouletteConfig config, OrbitalSpawner orbitalSpawner, PlayerHealth player)
    {
        var sb = new StringBuilder(2048);

        sb.AppendLine("===== SCRAPWAVES QA BALANCE REPORT =====");
        sb.AppendLine($"Fecha: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"timeScale: {Time.timeScale:0.##}");
        sb.AppendLine();

        AppendPlayer(sb, player);
        AppendOrbitalSpawner(sb, orbitalSpawner);
        AppendRoulette(sb, config);
        AppendEnemyStats(sb, config);

        sb.AppendLine("===== FIN DEL REPORTE =====");
        return sb.ToString();
    }

    private static void AppendPlayer(StringBuilder sb, PlayerHealth player)
    {
        sb.AppendLine("## JUGADOR");
        if (player == null)
        {
            sb.AppendLine("  (sin PlayerHealth)");
        }
        else
        {
            sb.AppendLine($"  HP actual/max: {player.CurrentHealth}/{player.MaxHealth}");
        }

        sb.AppendLine();
    }

    private static void AppendOrbitalSpawner(StringBuilder sb, OrbitalSpawner orbitalSpawner)
    {
        sb.AppendLine("## SPAWNER ORBITAL (continuo, ruleta)");
        if (orbitalSpawner == null)
        {
            sb.AppendLine("  (sin OrbitalSpawner)");
        }
        else
        {
            sb.AppendLine($"  Activo: {orbitalSpawner.enabled} | activos: {orbitalSpawner.ActiveSpawnedCount}");
            DumpComponentFields(sb, orbitalSpawner, "  ");
        }

        sb.AppendLine();
    }

    private static void AppendRoulette(StringBuilder sb, EnemySpawnRouletteConfig config)
    {
        sb.AppendLine("## RULETA (pesos / batch)");
        if (config == null || config.Entries == null)
        {
            sb.AppendLine("  (sin config)");
            sb.AppendLine();
            return;
        }

        sb.AppendLine($"  VariantBonus: +{config.VariantWeightBonusPerStep} cada {config.VariantWeightBonusEverySeconds}s");
        foreach (EnemySpawnRouletteConfig.Entry entry in config.Entries)
        {
            if (entry == null)
                continue;

            sb.AppendLine($"  {entry.Kind,-16} weight={entry.BaseWeight,-4} batch={entry.BatchSize,-3} variant={entry.IsVariant}");
        }

        sb.AppendLine();
    }

    private static void AppendEnemyStats(StringBuilder sb, EnemySpawnRouletteConfig config)
    {
        sb.AppendLine("## STATS POR ENEMIGO");
        if (config == null || config.Entries == null)
        {
            sb.AppendLine("  (sin config)");
            return;
        }

        foreach (EnemySpawnRouletteConfig.Entry entry in config.Entries)
        {
            if (entry == null)
                continue;

            sb.AppendLine($"- {entry.Kind}  (prefab: {(entry.Prefab != null ? entry.Prefab.name : "NULL")})");
            if (entry.Prefab == null)
                continue;

            MonoBehaviour[] components = entry.Prefab.GetComponentsInChildren<MonoBehaviour>(true);
            foreach (MonoBehaviour component in components)
            {
                if (component == null)
                    continue;

                if (Array.IndexOf(InterestingComponents, component.GetType().Name) < 0)
                    continue;

                sb.AppendLine($"  [{component.GetType().Name}]");
                DumpComponentFields(sb, component, "    ");
            }
        }
    }

    private static void DumpComponentFields(StringBuilder sb, object component, string indent)
    {
        FieldInfo[] fields = component.GetType().GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        foreach (FieldInfo field in fields)
        {
            bool serialized = field.IsPublic || field.IsDefined(typeof(SerializeField), true);
            if (!serialized || field.IsNotSerialized)
                continue;

            object value = field.GetValue(component);
            sb.AppendLine($"{indent}{field.Name} = {FormatValue(value)}");
        }
    }

    private static string FormatValue(object value)
    {
        switch (value)
        {
            case null:
                return "null";
            case LayerMask mask:
                return $"mask(0x{mask.value:X})";
            case Vector3 v:
                return $"({v.x:0.##}, {v.y:0.##}, {v.z:0.##})";
            case float f:
                return f.ToString("0.###");
            case UnityEngine.Object obj:
                return obj != null ? obj.name : "null";
            case IEnumerable<object> _:
                return "[...]";
            default:
                return value.ToString();
        }
    }
}
