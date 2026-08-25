using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class PassiveItemInventoryTests
{
    private readonly List<Object> _cleanup = new();

    [TearDown]
    public void TearDown()
    {
        for (int i = _cleanup.Count - 1; i >= 0; i--)
        {
            if (_cleanup[i] != null)
                Object.DestroyImmediate(_cleanup[i]);
        }

        _cleanup.Clear();
    }

    [TestCase(PassiveItemSlot.Head, 1)]
    [TestCase(PassiveItemSlot.Core, 1)]
    [TestCase(PassiveItemSlot.Arm, 2)]
    [TestCase(PassiveItemSlot.Leg, 2)]
    public void SlotCapacity_MatchesPhysicalLoadout(PassiveItemSlot slot, int expected)
    {
        Assert.That(PassiveItemInventory.GetCapacity(slot), Is.EqualTo(expected));
    }

    [Test]
    public void TryAssign_TargetsExactPhysicalSlot_AndRejectsDuplicateData()
    {
        var inventory = new PassiveItemInventory();
        PassiveItemData firstData = CreateItem("First Arm", PassiveItemSlot.Arm);
        PassiveItemData secondData = CreateItem("Second Arm", PassiveItemSlot.Arm);
        var first = new PassiveItemInstance { Data = firstData };
        var second = new PassiveItemInstance { Data = secondData };

        Assert.That(inventory.TryAssign(first, 1), Is.True);
        Assert.That(inventory.Get(PassiveItemSlot.Arm, 0), Is.Null);
        Assert.That(inventory.Get(PassiveItemSlot.Arm, 1), Is.SameAs(first));
        Assert.That(first.SlotIndex, Is.EqualTo(1));

        Assert.That(inventory.TryAssign(new PassiveItemInstance { Data = firstData }, 0), Is.False);
        Assert.That(inventory.TryAssign(second, 0), Is.True);
        Assert.That(inventory.CountEquipped(PassiveItemSlot.Arm), Is.EqualTo(2));
        Assert.That(inventory.TryAssign(new PassiveItemInstance { Data = CreateItem("Third Arm", PassiveItemSlot.Arm) }, 2), Is.False);
    }

    [Test]
    public void ReplaceRemoveAndClear_MutateOnlyRequestedSlots()
    {
        var inventory = new PassiveItemInventory();
        var head = new PassiveItemInstance { Data = CreateItem("Head", PassiveItemSlot.Head) };
        var firstArm = new PassiveItemInstance { Data = CreateItem("First Arm", PassiveItemSlot.Arm) };
        var replacement = new PassiveItemInstance { Data = CreateItem("Replacement", PassiveItemSlot.Arm), Level = 4 };
        Assert.That(inventory.TryAssign(head, 0), Is.True);
        Assert.That(inventory.TryAssign(firstArm, 1), Is.True);

        Assert.That(inventory.TryReplace(PassiveItemSlot.Arm, 1, replacement, out PassiveItemInstance previous), Is.True);
        Assert.That(previous, Is.SameAs(firstArm));
        Assert.That(inventory.Get(PassiveItemSlot.Arm, 1), Is.SameAs(replacement));
        Assert.That(inventory.Get(PassiveItemSlot.Head, 0), Is.SameAs(head));

        Assert.That(inventory.TryRemove(PassiveItemSlot.Arm, 1, out PassiveItemInstance removed), Is.True);
        Assert.That(removed, Is.SameAs(replacement));
        Assert.That(inventory.Get(PassiveItemSlot.Arm, 1), Is.Null);
        Assert.That(inventory.TryRemove(PassiveItemSlot.Arm, 1, out _), Is.False);

        Assert.That(inventory.Clear(), Is.True);
        Assert.That(inventory.Get(PassiveItemSlot.Head, 0), Is.Null);
        Assert.That(inventory.Clear(), Is.False);
    }

    private PassiveItemData CreateItem(string displayName, PassiveItemSlot slot)
    {
        PassiveItemData data = ScriptableObject.CreateInstance<PassiveItemData>();
        _cleanup.Add(data);
        data.name = displayName;
        SetPrivateField(data, "_displayName", displayName);
        SetPrivateField(data, "_slot", slot);
        SetPrivateField(data, "_maxLevel", 6);
        return data;
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.That(field, Is.Not.Null, $"Missing field {fieldName} on {target.GetType().Name}");
        field.SetValue(target, value);
    }
}
