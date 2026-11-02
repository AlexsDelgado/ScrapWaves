using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Reads native particle positions at birth, before motion can conceal a placement error.</summary>
public sealed class StalkerScrapPlacementTests
{
    private const string SetupKey = "StalkerScrapPlacementTests.SceneSetup";
    private const float Lift = 0.1f;
    private const float Tolerance = 0.002f;
    [Serializable] private sealed class SavedSetup { public SavedScene[] Scenes; }
    [Serializable] private sealed class SavedScene { public string Path; public bool Loaded; public bool Active; }

    [UnityTest]
    public IEnumerator BirthPlacement_FollowsSupportingSurfaceAndLedgesWithoutChangingEntryBurst()
    {
        SaveAndClearScene();
        yield return new EnterPlayMode();
        // Everything below runs synchronously: these are emission positions, not
        // positions after gravity, velocity or a frame of particle simulation.
        RunBirthPlacementChecks();
    }

    private static void RunBirthPlacementChecks()
    {
        Assert.That(Application.isPlaying, Is.True);
        BoxCollider slope = CreateBox("Elevated 20-degree supporting slope", new Vector3(317f, 23f, -481f),
            new Vector3(12f, 1f, 12f), Quaternion.Euler(0f, 0f, 20f));
        Physics.SyncTransforms();
        Vector3 groundPoint = SurfaceAt(slope, slope.transform.position);
        GigaWormGroundBurstVfx.Spawn(groundPoint, 1, slope);
        StalkerScrapBurstVfx manager = Object.FindAnyObjectByType<StalkerScrapBurstVfx>();
        Assert.That(manager, Is.Not.Null);
        ClearParticles(manager);
        manager.transform.position = new Vector3(-131f, 58f, 219f);
        foreach (ParticleSystem system in manager.GetComponentsInChildren<ParticleSystem>(true))
            Assert.That(system.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.World));

        manager.Emit(groundPoint, false, 24, slope);
        Vector3[] slopeBirths = BirthPositions(manager);
        Assert.That(slopeBirths.Length, Is.EqualTo(24));
        foreach (Vector3 position in slopeBirths)
        {
            Vector3 surface = SurfaceAt(slope, position);
            Assert.That(position.y, Is.EqualTo(surface.y + Lift).Within(Tolerance),
                "Trail scrap must follow the supporting slope at its own scattered XZ position.");
            Assert.That(PlanarDistance(position, groundPoint), Is.LessThanOrEqualTo(1.2f + Tolerance),
                "The translated manager changed a world-space birth position.");
        }
        Assert.That(slopeBirths.Max(position => position.y) - slopeBirths.Min(position => position.y), Is.GreaterThan(0.25f),
            "The slope fixture must reject the old constant-height scatter plane.");

        ClearParticles(manager);
        BoxCollider ledge = CreateBox("Narrow support", new Vector3(-240f, 47f, 510f),
            new Vector3(0.25f, 0.4f, 0.25f), Quaternion.identity);
        BoxCollider overhead = CreateBox("Unrelated Default overhead prop", ledge.transform.position + Vector3.up * 4f,
            new Vector3(8f, 1f, 8f), Quaternion.identity);
        Physics.SyncTransforms();
        Vector3 ledgePoint = SurfaceAt(ledge, ledge.transform.position);
        Assert.That(Physics.Raycast(new Ray(ledgePoint + Vector3.up * 10f, Vector3.down),
            out RaycastHit obstructed, 20f, 1 << 0, QueryTriggerInteraction.Ignore), Is.True);
        Assert.That(obstructed.collider, Is.SameAs(overhead), "The fixture must include an overhead collider that a broad ray would hit first.");
        manager.Emit(ledgePoint, false, 24, ledge);
        Vector3[] ledgeBirths = BirthPositions(manager);
        Assert.That(ledgeBirths.Length, Is.EqualTo(24));
        int centerFallbacks = 0;
        foreach (Vector3 position in ledgeBirths)
        {
            Vector3 surface = SurfaceAt(ledge, position);
            Assert.That(position.y, Is.EqualTo(surface.y + Lift).Within(Tolerance),
                "A ledge scatter must stay on its supporting collider, never on the overhead prop or in open air.");
            if (PlanarDistance(position, ledgePoint) < Tolerance)
            {
                centerFallbacks++;
                Assert.That(Vector3.Distance(position, ledgePoint + Vector3.up * Lift), Is.LessThan(Tolerance));
            }
        }
        Assert.That(centerFallbacks, Is.GreaterThan(0), "Out-of-ledge scatter must fall back to the known valid center.");

