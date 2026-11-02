using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(WeaponData))]
public sealed partial class WeaponDataEditor : Editor
{
    private SerializedProperty _specificTuning;
    private bool _showIdentityAndPresentation;

    private void OnEnable()
    {
        _specificTuning = serializedObject.FindProperty("_specificTuning");
    }

    public override void OnInspectorGUI()
    {
        float previousLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = Mathf.Clamp(EditorGUIUtility.currentViewWidth * 0.46f, 180f, 380f);
        try { DrawInspectorContents(); }
        finally { EditorGUIUtility.labelWidth = previousLabelWidth; }
    }

    private void DrawInspectorContents()
    {
        serializedObject.Update();
        DrawCoreFields();
        EditorGUILayout.Space();
        DrawStatAuthoring();
        EditorGUILayout.Space();
        _showSharedTuning = EditorGUILayout.Foldout(_showSharedTuning, "Shared weapon tuning (all levels)", true);
        if (_showSharedTuning)
        {
            EditorGUILayout.HelpBox("These source values affect every level that inherits them. Level overrides above remain independent.", MessageType.Info);
            DrawBaseCombatFields();
            EditorGUILayout.Space();
            DrawWeaponSpecificFields();
            EditorGUILayout.Space();
            DrawUpgradeFields();
        }
        serializedObject.ApplyModifiedProperties();
    }
    private void DrawCoreFields()
    {
        string displayName = serializedObject.FindProperty(nameof(WeaponData.DisplayName)).stringValue;
        EditorGUILayout.LabelField(string.IsNullOrEmpty(displayName) ? target.name : displayName, EditorStyles.boldLabel);
        _showIdentityAndPresentation = EditorGUILayout.Foldout(_showIdentityAndPresentation, "Identity and presentation", true);
        if (!_showIdentityAndPresentation) return;
        EditorGUILayout.LabelField("Identity", EditorStyles.boldLabel);
        DrawProperty(nameof(WeaponData.WeaponId));
        DrawProperty(nameof(WeaponData.DisplayName));
        DrawProperty(nameof(WeaponData.WeaponType));
        DrawProperty(nameof(WeaponData.AutoTargetingMode));
        DrawProperty(nameof(WeaponData.AutomaticAimConstraint));
        DrawProperty(nameof(WeaponData.ManualMode));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("UI", EditorStyles.boldLabel);
        DrawProperty(nameof(WeaponData.Icon));


        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Presentation", EditorStyles.boldLabel);
        DrawProperty(nameof(WeaponData.PresentationProfile));
    }

