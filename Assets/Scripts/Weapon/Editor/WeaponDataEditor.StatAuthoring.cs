using System;
using UnityEditor;
using UnityEngine;

public enum WeaponStatAuthoringMode { Automatic, Manual, Ability }

// This policy is also exercised by negative visibility tests.
public static class WeaponStatInspectorPolicy
{
    public static bool ShowsCriticalControls(WeaponType type, bool mortarEnabled = false) => type != WeaponType.Mortar || mortarEnabled;
    public static bool ShowsAbilityCooldown(WeaponStatAuthoringMode mode) => mode == WeaponStatAuthoringMode.Ability;

    public static bool ShowsProgressionAttackRate(WeaponInstance instance, WeaponStatAuthoringMode mode)
    {
        if (instance?.Data == null || mode == WeaponStatAuthoringMode.Ability
            || instance.Data.WeaponType == WeaponType.Flamethrower) return false;
        WeaponStatLevelRow row = WeaponStatAuthoringResolver.GetLevelRow(instance);
        WeaponStatModeValues values = mode == WeaponStatAuthoringMode.Manual ? row?.Manual : row?.Automatic;
        bool inheritedCadence = values?.AttackCyclesPerSecond.Enabled != true;
        // Blade contact cadence and rotation are separate consumers of the same source.
        bool inheritedSpin = instance.Data.WeaponType == WeaponType.RotatingBlade
            && mode == WeaponStatAuthoringMode.Automatic && row?.Blade?.RotationsPerSecond.Enabled != true;
        return inheritedCadence || inheritedSpin;
    }

    public static bool ShowsLevelAmmoMultiplier(WeaponInstance instance, WeaponStatAuthoringMode mode)
    {
        if (instance?.Data == null || mode != WeaponStatAuthoringMode.Manual) return false;
        if (WeaponStatAuthoringResolver.GetLevelRow(instance)?.Manual?.AmmoCapacity.Enabled == true) return false;
        WeaponUpgradePathData path = WeaponMath.GetPathData(instance);
        return path == null || path.ManualAmmoOverride < 0f;
    }

    public static bool ShowsPathAmmoOverride(WeaponInstance instance, WeaponStatAuthoringMode mode) =>
        mode == WeaponStatAuthoringMode.Manual && instance?.Data != null && WeaponMath.GetPathData(instance) != null;

