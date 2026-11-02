# Shared weapon stat composition contract

This records the preserved live composition used by the stat-authoring implementation. Historical CSV bindings are not migration rules. The approved plan (Library `libfile_e391263b9d908191a4cf1f210bc8cb06`, version 1, player bonus composition on page 3 and appendix introduction) explicitly describes partial effectiveness as a proposed contract requiring approval and requires preserving live formulas meanwhile.

## Current runtime

- **Exact progression:** select the current weapon type, level and path. Basic levels 1-5 use None; advanced levels 6-10 use A or B. An enabled mode value replaces its neutral weapon quantity. Disabled values preserve the current numeric formula. No previous level is accumulated; level 5 is not applied again to level 6.
- **Damage:** inherited damage starts at WeaponData.BaseDamage, then applies the selected exact level and path factors. Authored direct damage replaces that composed weapon baseline, including a caller's fixed weapon-mode damage factor; those factors become 1. Both routes then apply player DamageMultiplier, AbilityDamageMultiplier only for ability damage, eligible critical severity, EliteDamageMultiplier only for elite/boss targets, the existing positional range multiplier, and the action/impact scales once. The existing minimum integer damage of 1 and rounding remain. Burn can explicitly retain its separate inherited damage source while keeping the captured action mode.
- **Critical chance:** inherited chance uses the player stat. Enabled AddToPlayer adds the authored fraction to that stat; ReplacePlayer uses the authored fraction alone. Final chance is clamped to 0-1. Every eligible action consumes exactly one random sample, including authored zero/one; zero never crits and one always crits. Ineligible channels consume none. Mortar remains ineligible unless the selected row opts in. Burn stays ineligible.
- **Critical severity:** the enabled authored factor replaces only the weapon's severity factor. The critical multiplier remains max(1, player CriticalDamage * weapon factor). An inherited Cannon factor still includes its existing advanced-B factor. This is independent of chance composition.
- **Knockback:** strength * player Knockback scale * (1 + sqrt(nonnegative damage) * 0.25) * caller scale * falloff. Existing mode/path factors remain in caller scales. Authored strength is captured with the action's exact mode/key. Inherited base strength and the player bonus retain their old impact-time sampling.
- **Ammo capacity:** an enabled manual capacity replaces the neutral level-composed capacity. Player AmmoMultiplier applies unless the selected legacy path has ManualAmmoOverride >= 0; that path's existing player-scaling bypass remains. Q ammo cost is independent of capacity. An enabled Q cost replaces the effective legacy cost (80 for Cannon path A, otherwise ActiveAbilityAmmoCost). Zero is free; each weapon's existing full/partial-spend rule remains.
- **Cooldown:** an enabled duration replaces the weapon's base Q cooldown, then the existing player reduction is applied once: base * (1 - clamp(reduction, 0, 0.95)). Ordinary reload and switch behavior is unchanged. An eligible Q action validates authoring before spending resources.
- **Cadence and geometry:** selected neutral rate/range/area values retain their existing eligible player and heat consumers. The shared GetStatScale helper returns 1 for a missing definition and otherwise clamps its multiplier to at least 0.01. Weapon-specific consumers and units are recorded separately in the 72-row traceability matrix; this change does not add arbitrary stat rebinding.
- **Sampling and attribution:** direct damage, critical outcome, selected authored strength and source type/level/path/mode are captured when the context is created. Copying a context preserves them. Damage attribution labels use that captured key after switches/upgrades. Positional range bonuses retain impact-time evaluation.

## Preserved metadata and unimplemented proposal

`WeaponStatScaling.StatType` and `Effectiveness` are serialized metadata on legacy `BalanceStats` and `UpgradeSpecificStats` rows. They have no current gameplay consumer. They remain hidden and preserved, including manually entered values. They are not exposed as functioning runtime authoring controls and are not claimed as live mutation coverage.

The plan proposes `1 + effectiveness * (player multiplier - 1)`, with 0%, 50% and 100% checks, and explicitly requires confirmation before implementing it. That balance proposal and selectable arbitrary scaling bindings remain unimplemented. Current player composition above is the numeric authority. No CSV value or scaling metadata is automatically imported.

## Evidence references

- `WeaponStatAuthoringTests.CriticalChance_ComposesInExplicitFractionUnits`: resolver add/replace/clamp cases.
- `WeaponModeResolutionTests.CriticalComposition_ChangesActualRollAndDamageWithTheSameSampleAndPlayerSeverity`: actual context rolls and damage across automatic/manual/Q, same random sample, additive versus replacement chance, factor3 * player severity2, disabled fallback, and fired-context isolation.
- `WeaponStatAuthoringTests.CannonProjectile_AuthoredCriticalControlsChangeActualTargetHealth`: real Cannon projectile collision and target HP.
- `WeaponStatAuthoringTests.CriticalRoll_UsesDeterministicBoundaryAndConsumesOneSampleAtZeroAndOne`: chance boundaries and random-stream sampling.
- `WeaponStatAuthoringTests.ExactRows_IsolateWeaponsLevelsPathsAndLevelFiveToSixTransitions`: exact-key isolation and both advanced paths.
- `WeaponModeResolutionTests.AuthoredDamage_ReplacesNeutralLevelPathAndModeFactorsBeforePlayerAndDynamicScaling`: combined damage/player/ability/dynamic factors without reapplying level/path/fixed-mode multipliers.
- `WeaponModeResolutionTests.CapturedModeAndAuthoredKnockback_SurviveModePathLevelAndProfileChanges` and `DisabledKnockback_PreservesImpactTimeLegacyStrengthSampling`: captured versus preserved legacy sampling.
- `WeaponStatAuthoringTests.CannonQ_StartsResolvedCooldownAndRestartsAfterRepeatedReloads` and `Switching_RefreshesOnlyIncomingCooldownAndPreservesBothAuthoredDurations`: repeated Q/reload and switching behavior.
- `WeaponModeResolutionTests.AbilityAmmoCost_ReplacesEffectiveLegacyAndPreservesExactKeyAndCapacityIsolation`: all five weapon types, free Q, exact keys, capacity independence and disabled fallback. Weapon-specific suites exercise actual ability spending.
- `WeaponBalanceStartupTests`: preservation of legacy rows containing explicit StatType and Effectiveness sentinels, rather than runtime scaling claims.

These are test definitions and source evidence. Execution status belongs to the validation logs/report; compilation alone is not a Unity gameplay test result.