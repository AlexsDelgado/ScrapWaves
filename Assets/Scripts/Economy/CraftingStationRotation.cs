using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Rotates between authored station placements without disabling their roots or sharing run state across scenes.</summary>
public static class CraftingStationRotation
{
    public interface IRandomSource
    {
        double NextDouble();
        int NextIndex(int exclusiveMax);
    }

    private sealed class RandomSource : IRandomSource
    {
        private readonly System.Random _random = new System.Random();
        public double NextDouble() => _random.NextDouble();
        public int NextIndex(int exclusiveMax) => _random.Next(exclusiveMax);
    }

    private sealed class ScenePool
    {
        public readonly List<CraftingStation> Stations = new List<CraftingStation>();
        public IRandomSource Random = new RandomSource();
        public CraftingStation Active;
        public bool Initialized;
    }

    private static readonly Dictionary<int, ScenePool> Pools = new Dictionary<int, ScenePool>();
    public static event Action<CraftingStation> ActiveStationChanged;
    public static event Action<CraftingStation> StationInteracted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Pools.Clear();
        ActiveStationChanged = null;
        StationInteracted = null;
        SceneManager.sceneUnloaded -= OnSceneUnloaded;
        SceneManager.sceneUnloaded += OnSceneUnloaded;
    }

    public static CraftingStation GetActiveStation(Scene scene)
    {
        if (!scene.IsValid() || !Pools.TryGetValue(scene.handle, out ScenePool pool)) return null;
        return pool.Active != null && pool.Active.IsAvailable ? pool.Active : null;
    }

    public static void Register(CraftingStation station)
    {
        if (!IsEligible(station)) return;
        ScenePool pool = GetOrCreate(station.gameObject.scene);
        if (pool.Stations.Contains(station)) return;
        pool.Stations.Add(station);
        station.SetAvailability(false);
        // Late arrivals do not replace a healthy active station.
        if (pool.Initialized && pool.Active == null) Activate(pool, Choose(pool, null));
    }

    public static void Unregister(CraftingStation station)
    {
        if (station == null) return;
        Scene scene = station.gameObject.scene;
        if (!scene.IsValid() || !Pools.TryGetValue(scene.handle, out ScenePool pool)) return;
        pool.Stations.Remove(station);
        if (pool.Active != station) return;
        Activate(pool, Choose(pool, station));
    }

    /// <summary>Called from Start, after all scene stations have registered and tutorial exclusions are applied.</summary>
    public static void InitializeScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;
        ScenePool pool = GetOrCreate(scene);
        if (pool.Initialized) return;
        // Also supports authored scenes and deterministic EditMode fixtures before MonoBehaviour callbacks run.
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (CraftingStation station in root.GetComponentsInChildren<CraftingStation>(true))
                Register(station);
        pool.Initialized = true;
        Activate(pool, Choose(pool, null));
    }

    /// <summary>Replace the isolated scene RNG for a seeded run or a deterministic test, without touching UnityEngine.Random.</summary>
    public static void SetRandomSource(Scene scene, IRandomSource random)
    {
        if (!scene.IsValid()) throw new ArgumentException("A valid station scene is required.", nameof(scene));
        GetOrCreate(scene).Random = random ?? throw new ArgumentNullException(nameof(random));
    }

    internal static bool CompleteTransaction(CraftingStation station)
    {
        Scene scene = station.gameObject.scene;
        if (!Pools.TryGetValue(scene.handle, out ScenePool pool) || pool.Active != station) return false;
        double roll = pool.Random.NextDouble();
        if (roll >= station.CurrentBreakChancePercent / 100d)
        {
            station.IncreaseBreakChance();
            return false;
        }
        CraftingStation next = Choose(pool, station);
        // With a single station the same table visibly resets and the purchase session still ends.
        if (next == null && IsEligible(station)) next = station;
        Activate(pool, next);
        return true;
    }

    internal static void NotifyInteracted(CraftingStation station) => StationInteracted?.Invoke(station);

    private static ScenePool GetOrCreate(Scene scene)
    {
        if (!Pools.TryGetValue(scene.handle, out ScenePool pool))
        {
            pool = new ScenePool();
            Pools.Add(scene.handle, pool);
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }
        return pool;
    }

    private static bool IsEligible(CraftingStation station) =>
        station != null && station.isActiveAndEnabled && station.ParticipatesInRotation
        && station.gameObject.scene.IsValid() && station.gameObject.scene.isLoaded;

    private static CraftingStation Choose(ScenePool pool, CraftingStation except)
    {
        int count = 0;
        for (int i = pool.Stations.Count - 1; i >= 0; i--)
        {
            if (!IsEligible(pool.Stations[i])) pool.Stations.RemoveAt(i);
            else if (pool.Stations[i] != except) count++;
        }
        if (count == 0) return null;
        int selected = pool.Random.NextIndex(count);
        foreach (CraftingStation station in pool.Stations)
        {
            if (station == except) continue;
            if (selected-- == 0) return station;
        }
        throw new InvalidOperationException("Station random source returned an out-of-range index.");
    }

    private static void Activate(ScenePool pool, CraftingStation next)
    {
        CraftingStation previous = pool.Active;
        pool.Active = next;
        if (previous != null) previous.SetAvailability(false);
        if (next != null) next.SetAvailability(true);
        ActiveStationChanged?.Invoke(next);
    }

    private static void OnSceneUnloaded(Scene scene)
    {
        if (!Pools.TryGetValue(scene.handle, out ScenePool pool)) return;
        Pools.Remove(scene.handle);
        if (pool.Active != null) pool.Active.SetAvailability(false);
        ActiveStationChanged?.Invoke(null);
    }
}