    public static bool ShowsSpecificField(WeaponType type, string name, int level,
        WeaponUpgradePath path, WeaponStatAuthoringMode mode)
    {
        Type tuningType = type switch
        {
            WeaponType.AutomaticCannon => typeof(AutomaticCannonTuning),
            WeaponType.Flamethrower => typeof(FlamethrowerTuning),
            WeaponType.RocketLauncher => typeof(RocketLauncherTuning),
            WeaponType.Mortar => typeof(MortarTuning),
            WeaponType.RotatingBlade => typeof(RotatingBladeTuning),
            _ => null
        };
        if (tuningType == null || string.IsNullOrEmpty(name) || tuningType.GetField(name) == null) return false;
        bool auto = mode == WeaponStatAuthoringMode.Automatic;
        bool manual = mode == WeaponStatAuthoringMode.Manual;
        bool ability = mode == WeaponStatAuthoringMode.Ability;
        bool pathA = level >= 6 && path == WeaponUpgradePath.PathA;
        bool pathB = level >= 6 && path == WeaponUpgradePath.PathB;
        switch (type)
        {
            case WeaponType.AutomaticCannon:
                // Continuous Fire still derives inherited cadence from the old burst counts.
                // Head Hunter does not; do not expose a count that only changes diagnostics.
                if (name == nameof(AutomaticCannonTuning.CannonAutoBurstCount)) return auto && !pathB;
                if (name == nameof(AutomaticCannonTuning.CannonManualBurstCount)) return manual && !pathB;
                if (name == nameof(AutomaticCannonTuning.CannonAutoLineSpacing)) return auto && !pathA && !pathB;
                if (name == nameof(AutomaticCannonTuning.CannonManualLineSpacing)) return manual && !pathA && !pathB;
                if (name == nameof(AutomaticCannonTuning.CannonBurstShotInterval)) return !ability && !pathA && !pathB;
                if (name == nameof(AutomaticCannonTuning.CannonAutoAccuracySpreadDegrees)) return auto && !pathB;
                if (name == nameof(AutomaticCannonTuning.CannonActiveBaseBulletCount)
                    || name == nameof(AutomaticCannonTuning.CannonActiveHeatBulletStepPercent)) return ability && !pathA && !pathB;
                if (name == nameof(AutomaticCannonTuning.CannonAbilityScatterRadius)) return ability && !pathB;
                if (name.StartsWith("ContinuousFire", StringComparison.Ordinal)) return pathA && auto;
                if (name.StartsWith("HeadHunter", StringComparison.Ordinal)) return pathB && auto;
                if (name == nameof(AutomaticCannonTuning.CannonCriticalDamageMultiplierOverride)) return true;
                if (name.Contains("HeatDamage")) return auto && !pathB;
                if (name.Contains("Active") || name.Contains("Ability")) return ability;
                if (name.Contains("Auto")) return auto;
                if (name.Contains("Manual")) return manual;
                return !ability && !pathB;
            case WeaponType.Flamethrower:
                // The current moving-area renderer never consumes this legacy stream lifetime.
                if (name == nameof(FlamethrowerTuning.FlameVisualDuration)
                    || name == nameof(FlamethrowerTuning.FlameAutoConeAngle)
                    || name == nameof(FlamethrowerTuning.FlameManualConeAngle)) return false;
                if (name == nameof(FlamethrowerTuning.FlameFuelPuddleRadius)) return pathA;
                if (name == nameof(FlamethrowerTuning.FlameBurnDuration)) return !auto || pathA || pathB; // Also guards inherited Nitrogen application.
                if (name.Contains("Burn")) return !pathB && (!auto || pathA);
                if (name == nameof(FlamethrowerTuning.FlameActiveExpansionDuration)) return ability;
                if (name.Contains("Active")) return ability;
                if (name == nameof(FlamethrowerTuning.FlameManualKnockbackScale)) return manual;
                if (name == nameof(FlamethrowerTuning.FlameAutomaticRangeMultiplier)) return auto;
                if (name.Contains("ManualAmmo")) return manual;
                if (name.Contains("ManualTick")) return manual;
                if (name.Contains("AutoTick") || name.Contains("Overheat")) return auto;
                if (name.Contains("Area") || name.Contains("RangeHeat") || name.Contains("Visual")) return !ability;
                return true;
            case WeaponType.RocketLauncher:
                if (name == nameof(RocketLauncherTuning.RocketActiveBaseRocketCount)
                    || name == nameof(RocketLauncherTuning.RocketActiveInitialTargetCount)
                    || name == nameof(RocketLauncherTuning.RocketActiveTargetLockInterval)
                    || name == nameof(RocketLauncherTuning.RocketActiveConeAngle)
                    || name == nameof(RocketLauncherTuning.RocketActiveTargetMarkerRadius)) return ability && !pathB;
                // Fragmentation Q children deliberately inherit these legacy Manual sources.
                if (name == nameof(RocketLauncherTuning.RocketManualExplosionRadius)
                    || name == nameof(RocketLauncherTuning.RocketManualExplosionFalloff)
                    || name == nameof(RocketLauncherTuning.RocketManualSpeedMultiplier)) return manual || (ability && pathB);
                if (name.Contains("Active")) return ability;
                if (name.Contains("Auto")) return auto;
                if (name.Contains("Manual")) return manual;
                return false;
            case WeaponType.Mortar:
                if (name == nameof(MortarTuning.MortarArcHeight)) return !ability; // Q falls from its authored drop height with arc zero.
                if (name == nameof(MortarTuning.MortarExplosionFalloff)) return !(pathA && !ability); // Ordinary Grapeshot children use their own falloff.
                // This old field is not consumed; never present it as a functioning control.
                if (name == nameof(MortarTuning.MortarActiveShellCount)) return false;
                if (name.Contains("Active") || name.Contains("Barrage")) return ability;
                if (name.Contains("HeatFire")) return auto;
                if (name.Contains("HeatManual")) return manual;
                if (name.Contains("Auto")) return auto;
                if (name.Contains("Manual")) return manual;
                if (name == nameof(MortarTuning.MortarShellTravelTime)) return auto;
                return true;
            case WeaponType.RotatingBlade:
                if (name == nameof(RotatingBladeTuning.BladeVisualDuration)) return !(pathB && ability); // Atomic dash lifetime is its segment duration.
                if (name.StartsWith("Atomic", StringComparison.Ordinal)) return pathB && ability;
                if (name == nameof(RotatingBladeTuning.BladeActiveDamageScale)) return ability && !pathB;
                if (name.Contains("Active") && (name.Contains("RangeMultiplier") || name.Contains("HeatStep"))) return ability && !pathB;
                if (name.Contains("Knockback") && pathB) return false;
                if (name.Contains("Orbit") || name.Contains("HitRadius") || name.Contains("Spin") || name.Contains("Auto")) return auto;
                if (name == nameof(RotatingBladeTuning.BladeManualRange)) return manual || ability; // Explicit legacy source of Q thrust/dash reach.
                if (name.Contains("Manual")) return manual;
                if (name.Contains("Active")) return ability;
                return true;
            default:
                return false;
        }
    }
    // Called by DrawSpecificProperty so baseline links and presentation-only knobs remain explicit.
    public static string SpecificFieldLabel(WeaponType type, string name, int level,
        WeaponUpgradePath path, WeaponStatAuthoringMode mode, string fallback)
    {
        bool a = level >= 6 && path == WeaponUpgradePath.PathA;
        bool b = level >= 6 && path == WeaponUpgradePath.PathB;
        bool q = mode == WeaponStatAuthoringMode.Ability;
        if (type == WeaponType.AutomaticCannon)
        {
            if (name == nameof(AutomaticCannonTuning.CannonAbilityScatterRadius)) return "Q projectile scatter (degrees)";
            if (a && name == nameof(AutomaticCannonTuning.CannonAutoBurstCount)) return "Inherited Auto cadence factor (burst count)";
            if (a && name == nameof(AutomaticCannonTuning.CannonManualBurstCount)) return "Inherited Manual cadence factor (burst count)";
            if (name == nameof(AutomaticCannonTuning.CannonBurstShotInterval)) return "Minimum burst shot interval (s)";
            if (name == nameof(AutomaticCannonTuning.CannonAutoLineSpacing)) return "Auto separation timing source (m)";
            if (name == nameof(AutomaticCannonTuning.CannonManualLineSpacing)) return "Manual separation timing source (m)";
        }
        if (type == WeaponType.RocketLauncher)
        {
            if (b && q && name == nameof(RocketLauncherTuning.RocketManualExplosionRadius)) return "Cluster child legacy blast radius (m)";
            if (b && q && name == nameof(RocketLauncherTuning.RocketManualSpeedMultiplier)) return "Cluster child legacy speed factor (x)";
            if (b && q && name == nameof(RocketLauncherTuning.RocketManualExplosionFalloff)) return "Cluster child edge damage loss (fraction 0-1)";
            if (name.Contains("ExplosionFalloff")) return fallback + " (edge damage loss, fraction 0-1)";
            if (name == nameof(RocketLauncherTuning.RocketActiveTargetMarkerRadius)) return "Target marker radius (m, presentation only)";
        }
        if (type == WeaponType.Flamethrower)
        {
            if (b && name == nameof(FlamethrowerTuning.FlameBurnDuration)) return "Inherited status enable gate (> 0; legacy burn seconds)";
            if (name == nameof(FlamethrowerTuning.FlameManualRangeHeatMultiplier)) return "Auto / Manual heat travel bonus (factor)";
            if (name == nameof(FlamethrowerTuning.FlameActiveVisualDuration)) return "Q visual lifetime (s, presentation only)";
        }
        if (type == WeaponType.Mortar)
        {
            if (a && !q && (name == nameof(MortarTuning.MortarAutoExplosionRadius)
                || name == nameof(MortarTuning.MortarManualExplosionRadius))) return "Grapeshot legacy geometry source (m)";
            if (name == nameof(MortarTuning.MortarExplosionFalloff)) return "Edge damage loss (fraction 0-1)";
        }
        if (type == WeaponType.RotatingBlade)
        {
            if (q && name == nameof(RotatingBladeTuning.BladeManualRange)) return "Inherited Q / dash range source (m)";
            if (name == nameof(RotatingBladeTuning.BladeVisualDuration)) return "Visual lifetime (s, presentation only)";
        }
        return fallback;
    }

