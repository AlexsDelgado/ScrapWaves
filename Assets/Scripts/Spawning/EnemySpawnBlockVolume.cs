using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Volumen que rechaza spawns de enemigos. El BoxCollider marca la zona (trigger,
/// para no frenar al jugador). Con columna completa, bloquea toda la huella en XZ
/// sin importar la altura: así un anillo que cae sobre un interior no deja al enemigo adentro.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
public sealed class EnemySpawnBlockVolume : MonoBehaviour
{
    private static readonly List<EnemySpawnBlockVolume> Active = new(8);

    [SerializeField, Tooltip("Si está activo, cualquier punto sobre la huella XZ del box queda bloqueado, aunque esté por encima o por debajo.")]
    private bool _blockFullColumn = true;

    private BoxCollider _box;

    public static bool Blocks(Vector3 worldPoint)
    {
        for (int i = 0; i < Active.Count; i++)
        {
            EnemySpawnBlockVolume volume = Active[i];
            if (volume != null && volume.isActiveAndEnabled && volume.Contains(worldPoint))
                return true;
        }

        return false;
    }

    private void Reset()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box != null)
            box.isTrigger = true;
    }

    private void OnEnable()
    {
        if (!Active.Contains(this))
            Active.Add(this);
    }

    private void OnDisable()
    {
        Active.Remove(this);
    }

    private bool Contains(Vector3 worldPoint)
    {
        if (_box == null)
            _box = GetComponent<BoxCollider>();
        if (_box == null || !_box.enabled)
            return false;

        Vector3 local = transform.InverseTransformPoint(worldPoint) - _box.center;
        Vector3 half = _box.size * 0.5f;
        if (_blockFullColumn)
            local.y = 0f;

        return Mathf.Abs(local.x) <= half.x
            && Mathf.Abs(local.y) <= half.y
            && Mathf.Abs(local.z) <= half.z;
    }

    private void OnDrawGizmos()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null)
            return;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = new Color(1f, 0.45f, 0.1f, 0.9f);
        Gizmos.DrawWireCube(box.center, box.size);
    }
}
