using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.VFX;
using Object = UnityEngine.Object;

/// <summary>
/// Los VFX de powerups cuelgan del player y arrancan encendidos (OnPlay). Los apaga PlayerPowerupVfx, que crea el
/// TemporaryPowerupController, que a su vez agrega SpecMetaBootstrap. Entrando desde el menú ese bootstrap
/// solo corría para TitleScreen y los efectos quedaban siempre activos.
/// </summary>
public sealed class PowerupVfxStartupTests
{
    private const string GameplayScenePath = "Assets/Scenes/GameplayScene.unity";

    [Test]
    public void PlayerPowerupVfx_HidesLinkedEffectsOnAwake_AndShowsThemForTheirBuff()
    {
        var root = new GameObject("Powerup VFX fixture");
        try
        {
            var asset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>("Assets/Visual_Effects/Vfx_Damage.vfx");
            Assert.That(asset, Is.Not.Null);
            var child = new GameObject("Vfx_Damage");
            child.transform.SetParent(root.transform, false);
            var effect = child.AddComponent<VisualEffect>();
            effect.visualEffectAsset = asset;
            Assert.That(child.activeSelf, Is.True, "El efecto arranca encendido.");

            var vfx = root.AddComponent<PlayerPowerupVfx>();
            typeof(PlayerPowerupVfx).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(vfx, null);

            Assert.That(child.activeSelf, Is.False, "Hasta que se recoja el powerup no debe haber VFX.");
            Assert.That(vfx.Has(TemporaryPowerupType.ExtraDamage), Is.True);
            Assert.That(vfx.Show(TemporaryPowerupType.ExtraDamage), Is.True);
            Assert.That(child.activeSelf, Is.True);
            Assert.That(vfx.Show(TemporaryPowerupType.Nuke), Is.False, "Un tipo sin VFX enlazado no cambia nada.");
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    [UnityTest]
    public IEnumerator EnteringGameplayFromAnotherScene_AddsPowerupControllerAndKeepsVfxOff()
    {
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        // Cargar GameplayScene sin pasar por el menú loguea errores ajenos (p. ej. PauseMenuUI sin servicio de ajustes).
        LogAssert.ignoreFailingMessages = true;
        try
        {
            Time.timeScale = 1f;
            // Lo que pasa al abrir el juego en TitleScreen: el hook de arranque corre una vez, sin player.
            typeof(SpecMetaBootstrap).GetMethod("OnSceneLoaded", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            Assert.That(Object.FindAnyObjectByType<PlayerMovement>(), Is.Null);

            // Y después se entra a la partida desde el menú.
            yield return EditorSceneManager.LoadSceneInPlayMode(GameplayScenePath, new LoadSceneParameters(LoadSceneMode.Single));
            float deadline = Time.realtimeSinceStartup + 20f;
            while (Time.realtimeSinceStartup < deadline &&
                   (Object.FindAnyObjectByType<PlayerMovement>() == null || TemporaryPowerupController.Instance == null))
                yield return null;

            PlayerMovement player = Object.FindAnyObjectByType<PlayerMovement>();
            Assert.That(player, Is.Not.Null, "La escena de juego debe tener player.");
            Assert.That(TemporaryPowerupController.Instance, Is.Not.Null,
                "Entrando desde otra escena el player debe recibir el controlador de powerups.");
            Assert.That(player.GetComponent<MetaProgressionApplier>(), Is.Not.Null);
            Assert.That(player.GetComponent<PlayerPowerupVfx>(), Is.Not.Null);

            VisualEffect[] effects = player.GetComponentsInChildren<VisualEffect>(true)
                .Where(e => e.visualEffectAsset != null && e.visualEffectAsset.name.StartsWith("Vfx_")).ToArray();
            Assert.That(effects, Is.Not.Empty, "Los VFX de powerups cuelgan del player.");
            foreach (VisualEffect effect in effects)
                Assert.That(effect.gameObject.activeSelf, Is.False, $"{effect.gameObject.name} no debe estar activo sin powerup.");
        }
        finally
        {
            LogAssert.ignoreFailingMessages = false;
            Time.timeScale = 1f;
            GameplayPause.Reset();
        }
        yield return null;
        yield return new ExitPlayMode();
    }

    [UnityTearDown]
    public IEnumerator RestoreMode()
    {
        if (Application.isPlaying) { Time.timeScale = 1f; yield return new ExitPlayMode(); }
    }
}
