using UnityEngine;

/// <summary>Fixed, fully prewarmed view pool. It never grows after construction.</summary>
public sealed class CombatTextPool
{
    private readonly CombatTextView[] _views;
    private readonly CombatTextView[] _available;
    private int _availableCount;

    public CombatTextPool(RectTransform parent, CombatTextProfile profile, int capacity)
    {
        int count = Mathf.Clamp(capacity, 1, profile.MaximumPooledViews);
        _views = new CombatTextView[count];
        _available = new CombatTextView[count];

        for (int i = 0; i < count; i++)
        {
            CombatTextView view = profile.ViewPrefab != null
                ? Object.Instantiate(profile.ViewPrefab, parent, false)
                : CombatTextView.CreateProgrammatic(parent, profile, i);
            if (view == null)
                view = CombatTextView.CreateProgrammatic(parent, profile, i);
            view.name = $"CombatTextView_{i:00}";
            view.Initialize(profile);
            _views[i] = view;
            _available[i] = view;
        }
        _availableCount = count;
    }

    public int Capacity => _views.Length;
    public int AvailableCount => _availableCount;
    public int ActiveCount => Capacity - AvailableCount;

    public bool TryAcquire(out CombatTextView view)
    {
        if (_availableCount <= 0)
        {
            view = null;
            return false;
        }
        view = _available[--_availableCount];
        _available[_availableCount] = null;
        return view != null;
    }

    public void Release(CombatTextView view)
    {
        if (view == null)
            return;
        view.ReleaseImmediately();
        if (_availableCount >= _available.Length)
            return;
        _available[_availableCount++] = view;
    }

    public void ReleaseAll()
    {
        _availableCount = 0;
        for (int i = 0; i < _views.Length; i++)
        {
            CombatTextView view = _views[i];
            if (view == null)
                continue;
            view.ReleaseImmediately();
            _available[_availableCount++] = view;
        }
    }
}