    // Explicit units for the currently authored optional payloads. Unknown future fields must
    // supply their own presentation; returning null allows the caller to flag missing metadata.
    public static string SpecificOverrideUnit(string name)
    {
        switch (name)
        {
            case "ContinuousAbilityBulletsPerSecond": return "bullets / s";
            case "ContinuousAbilityDurationSeconds":
            case "AutomaticProjectileLifetimeSeconds":
            case "ManualProjectileLifetimeSeconds":
            case "AbilityProjectileLifetimeSeconds":
            case "AbilityClusterLifetimeSeconds":
            case "NitrogenSlowDurationSeconds":
            case "HeadHunterAbilityChargeSeconds":
            case "ContactPuddleDurationSeconds":
            case "AbilityPuddleDurationSeconds":
            case "AbilityFreezeDurationSeconds":
            case "KineticDebuffDurationSeconds":
            case "GrapeshotAbilityDurationSeconds":
            case "MultiChargedRepeatDelaySeconds":
            case "BasicAbilityShellSpacingSeconds":
            case "AtomicDashSegmentSeconds":
            case "AtomicPostDashInvulnerabilitySeconds":
            case "MultiBladeActionIntervalSeconds": return "s";
            case "ContinuousAbilityHeatDurationSteps": return "1 s steps at full heat";
            case "ContinuousModeHeatStep":
            case "HeadHunterWeakPointHeatStep": return "heat fraction (0-1)";
            case "ContinuousModeHeatSpeedBonus": return "speed fraction / heat step";
            case "HeadHunterPierceDamageLossPerTarget": return "damage fraction / target";
            case "HeadHunterWeakPointMultiplier":
            case "HeadHunterWeakPointMaximum":
            case "HeadHunterEliteDamageMultiplier":
            case "HeadHunterBossDamageMultiplier":
            case "KineticDamageTakenMultiplier":
            case "KineticExplosionRadiusMultiplier":
            case "KineticExplosionFalloffMultiplier":
            case "KineticOrdinaryKnockbackMultiplier":
            case "KineticAbilityKnockbackMultiplier":
            case "FragmentOrdinaryExplosionRadiusMultiplier":
            case "FragmentKnockbackMultiplier":
            case "FragmentDamageMultiplier":
            case "AbilityClusterDamageMultiplier":
            case "AbilityClusterSpeedMultiplier":
            case "JellifiedBurnDamageMultiplier":
            case "JellifiedBurnDurationMultiplier":
            case "GrapeshotDamageMultiplier": return "x";
            case "NitrogenInitialSlowMultiplier":
            case "NitrogenMinimumSlowMultiplier":
            case "AbilitySlowMultiplier": return "remaining movement fraction";
            case "NitrogenHitsToMinimum": return "hits to minimum speed";
            case "AbilitySlowDurationPerFreezeSecond": return "slow s per freeze s";
            case "AbilityClusterFalloff": return "edge damage loss (0-1)";
            case "FragmentAdditionalAutomaticRockets": return "additional rockets";
            case "AbilityClusterRocketCount": return "child rockets";
            case "HeadHunterPierceTargetCount": return "total targets";
            case "ActiveShellCount": return "total shells";
            case "GrapeshotPelletCount": return "total pellets";
            case "ExtraBladeCount": return "additional blades";
            case "AbilityHeatShellBonus": return "extra shells at full heat";
            case "GrapeshotAbilityProjectilesPerSecond": return "shells / s";
            case "GrapeshotAbilityHeatProjectilesPerSecond": return "extra shells / s at full heat";
            case "MultiChargedTotalExplosions": return "total explosions";
            case "FragmentConeRangePerExplosionRadius": return "m per blast-radius m";
            case "AtomicRangePerHitMultiplier": return "base-range fraction / hit";
            case "RotationsPerSecond": return "rotations / s";
            case "FragmentConeAngleDegrees":
            case "GrapeshotConeAngleDegrees": // Retired once truthful landing spread is applied; never a visibility grant.
            case "ManualConeAngleDegrees":
            case "MultiBladeSpreadAngleDegrees": return "degrees";
            case "AutomaticProjectileTravelRangeMeters":
            case "AbilityProjectileTravelRangeMeters":
            case "AbilityClusterTravelRangeMeters": return "travel m";
            case "HeadHunterAbilityPierceRangeMeters": return "pierce m";
            case "ContactPuddleRadiusMeters":
            case "AbilityPuddleRadiusMeters":
            case "AbilityClusterExplosionRadiusMeters":
            case "BarrageRadiusMeters":
            case "GrapeshotAutomaticLandingSpreadMeters":
            case "GrapeshotManualLandingSpreadMeters":
            case "OrbitRadiusMeters":
            case "AutomaticHitRadiusMeters":
            case "AbilityHitRadiusMeters":
            case "AtomicHitRadiusMeters": return "radius m before player scaling";
            case "ThrustLengthMeters":
            case "AtomicDashRangeMeters": return "length m before player scaling";
            default: return null;
        }
    }

}

