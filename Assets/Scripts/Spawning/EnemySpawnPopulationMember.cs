using System;
using System.Collections.Generic;
using UnityEngine;

public enum EnemySpawnSource
{
    Unassigned,
    Orbital,
    InWorld,
    Objective
}

/// <summary>
/// Tracks the source of a successfully placed, living enemy lease independently of targeting.
/// In-world ambushes are recorded but do not consume the ordinary spawn population cap.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemySpawnPopulationMember : MonoBehaviour, IEnemySpawnLifecycle
{
    private static readonly HashSet<EnemySpawnPopulationMember> s_Members = new();

    private EnemyHealth _health;
    private Action _onDied;
    private uint _leaseVersion;

    public EnemySpawnSource Source { get; private set; }

    public static int ActiveCount
    {
        get
        {
            // Also cover destroyed health components and reentrant reads during OnDied.
            s_Members.RemoveWhere(RemoveInvalidMember);
            int count = 0;
            foreach (EnemySpawnPopulationMember member in s_Members)
            {
                if (member.Source == EnemySpawnSource.Orbital || member.Source == EnemySpawnSource.Objective)
                    count++;
            }
            return count;
        }
    }

    /// <summary>Call after placement and spawn modifiers succeed, never from pool activation.</summary>
    public static void RecordSpawn(GameObject instance, EnemySpawnSource source)
    {
        if (instance == null)
            return;

        EnemySpawnPopulationMember member = instance.GetComponent<EnemySpawnPopulationMember>();
        if (member != null)
            member.ClearSpawn();

        if (!instance.activeInHierarchy ||
            (source != EnemySpawnSource.Orbital && source != EnemySpawnSource.InWorld && source != EnemySpawnSource.Objective))
            return;

        EnemyHealth health = instance.GetComponent<EnemyHealth>();
        if (health == null || health.CurrentHealth <= 0)
            return;

        if (member == null)
            member = instance.AddComponent<EnemySpawnPopulationMember>();

        // This marker owns lease bookkeeping; it must receive disable callbacks for this lease.
        member.enabled = true;
        member.Source = source;
        member._health = health;
        uint leaseVersion = member._leaseVersion;
        member._onDied = () =>
        {
            // An earlier death listener can release and reuse this object before our callback runs.
            if (member._leaseVersion == leaseVersion)
                member.ClearSpawn();
        };
        health.OnDied += member._onDied;
        s_Members.Add(member);
    }

    // OnEnable deliberately does not enroll: prewarming and checkout precede placement.
    public void OnPoolSpawn() => ClearSpawn();

    public void OnPoolDespawn() => ClearSpawn();

    private void OnDisable() => ClearSpawn();

    private void OnDestroy() => ClearSpawn();

    private void ClearSpawn()
    {
        s_Members.Remove(this);
        ClearSource();
    }

    private void ClearSource()
    {
        _leaseVersion++;
        if (!ReferenceEquals(_health, null) && _onDied != null)
            _health.OnDied -= _onDied;
        _health = null;
        _onDied = null;
        Source = EnemySpawnSource.Unassigned;
    }

    private static bool RemoveInvalidMember(EnemySpawnPopulationMember member)
    {
        if (member == null)
            return true;
        if (member.isActiveAndEnabled && member._health != null && member._health.CurrentHealth > 0)
            return false;

        // RemoveWhere owns collection removal; clearing source must not mutate the set.
        member.ClearSource();
        return true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        foreach (EnemySpawnPopulationMember member in s_Members)
        {
            if (member != null)
                member.ClearSource();
        }
        s_Members.Clear();
    }
}