    private void DrawBaseCombatFields()
    {
        EditorGUILayout.LabelField("Shared combat sources", EditorStyles.boldLabel);
        DrawProperty(nameof(WeaponData.BaseDamage));
        if ((SelectedAuthoringType == WeaponType.RocketLauncher && _authoringMode == WeaponStatAuthoringMode.Manual)
            || (SelectedAuthoringType == WeaponType.Mortar && _authoringMode != WeaponStatAuthoringMode.Ability))
            DrawProperty(nameof(WeaponData.BaseAttackRate));
        bool usesBaseRange = SelectedAuthoringType == WeaponType.RocketLauncher || SelectedAuthoringType == WeaponType.Mortar
            || (SelectedAuthoringType == WeaponType.Flamethrower && _authoringMode != WeaponStatAuthoringMode.Ability)
            || (SelectedAuthoringType == WeaponType.AutomaticCannon && (_authoringMode == WeaponStatAuthoringMode.Automatic
                || (_authoringMode == WeaponStatAuthoringMode.Ability && SelectedAuthoringPath == WeaponUpgradePath.PathB)));
        if (usesBaseRange)
        {
            DrawProperty(nameof(WeaponData.BaseRange));
            if (_authoringMode == WeaponStatAuthoringMode.Ability)
            {
                string purpose = SelectedAuthoringType == WeaponType.Mortar ? "Inherited distance to the aimed barrage center; barrage radius controls spread."
                    : SelectedAuthoringType == WeaponType.RocketLauncher && SelectedAuthoringPath == WeaponUpgradePath.PathB
                        ? "Inherited parent and child maximum travel sources; this path does not lock targets."
                    : SelectedAuthoringType == WeaponType.AutomaticCannon
                        ? "Inherited pierce reach source, with the existing minimum of 1000 m. The exact Q override above replaces that reach."
                    : null;
                if (purpose != null) EditorGUILayout.HelpBox(purpose, MessageType.None);
            }
        }
        if (!(SelectedAuthoringType == WeaponType.Flamethrower && _authoringMode == WeaponStatAuthoringMode.Automatic)
            && !(SelectedAuthoringType == WeaponType.RotatingBlade && SelectedAuthoringPath == WeaponUpgradePath.PathB))
            DrawProperty(nameof(WeaponData.BaseKnockback));
        if (_authoringMode == WeaponStatAuthoringMode.Manual)
            DrawProperty(nameof(WeaponData.BaseManualAmmo));
        if (_authoringMode == WeaponStatAuthoringMode.Ability)
        {
            if (SelectedAuthoringType == WeaponType.AutomaticCannon && SelectedAuthoringPath == WeaponUpgradePath.PathA)
                EditorGUILayout.HelpBox("Continuous Fire inherits its existing 80-ammo Q cost. Use the exact level Q ammo override above to change it.", MessageType.None);
            else DrawProperty(nameof(WeaponData.ActiveAbilityAmmoCost));
            DrawProperty(nameof(WeaponData.SkillCooldown));
        }
    }
    private void DrawWeaponSpecificFields()
    {
        SerializedProperty weaponTypeProperty = serializedObject.FindProperty(nameof(WeaponData.WeaponType));
        WeaponType weaponType = (WeaponType)weaponTypeProperty.enumValueIndex;
        WeaponSpecificTuning expected = WeaponData.CreateSpecificTuning(weaponType);
        object current = _specificTuning.managedReferenceValue;
        if (expected == null || current == null || current.GetType() != expected.GetType())
        {
            EditorGUILayout.HelpBox("No matching tuning payload is initialized. Initializing it retains the other weapon payloads.", MessageType.Info);
            if (GUILayout.Button("Initialize selected weapon tuning"))
            {
                serializedObject.ApplyModifiedProperties();
                Undo.RecordObject(target, "Initialize weapon tuning");
                ((WeaponData)target).EnsureSpecificTuningForCurrentType();
                EditorUtility.SetDirty(target);
                serializedObject.Update();
            }
            return;
        }

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
        DrawSpecificProperty(nameof(AutomaticCannonTuning.CannonManualLineSpacing), "Manual Projectile Separation");
        DrawSpecificProperty(nameof(AutomaticCannonTuning.CannonAutoLineSpacing), "Auto Projectile Separation");
        DrawSpecificProperty(nameof(AutomaticCannonTuning.CannonAutoAccuracySpreadDegrees), "Auto Accuracy Spread Degrees");
        DrawSpecificProperty(nameof(AutomaticCannonTuning.CannonBurstProjectileScatterDegrees), "Burst Projectile Scatter Degrees");
        DrawSpecificProperty(nameof(AutomaticCannonTuning.CannonBurstShotInterval), "Burst Shot Interval");
        DrawSpecificProperty(nameof(AutomaticCannonTuning.CannonAutoBurstsPerSecond), "Auto Bursts Per Second");
        DrawSpecificProperty(nameof(AutomaticCannonTuning.CannonManualBurstsPerSecond), "Manual Bursts Per Second");
        DrawSpecificProperty(nameof(AutomaticCannonTuning.ContinuousFireAutoAttackSpeedMultiplier), "Continuous Fire Auto Speed Multiplier");
        DrawSpecificProperty(nameof(AutomaticCannonTuning.HeadHunterAutoAttackSpeedMultiplier), "Head Hunter Auto Speed Multiplier");

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
        EditorGUILayout.LabelField("Manual Areas", EditorStyles.boldLabel);
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameAreaEmissionInterval), "Emission Interval (s)");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameAreaLifetime), "Lifetime (s)");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameAreaTimeToStop), "Time to Stop (s)");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameAreaInitialRadius), "Initial Radius (m)");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameAreaFinalRadius), "Final Radius (m)");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameAreaGrowthDuration), "Growth Duration (s)");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameAutomaticRangeMultiplier), "Automatic / Manual Travel");

        EditorGUILayout.Space();
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameAutoTickInterval), "Auto Tick Interval");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameOverheatAutoTickInterval), "Overheat Auto Tick Interval");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameOverheatTickThresholdPercent), "Overheat Tick Threshold Percent");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameManualTickInterval), "Manual Tick Interval");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameManualAmmoPerSecond), "Manual Ammo Per Second");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameManualRangeHeatMultiplier), "Manual Range Heat Multiplier");

        EditorGUILayout.Space();
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameBurnDuration), "Burn Duration");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameFuelPuddleRadius), "Fuel Contact Puddle Radius (m)");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameBurnTickInterval), "Burn Tick Interval");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameBurnDamageScale), "Burn Damage Scale");

        EditorGUILayout.Space();
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameActiveRadius), "Active Radius");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameActiveExpansionDuration), "Q Expansion Duration");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameActiveDamageScale), "Active Damage Scale");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameManualKnockbackScale), "Manual Knockback Scale");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameActiveKnockbackScale), "Active Knockback Scale");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameVisualDuration), "Stream Visual Duration");
        DrawSpecificProperty(nameof(FlamethrowerTuning.FlameActiveVisualDuration), "Active Visual Duration");
    }

    private void DrawRocketLauncherFields()
    {
        EditorGUILayout.LabelField("Rocket Launcher", EditorStyles.boldLabel);
        EditorGUILayout.LabelField("Automatic", EditorStyles.boldLabel);
        DrawSpecificProperty(nameof(RocketLauncherTuning.RocketAutoBurstsPerSecond), "Auto Bursts Per Second");
        DrawSpecificProperty(nameof(RocketLauncherTuning.RocketAutoVolleyShotInterval), "Auto Volley Shot Interval (s)");
        DrawSpecificProperty(nameof(RocketLauncherTuning.RocketAutoBaseRocketCount), "Auto Base Rocket Count");
        DrawSpecificProperty(nameof(RocketLauncherTuning.RocketAutoHeatBonuses), "Auto Heat Bonuses", includeChildren: true);
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Active Ability", EditorStyles.boldLabel);
        DrawSpecificProperty(nameof(RocketLauncherTuning.RocketActiveBaseRocketCount), "Active Base Rocket Count");
        DrawSpecificProperty(nameof(RocketLauncherTuning.RocketActiveInitialTargetCount), "Active Initial Target Count");
        DrawSpecificProperty(nameof(RocketLauncherTuning.RocketActiveTargetLockInterval), "Active Target Lock Interval");
        DrawSpecificProperty(nameof(RocketLauncherTuning.RocketActiveConeAngle), "Active Cone Angle");
        DrawSpecificProperty(nameof(RocketLauncherTuning.RocketActiveTargetMarkerRadius), "Active Target Marker Radius");

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
        EditorGUILayout.LabelField("Manual pitch to horizontal range", EditorStyles.boldLabel);
        DrawSpecificProperty(nameof(MortarTuning.MortarManualMinimumPitch), "Minimum Pitch (degrees)");
        DrawSpecificProperty(nameof(MortarTuning.MortarManualMinimumRange), "Minimum Range (m)");
        DrawSpecificProperty(nameof(MortarTuning.MortarManualLevelPitch), "Middle Pitch (degrees)");
        DrawSpecificProperty(nameof(MortarTuning.MortarManualLevelRange), "Middle Range (m)");
        DrawSpecificProperty(nameof(MortarTuning.MortarManualMaximumPitch), "Maximum Pitch (degrees)");
        DrawSpecificProperty(nameof(MortarTuning.MortarManualMaximumRange), "Maximum Range (m)");
        DrawSpecificProperty(nameof(MortarTuning.MortarManualSurfaceProbeAbove), "Surface Search Above (m)");
        DrawSpecificProperty(nameof(MortarTuning.MortarManualSurfaceProbeBelow), "Surface Search Below (m)");
        EditorGUILayout.Space();
        DrawSpecificProperty(nameof(MortarTuning.MortarAutoExplosionRadius), "Auto Explosion Radius");
        DrawSpecificProperty(nameof(MortarTuning.MortarManualExplosionRadius), "Manual Explosion Radius");
        DrawSpecificProperty(nameof(MortarTuning.MortarActiveExplosionRadius), "Active Explosion Radius");

        EditorGUILayout.Space();
        DrawSpecificProperty(nameof(MortarTuning.MortarAutoAccuracyRadius), "Auto Accuracy Radius");
        // Retained in assets for compatibility; precision manual aiming has no random scatter.
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
        DrawSpecificProperty(nameof(RotatingBladeTuning.AtomicDashBaseRangeMultiplier), "Atomic Dash Base Range Multiplier");
        DrawSpecificProperty(nameof(RotatingBladeTuning.AtomicDashRangePerHitMultiplier), "Atomic Dash Range Per Hit Multiplier");
        DrawSpecificProperty(nameof(RotatingBladeTuning.BladeVisualDuration), "Visual Duration");
    }

    private void DrawUpgradeFields()
    {
        EditorGUILayout.LabelField("Selected progression source", EditorStyles.boldLabel);
        var context = new WeaponInstance { Data = (WeaponData)target, Level = _authoringLevel, SelectedPath = SelectedAuthoringPath };
        if (context.Data.StatProfile != null && !context.Data.StatProfile.TryValidate(SelectedAuthoringType, out _)) return;
        if (_authoringMode == WeaponStatAuthoringMode.Ability)
            EditorGUILayout.HelpBox("Q spends the Manual magazine. Edit its shared capacity in Manual mode; Q ammo cost and timing are separate controls.", MessageType.None);
        else if (_authoringMode == WeaponStatAuthoringMode.Manual)
            EditorGUILayout.HelpBox("Manual magazine capacity is shared with Q. An exact capacity override replaces the inherited level multiplier and path capacity.", MessageType.None);
        if (_authoringLevel < 6)
            DrawSelectedProgressionRow(serializedObject.FindProperty(nameof(WeaponData.LevelData)));
        else
        {
            SerializedProperty path = serializedObject.FindProperty(SelectedAuthoringPath == WeaponUpgradePath.PathA
                ? nameof(WeaponData.PathA) : nameof(WeaponData.PathB));
            if (path != null)
            {
                EditorGUILayout.PropertyField(path.FindPropertyRelative(nameof(WeaponUpgradePathData.PathName)));
                EditorGUILayout.PropertyField(path.FindPropertyRelative(nameof(WeaponUpgradePathData.DamageMultiplier)));
                if (WeaponStatInspectorPolicy.ShowsProgressionAttackRate(context, _authoringMode))
                    EditorGUILayout.PropertyField(path.FindPropertyRelative(nameof(WeaponUpgradePathData.AttackRateMultiplier)));
                if (WeaponStatInspectorPolicy.ShowsPathAmmoOverride(context, _authoringMode))
                {
                    EditorGUILayout.PropertyField(path.FindPropertyRelative(nameof(WeaponUpgradePathData.ManualAmmoOverride)));
                    EditorGUILayout.HelpBox("Negative inherits capacity and applies player ammo scaling. Zero or positive sets a fixed capacity without player scaling. With an exact capacity override, only this sign still controls player scaling; the exact value supplies capacity.", MessageType.None);
                }
                DrawSelectedProgressionRow(path.FindPropertyRelative(nameof(WeaponUpgradePathData.LevelData)));
            }
        }
    }
    private void DrawProperty(string propertyName, bool includeChildren = false)
    {
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property != null)
            EditorGUILayout.PropertyField(property, includeChildren);
    }

    private void DrawSpecificProperty(string propertyName, string label, bool includeChildren = false)
    {
        if (!WeaponStatInspectorPolicy.ShowsSpecificField(SelectedAuthoringType, propertyName,
            _authoringLevel, SelectedAuthoringPath, _authoringMode)) return;
        SerializedProperty property = _specificTuning.FindPropertyRelative(propertyName);
        if (property != null)
        {
            label = WeaponStatInspectorPolicy.SpecificFieldLabel(SelectedAuthoringType, propertyName,
                _authoringLevel, SelectedAuthoringPath, _authoringMode, label);
            EditorGUILayout.PropertyField(property, new GUIContent(label, property.tooltip), includeChildren);
        }
    }
}
