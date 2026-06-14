using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WeaponData))]
public sealed class WeaponDataEditor : Editor
{
    private SerializedProperty _specificTuning;

    private void OnEnable()
    {
        _specificTuning = serializedObject.FindProperty("_specificTuning");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        DrawCoreFields();
        EditorGUILayout.Space();
        DrawBaseCombatFields();
        EditorGUILayout.Space();
        DrawWeaponSpecificFields();
        EditorGUILayout.Space();
        DrawUpgradeFields();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawCoreFields()
    {
        EditorGUILayout.LabelField("Identity", EditorStyles.boldLabel);
        DrawProperty(nameof(WeaponData.WeaponId));
        DrawProperty(nameof(WeaponData.DisplayName));
        DrawProperty(nameof(WeaponData.WeaponType));
        DrawProperty(nameof(WeaponData.AutoTargetingMode));
        DrawProperty(nameof(WeaponData.ManualMode));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("UI", EditorStyles.boldLabel);
        DrawProperty(nameof(WeaponData.Icon));
        DrawProperty(nameof(WeaponData.SkillCooldown));
    }

    private void DrawBaseCombatFields()
    {
        EditorGUILayout.LabelField("Base Combat", EditorStyles.boldLabel);
        DrawProperty(nameof(WeaponData.BaseDamage));
        DrawProperty(nameof(WeaponData.BaseAttackRate));
        DrawProperty(nameof(WeaponData.BaseRange));
        DrawProperty(nameof(WeaponData.BaseKnockback));
        DrawProperty(nameof(WeaponData.BaseManualAmmo));
        DrawProperty(nameof(WeaponData.ActiveAbilityAmmoCost));
    }

    private void DrawWeaponSpecificFields()
    {
        SerializedProperty weaponTypeProperty = serializedObject.FindProperty(nameof(WeaponData.WeaponType));
        WeaponType weaponType = (WeaponType)weaponTypeProperty.enumValueIndex;
        EnsureSpecificTuning(weaponType);

        switch (weaponType)
        {
            case WeaponType.AutomaticCannon:
                DrawAutomaticCannonFields();
                break;
            case WeaponType.Flamethrower:
                DrawFlamethrowerFields();
                break;
            case WeaponType.RocketLauncher:
                DrawRocketLauncherFields();
                break;
            case WeaponType.Mortar:
                DrawMortarFields();
                break;
            case WeaponType.RotatingBlade:
                DrawRotatingBladeFields();
                break;
            default:
                EditorGUILayout.LabelField("Weapon Specific", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("No weapon-specific tuning fields for this weapon type yet.", MessageType.Info);
                break;
        }
    }

    private void DrawAutomaticCannonFields()
    {
        EditorGUILayout.LabelField("Automatic Cannon", EditorStyles.boldLabel);
        DrawSpecificProperty(nameof(AutomaticCannonTuning.CannonAutoBurstCount), "Auto Burst Count");
        DrawSpecificProperty(nameof(AutomaticCannonTuning.CannonManualBurstCount), "Manual Burst Count");
        DrawSpecificProperty(nameof(AutomaticCannonTuning.CannonActiveBaseBulletCount), "Active Base Bullet Count");

        EditorGUILayout.Space();
        DrawSpecificProperty(nameof(AutomaticCannonTuning.CannonAbilityScatterRadius), "Ability Scatter Radius");
        DrawSpecificProperty(nameof(AutomaticCannonTuning.CannonManualLineSpacing), "Manual Line Spacing");
        DrawSpecificProperty(nameof(AutomaticCannonTuning.CannonAutoLineSpacing), "Auto Line Spacing");
        DrawSpecificProperty(nameof(AutomaticCannonTuning.CannonAutoAccuracySpreadDegrees), "Auto Accuracy Spread Degrees");

        EditorGUILayout.Space();
        DrawSpecificProperty(nameof(AutomaticCannonTuning.CannonActiveHeatBulletStepPercent), "Active Heat Bullet Step Percent");
        DrawSpecificProperty(nameof(AutomaticCannonTuning.CannonHeatDamageThresholdStepPercent), "Heat Damage Threshold Step Percent");
        DrawSpecificProperty(nameof(AutomaticCannonTuning.CannonHeatDamageThresholdCount), "Heat Damage Threshold Count");
        DrawSpecificProperty(nameof(AutomaticCannonTuning.CannonHeatDamageBonusPerThreshold), "Heat Damage Bonus Per Threshold");
        DrawSpecificProperty(nameof(AutomaticCannonTuning.CannonCriticalDamageMultiplierOverride), "Critical Damage Multiplier Override");
    }

    private void DrawFlamethrowerFields()
    {
        EditorGUILayout.LabelField("Flamethrower", EditorStyles.boldLabel);
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameMaxTargetsPerTick), "Max Targets Per Tick");

        EditorGUILayout.Space();
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameHoseRadius), "Hose Radius");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameHoseSegmentCount), "Hose Segment Count");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameHoseNearFollow), "Hose Near Follow");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameHoseFarFollow), "Hose Far Follow");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameHoseTurbulence), "Hose Turbulence");

        EditorGUILayout.Space();
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameAutoTickInterval), "Auto Tick Interval");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameOverheatAutoTickInterval), "Overheat Auto Tick Interval");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameOverheatTickThresholdPercent), "Overheat Tick Threshold Percent");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameManualTickInterval), "Manual Tick Interval");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameManualAmmoPerSecond), "Manual Ammo Per Second");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameManualRangeHeatMultiplier), "Manual Range Heat Multiplier");

        EditorGUILayout.Space();
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameBurnDuration), "Burn Duration");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameBurnTickInterval), "Burn Tick Interval");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameBurnDamageScale), "Burn Damage Scale");

        EditorGUILayout.Space();
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameActiveRadius), "Active Radius");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameActiveDamageScale), "Active Damage Scale");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameManualKnockbackScale), "Manual Knockback Scale");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameActiveKnockbackScale), "Active Knockback Scale");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameVisualDuration), "Stream Visual Duration");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameActiveVisualDuration), "Active Visual Duration");
    }

    private void DrawRocketLauncherFields()
    {
        EditorGUILayout.LabelField("Rocket Launcher", EditorStyles.boldLabel);
        DrawSpecificProperty(nameof(RocketLauncherTuning.RocketAutoBaseRocketCount), "Auto Base Rocket Count");
        DrawSpecificProperty(nameof(RocketLauncherTuning.RocketActiveBaseRocketCount), "Active Base Rocket Count");
        DrawSpecificProperty(nameof(RocketLauncherTuning.RocketActiveConeAngle), "Active Cone Angle");

        EditorGUILayout.Space();
        DrawSpecificProperty(nameof(RocketLauncherTuning.RocketAutoExplosionRadius), "Auto Explosion Radius");
        DrawSpecificProperty(nameof(RocketLauncherTuning.RocketManualExplosionRadius), "Manual Explosion Radius");
        DrawSpecificProperty(nameof(RocketLauncherTuning.RocketActiveExplosionRadius), "Active Explosion Radius");

        EditorGUILayout.Space();
        DrawSpecificProperty(nameof(RocketLauncherTuning.RocketAutoExplosionFalloff), "Auto Explosion Falloff");
        DrawSpecificProperty(nameof(RocketLauncherTuning.RocketManualExplosionFalloff), "Manual Explosion Falloff");
        DrawSpecificProperty(nameof(RocketLauncherTuning.RocketActiveExplosionFalloff), "Active Explosion Falloff");

        EditorGUILayout.Space();
        DrawSpecificProperty(nameof(RocketLauncherTuning.RocketAutoSpeedMultiplier), "Auto Speed Multiplier");
        DrawSpecificProperty(nameof(RocketLauncherTuning.RocketManualSpeedMultiplier), "Manual Speed Multiplier");
        DrawSpecificProperty(nameof(RocketLauncherTuning.RocketActiveSpeedMultiplier), "Active Speed Multiplier");
        DrawSpecificProperty(nameof(RocketLauncherTuning.RocketActiveDamageScale), "Active Damage Scale");
    }

    private void DrawMortarFields()
    {
        EditorGUILayout.LabelField("Mortar", EditorStyles.boldLabel);
        DrawSpecificProperty(nameof(MortarTuning.MortarAutoExplosionRadius), "Auto Explosion Radius");
        DrawSpecificProperty(nameof(MortarTuning.MortarManualExplosionRadius), "Manual Explosion Radius");
        DrawSpecificProperty(nameof(MortarTuning.MortarActiveExplosionRadius), "Active Explosion Radius");

        EditorGUILayout.Space();
        DrawSpecificProperty(nameof(MortarTuning.MortarAutoAccuracyRadius), "Auto Accuracy Radius");
        DrawSpecificProperty(nameof(MortarTuning.MortarManualAccuracyRadius), "Manual Accuracy Radius");
        DrawSpecificProperty(nameof(MortarTuning.MortarBarrageRadius), "Barrage Radius");

        EditorGUILayout.Space();
        DrawSpecificProperty(nameof(MortarTuning.MortarShellTravelTime), "Shell Travel Time");
        DrawSpecificProperty(nameof(MortarTuning.MortarManualTravelTime), "Manual Travel Time");
        DrawSpecificProperty(nameof(MortarTuning.MortarActiveTravelTime), "Active Travel Time");
        DrawSpecificProperty(nameof(MortarTuning.MortarShellCollisionRadius), "Shell Collision Radius");
        DrawSpecificProperty(nameof(MortarTuning.MortarActiveDropHeight), "Active Drop Height");

        EditorGUILayout.Space();
        DrawSpecificProperty(nameof(MortarTuning.MortarExplosionFalloff), "Explosion Falloff");
        DrawSpecificProperty(nameof(MortarTuning.MortarArcHeight), "Arc Height");
        DrawSpecificProperty(nameof(MortarTuning.MortarActiveShellCount), "Active Shell Count");
        DrawSpecificProperty(nameof(MortarTuning.MortarActiveDamageScale), "Active Damage Scale");
        DrawSpecificProperty(nameof(MortarTuning.MortarHeatFireRateBonusAbove50), "Heat Fire Rate Bonus Above 50");
        DrawSpecificProperty(nameof(MortarTuning.MortarHeatManualSpeedBonus), "Heat Manual Speed Bonus");
    }

    private void DrawRotatingBladeFields()
    {
        EditorGUILayout.LabelField("Rotating Blade", EditorStyles.boldLabel);
        DrawSpecificProperty(nameof(RotatingBladeTuning.BladeOrbitRadius), "Orbit Radius");
        DrawSpecificProperty(nameof(RotatingBladeTuning.BladeHitRadius), "Hit Radius");
        DrawSpecificProperty(nameof(RotatingBladeTuning.BladeAutoDamageInterval), "Auto Damage Interval");
        DrawSpecificProperty(nameof(RotatingBladeTuning.BladeBaseSpinDegreesPerSecond), "Base Spin Degrees Per Second");

        EditorGUILayout.Space();
        DrawSpecificProperty(nameof(RotatingBladeTuning.BladeManualConeAngle), "Manual Cone Angle");
        DrawSpecificProperty(nameof(RotatingBladeTuning.BladeManualRange), "Manual Range");
        DrawSpecificProperty(nameof(RotatingBladeTuning.BladeManualCooldown), "Manual Cooldown");
        DrawSpecificProperty(nameof(RotatingBladeTuning.BladeManualMaxHeatDamageBonus), "Manual Max Heat Damage Bonus");

        EditorGUILayout.Space();
        DrawSpecificProperty(nameof(RotatingBladeTuning.BladeAutoKnockbackScale), "Auto Knockback Scale");
        DrawSpecificProperty(nameof(RotatingBladeTuning.BladeAutoMaxHeatKnockbackBonus), "Auto Max Heat Knockback Bonus");
        DrawSpecificProperty(nameof(RotatingBladeTuning.BladeManualKnockbackScale), "Manual Knockback Scale");
        DrawSpecificProperty(nameof(RotatingBladeTuning.BladeActiveKnockbackScale), "Active Knockback Scale");

        EditorGUILayout.Space();
        DrawSpecificProperty(nameof(RotatingBladeTuning.BladeActiveBaseRangeMultiplier), "Active Base Range Multiplier");
        DrawSpecificProperty(nameof(RotatingBladeTuning.BladeActiveMaxRangeMultiplier), "Active Max Range Multiplier");
        DrawSpecificProperty(nameof(RotatingBladeTuning.BladeActiveHeatStepPercent), "Active Heat Step Percent");
        DrawSpecificProperty(nameof(RotatingBladeTuning.BladeActiveLineWidth), "Active Line Width");
        DrawSpecificProperty(nameof(RotatingBladeTuning.BladeActiveDamageScale), "Active Damage Scale");
        DrawSpecificProperty(nameof(RotatingBladeTuning.BladeVisualDuration), "Visual Duration");
    }

    private void DrawUpgradeFields()
    {
        EditorGUILayout.LabelField("Levels And Paths", EditorStyles.boldLabel);
        DrawProperty(nameof(WeaponData.LevelData), includeChildren: true);
        DrawProperty(nameof(WeaponData.PathA), includeChildren: true);
        DrawProperty(nameof(WeaponData.PathB), includeChildren: true);
    }

    private void EnsureSpecificTuning(WeaponType weaponType)
    {
        WeaponSpecificTuning expected = WeaponData.CreateSpecificTuning(weaponType);
        object current = _specificTuning.managedReferenceValue;

        if (expected == null)
        {
            if (current != null)
                _specificTuning.managedReferenceValue = null;
            return;
        }

        if (current == null || current.GetType() != expected.GetType())
            _specificTuning.managedReferenceValue = expected;
    }

    private void DrawProperty(string propertyName, bool includeChildren = false)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            EditorGUILayout.PropertyField(property, includeChildren);
    }

    private void DrawSpecificProperty(string propertyName, string label)
    {
        SerializedProperty property = _specificTuning.FindPropertyRelative(propertyName);
        if (property != null)
            EditorGUILayout.PropertyField(property, new GUIContent(label));
    }
}