public sealed partial class WeaponDataEditor
{
    private int _authoringLevel = 1;
    private WeaponUpgradePath _authoringPath = WeaponUpgradePath.PathA;
    private WeaponStatAuthoringMode _authoringMode;
    private bool _showSharedTuning;

    private WeaponUpgradePath SelectedAuthoringPath => _authoringLevel < 6 ? WeaponUpgradePath.None : _authoringPath;
    private WeaponType SelectedAuthoringType => (WeaponType)serializedObject.FindProperty(nameof(WeaponData.WeaponType)).enumValueIndex;

    // Preview tooling selects context without changing or saving the asset.
    public void SetAuthoringContext(int level, WeaponUpgradePath path, WeaponStatAuthoringMode mode)
    {
        _authoringLevel = Mathf.Clamp(level, 1, 10);
        _authoringPath = path == WeaponUpgradePath.PathB ? WeaponUpgradePath.PathB : WeaponUpgradePath.PathA;
        _authoringMode = mode;
    }

    private void DrawStatAuthoring()
    {
        EditorGUILayout.LabelField("Level and mode", EditorStyles.boldLabel);
        _authoringLevel = EditorGUILayout.IntSlider("Level", _authoringLevel, 1, 10);
        if (_authoringLevel < 6)
            EditorGUILayout.LabelField("Path", "Basic");
        else
        {
            int pathIndex = _authoringPath == WeaponUpgradePath.PathB ? 1 : 0;
            string a = serializedObject.FindProperty(nameof(WeaponData.PathA))?.FindPropertyRelative("PathName")?.stringValue;
            string b = serializedObject.FindProperty(nameof(WeaponData.PathB))?.FindPropertyRelative("PathName")?.stringValue;
            pathIndex = EditorGUILayout.Popup("Advanced path", pathIndex,
                new[] { string.IsNullOrEmpty(a) ? "Path A" : "A - " + a, string.IsNullOrEmpty(b) ? "Path B" : "B - " + b });
            _authoringPath = pathIndex == 1 ? WeaponUpgradePath.PathB : WeaponUpgradePath.PathA;
        }
        _authoringMode = (WeaponStatAuthoringMode)EditorGUILayout.Popup("Mode", (int)_authoringMode,
            new[] { "Automatic", "Manual", "Q ability" });
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Overrides for this level and path", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Inherit keeps current tuning. Set changes only this exact level and path. Shared critical controls apply to eligible direct damage modes; burn remains noncritical. Mortar critical hits require the explicit switch below.", MessageType.Info);

