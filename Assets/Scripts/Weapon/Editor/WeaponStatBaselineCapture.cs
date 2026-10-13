using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

/// <summary>
/// Observation-only production capture. This source deliberately also compiles against
/// the published pre-profile runtime so before/after data comes from independent code.
/// It never authors or saves a WeaponData asset and does not simulate attacks.
/// </summary>
public static class WeaponStatBaselineCapture
{
    public static readonly string[] ProductionNames =
        { "AutomaticCannon", "Flamethrower", "Mortar", "RocketLauncher", "RotatingBlade" };

    [Serializable] public sealed class Entry { public string Key; public string Value; }
    [Serializable] public sealed class ModeState
    {
        public string Asset; public string Weapon; public int Level; public string Path;
        public string Mode; public List<Entry> Values = new();
    }
    [Serializable] public sealed class AssetHash { public string Path; public string Sha256; public long Bytes; }
    [Serializable] public sealed class Report
    {
        public string UnityVersion; public string CapturedUtc;
        public string ManifestVersion = "strict-neutral-v2";
        public string[] CoverageLimitations =
        {
            "Observation only: no emitted attacks, hit/impulse/status outcomes, timing, heat/player scenarios, pooling or lifecycle proof.",
            "Exact getter contracts cover scalar zero-argument or typed-tuning getters present in the independent runtimes; missing required signatures fail.",
            "Inline formulas and getters with gameplay arguments remain diagnostic-only unless explicitly adapted. Raw tuning is not a resolved outcome.",
            "Diagnostic labels/formatting may change; compare all common values and review every added/removed key explicitly. Never compare only an intersection.",
            "Production profiles must remain unmodified/inherited for Gate A; authored overrides require separate Gate B mutation/isolation tests."
        };
        public string Scope = "5 production weapons x 15 exact level/path keys x 3 modes; neutral player, zero heat";
        public List<AssetHash> Assets; public List<ModeState> States = new();
    }

    public static void CaptureCommand()
    {
        string output = Environment.GetEnvironmentVariable("SCRAPWAVES_BASELINE_OUTPUT");
        if (string.IsNullOrWhiteSpace(output)) output = "Library/CodexValidation/WeaponStatBaseline.json";
        Report report = Capture();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        File.WriteAllText(output, JsonUtility.ToJson(report, true));
        Debug.Log($"Weapon baseline captured: {report.States.Count} production mode states; {report.Assets.Count} asset/metadata hashes; no asset bytes changed. {output}");
    }

