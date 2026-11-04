using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

/// <summary>Cuerpo real del jugador cayendo al vacío bajo un VoidRescueVolume, con física nativa.</summary>
public sealed class VoidRescueVolumeTests
{
    [UnityTest]
    public IEnumerator PlayerThatFallsIntoTheVoid_ReturnsToTheLastStableSpot_AtRest()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        GameObject world = new("Void rescue fixture");
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        keyboard.MakeCurrent();
        SimulationMode oldMode = Physics.simulationMode;
        Vector3 oldGravity = Physics.gravity;
        float oldStep = Time.fixedDeltaTime;
        try
        {
            Time.timeScale = 1f;
            GameplayPause.Reset();
            Physics.simulationMode = SimulationMode.FixedUpdate;
            Physics.gravity = new Vector3(0f, -9.81f, 0f);
            Time.fixedDeltaTime = .02f;
            var ground = new GameObject("Ground");
            ground.transform.SetParent(world.transform, false);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.AddComponent<BoxCollider>().size = new Vector3(100f, 1f, 100f);

            // La cara superior queda 20 m bajo el suelo, como en la escena real (15 m de margen).
            var volumeObject = new GameObject("Void volume");
            volumeObject.transform.SetParent(world.transform, false);
            volumeObject.transform.position = new Vector3(0f, -120f, 0f);
            var box = volumeObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(800f, 200f, 800f);
            var volume = volumeObject.AddComponent<VoidRescueVolume>();

            var player = new GameObject("Native player");
            player.SetActive(false);
            player.transform.SetParent(world.transform, false);
            player.transform.position = new Vector3(0f, 1.02f, 0f);
            var capsule = player.AddComponent<CapsuleCollider>();
            capsule.height = 2f; capsule.radius = .5f;
            var body = player.AddComponent<Rigidbody>();
            body.useGravity = true;
            var stats = player.AddComponent<PlayerStats>();
            var definitions = new List<StatDefinition>();
            foreach (string name in new[] { "MovementSpeed", "JumpHeight", "DashCharges", "AirJumps", "DashSpeed", "MaxHealth", "DamageResistance" })
            {
                var definition = AssetDatabase.LoadAssetAtPath<StatDefinition>($"Assets/ScriptableObjects/PlayerSO/Stats/{name}.asset");
                Assert.That(definition, Is.Not.Null, name);
                definitions.Add(definition);
            }
            Set(stats, "_statDefinitions", definitions);
            var movement = player.AddComponent<PlayerMovement>();
            Set(movement, "_cameraTransform", world.transform);
            var health = player.AddComponent<PlayerHealth>();
            player.SetActive(true);

            yield return Wait(() => movement.IsGroundedOnSurface, "pararse sobre el suelo");
            yield return Settle(0.8f); // el punto seguro exige estar parado unos instantes

            // Camina hasta otro punto del suelo: ese pasa a ser el último punto estable (no la posición inicial).
            body.position = new Vector3(30f, 1.02f, 20f);
            player.transform.position = body.position;
            body.linearVelocity = Vector3.zero;
            Physics.SyncTransforms();
            yield return Settle(1f);
            Assert.That(volume.RescueCount, Is.Zero, "Estar sobre el suelo no dispara el rescate.");

            // Cae por un "hueco": se saca al jugador de la plataforma y queda sin suelo bajo los pies.
            body.position = new Vector3(200f, 2f, 0f);
            player.transform.position = body.position;
            Physics.SyncTransforms();
            yield return Wait(() => volume.RescueCount >= 1, "ser rescatado");

            Vector3 p = player.transform.position;
            Assert.That(new Vector2(p.x - 30f, p.z - 20f).magnitude, Is.LessThan(0.8f), "Vuelve al último punto estable, no al spawn ni al vacío.");
            Assert.That(p.y, Is.GreaterThan(0f), "Queda sobre el suelo.");
            Assert.That(health.GrantedInvulnerabilityRemaining, Is.GreaterThan(0f), "Recibe unos segundos de protección.");

            yield return Wait(() => movement.IsGroundedOnSurface, "volver a apoyarse");
            // Caer 20 m deja ~20 m/s; el rescate la pone a cero y solo queda la caída de los 0,4 m de margen (~2,8 m/s).
            Assert.That(body.linearVelocity.magnitude, Is.LessThan(5f), "No arrastra la velocidad de la caída al vacío.");
            yield return Settle(1f);
            Assert.That(volume.RescueCount, Is.EqualTo(1), "Un solo rescate: no vuelve a caer.");
        }
        finally
        {
            Object.Destroy(world);
            InputSystem.RemoveDevice(keyboard);
            Physics.simulationMode = oldMode;
            Physics.gravity = oldGravity;
            Time.fixedDeltaTime = oldStep;
            Time.timeScale = 1f;
            GameplayPause.Reset();
        }
        yield return null;
        yield return new ExitPlayMode();
    }

    [UnityTest]
    public IEnumerator PlayerThatFallsBeforeAnyStableSpot_IsPlacedOnTheGroundBelowTheStartPosition()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        GameObject world = new("Void rescue fixture 2");
        Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
        keyboard.MakeCurrent();
        SimulationMode oldMode = Physics.simulationMode;
        Vector3 oldGravity = Physics.gravity;
        float oldStep = Time.fixedDeltaTime;
        try
        {
            Time.timeScale = 1f;
            GameplayPause.Reset();
            Physics.simulationMode = SimulationMode.FixedUpdate;
            Physics.gravity = new Vector3(0f, -9.81f, 0f);
            Time.fixedDeltaTime = .02f;
            var ground = new GameObject("Ground");
            ground.transform.SetParent(world.transform, false);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.AddComponent<BoxCollider>().size = new Vector3(100f, 1f, 100f);

            var volumeObject = new GameObject("Void volume");
            volumeObject.transform.SetParent(world.transform, false);
            volumeObject.transform.position = new Vector3(0f, -120f, 0f);
            var box = volumeObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(800f, 200f, 800f);
            var volume = volumeObject.AddComponent<VoidRescueVolume>();
            Set(volume, "_stableGroundSeconds", 60f); // nunca llega a guardar un punto seguro

            var player = new GameObject("Native player 2");
            player.SetActive(false);
            player.transform.SetParent(world.transform, false);
            player.transform.position = new Vector3(5f, 1.02f, 3f);
            var capsule = player.AddComponent<CapsuleCollider>();
            capsule.height = 2f; capsule.radius = .5f;
            var body = player.AddComponent<Rigidbody>();
            body.useGravity = true;
            var stats = player.AddComponent<PlayerStats>();
            var definitions = new List<StatDefinition>();
            foreach (string name in new[] { "MovementSpeed", "JumpHeight", "DashCharges", "AirJumps", "DashSpeed", "MaxHealth", "DamageResistance" })
            {
                var definition = AssetDatabase.LoadAssetAtPath<StatDefinition>($"Assets/ScriptableObjects/PlayerSO/Stats/{name}.asset");
                Assert.That(definition, Is.Not.Null, name);
                definitions.Add(definition);
            }
            Set(stats, "_statDefinitions", definitions);
            var movement = player.AddComponent<PlayerMovement>();
            Set(movement, "_cameraTransform", world.transform);
            player.AddComponent<PlayerHealth>();
            player.SetActive(true);

            yield return null;
            body.position = new Vector3(-250f, 2f, 40f);
            player.transform.position = body.position;
            Physics.SyncTransforms();
            yield return Wait(() => volume.RescueCount >= 1, "ser rescatado");

            Vector3 p = player.transform.position;
            Assert.That(new Vector2(p.x - 5f, p.z - 3f).magnitude, Is.LessThan(0.6f), "Se coloca sobre el suelo bajo la posición inicial.");
            Assert.That(p.y, Is.GreaterThan(0f));
        }
        finally
        {
            Object.Destroy(world);
            InputSystem.RemoveDevice(keyboard);
            Physics.simulationMode = oldMode;
            Physics.gravity = oldGravity;
            Time.fixedDeltaTime = oldStep;
            Time.timeScale = 1f;
            GameplayPause.Reset();
        }
        yield return null;
        yield return new ExitPlayMode();
    }

    // Espera por frames de juego (WaitForSeconds dentro de un test lanzado desde el editor no los deja avanzar igual).
    private static IEnumerator Settle(float seconds)
    {
        float until = Time.realtimeSinceStartup + seconds;
        while (Time.realtimeSinceStartup < until) yield return null;
    }

    private static IEnumerator Wait(Func<bool> ready, string purpose)
    {
        float deadline = Time.realtimeSinceStartup + 15f;
        while (!ready() && Time.realtimeSinceStartup < deadline) yield return null;
        Assert.That(ready(), Is.True, "Timeout esperando: " + purpose);
    }

    private static void Set(object target, string field, object value) => target.GetType()
        .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

    [UnityTearDown]
    public IEnumerator RestoreMode()
    {
        if (Application.isPlaying) { Time.timeScale = 1f; yield return new ExitPlayMode(); }
    }
}
