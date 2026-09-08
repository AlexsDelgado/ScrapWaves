using System;
using UnityEngine;

[CreateAssetMenu(
    fileName = "TemporaryPowerupVisualCatalog",
    menuName = "ScrapWaves/Powerups/Temporary Powerup Visual Catalog")]
public class TemporaryPowerupVisualCatalog : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public TemporaryPowerupType Type;
        public Mesh Mesh;
    }

    [SerializeField] private Entry[] _entries =
    {
        new() { Type = TemporaryPowerupType.ExtraDamage },
        new() { Type = TemporaryPowerupType.ExtraSpeed },
        new() { Type = TemporaryPowerupType.ExtraScavenging },
        new() { Type = TemporaryPowerupType.Invulnerability },
        new() { Type = TemporaryPowerupType.FullHeal },
        new() { Type = TemporaryPowerupType.Nuke }
    };

    public Mesh GetMesh(TemporaryPowerupType type)
    {
        if (_entries == null)
            return null;

        for (int i = 0; i < _entries.Length; i++)
        {
            Entry entry = _entries[i];
            if (entry != null && entry.Type == type)
                return entry.Mesh;
        }

        return null;
    }

    public void SetMesh(TemporaryPowerupType type, Mesh mesh)
    {
        if (_entries == null)
            _entries = Array.Empty<Entry>();

        for (int i = 0; i < _entries.Length; i++)
        {
            if (_entries[i] != null && _entries[i].Type == type)
            {
                _entries[i].Mesh = mesh;
                return;
            }
        }

        Array.Resize(ref _entries, _entries.Length + 1);
        _entries[_entries.Length - 1] = new Entry { Type = type, Mesh = mesh };
    }
}