    public static Report Capture()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Capture the baseline in Edit Mode.");
        var cleanup = new List<Object>();
        var report = new Report { UnityVersion = Application.unityVersion, CapturedUtc = DateTime.UtcNow.ToString("O"), Assets = CaptureHashes() };
        Random.State random = Random.state;
        int damageEvents = 0;
        Action<WeaponDamageRoll> onDamage = _ => damageEvents++;
        WeaponDamageResolver.OnDamageResolved += onDamage;
        try
        {
            PlayerStats stats = CreateNeutralStats(cleanup);
            foreach (string name in ProductionNames)
            {
                string assetPath = $"Assets/ScriptableObjects/WeaponSO/{name}.asset";
                WeaponData original = AssetDatabase.LoadAssetAtPath<WeaponData>(assetPath);
                if (original == null) throw new InvalidOperationException("Missing production weapon: " + assetPath);
                string originalJson = EditorJsonUtility.ToJson(original);
                bool dirty = EditorUtility.IsDirty(original);
                var data = Object.Instantiate(original);
                cleanup.Add(data);
                foreach (WeaponUpgradePath path in Enum.GetValues(typeof(WeaponUpgradePath)))
                {
                    int first = path == WeaponUpgradePath.None ? 1 : 6;
                    int last = path == WeaponUpgradePath.None ? 5 : 10;
                    for (int level = first; level <= last; level++)
                    {
                        var instance = new WeaponInstance { Data = data, Level = level, SelectedPath = path,
                            State = WeaponState.Manual, CurrentAmmo = 37f, ManualCooldownTimer = .75f, AbilityCooldownTimer = 1.25f };
                        var weapon = (BasicProjectileWeapon)WeaponBehaviourFactory.Create(data, null, null, stats.transform, null);
                        weapon.Setup(instance, stats.transform, stats, null);
                        string runtimeBefore = JsonUtility.ToJson(instance);
                        string dataBefore = EditorJsonUtility.ToJson(data);
                        string privateBefore = CapturePrivateObservationState(weapon);
                        int[] objectsBefore = LoadedGameObjectIds();
                        WeaponDiagnosticsSnapshot snapshot = weapon.CaptureDiagnostics();
                        foreach (string mode in new[] { "Automatic", "Manual", "Active Ability" })
                        {
                            var state = new ModeState { Asset = assetPath, Weapon = data.WeaponType.ToString(), Level = level, Path = path.ToString(), Mode = mode };
                            WeaponDiagnosticSection section = snapshot.Sections.Single(s => s.Name == mode);
                            state.Values.AddRange(section.Values.Select(v => new Entry { Key = "diagnostic/" + v.Label, Value = v.Value }));
                            Add(state, "raw/BaseDamage", data.BaseDamage);
                            Add(state, "raw/BaseRange", data.BaseRange);
                            Add(state, "raw/BaseAttackRate", data.BaseAttackRate);
                            Add(state, "raw/BaseKnockback", data.BaseKnockback);
                            Add(state, "resolved/ManualCapacity", WeaponMath.GetMaxManualAmmo(instance, stats));
                            Add(state, "resolved/Cooldown", WeaponMath.GetAbilityCooldownDuration(instance, stats));
                            Add(state, "resolved/LevelDamageMultiplier", WeaponDamageResolver.GetLevelDamageMultiplier(instance));
                            Add(state, "resolved/PathDamageMultiplier", WeaponDamageResolver.GetPathDamageMultiplier(instance));
                            Add(state, "resolved/AttackRateMultiplier", WeaponMath.GetAttackRateMultiplier(instance));
                            Add(state, "resolved/SharedAbilityAmmoCost", WeaponMath.GetActiveAbilityAmmoCost(instance));
                            CaptureTuningScalars(GetTuning(data), "raw/tuning", state, 0);
                            CaptureExactGetters(weapon, data, mode, state);
                            if (state.Values.Select(v => v.Key).Distinct(StringComparer.Ordinal).Count() != state.Values.Count)
                                throw new InvalidOperationException("Duplicate observation key: " + assetPath + "/" + level + "/" + path + "/" + mode);
                            report.States.Add(state);
                        }
                        if (runtimeBefore != JsonUtility.ToJson(instance) || dataBefore != EditorJsonUtility.ToJson(data)
                            || privateBefore != CapturePrivateObservationState(weapon)
                            || !objectsBefore.SequenceEqual(LoadedGameObjectIds()))
                            throw new InvalidOperationException("Observation mutated runtime or cloned data: " + assetPath);
                    }
                }
                if (originalJson != EditorJsonUtility.ToJson(original) || dirty != EditorUtility.IsDirty(original))
                    throw new InvalidOperationException("Observation mutated a production asset: " + assetPath);
            }
            if (!Random.state.Equals(random) || damageEvents != 0)
                throw new InvalidOperationException("Baseline observation consumed randomness or resolved live damage.");
            if (report.States.Count != 225 || report.States.Select(s => s.Asset + "|" + s.Level + "|" + s.Path + "|" + s.Mode)
                .Distinct(StringComparer.Ordinal).Count() != 225)
                throw new InvalidOperationException("Expected exactly 225 unique production mode states.");
            var after = CaptureHashes();
            if (JsonUtility.ToJson(new HashList { Files = report.Assets }) != JsonUtility.ToJson(new HashList { Files = after }))
                throw new InvalidOperationException("Weapon asset/metadata bytes changed during baseline capture.");
            return report;
        }
        finally
        {
            WeaponDamageResolver.OnDamageResolved -= onDamage;
            Random.state = random;
            for (int i = cleanup.Count - 1; i >= 0; i--) if (cleanup[i] != null) Object.DestroyImmediate(cleanup[i]);
        }
    }

    [Serializable] private sealed class HashList { public List<AssetHash> Files; }
    public static List<AssetHash> CaptureHashes() => AssetDatabase.FindAssets("t:WeaponData")
        .Select(AssetDatabase.GUIDToAssetPath).Distinct().OrderBy(p => p, StringComparer.Ordinal)
        .SelectMany(p => new[] { p, p + ".meta" }).Select(p =>
        {
            byte[] bytes = File.ReadAllBytes(p);
            using var sha = SHA256.Create();
            return new AssetHash { Path = p, Bytes = bytes.Length, Sha256 = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "") };
        }).ToList();

    public static PlayerStats CreateNeutralStats(List<Object> cleanup, Scene? previewScene = null)
    {
        var owner = new GameObject("Weapon baseline observation") { hideFlags = HideFlags.HideAndDontSave };
        cleanup.Add(owner);
        if (previewScene.HasValue) SceneManager.MoveGameObjectToScene(owner, previewScene.Value);
        PlayerStats stats = owner.AddComponent<PlayerStats>();
        var definitions = new List<StatDefinition>();
        foreach (StatType type in Enum.GetValues(typeof(StatType)))
        {
            var definition = ScriptableObject.CreateInstance<StatDefinition>(); definition.hideFlags = HideFlags.HideAndDontSave; cleanup.Add(definition);
            typeof(StatDefinition).GetField("<StatType>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(definition, type);
            float value = type == StatType.CriticalChance || type == StatType.AbilityCooldownReduction ? 0f
                : type == StatType.CriticalDamage ? 2f : 1f;
            typeof(StatDefinition).GetField("<BaseValue>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(definition, value);
            definitions.Add(definition);
        }
        typeof(PlayerStats).GetField("_statDefinitions", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(stats, definitions);
        typeof(PlayerStats).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(stats, null);
        return stats;
    }

    private static void Add(ModeState state, string key, float value) =>
        state.Values.Add(new Entry { Key = key, Value = value.ToString("R", CultureInfo.InvariantCulture) });

    // Contracts are intentionally explicit. Renames are source-reviewed adapters, never a best-effort scan.
    // Only neutral, zero-heat observations are covered; keys describe the sampled mechanic and units.
    private static void CaptureExactGetters(BasicProjectileWeapon weapon, WeaponData data, string mode, ModeState state)
    {
        bool automatic = mode == "Automatic";
        bool manual = mode == "Manual";
        bool ability = mode == "Active Ability";
        bool a = state.Level >= 6 && state.Path == WeaponUpgradePath.PathA.ToString();
        bool b = state.Level >= 6 && state.Path == WeaponUpgradePath.PathB.ToString();
        bool profiledRuntime = typeof(WeaponData).GetField("StatProfile") != null;
        void Read(string key, string method) => CaptureRequiredGetter(weapon, data, state, key, method);

        Read("exact/player/AreaSizeMultiplier", "GetAreaSizeMultiplier");
        Read("exact/weapon/CriticalDamageFactor", "GetCritMultiplierOverride");
        switch (data.WeaponType)
        {
            case WeaponType.AutomaticCannon:
                if (automatic || manual)
                {
                    Read("exact/cycle/IntervalSeconds", automatic ? "GetAutomaticFireInterval" : "GetManualFireInterval");
                    Read("exact/cycle/ProjectileCount", automatic ? "GetAutomaticShotCount" : "GetManualShotCount");
                    if (a)
                    {
                        Read("exact/continuous/HeatSpeedFactor", "GetContinuousFireAttackSpeedMultiplier");
                        Read("exact/continuous/ModeSpeedFactor", automatic ? "GetContinuousFireAutoAttackSpeedMultiplier" : "GetContinuousFireManualAttackSpeedMultiplier");
                    }
                    if (b)
                    {
                        Read("exact/headhunter/PierceTargetCount", "GetHeadHunterPierceLimit");
                        if (manual)
                        {
                            Read("exact/range/ManualPierceMeters", profiledRuntime ? "GetHeadHunterManualRange" : "GetHeadHunterProjectileRange");
                            Read("exact/headhunter/WeakPointFactor", "GetHeadHunterWeakPointScale");
                        }
                    }
                    if (automatic && !b) Read("exact/damage/HeatFactor", "GetHeatDamageMultiplier");
                }
                if (ability)
                {
                    Read("exact/resource/AbilityAmmoCost", "GetActiveAbilityAmmoCost");
                    if (a)
                    {
                        Read("exact/ability/DurationSeconds", "GetContinuousFireActiveDuration");
                        Read("exact/ability/ProjectilesPerSecond", "GetContinuousFireActiveBulletsPerSecond");
                        Read("exact/ability/ProjectileCount", "GetContinuousFireActiveBulletCount");
                    }
                    else if (b)
                    {
                        Read("exact/ability/ChargeSeconds", "GetHeadHunterActiveChargeSeconds");
                        Read("exact/ability/PierceTargetLimit", "GetHeadHunterActivePierceLimit");
                        Read("exact/range/AbilityPierceMeters", "GetHeadHunterActivePierceRange");
                        Read("exact/headhunter/WeakPointFactor", "GetHeadHunterWeakPointScale");
                    }
                    else Read("exact/ability/HeatBonusProjectiles", "GetActiveHeatBonusBulletCount");
                }
                break;
            case WeaponType.Flamethrower:
                if (automatic)
                {
                    Read("exact/cycle/TickIntervalSeconds", "GetAutomaticTickInterval");
                    Read("exact/range/AreaTravelMeters", "GetAutomaticRange");
                }
                else if (manual) Read("exact/range/AreaTravelMeters", "GetManualRange");
                else Read("exact/radius/PathAdjustedWaveMetersBeforePlayer", "GetPathAdjustedActiveRadius");
                if (!b) Read("exact/status/BurnDurationSeconds", "GetPathAdjustedBurnDuration");
                if (a)
                {
                    Read("exact/path/JellifiedLevelFactor", "GetJellifiedFuelLevelScale");
                    // Legacy source radius is distinct from final authored puddle radius.
                    if (!ability) Read("exact/source/LegacyContactPuddleRadiusMeters", "GetScaledFuelPuddleRadius");
                }
                break;
            case WeaponType.Mortar:
                if (automatic)
                    Read("exact/cycle/IntervalSeconds", profiledRuntime ? "GetAutomaticFireInterval" : "GetFireInterval");
                if (manual)
                {
                    Read("exact/cycle/IntervalSeconds", "GetManualFireInterval");
                    Read("exact/radius/ManualBlastMeters", "get_ManualExplosionRadius");
                    Read("exact/travel/ManualSeconds", "GetManualTravelTime");
                }
                Read("exact/radius/ShellCollisionMeters", "get_ShellCollisionRadius");
                Read("exact/travel/ArcHeightMeters", "get_ArcHeight");
                if (automatic) Read("exact/cycle/HeatRateFactor", "GetHeatFireRateMultiplier");
                if (ability)
                    Read("exact/ability/ShellCount", a ? "GetGrapeshotRainShellCount" : "GetActiveShellCount");
                break;
            case WeaponType.RocketLauncher:
                if (automatic)
                {
                    Read("exact/cycle/IntervalSeconds", "GetAutomaticFireInterval");
                    Read("exact/cycle/VolleyShotIntervalSeconds", "GetAutomaticVolleyShotInterval");
                    Read("exact/cycle/HeatBonusRockets", "GetThresholdRocketBonus");
                    Read("exact/cycle/FragmentBonusRockets", "GetFragmentationRocketBonus");
                    Read("exact/travel/LaunchClearanceHeightMeters", "GetAutomaticLaunchClearanceHeight");
                }
                if (manual) Read("exact/cycle/IntervalSeconds", "GetManualFireInterval");
                if (ability)
                {
                    if (!b)
                    {
                        Read("exact/ability/MaximumRocketCount", "GetMaximumActiveRocketCount");
                        Read("exact/ability/TargetLimit", "GetActiveTargetLimit");
                        Read("exact/ability/InitialTargetCount", "GetInitialActiveTargetCount");
                    }
                    if (b)
                    {
                        Read("exact/cluster/ChildCount", "GetFragmentClusterRocketCount");
                        Read("exact/cluster/DamageFactor", "GetFragmentClusterDamageScale");
                    }
                }
                break;
            case WeaponType.RotatingBlade:
                Read("exact/blade/Count", "GetBladeCount");
                if (automatic)
                {
                    Read("exact/cycle/ContactTickIntervalSeconds", "GetAutoDamageInterval");
                    Read("exact/radius/OrbitMeters", "GetScaledOrbitRadius");
                    Read("exact/radius/ContactMeters", "GetScaledHitRadius");
                    Read("exact/damage/AtomicFactor", "GetAtomicSharpnessDamageScale");
                    Read("exact/rotation/AtomicFactor", "GetAtomicSharpnessSpinMultiplier");
                    Read("exact/knockback/AutomaticModeFactorBeforeAtomic", "GetAutomaticKnockbackScale");
                }
                if (manual)
                {
                    Read("exact/cycle/SwingIntervalSeconds", "GetManualSwingInterval");
                    Read("exact/range/SlashMeters", "GetScaledManualRange");
                    Read("exact/damage/ManualHeatFactor", "GetManualDamageScale");
                    Read("exact/damage/AtomicFactor", "GetAtomicSharpnessDamageScale");
                }
                if (ability)
                {
                    Read("exact/geometry/LegacyLineWidthMeters", "GetScaledActiveLineWidth");
                    if (b)
                    {
                        Read("exact/range/AtomicBaseDashMeters", "GetAtomicDashBaseRange");
                        Read("exact/damage/AtomicAbilityFactor", "GetAtomicActiveDamageScale");
                        Read("exact/timing/AtomicSegmentSeconds", "GetAtomicActiveDashSegmentSeconds");
                        Read("exact/timing/AtomicPostDashInvulnerabilitySeconds", "GetAtomicActivePostDashInvulnerabilitySeconds");
                    }
                    else Read("exact/range/ThrustMeters", "GetScaledActiveRange");
                }
                if (a && !automatic) Read("exact/timing/MultibladeActionIntervalSeconds", "GetMultiBladeActionInterval");
                break;
            default: throw new InvalidOperationException("No baseline getter contract for " + data.WeaponType);
        }
    }

    private static object GetTuning(WeaponData data) => data.WeaponType switch
    {
        WeaponType.AutomaticCannon => data.AutomaticCannon, WeaponType.Flamethrower => data.Flamethrower,
        WeaponType.RocketLauncher => data.RocketLauncher, WeaponType.Mortar => data.Mortar,
        WeaponType.RotatingBlade => data.RotatingBlade,
        _ => throw new InvalidOperationException("No tuning contract for " + data.WeaponType)
    };

    private static void CaptureRequiredGetter(BasicProjectileWeapon weapon, WeaponData data, ModeState state, string key, string name)
    {
        object tuning = GetTuning(data);
        var matches = new List<MethodInfo>();
        // DeclaredOnly traversal includes private base getters without accidentally picking an overload.
        for (Type type = weapon.GetType(); type != null; type = type.BaseType)
        {
            matches.AddRange(type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == name && (m.ReturnType == typeof(float) || m.ReturnType == typeof(int))
                    && (m.GetParameters().Length == 0 || (m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType == tuning.GetType()))));
            if (matches.Count > 0) break; // Most-derived override is the contract.
        }
        if (matches.Count != 1)
            throw new InvalidOperationException("Required scalar getter missing/ambiguous: " + weapon.GetType().Name + "." + name
                + " for " + state.Path + "/" + state.Level + "/" + state.Mode + "/" + key);
        MethodInfo method = matches[0];
        object value = method.Invoke(weapon, method.GetParameters().Length == 0 ? null : new[] { tuning });
        if (value is float number)
        {
            if (float.IsNaN(number) || float.IsInfinity(number))
                throw new InvalidOperationException("Non-finite observation: " + key);
            Add(state, key, number);
        }
        else state.Values.Add(new Entry { Key = key, Value = ((int)value).ToString(CultureInfo.InvariantCulture) });
    }

    // Raw authoring controls are captured with full precision, including list thresholds/counts.
    // They are explicitly raw; matching them does not prove a runtime consumer exists.
    private static void CaptureTuningScalars(object value, string prefix, ModeState state, int depth)
    {
        if (depth > 8) throw new InvalidOperationException("Unexpected tuning graph depth: " + prefix);
        if (value == null) { state.Values.Add(new Entry { Key = prefix, Value = "null" }); return; }
        Type type = value.GetType();
        if (type == typeof(float)) { Add(state, prefix, (float)value); return; }
        if (type.IsPrimitive || type.IsEnum || type == typeof(decimal))
        {
            state.Values.Add(new Entry { Key = prefix, Value = Convert.ToString(value, CultureInfo.InvariantCulture) }); return;
        }
        if (value is string || value is Object) return; // Presentation references and names are protected by asset hashes.
        if (value is System.Collections.IList list)
        {
            state.Values.Add(new Entry { Key = prefix + "/Count", Value = list.Count.ToString(CultureInfo.InvariantCulture) });
            for (int index = 0; index < list.Count; index++) CaptureTuningScalars(list[index], prefix + "/" + index, state, depth + 1);
            return;
        }
        foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public).OrderBy(f => f.Name, StringComparer.Ordinal))
            CaptureTuningScalars(field.GetValue(value), prefix + "/" + field.Name, state, depth + 1);
    }

    private static int[] LoadedGameObjectIds() => Resources.FindObjectsOfTypeAll<GameObject>()
        .Select(o => o.GetInstanceID()).OrderBy(id => id).ToArray();

    private static string CapturePrivateObservationState(BasicProjectileWeapon weapon)
    {
        var values = new List<string>();
        for (Type type = weapon.GetType(); type != null; type = type.BaseType)
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .OrderBy(f => f.Name, StringComparer.Ordinal))
            {
                object value = field.GetValue(weapon);
                string key = type.FullName + "." + field.Name;
                if (value == null) values.Add(key + "=null");
                else if (field.FieldType.IsPrimitive || field.FieldType.IsEnum || value is string)
                    values.Add(key + "=" + Convert.ToString(value, CultureInfo.InvariantCulture));
                else if (value is Vector2 || value is Vector3 || value is Quaternion)
                    values.Add(key + "=" + JsonUtility.ToJson(value));
                else if (value is Object unityObject) values.Add(key + "=object:" + unityObject.GetInstanceID());
                else if (value is System.Collections.ICollection collection) values.Add(key + "=count:" + collection.Count);
            }
        return string.Join("\n", values);
    }
}
