using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

public sealed partial class WeaponDataEditor
{
    private readonly List<Object> _previewObjects = new();
    private PlayerStats _previewStats;
    private Scene _previewScene;
    private bool _showResolvedGameplay = true;

    private void EnsurePreviewPlayer()
    {
        if (_previewStats != null) return;
        DisposePreviewPlayer();
        try
        {
            _previewScene = EditorSceneManager.NewPreviewScene();
            _previewStats = WeaponStatBaselineCapture.CreateNeutralStats(_previewObjects, _previewScene);
        }
        catch
        {
            DisposePreviewPlayer();
            throw;
        }
    }

    private void DrawResolvedConsumerPreview()
    {
        _showResolvedGameplay = EditorGUILayout.Foldout(_showResolvedGameplay, "Resolved gameplay: neutral player, zero heat", true);
        if (!_showResolvedGameplay) return;
        var data = (WeaponData)target;
        if (data.StatProfile != null && !data.StatProfile.TryValidate(data.WeaponType, out string error))
        {
            EditorGUILayout.HelpBox(error, MessageType.Error);
            return;
        }
        try
        {
            EnsurePreviewPlayer();
            var instance = new WeaponInstance { Data = data, Level = _authoringLevel, SelectedPath = SelectedAuthoringPath,
                State = SelectedRuntimeMode == WeaponStatMode.Automatic ? WeaponState.Automatic : WeaponState.Manual };
            var weapon = (BasicProjectileWeapon)WeaponBehaviourFactory.Create(data, null, null, _previewStats.transform, null);
            weapon.Setup(instance, _previewStats.transform, _previewStats, null);
            string selectedMode = SelectedRuntimeMode == WeaponStatMode.Ability ? "Active Ability" : SelectedRuntimeMode.ToString();
            var section = weapon.CaptureDiagnostics().Sections.Single(s => s.Name == selectedMode);
            foreach (WeaponDiagnosticValue value in section.Values)
                EditorGUILayout.LabelField(value.Label, value.Value, EditorStyles.wordWrappedLabel);
            if (SelectedRuntimeMode == WeaponStatMode.Manual)
                EditorGUILayout.LabelField("Maximum manual capacity", WeaponMath.GetMaxManualAmmo(instance, _previewStats).ToString("0.###"));
            EditorGUILayout.LabelField("Preview modifiers", "Player multipliers 1x; player crit chance 0%; player crit severity 2x; heat 0%", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField("Observation", "Uses gameplay diagnostic getters. No attacks, random rolls, ammo spending, timer advancement, or asset saves.", EditorStyles.wordWrappedMiniLabel);
        }
        catch (Exception exception)
        {
            EditorGUILayout.HelpBox("Preview unavailable: " + exception.Message, MessageType.Error);
        }
    }

    private void OnDisable()
    {
        DisposePreviewPlayer();
    }

    private void DisposePreviewPlayer()
    {
        try
        {
            for (int i = _previewObjects.Count - 1; i >= 0; i--)
                if (_previewObjects[i] != null) Object.DestroyImmediate(_previewObjects[i]);
        }
        finally
        {
            _previewObjects.Clear();
            _previewStats = null;
            if (_previewScene.IsValid()) EditorSceneManager.ClosePreviewScene(_previewScene);
            _previewScene = default;
        }
    }
}
