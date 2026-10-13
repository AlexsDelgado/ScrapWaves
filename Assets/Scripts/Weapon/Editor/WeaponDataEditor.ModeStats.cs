using System;
using UnityEditor;
using UnityEngine;

public sealed partial class WeaponDataEditor
{
    private WeaponStatMode SelectedRuntimeMode => (WeaponStatMode)(int)_authoringMode;

    private void DrawSelectedModeOverrides(SerializedProperty row)
    {
        SerializedProperty mode = row.FindPropertyRelative(SelectedRuntimeMode.ToString());
        EditorGUILayout.LabelField(SelectedRuntimeMode == WeaponStatMode.Ability ? "Q ability" : SelectedRuntimeMode.ToString(), EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Set values replace this level's weapon value before player and heat modifiers. Other levels, paths, and modes keep their own values. Damage, travel range, and attack rate must be greater than zero.", MessageType.None);
        foreach (WeaponStatField field in Enum.GetValues(typeof(WeaponStatField)))
        {
            if (!WeaponStatCapabilities.SupportsModeField(SelectedAuthoringType, _authoringLevel,
                SelectedAuthoringPath, SelectedRuntimeMode, field)) continue;
            GetModePresentation(field, out string label, out string unit);
            DrawOptional(mode, field.ToString(), label, unit);
        }
        if (SelectedRuntimeMode == WeaponStatMode.Manual)
        {
            var instance = new WeaponInstance { Data = (WeaponData)target, Level = _authoringLevel, SelectedPath = SelectedAuthoringPath };
            bool bypassesPlayer = WeaponMath.GetPathData(instance)?.ManualAmmoOverride >= 0f;
            EditorGUILayout.LabelField("Capacity player binding", bypassesPlayer ? "Path capacity bypasses player ammo scaling" : "Scaled by player ammo multiplier", EditorStyles.miniLabel);
        }
        if (SelectedAuthoringType == WeaponType.Flamethrower && SelectedRuntimeMode != WeaponStatMode.Ability)
            EditorGUILayout.HelpBox("Inherited tick clocks retain current behavior. A set tick rate uses player attack speed; Automatic also retains its existing overheat ratio. Tick rates are limited to 100 per second. Area emission is controlled separately in shared tuning.", MessageType.None);
    }

    private void GetModePresentation(WeaponStatField field, out string label, out string unit)
    {
        bool auto = SelectedRuntimeMode == WeaponStatMode.Automatic;
        bool q = SelectedRuntimeMode == WeaponStatMode.Ability;
        unit = "";
        switch (field)
        {
            case WeaponStatField.Damage:
                label = "Damage before player / heat"; unit = "damage per hit / tick"; return;
            case WeaponStatField.RangeMeters:
                label = SelectedAuthoringType switch
                {
                    WeaponType.Flamethrower => "Area travel distance",
                    WeaponType.RocketLauncher => auto ? "Target acquisition range" : q ? "Target lock range" : "Maximum projectile travel",
                    WeaponType.Mortar => auto ? "Target acquisition range" : "Aim range",
                    WeaponType.RotatingBlade => "Slash range",
                    WeaponType.AutomaticCannon => auto ? "Target acquisition range" : SelectedAuthoringPath == WeaponUpgradePath.PathB ? "Pierce range" : "Maximum projectile travel",
                    _ => "Range"
                };
                unit = "m before player / heat"; return;
            case WeaponStatField.AttackCyclesPerSecond:
                label = SelectedAuthoringType == WeaponType.Flamethrower ? "Damage ticks per second"
                    : SelectedAuthoringType == WeaponType.RotatingBlade && auto ? "Contact ticks per second"
                    : SelectedAuthoringType == WeaponType.AutomaticCannon ? "Attack cycles per second"
                    : SelectedAuthoringType == WeaponType.RocketLauncher && auto ? "Volleys per second"
                    : "Attacks per second";
                unit = "per s before player / heat"; return;
            case WeaponStatField.AmmoCapacity: label = "Manual capacity"; unit = "ammo units"; return;
            case WeaponStatField.KnockbackStrength: label = "Knockback strength"; unit = "before hit / player scaling"; return;
            case WeaponStatField.ExplosionRadiusMeters:
                label = SelectedAuthoringType == WeaponType.Flamethrower ? "Wave radius"
                    : SelectedAuthoringType == WeaponType.Mortar ? "Shell blast radius" : "Explosion radius";
                unit = "m before player scaling"; return;
            default: label = field.ToString(); return;
        }
    }

    private void DrawSelectedSpecificOverrides(SerializedProperty row)
    {
        string payloadName = SelectedAuthoringType switch
        {
            WeaponType.AutomaticCannon => "Cannon", WeaponType.Flamethrower => "Flame",
            WeaponType.RocketLauncher => "Rocket", WeaponType.Mortar => "Mortar",
            WeaponType.RotatingBlade => "Blade", _ => null
        };
        SerializedProperty payload = payloadName == null ? null : row.FindPropertyRelative(payloadName);
        if (payload == null) return;
        SerializedProperty field = payload.Copy();
        SerializedProperty end = payload.GetEndProperty();
        bool enter = true;
        bool heading = false;
        while (field.NextVisible(enter) && !SerializedProperty.EqualContents(field, end))
        {
            enter = false;
            if (field.propertyType == SerializedPropertyType.Boolean) continue;
            if (field.depth != payload.depth + 1 || !WeaponStatCapabilities.SupportsSpecificField(
                SelectedAuthoringType, _authoringLevel, SelectedAuthoringPath, SelectedRuntimeMode, field.name)) continue;
            if (!heading)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Weapon mechanics for this context", EditorStyles.boldLabel);
                heading = true;
            }
            string label = ObjectNames.NicifyVariableName(field.name);
            string unit = WeaponStatInspectorPolicy.SpecificOverrideUnit(field.name);
            if (unit == null)
            {
                EditorGUILayout.HelpBox("Missing authoring units for " + field.name + ". Fix its presentation metadata before editing.", MessageType.Error);
                continue;
            }
            if (field.name == "GrapeshotPelletCount") label = "Total Grapeshot pellets";
            if (field.name == "GrapeshotAutomaticLandingSpreadMeters" || field.name == "GrapeshotManualLandingSpreadMeters")
                label = "Grapeshot landing spread radius";
            if (SelectedAuthoringType == WeaponType.RocketLauncher && SelectedRuntimeMode == WeaponStatMode.Ability
                && field.name.StartsWith("FragmentCone", StringComparison.Ordinal))
                label = "Child rocket " + ObjectNames.NicifyVariableName(field.name.Substring("Fragment".Length)).ToLowerInvariant();
            if (SelectedAuthoringType == WeaponType.RocketLauncher && SelectedRuntimeMode == WeaponStatMode.Ability)
            {
                if (field.name == "FragmentOrdinaryExplosionRadiusMultiplier") label = "Inherited child blast radius factor";
                if (field.name == "FragmentDamageMultiplier") label = "Child rocket fragment damage factor";
                if (field.name == "FragmentKnockbackMultiplier") label = "Parent and child knockback factor";
            }
            if (field.name == "AutomaticProjectileTravelRangeMeters") label = "Maximum flight arc travel";
            if (field.name == "ContactPuddleRadiusMeters") label = "Contact puddle radius (all modes)";
            if (field.name == "ContactPuddleDurationSeconds") label = "Contact puddle duration (all modes)";
            if (field.name == "AbilityPuddleRadiusMeters") label = "Central Q puddle radius";
            if (field.name == "AbilityPuddleDurationSeconds") label = "Central Q puddle duration";
            if (field.name == "MultiChargedTotalExplosions") label = "Total explosions per shell";
            if (field.name == "ExtraBladeCount") label = "Additional blades (0 allowed)";
            if (field.name == "AbilityHitRadiusMeters" || field.name == "AtomicHitRadiusMeters")
                label = "Q hit radius (not diameter)";
            DrawOptional(payload, field.name, label, unit);
        }
        if (SelectedAuthoringType == WeaponType.RotatingBlade)
            EditorGUILayout.HelpBox("Orbit radius, contact radius, thrust length, and dash range are separate mechanics. These controls retain the current geometry and do not reinterpret orbit radius as physical blade length.", MessageType.None);
        if (SelectedAuthoringType == WeaponType.Mortar && _authoringLevel >= 6)
            EditorGUILayout.LabelField("Current path mechanic", SelectedAuthoringPath == WeaponUpgradePath.PathA ? "Grapeshot (A)" : "Multi-Charged (B)", EditorStyles.miniLabel);
        if (SelectedAuthoringType == WeaponType.RocketLauncher && SelectedRuntimeMode == WeaponStatMode.Automatic)
            EditorGUILayout.HelpBox("A set flight travel distance is measured along the rocket's arc. Inherit retains the existing reach budget converted into arc length. Target acquisition is independent.", MessageType.None);
        if (SelectedAuthoringType == WeaponType.RocketLauncher
            || (SelectedAuthoringType == WeaponType.AutomaticCannon && SelectedRuntimeMode == WeaponStatMode.Manual
                && SelectedAuthoringPath != WeaponUpgradePath.PathB))
            EditorGUILayout.HelpBox("Maximum travel and projectile lifetime are independent limits. A projectile can expire before reaching its maximum travel distance.", MessageType.None);
    }
}