        // Ground-entry placement is deliberately unchanged by the trail correction.
        ClearParticles(manager);
        manager.Emit(groundPoint, true, -1, slope);
        Vector3[] entryBirths = BirthPositions(manager);
        Assert.That(entryBirths.Length, Is.EqualTo(20));
        foreach (Vector3 position in entryBirths)
            Assert.That(position.y, Is.EqualTo(groundPoint.y + Lift).Within(Tolerance),
                "Supplying a surface must not reproject the accepted entry burst.");
        Assert.That(entryBirths.Max(position => Mathf.Abs(position.y - (SurfaceAt(slope, position).y + Lift))),
            Is.GreaterThan(0.15f), "The entry fixture must distinguish its unchanged center plane from terrain projection.");
        Assert.That(Vector3.Distance(manager.LastEntryPosition, groundPoint), Is.LessThan(Tolerance));

        CheckTerrainPreferenceAndDefaultFallback(manager);
    }

    private static void CheckTerrainPreferenceAndDefaultFallback(StalkerScrapBurstVfx manager)
    {
        int terrainLayer = LayerMask.NameToLayer("Terrain");
        Assert.That(terrainLayer, Is.GreaterThanOrEqualTo(0), "The project must expose its configured Terrain layer.");
        LayerMask groundMask = (1 << terrainLayer) | (1 << 0);
        TerrainData data = new() { heightmapResolution = 33, size = new Vector3(40f, 6f, 40f) };
        data.SetHeights(0, 0, new float[33, 33]);
        GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
        try
        {
            terrainObject.name = "Supported Terrain beneath a Default prop";
            terrainObject.layer = terrainLayer;
            terrainObject.transform.position = new Vector3(450f, 13f, -320f);
            TerrainCollider terrain = terrainObject.GetComponent<TerrainCollider>();
            Vector3 sample = new(470f, 19f, -300f);
            BoxCollider prop = CreateBox("Default workbench-like obstruction", new Vector3(sample.x, 18f, sample.z),
                new Vector3(10f, 2f, 10f), Quaternion.identity);
            Physics.SyncTransforms();
            Assert.That(Physics.Raycast(new Ray(sample + Vector3.up * 30f, Vector3.down),
                out RaycastHit obstructed, 80f, groundMask, QueryTriggerInteraction.Ignore), Is.True);
            Assert.That(obstructed.collider, Is.SameAs(prop), "The fixture must reproduce a Default prop hiding Terrain from the broad mask.");
            ClearParticles(manager);
            GigaWormGroundBurstVfx.SpawnOnSurface(sample, groundMask);
            Assert.That(Object.FindAnyObjectByType<StalkerScrapBurstVfx>(), Is.SameAs(manager));
            Vector3[] terrainBirths = BirthPositions(manager);
            Assert.That(terrainBirths.Length, Is.EqualTo(3));
            foreach (Vector3 position in terrainBirths)
                Assert.That(position.y, Is.EqualTo(SurfaceAt(terrain, position).y + Lift).Within(Tolerance),
                    "The pre-emergence burst selected a Default prop instead of supported Terrain.");

            // The same elevated Default collider becomes legitimate ground when it
            // is the explicitly occupied support; Terrain below must not steal it.
            ClearParticles(manager);
            GigaWormGroundBurstVfx.SpawnOnSurface(sample, groundMask, prop);
            Vector3[] occupiedBirths = BirthPositions(manager);
            Assert.That(occupiedBirths.Length, Is.EqualTo(3));
            foreach (Vector3 position in occupiedBirths)
                Assert.That(position.y, Is.EqualTo(SurfaceAt(prop, position).y + Lift).Within(Tolerance),
                    "The explicitly occupied elevated floor must take priority over Terrain underneath it.");

            BoxCollider defaultFloor = CreateBox("Default-only floor fallback", new Vector3(-440f, 37f, 270f),
                new Vector3(12f, 1f, 12f), Quaternion.identity);
            Physics.SyncTransforms();
            Vector3 floorPoint = SurfaceAt(defaultFloor, defaultFloor.transform.position);
            ClearParticles(manager);
            GigaWormGroundBurstVfx.SpawnOnSurface(floorPoint, groundMask);
            Vector3[] fallbackBirths = BirthPositions(manager);
            Assert.That(fallbackBirths.Length, Is.EqualTo(3));
            foreach (Vector3 position in fallbackBirths)
                Assert.That(position.y, Is.EqualTo(SurfaceAt(defaultFloor, position).y + Lift).Within(Tolerance),
                    "A valid configured Default floor must still work when there is no Terrain hit.");

            ClearParticles(manager);
            int trailCount = manager.TrailBurstCount;
            GigaWormGroundBurstVfx.SpawnOnSurface(new Vector3(900f, 200f, 900f), groundMask);
            Assert.That(manager.ActiveParticleCount, Is.EqualTo(0), "A missing support must not produce floating warning scrap.");
            Assert.That(manager.TrailBurstCount, Is.EqualTo(trailCount));
        }
        finally
        {
            Object.DestroyImmediate(terrainObject);
            Object.DestroyImmediate(data);
        }
    }

    private static BoxCollider CreateBox(string name, Vector3 position, Vector3 size, Quaternion rotation)
    {
        GameObject item = new(name);
        item.transform.SetPositionAndRotation(position, rotation);
        BoxCollider collider = item.AddComponent<BoxCollider>();
        collider.size = size;
        return collider;
    }

    private static Vector3 SurfaceAt(Collider surface, Vector3 position)
    {
        Ray ray = new(new Vector3(position.x, surface.bounds.max.y + 10f, position.z), Vector3.down);
        Assert.That(surface.Raycast(ray, out RaycastHit hit, surface.bounds.size.y + 20f), Is.True,
            "The sampled particle position is not supported at XZ " + position.x + ", " + position.z);
        return hit.point;
    }

    private static float PlanarDistance(Vector3 first, Vector3 second) =>
        Vector2.Distance(new Vector2(first.x, first.z), new Vector2(second.x, second.z));

    private static Vector3[] BirthPositions(StalkerScrapBurstVfx manager)
    {
        List<Vector3> positions = new();
        ParticleSystem.Particle[] buffer = new ParticleSystem.Particle[StalkerScrapBurstVfx.MaxParticlesPerKind];
        foreach (ParticleSystem system in manager.GetComponentsInChildren<ParticleSystem>(true))
        {
            Assert.That(system.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.World));
            int count = system.GetParticles(buffer);
            for (int i = 0; i < count; i++) positions.Add(buffer[i].position);
        }
        Assert.That(positions.Count, Is.EqualTo(manager.ActiveParticleCount));
        return positions.ToArray();
    }

    private static void ClearParticles(StalkerScrapBurstVfx manager)
    {
        foreach (ParticleSystem system in manager.GetComponentsInChildren<ParticleSystem>(true))
            system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        Assert.That(manager.ActiveParticleCount, Is.EqualTo(0));
    }

    private static void SaveAndClearScene()
    {
        SessionState.SetString(SetupKey, JsonUtility.ToJson(new SavedSetup
        {
            Scenes = EditorSceneManager.GetSceneManagerSetup().Select(scene => new SavedScene
                { Path = scene.path, Loaded = scene.isLoaded, Active = scene.isActive }).ToArray()
        }));
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    [UnityTearDown]
    public IEnumerator CleanupAndRestoreScene()
    {
        if (Application.isPlaying) yield return new ExitPlayMode();
        string json = SessionState.GetString(SetupKey, string.Empty);
        SessionState.EraseString(SetupKey);
        if (string.IsNullOrEmpty(json)) yield break;
        SavedSetup saved = JsonUtility.FromJson<SavedSetup>(json);
        SceneSetup[] setup = saved.Scenes.Where(scene => !string.IsNullOrEmpty(scene.Path)).Select(scene =>
            new SceneSetup { path = scene.Path, isLoaded = scene.Loaded, isActive = scene.Active }).ToArray();
        if (setup.Length > 0 && setup.Any(scene => scene.isActive)) EditorSceneManager.RestoreSceneManagerSetup(setup);
    }
}
