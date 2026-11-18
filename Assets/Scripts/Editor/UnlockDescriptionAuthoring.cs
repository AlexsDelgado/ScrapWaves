using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// Writes the player-facing descriptions into weapon / passive assets and adds a description
/// field to the authored Unlocks detail panel. Idempotent: reruns only refresh the text.
/// </summary>
public static class UnlockDescriptionAuthoring
{
    private const string ScenePath = "Assets/Scenes/TitleScreen.unity";

    private static readonly Dictionary<string, string> WeaponDescriptions = new()
    {
        ["AutomaticCannon"] =
            "Rapid-fire cannon that locks onto the nearest enemy in front of you. Hold fire for precise manual shots.\n" +
            "Specialty: sustained single-target damage.\n" +
            "Active: unleashes a volley of bullets that grows with Heat.",
        ["RocketLauncher"] =
            "Fires rockets that explode on impact and hurt everything in the blast.\n" +
            "Specialty: area damage at long range.\n" +
            "Active (hold): lock onto several targets, release to send a rocket at each one.",
        ["Mortar"] =
            "Lobs shells that land after a short delay and explode in an area.\n" +
            "Specialty: heavy area damage on groups, even behind cover.\n" +
            "Active: calls down a barrage of shells around the target area.",
        ["Flamethrower"] =
            "Short-range stream of fire that keeps burning enemies it touches.\n" +
            "Specialty: constant damage on crowds up close.\n" +
            "Active: a wave of fire expands outward from you and pushes enemies back.",
        ["RotatingBlade"] =
            "A blade orbits around you and cuts anything it touches. In manual mode, hold fire for cone slashes.\n" +
            "Specialty: close-range defense and crowd shredding.\n" +
            "Active: a thick thrust forward whose reach grows with Heat."
    };

    private static readonly Dictionary<string, string> PassiveDescriptions = new()
    {
        ["Shop_Head_BountyHunterModule"] = "Deal more damage to elite enemies.",
        ["Shop_Head_ScavengerModule"] = "Enemies drop more scrap and materials.",
        ["Shop_Head_SharpshooterModule"] = "Deal more damage to distant enemies.",
        ["Shop_Head_CQBModule"] = "Deal more damage to nearby enemies.",
        ["Shop_Core_PlatedReactor"] = "Raises your maximum health.",
        ["Shop_Core_RegenerativeNanotech"] = "Regenerate health over time, and start regenerating sooner after taking a hit.",
        ["Shop_Core_ScrapReassembly"] = "Recover a little health from the damage you deal.",
        ["Shop_Core_ElectromagneticShield"] = "Grants shield charges that absorb hits, then recharge after a delay.",
        ["Shop_Arm_HonedWeaponrySystem"] = "Increases the damage of all weapons.",
        ["Shop_Arm_AdvancedTargetingModule"] = "Increases critical hit chance.",
        ["Shop_Arm_BeltDrivenCartridges"] = "Weapons fire faster.",
        ["Shop_Arm_GaugeExpansion"] = "Projectiles and explosions are larger.",
        ["Shop_Arm_OrdnanceProduction"] = "Increases manual ammo capacity.",
        ["Shop_Leg_BionicBoots"] = "Increases movement speed.",
        ["Shop_Leg_PistonAssistedJumping"] = "Jump higher.",
        ["Shop_Leg_HeelCharges"] = "Adds extra jumps in mid-air.",
        ["Shop_Leg_ExplosiveBoosters"] = "Adds dash charges and makes your dash faster."
    };

    [MenuItem("ScrapWaves/UI/Author Unlock Descriptions")]
    public static void Author()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Author outside Play Mode.");
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (SceneManager.GetSceneAt(i).isDirty)
                throw new InvalidOperationException("Unsaved scene work is present. Nothing was changed.");

        int written = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:WeaponData"))
        {
            var weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(AssetDatabase.GUIDToAssetPath(guid));
            if (weapon != null && WeaponDescriptions.TryGetValue(weapon.WeaponId ?? string.Empty, out string text))
                written += SetString(weapon, "Description", text);
        }

