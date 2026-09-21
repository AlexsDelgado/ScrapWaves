using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Cubre la dormancia vertical (pausar en vez de borrar al cambiar de piso) y el marcador de
/// objetivo que evita el softlock de elites.
/// </summary>
public class EnemyDormancyTests
{
    private readonly List<GameObject> _spawned = new();

    private GameObject NewEnemy(string name)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        _spawned.Add(go);
        return go;
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = _spawned.Count - 1; i >= 0; i--)
        {
            if (_spawned[i] != null)
                Object.DestroyImmediate(_spawned[i]);
        }
        _spawned.Clear();

        EnemyDormancyRegistry.MaxDormant = 150;
        UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
    }

    // ---------- Dormancia ----------

    [Test]
    public void EnterDormancy_KeepsTheObjectAliveButInert()
    {
        GameObject go = NewEnemy("Dormilon");
        EnemyVerticalEngagement engagement = go.AddComponent<EnemyVerticalEngagement>();

        engagement.EnterDormancy();

        Assert.That(engagement.IsDormant, Is.True);
        Assert.That(go, Is.Not.Null, "el enemigo NO se destruye: ese era el bug de inmersion");
        Assert.That(go.activeInHierarchy, Is.True, "sigue activo, solo congelado y oculto");
        Assert.That(go.GetComponent<Renderer>().enabled, Is.False);
        Assert.That(go.GetComponent<Collider>().enabled, Is.False);
        Assert.That(EnemyDormancyRegistry.DormantCount, Is.EqualTo(1));
    }

    [Test]
    public void ExitDormancy_RestoresTheExactPreviousEnabledState()
    {
        GameObject go = NewEnemy("Restaurado");
        // Un collider que YA venia deshabilitado no debe quedar habilitado al despertar.
        BoxCollider extra = go.AddComponent<BoxCollider>();
        extra.enabled = false;

        EnemyVerticalEngagement engagement = go.AddComponent<EnemyVerticalEngagement>();

        engagement.EnterDormancy();
        engagement.ExitDormancy();

        Assert.That(engagement.IsDormant, Is.False);
        Assert.That(go.GetComponent<Renderer>().enabled, Is.True);
        Assert.That(extra.enabled, Is.False, "un collider ya deshabilitado tiene que seguir deshabilitado");
        Assert.That(EnemyDormancyRegistry.DormantCount, Is.EqualTo(0));
    }

    [Test]
    public void OnPoolDespawn_WakesTheEnemyBeforeItGoesBackToThePool()
    {
        // Regresion: SetActive(false)/(true) no restaura los .enabled por componente, asi que
        // soltar al pool un enemigo dormido devolvia una instancia invisible para siempre.
        GameObject go = NewEnemy("Reciclado");
        EnemyVerticalEngagement engagement = go.AddComponent<EnemyVerticalEngagement>();

        engagement.EnterDormancy();
        ((IEnemySpawnLifecycle)engagement).OnPoolDespawn();

        Assert.That(engagement.IsDormant, Is.False);
        Assert.That(go.GetComponent<Renderer>().enabled, Is.True);
        Assert.That(go.GetComponent<Collider>().enabled, Is.True);
    }

    // ---------- Exento ----------

    [Test]
    public void ExemptEnemy_NeverGoesDormant()
    {
        GameObject go = NewEnemy("Elite");
        EnemyVerticalEngagement engagement = go.AddComponent<EnemyVerticalEngagement>();

        engagement.SetExempt(true);
        engagement.EnterDormancy();

        Assert.That(engagement.IsExempt, Is.True);
        Assert.That(engagement.IsDormant, Is.False);
        Assert.That(EnemyDormancyRegistry.DormantCount, Is.EqualTo(0));
    }

    [Test]
    public void ResetEngagement_ClearsExemptSoRecycledElitesBehaveAsNormalEnemies()
    {
        // Regresion: _exempt es un SerializeField que solo se escribia en Awake, asi que una
        // instancia pooleada marcada como elite quedaba exenta para siempre.
        GameObject go = NewEnemy("Reutilizado");
        EnemyVerticalEngagement engagement = go.AddComponent<EnemyVerticalEngagement>();

        engagement.SetExempt(true);
        Assert.That(engagement.IsExempt, Is.True);

        engagement.ResetEngagement();

        Assert.That(engagement.IsExempt, Is.False, "al volver del pool deja de ser objetivo");
        engagement.EnterDormancy();
        Assert.That(engagement.IsDormant, Is.True);
    }

    // ---------- Tope de dormidos ----------

    [Test]
    public void DormantCap_EvictsTheFarthestFirst()
    {
        // La expulsión usa Object.Destroy, que es lo correcto en runtime pero loguea error en
        // EditMode. Es un artefacto del test, no del código de producción.
        UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;

        EnemyDormancyRegistry.MaxDormant = 2;

        GameObject near = NewEnemy("Cerca");
        near.transform.position = new Vector3(1f, 0f, 0f);
        GameObject mid = NewEnemy("Medio");
        mid.transform.position = new Vector3(20f, 0f, 0f);
        GameObject far = NewEnemy("Lejos");
        far.transform.position = new Vector3(900f, 0f, 0f);

        near.AddComponent<EnemyVerticalEngagement>().EnterDormancy();
        far.AddComponent<EnemyVerticalEngagement>().EnterDormancy();
        mid.AddComponent<EnemyVerticalEngagement>().EnterDormancy();

        Assert.That(EnemyDormancyRegistry.DormantCount, Is.EqualTo(2), "el tope se respeta");
        Assert.That(far.GetComponent<EnemyVerticalEngagement>().IsDormant, Is.False,
            "el mas lejano al jugador es el primero en irse");
    }

    // ---------- Marcador de objetivo ----------

    [Test]
    public void EliteObjectiveMarker_ReportsRemovalExactlyOnce()
    {
        GameObject go = NewEnemy("EliteMarcado");
        int calls = 0;
        EliteObjectiveMarker marker = EliteObjectiveMarker.Bind(go, _ => calls++);

        Assert.That(marker.IsTrackedObjective, Is.True);

        ((IEnemySpawnLifecycle)marker).OnPoolDespawn();
        ((IEnemySpawnLifecycle)marker).OnPoolDespawn();

        Assert.That(calls, Is.EqualTo(1), "muerte y despawn seguidos no pueden contar dos veces");
        Assert.That(marker.IsTrackedObjective, Is.False);
    }

    [Test]
    public void EliteObjectiveMarker_UnbindSuppressesTheCallback()
    {
        GameObject go = NewEnemy("EliteLimpiado");
        int calls = 0;
        EliteObjectiveMarker marker = EliteObjectiveMarker.Bind(go, _ => calls++);

        marker.Unbind();
        ((IEnemySpawnLifecycle)marker).OnPoolDespawn();

        Assert.That(calls, Is.EqualTo(0), "el spawner que limpia no debe avisarse a si mismo");
    }

    [Test]
    public void EliteObjectiveMarker_PoolSpawnDropsAStaleBinding()
    {
        GameObject go = NewEnemy("EliteReciclado");
        int calls = 0;
        EliteObjectiveMarker marker = EliteObjectiveMarker.Bind(go, _ => calls++);

        ((IEnemySpawnLifecycle)marker).OnPoolSpawn();
        ((IEnemySpawnLifecycle)marker).OnPoolDespawn();

        Assert.That(calls, Is.EqualTo(0), "un lease nuevo deja de ser objetivo de la oleada vieja");
    }

    // ---------- Contador de elites ----------

    [Test]
    public void EliteCounter_ReachesZeroWhenAnEliteLeavesWithoutDying()
    {
        // Este es el softlock: la culla vertical devolvia el elite al pool sin disparar OnDied,
        // el contador quedaba clavado y el Overheat, que no tiene temporizador, no cerraba nunca.
        GameObject host = NewEnemy("EliteWaveSpawner");
        OverheatEliteWaveSpawner spawner = host.AddComponent<OverheatEliteWaveSpawner>();

        GameObject elite = NewEnemy("Slime_Elite");
        elite.AddComponent<EnemyHealth>();

        Invoke(spawner, "TrackElite", elite);
        Assert.That(spawner.ElitesRemaining, Is.EqualTo(1));

        // Sale de juego sin morir.
        Invoke(spawner, "UntrackElite", elite.transform);
        Assert.That(spawner.ElitesRemaining, Is.EqualTo(0));

        // Y repetirlo no lo deja en negativo ni vuelve a avisar.
        Invoke(spawner, "UntrackElite", elite.transform);
        Assert.That(spawner.ElitesRemaining, Is.EqualTo(0));
    }

    private static void Invoke(object target, string method, object arg)
    {
        MethodInfo mi = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(mi, Is.Not.Null, "no existe el metodo " + method);
        mi.Invoke(target, new[] { arg });
    }
}
