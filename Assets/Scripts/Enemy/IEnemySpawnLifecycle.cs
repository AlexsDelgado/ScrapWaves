/// <summary>
/// Hook opcional para resetear estado al sacar/devolver enemigos del pool.
/// </summary>
public interface IEnemySpawnLifecycle
{
    void OnPoolSpawn();
    void OnPoolDespawn();
}
