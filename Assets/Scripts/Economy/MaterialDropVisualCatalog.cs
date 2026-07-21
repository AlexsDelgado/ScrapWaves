using System;
using UnityEngine;

[CreateAssetMenu(fileName = "MaterialDropVisualCatalog", menuName = "ScrapWaves/Economy/Material Drop Visual Catalog")]
public class MaterialDropVisualCatalog : ScriptableObject
{
    [Serializable]
    public class Entry
    {
        public MaterialType Type;
        public GameObject VisualPrefab;
    }

    [SerializeField] private Entry[] _entries =
    {
        new() { Type = MaterialType.SheetMetal },
        new() { Type = MaterialType.MetalPipe },
        new() { Type = MaterialType.Gears },
        new() { Type = MaterialType.JellifiedFuel },
        new() { Type = MaterialType.PlasticExplosive },
        new() { Type = MaterialType.Wiring }
    };

    public GameObject GetVisualPrefab(MaterialType type)
    {
        if (_entries == null)
            return null;

        for (int i = 0; i < _entries.Length; i++)
        {
            Entry entry = _entries[i];
            if (entry != null && entry.Type == type)
                return entry.VisualPrefab;
        }

        return null;
    }

    public void SetVisual(MaterialType type, GameObject prefab)
    {
        if (_entries == null)
            _entries = Array.Empty<Entry>();

        for (int i = 0; i < _entries.Length; i++)
        {
            if (_entries[i] != null && _entries[i].Type == type)
            {
                _entries[i].VisualPrefab = prefab;
                return;
            }
        }

        Array.Resize(ref _entries, _entries.Length + 1);
        _entries[_entries.Length - 1] = new Entry { Type = type, VisualPrefab = prefab };
    }
}
