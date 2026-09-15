using System;
using UnityEngine;

/// <summary>A sampled group of nearby enemies. Directions use world space and the horizontal plane.</summary>
public readonly struct RearThreatSpike
{
    public int TrackId { get; }
    public Vector3 WorldDirection { get; }
    public float Distance { get; }
    public float Urgency { get; }
    public int EnemyCount { get; }

    public RearThreatSpike(int trackId, Vector3 worldDirection, float distance, float urgency, int enemyCount)
    {
        TrackId = trackId;
        WorldDirection = worldDirection;
        Distance = distance;
        Urgency = urgency;
        EnemyCount = enemyCount;
    }
}

/// <summary>An immutable value copy; retaining a snapshot never observes changes to sensor buffers.</summary>
public readonly struct RearThreatSnapshot
{
    public const int MaximumSpikes = 5;

    private readonly RearThreatSpike _spike0;
    private readonly RearThreatSpike _spike1;
    private readonly RearThreatSpike _spike2;
    private readonly RearThreatSpike _spike3;
    private readonly RearThreatSpike _spike4;

    public int Count { get; }
    public float OverallUrgency { get; }
    public bool IsValidPlayer { get; }
    public Vector3 Forward { get; }
    public RearThreatSpike this[int index] => GetSpike(index);

    internal RearThreatSnapshot(RearThreatSpike[] spikes, int count, bool isValidPlayer, Vector3 forward, float overallUrgency)
    {
        Count = Mathf.Clamp(count, 0, MaximumSpikes);
        IsValidPlayer = isValidPlayer;
        Forward = forward;
        _spike0 = Count > 0 ? spikes[0] : default;
        _spike1 = Count > 1 ? spikes[1] : default;
        _spike2 = Count > 2 ? spikes[2] : default;
        _spike3 = Count > 3 ? spikes[3] : default;
        _spike4 = Count > 4 ? spikes[4] : default;
        OverallUrgency = Mathf.Clamp01(overallUrgency);
    }

    public RearThreatSpike GetSpike(int index)
    {
        if (index < 0 || index >= Count)
            throw new ArgumentOutOfRangeException(nameof(index));
        return index switch
        {
            0 => _spike0,
            1 => _spike1,
            2 => _spike2,
            3 => _spike3,
            _ => _spike4
        };
    }
}