        foreach (string guid in AssetDatabase.FindAssets("t:PassiveItemData"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var passive = AssetDatabase.LoadAssetAtPath<PassiveItemData>(path);
            if (passive != null && PassiveDescriptions.TryGetValue(passive.name, out string text))
                written += SetString(passive, "_description", text);
        }

        foreach (string guid in AssetDatabase.FindAssets("t:WeaponPathUnlockData"))
        {
            var pathUnlock = AssetDatabase.LoadAssetAtPath<WeaponPathUnlockData>(AssetDatabase.GUIDToAssetPath(guid));
            if (pathUnlock != null && pathUnlock.Weapon != null)
                written += SetString(pathUnlock, "_description",
                    $"Unlocks upgrade path {pathUnlock.Path} for the {pathUnlock.Weapon.DisplayName}, available at the crafting table once the weapon reaches level 6.");
        }

        AssetDatabase.SaveAssets();
        AuthorScene();
        Debug.Log($"UNLOCK_DESCRIPTIONS_AUTHORED: {written} asset field(s) updated.");
    }

    private static int SetString(Object target, string property, string value)
    {
        var so = new SerializedObject(target);
        SerializedProperty prop = so.FindProperty(property);
        if (prop == null)
        {
            Debug.LogError($"Property {property} missing on {target.name}.");
            return 0;
        }
        if (prop.stringValue == value)
            return 0;
        prop.stringValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
        return 1;
    }

    private static void AuthorScene()
    {
        SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath);
            ObjectivesMenuUI menu = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                menu = root.GetComponentInChildren<ObjectivesMenuUI>(true);
                if (menu != null) break;
            }
            if (menu == null)
                throw new InvalidOperationException("ObjectivesMenuUI not found in TitleScreen.");

            var so = new SerializedObject(menu);
            SerializedProperty descProp = so.FindProperty("_unlockDescriptionText");
            if (descProp.objectReferenceValue != null)
            {
                Debug.Log("Unlock description field already authored; layout preserved.");
                return;
            }

            var requirement = (TMP_Text)so.FindProperty("_unlockRequirementText").objectReferenceValue;
            var type = (TMP_Text)so.FindProperty("_unlockTypeText").objectReferenceValue;
            var status = FindSibling(requirement, "UnlockStatus");
            var name = FindSibling(requirement, "UnlockName");
            if (requirement == null || type == null || status == null || name == null)
                throw new InvalidOperationException("Unlock detail texts are not all authored.");

            // Clone the requirement label so font/material/color match the panel.
            GameObject clone = Object.Instantiate(requirement.gameObject, requirement.transform.parent);
            clone.name = "UnlockDescription";
            var description = clone.GetComponent<TMP_Text>();
            description.text = string.Empty;
            description.enableAutoSizing = true;
            description.fontSizeMin = 16f;
            description.fontSizeMax = 22f;
            description.textWrappingMode = TextWrappingModes.Normal;
            description.overflowMode = TextOverflowModes.Truncate;
            description.alignment = TextAlignmentOptions.TopLeft;

            // Stack top-down: name, type, status, description, requirement (button/feedback unchanged).
            Anchor(name.rectTransform, 0.86f, 1.00f);
            Anchor(type.rectTransform, 0.79f, 0.86f);
            Anchor(status.rectTransform, 0.72f, 0.79f);
            Anchor(description.rectTransform, 0.40f, 0.72f);
            Anchor(requirement.rectTransform, 0.30f, 0.40f);

            descProp.objectReferenceValue = description;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            if (previous.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(previous);
        }
    }

    private static TMP_Text FindSibling(TMP_Text from, string childName)
    {
        if (from == null) return null;
        Transform t = from.transform.parent != null ? from.transform.parent.Find(childName) : null;
        return t != null ? t.GetComponent<TMP_Text>() : null;
    }

    private static void Anchor(RectTransform rt, float yMin, float yMax)
    {
        rt.anchorMin = new Vector2(0f, yMin);
        rt.anchorMax = new Vector2(1f, yMax);
        rt.offsetMin = new Vector2(24f, 0f);
        rt.offsetMax = new Vector2(-24f, 0f);
    }
}