        SerializedProperty profile = serializedObject.FindProperty(nameof(WeaponData.StatProfile));
        SerializedProperty rows = profile?.FindPropertyRelative(nameof(WeaponStatProfile.Levels));
        if (rows == null)
        {
            EditorGUILayout.HelpBox("The stat profile is missing. Restore a valid profile before authoring overrides.", MessageType.Error);
            return;
        }
        SerializedProperty row = FindSelectedRow(rows);
        if (row == null)
        {
            EditorGUILayout.LabelField("Source", "Inherited weapon tuning (no overrides)");
            if (GUILayout.Button("Add overrides for this level and path"))
            {
                // Unity array insertion duplicates the previous serialized item. A fresh
                // typed row must not borrow hidden values from another level or path.
                serializedObject.ApplyModifiedProperties();
                var data = (WeaponData)target;
                Undo.RecordObject(data, "Add weapon stat row");
                data.StatProfile.Levels.Add(WeaponStatProfile.CreateRow(
                    SelectedAuthoringType, _authoringLevel, SelectedAuthoringPath));
                EditorUtility.SetDirty(data);
                serializedObject.Update();
                rows = serializedObject.FindProperty(nameof(WeaponData.StatProfile))
                    .FindPropertyRelative(nameof(WeaponStatProfile.Levels));
                row = FindSelectedRow(rows);
            }
        }
        if (row != null)
        {
            DrawSelectedModeOverrides(row);
            DrawSelectedSpecificOverrides(row);
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Shared across eligible modes", EditorStyles.boldLabel);
            if (SelectedAuthoringType == WeaponType.Mortar)
                EditorGUILayout.PropertyField(row.FindPropertyRelative("Mortar").FindPropertyRelative("EnableCriticalHits"), new GUIContent("Enable critical hits", "Off by default. Enables critical hits only for this exact Mortar level and path, across its direct-damage modes."));
            if (WeaponStatInspectorPolicy.ShowsCriticalControls(SelectedAuthoringType, MortarCriticalEnabled(row)))
            {
                DrawOptional(row, nameof(WeaponStatLevelRow.CriticalChance), "Weapon critical chance", "%", percent: true);
                if (row.FindPropertyRelative(nameof(WeaponStatLevelRow.CriticalChance)).FindPropertyRelative(nameof(WeaponOptionalFloat.Enabled)).boolValue)
                {
                    SerializedProperty composition = row.FindPropertyRelative(nameof(WeaponStatLevelRow.CriticalChanceComposition));
                    composition.enumValueIndex = EditorGUILayout.Popup("Player chance", composition.enumValueIndex,
                        new[] { "Add percentage points to player chance", "Replace player chance" });
                }
                DrawOptional(row, nameof(WeaponStatLevelRow.CriticalDamageMultiplier), "Weapon critical damage factor", "x player critical damage");
            }
            if (WeaponStatInspectorPolicy.ShowsAbilityCooldown(_authoringMode))
            {
                DrawOptional(row, nameof(WeaponStatLevelRow.AbilityAmmoCost), "Q ammo cost", "ammo units");
                DrawOptional(row, nameof(WeaponStatLevelRow.AbilityCooldownSeconds), "Q cooldown", "s before player reduction");
            }
        }
        if (!WeaponStatInspectorPolicy.ShowsCriticalControls(SelectedAuthoringType, MortarCriticalEnabled(row)))
            EditorGUILayout.HelpBox("Mortar critical hits are off. Existing gameplay is preserved; hidden critical values are retained when the switch is off.", MessageType.Info);
        serializedObject.ApplyModifiedProperties();
        DrawNeutralAuthoringPreview();
        DrawResolvedConsumerPreview();
    }

    private static bool MortarCriticalEnabled(SerializedProperty row) =>
        row?.FindPropertyRelative("Mortar")?.FindPropertyRelative("EnableCriticalHits")?.boolValue == true;

    private SerializedProperty FindSelectedRow(SerializedProperty rows)
    {
        for (int index = 0; index < rows.arraySize; index++)
        {
            SerializedProperty row = rows.GetArrayElementAtIndex(index);
            if (row.FindPropertyRelative(nameof(WeaponStatLevelRow.WeaponType)).enumValueIndex == (int)SelectedAuthoringType
                && row.FindPropertyRelative(nameof(WeaponStatLevelRow.Level)).intValue == _authoringLevel
                && row.FindPropertyRelative(nameof(WeaponStatLevelRow.Path)).enumValueIndex == (int)SelectedAuthoringPath)
                return row;
        }
        return null;
    }

    private static void ResetOptional(SerializedProperty row, string field)
    {
        SerializedProperty optional = row.FindPropertyRelative(field);
        optional.FindPropertyRelative(nameof(WeaponOptionalFloat.Enabled)).boolValue = false;
        optional.FindPropertyRelative(nameof(WeaponOptionalFloat.Value)).floatValue = 0f;
    }

    private static void DrawOptional(SerializedProperty row, string field, string label, string unit, bool percent = false)
    {
        SerializedProperty optional = row.FindPropertyRelative(field);
        SerializedProperty enabled = optional.FindPropertyRelative(nameof(WeaponOptionalFloat.Enabled));
        SerializedProperty value = optional.FindPropertyRelative(nameof(WeaponOptionalFloat.Value));
        EditorGUILayout.BeginHorizontal();
        enabled.boolValue = EditorGUILayout.ToggleLeft(label, enabled.boolValue, GUILayout.MinWidth(150f));
        if (enabled.boolValue)
        {
            bool integer = value.propertyType == SerializedPropertyType.Integer;
            float display = integer ? value.intValue : percent ? value.floatValue * 100f : value.floatValue;
            EditorGUI.BeginChangeCheck();
            if (integer) display = EditorGUILayout.IntField(value.intValue, GUILayout.MinWidth(45f), GUILayout.MaxWidth(90f));
            else display = EditorGUILayout.FloatField(display, GUILayout.MinWidth(45f), GUILayout.MaxWidth(90f));
            if (EditorGUI.EndChangeCheck())
            {
                if (integer) value.intValue = (int)display;
                else value.floatValue = percent ? display * 0.01f : display;
            }
            GUILayout.Label(unit, GUILayout.MaxWidth(165f));
        }
        else GUILayout.Label("Inherit", EditorStyles.miniLabel);
        EditorGUILayout.EndHorizontal();
    }

    private void DrawNeutralAuthoringPreview()
    {
        var data = (WeaponData)target;
        if (data.StatProfile != null && !data.StatProfile.TryValidate(data.WeaponType, out string error))
        {
            EditorGUILayout.HelpBox(error, MessageType.Error);
            return;
        }
        var instance = new WeaponInstance { Data = data, Level = _authoringLevel, SelectedPath = SelectedAuthoringPath };
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Preview - neutral player / zero heat", EditorStyles.boldLabel);
        if (WeaponStatAuthoringResolver.SupportsCriticalHits(instance))
        {
            WeaponResolvedStat chance = WeaponStatAuthoringResolver.ResolveCriticalChance(instance, 0f);
            WeaponResolvedStat severity = WeaponStatAuthoringResolver.ResolveCriticalDamageMultiplier(instance,
                WeaponStatAuthoringResolver.GetLegacyCriticalDamageMultiplier(instance));
            EditorGUILayout.LabelField("Critical chance", (chance.Value * 100f).ToString("0.##") + "% - " + (chance.IsAuthored ? "selected row" : "player chance"));
            EditorGUILayout.LabelField("Critical damage factor", severity.Value.ToString("0.###") + "x - " + (severity.IsAuthored ? "selected row" : "weapon / path tuning"));
        }
        if (WeaponStatInspectorPolicy.ShowsAbilityCooldown(_authoringMode))
        {
            WeaponResolvedStat cooldown = WeaponStatAuthoringResolver.ResolveAbilityCooldownSeconds(instance);
            EditorGUILayout.LabelField("Q cooldown", cooldown.Value.ToString("0.###") + " s - " + (cooldown.IsAuthored ? "selected row" : "shared weapon tuning"));
        }
    }

    private void DrawSelectedProgressionRow(SerializedProperty rows)
    {
        var context = new WeaponInstance { Data = (WeaponData)target, Level = _authoringLevel, SelectedPath = SelectedAuthoringPath };
        if (rows != null)
        {
            for (int index = 0; index < rows.arraySize; index++)
            {
                SerializedProperty row = rows.GetArrayElementAtIndex(index);
                if (row.FindPropertyRelative(nameof(WeaponLevelData.Level)).intValue != _authoringLevel) continue;
                EditorGUILayout.PropertyField(row.FindPropertyRelative(nameof(WeaponLevelData.DamageMultiplier)));
                if (WeaponStatInspectorPolicy.ShowsProgressionAttackRate(context, _authoringMode))
                    EditorGUILayout.PropertyField(row.FindPropertyRelative(nameof(WeaponLevelData.AttackRateMultiplier)));
                if (WeaponStatInspectorPolicy.ShowsLevelAmmoMultiplier(context, _authoringMode))
                    EditorGUILayout.PropertyField(row.FindPropertyRelative(nameof(WeaponLevelData.ManualAmmoMultiplier)));
                return;
            }
        }
        EditorGUILayout.HelpBox("No exact level row is authored here. The current runtime fallback remains in effect; previous levels are not added together.", MessageType.Info);
    }
}
