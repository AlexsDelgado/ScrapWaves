using System;
using System.Collections.Generic;

/// <summary>
/// Picks BGM track indices for title (alternate two) or gameplay (shuffle bag).
/// </summary>
public sealed class BgmTrackSelector
{
    public enum Mode
    {
        AlternateTwo,
        ShuffleBag
    }

    private readonly Mode _mode;
    private readonly int _trackCount;
    private readonly Func<int, int> _nextRandom;
    private readonly List<int> _bag = new();
    private int _lastIndex = -1;
    private bool _hasPlayed;

    public BgmTrackSelector(Mode mode, int trackCount, Func<int, int> nextRandom = null)
    {
        if (trackCount < 0)
            throw new ArgumentOutOfRangeException(nameof(trackCount));
        if (mode == Mode.AlternateTwo && trackCount > 0 && trackCount != 2)
            throw new ArgumentException("AlternateTwo requires exactly 2 tracks.", nameof(trackCount));

        _mode = mode;
        _trackCount = trackCount;
        _nextRandom = nextRandom ?? (exclusiveMax => UnityEngine.Random.Range(0, exclusiveMax));
    }

    public int LastIndex => _lastIndex;

    public int Next()
    {
        if (_trackCount <= 0)
            throw new InvalidOperationException("No tracks available.");

        if (_trackCount == 1)
        {
            _lastIndex = 0;
            _hasPlayed = true;
            return 0;
        }

        int index = _mode == Mode.AlternateTwo ? NextAlternate() : NextFromBag();
        _lastIndex = index;
        _hasPlayed = true;
        return index;
    }

    private int NextAlternate()
    {
        if (!_hasPlayed)
            return _nextRandom(2);
        return 1 - _lastIndex;
    }

    private int NextFromBag()
    {
        if (_bag.Count == 0)
            RefillBag();

        int index = _bag[0];
        _bag.RemoveAt(0);
        return index;
    }

    private void RefillBag()
    {
        _bag.Clear();
        for (int i = 0; i < _trackCount; i++)
            _bag.Add(i);

        for (int i = _bag.Count - 1; i > 0; i--)
        {
            int j = _nextRandom(i + 1);
            (_bag[i], _bag[j]) = (_bag[j], _bag[i]);
        }

        if (_hasPlayed && _bag.Count > 1 && _bag[0] == _lastIndex)
        {
            int swapWith = 1 + _nextRandom(_bag.Count - 1);
            (_bag[0], _bag[swapWith]) = (_bag[swapWith], _bag[0]);
        }
    }
}
