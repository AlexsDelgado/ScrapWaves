using System.Collections.Generic;
using UnityEngine;

/// <summary>Las entradas de diálogo activas en un nivel. El <see cref="DialogueDirector"/> de la escena usa una.</summary>
[CreateAssetMenu(menuName = "ScrapWaves/Dialogue/Set", fileName = "DialogueSet_")]
public class DialogueSet : ScriptableObject
{
    [SerializeField] private List<DialogueEntry> _entries = new();

    public IReadOnlyList<DialogueEntry> Entries => _entries;
}
