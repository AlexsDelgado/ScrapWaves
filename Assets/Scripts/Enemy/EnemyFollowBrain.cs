using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(-60)]
[DisallowMultipleComponent]
public class EnemyFollowBrain : MonoBehaviour
{
    private static EnemyFollowBrain s_Instance;

    private readonly Dictionary<int, SimpleFollow> _followers = new Dictionary<int, SimpleFollow>(256);

    private void Awake()
    {
        if (s_Instance != null && s_Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        s_Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (s_Instance == this)
            s_Instance = null;
    }

    public static void EnsureExistsAndRegister(SimpleFollow follower)
    {
        if (follower == null)
            return;

        if (s_Instance == null)
        {
            var go = new GameObject("[EnemyFollowBrain]");
            s_Instance = go.AddComponent<EnemyFollowBrain>();
        }

        s_Instance.RegisterInternal(follower);
    }

    public static void UnregisterIfExists(SimpleFollow follower)
    {
        if (s_Instance == null || follower == null)
            return;
        s_Instance.UnregisterInternal(follower);
    }

    private void RegisterInternal(SimpleFollow follower)
    {
        int key = follower.GetInstanceID();
        _followers[key] = follower;
    }

    private void UnregisterInternal(SimpleFollow follower)
    {
        int key = follower.GetInstanceID();
        _followers.Remove(key);
    }

    private void FixedUpdate()
    {
        if (_followers.Count == 0)
            return;

        float dt = Time.fixedDeltaTime;
        EnemyMovementSteering.RefreshPositionCacheIfNeeded();

        foreach (var kv in _followers)
        {
            SimpleFollow follower = kv.Value;
            if (follower == null || !follower.isActiveAndEnabled)
                continue;

            follower.BrainFixedUpdate(dt);
        }
    }
}
