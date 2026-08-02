using UnityEngine;

public interface IWeaponVfxPrewarm
{
    void Prewarm();
}

public interface IWeaponVfxContextReceiver
{
    void ApplyContext(in WeaponPresentationContext context);
}

[DisallowMultipleComponent]
public sealed class PooledWeaponVfx : MonoBehaviour
{
    private ParticleSystem[] _particleSystems;
    private IWeaponVfxContextReceiver[] _contextReceivers;
    private Transform _poolParent;

    public bool IsActive { get; private set; }
    public bool IsLooping { get; private set; }
    public float ReleaseTime { get; private set; }
    public Transform CurrentAnchor { get; private set; }

    public void Initialize()
    {
        _poolParent = transform.parent;
        MonoBehaviour[] behaviours = GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IWeaponVfxPrewarm prewarmable)
                prewarmable.Prewarm();
        }

        _particleSystems = GetComponentsInChildren<ParticleSystem>(true);
        MonoBehaviour[] contextBehaviours = GetComponentsInChildren<MonoBehaviour>(true);
        System.Collections.Generic.List<IWeaponVfxContextReceiver> contextReceivers = new();
        for (int i = 0; i < contextBehaviours.Length; i++)
        {
            if (contextBehaviours[i] is IWeaponVfxContextReceiver receiver)
                contextReceivers.Add(receiver);
        }
        _contextReceivers = contextReceivers.ToArray();
        for (int i = 0; i < _particleSystems.Length; i++)
        {
            ParticleSystem.MainModule main = _particleSystems[i].main;
            if (main.stopAction == ParticleSystemStopAction.Destroy)
                main.stopAction = ParticleSystemStopAction.None;
        }
        Release();
    }

    public void Play(in WeaponPresentationContext context, float duration, float now, bool loop)
    {
        ApplyTransform(in context);

        if (_contextReceivers != null)
        {
            for (int i = 0; i < _contextReceivers.Length; i++)
                _contextReceivers[i].ApplyContext(in context);
        }

        gameObject.SetActive(true);
        IsActive = true;
        IsLooping = loop;
        ReleaseTime = loop ? float.PositiveInfinity : now + Mathf.Max(0f, duration);

        if (_particleSystems == null)
            _particleSystems = GetComponentsInChildren<ParticleSystem>(true);

        for (int i = 0; i < _particleSystems.Length; i++)
        {
            ParticleSystem particles = _particleSystems[i];
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.Play(true);
        }
    }

    public void UpdateTransform(in WeaponPresentationContext context)
    {
        if (!IsActive)
            return;

        ApplyTransform(in context);
        if (_contextReceivers != null)
        {
            for (int i = 0; i < _contextReceivers.Length; i++)
                _contextReceivers[i].ApplyContext(in context);
        }
    }

    public bool ShouldRelease(float now)
    {
        return IsActive && !IsLooping && now >= ReleaseTime;
    }

    public void Release()
    {
        if (_particleSystems != null)
        {
            for (int i = 0; i < _particleSystems.Length; i++)
                _particleSystems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        IsActive = false;
        IsLooping = false;
        ReleaseTime = 0f;
        gameObject.SetActive(false);
        ReturnToPoolParent();
    }

    private void ApplyTransform(in WeaponPresentationContext context)
    {
        Quaternion rotation = Quaternion.LookRotation(context.Direction, GetStableUp(context.Direction));
        CurrentAnchor = context.Anchor;
        if (CurrentAnchor != null)
        {
            transform.SetParent(CurrentAnchor, worldPositionStays: false);
            transform.localPosition = Vector3.zero;
            transform.rotation = rotation;
            return;
        }

        // World impacts must not inherit motion from the presentation controller,
        // which normally lives under the moving player hierarchy.
        if (transform.parent != null)
            transform.SetParent(null, worldPositionStays: false);
        transform.SetPositionAndRotation(context.Position, rotation);
    }

    private void ReturnToPoolParent()
    {
        CurrentAnchor = null;
        if (transform.parent != _poolParent)
            transform.SetParent(_poolParent, worldPositionStays: false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }

    private static Vector3 GetStableUp(Vector3 direction)
    {
        return Mathf.Abs(Vector3.Dot(direction.normalized, Vector3.up)) > 0.98f
            ? Vector3.forward
            : Vector3.up;
    }
}
