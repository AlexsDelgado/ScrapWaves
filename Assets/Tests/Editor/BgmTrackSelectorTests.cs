using System;
using System.Collections.Generic;
using NUnit.Framework;

public sealed class BgmTrackSelectorTests
{
    [Test]
    public void AlternateTwo_PicksRandomFirstThenAlternates()
    {
        int call = 0;
        int[] randomSequence = { 1 };
        var selector = new BgmTrackSelector(
            BgmTrackSelector.Mode.AlternateTwo,
            2,
            exclusiveMax => randomSequence[call++]);

        Assert.That(selector.Next(), Is.EqualTo(1));
        Assert.That(selector.Next(), Is.EqualTo(0));
        Assert.That(selector.Next(), Is.EqualTo(1));
        Assert.That(selector.Next(), Is.EqualTo(0));
    }

    [Test]
    public void ShuffleBag_DoesNotRepeatWithinAPass()
    {
        int call = 0;
        // Fisher-Yates with always j=0 for count=4 yields a full permutation without duplicates.
        var selector = new BgmTrackSelector(
            BgmTrackSelector.Mode.ShuffleBag,
            4,
            exclusiveMax =>
            {
                call++;
                return 0;
            });

        var firstPass = new List<int>
        {
            selector.Next(),
            selector.Next(),
            selector.Next(),
            selector.Next()
        };

        Assert.That(firstPass, Is.EquivalentTo(new[] { 0, 1, 2, 3 }));
        Assert.That(new HashSet<int>(firstPass).Count, Is.EqualTo(4));
        Assert.That(call, Is.GreaterThan(0));
    }

    [Test]
    public void ShuffleBag_ReshuffleAvoidsImmediateRepeatOfLastTrack()
    {
        int idx = 0;
        // First refill (count=3): j=0, j=0 => bag [1,2,0]
        // Second refill: j=2, j=1 => identity [0,1,2], then swap 0 with 1+0 => [1,0,2]
        int[] scripted = { 0, 0, 2, 1, 0 };

        int Scripted(int exclusiveMax)
        {
            int value = scripted[Math.Min(idx, scripted.Length - 1)] % exclusiveMax;
            idx++;
            return value;
        }

        var selector = new BgmTrackSelector(BgmTrackSelector.Mode.ShuffleBag, 3, Scripted);
        var firstPass = new List<int> { selector.Next(), selector.Next(), selector.Next() };
        int afterReshuffle = selector.Next();

        Assert.That(firstPass, Is.EquivalentTo(new[] { 0, 1, 2 }));
        Assert.That(firstPass[2], Is.EqualTo(0));
        Assert.That(afterReshuffle, Is.Not.EqualTo(0),
            "Reshuffled bag must not start with the track that just finished.");
    }

    [Test]
    public void AlternateTwo_RejectsWrongTrackCount()
    {
        Assert.Throws<ArgumentException>(() =>
            new BgmTrackSelector(BgmTrackSelector.Mode.AlternateTwo, 4));
    }
}
