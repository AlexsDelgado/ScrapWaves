using UnityEditor;

[CustomEditor(typeof(WeaponPresentationProfile))]
public sealed class WeaponPresentationProfileEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();
        serializedObject.ApplyModifiedProperties();

        WeaponPresentationProfile profile = (WeaponPresentationProfile)target;
        profile.RebuildCache();
        if (profile.HasDuplicateCues)
        {
            EditorGUILayout.HelpBox(
                "Duplicate presentation cues are configured. Runtime lookup keeps the first entry for each cue.",
                MessageType.Error);
        }
    }
}
