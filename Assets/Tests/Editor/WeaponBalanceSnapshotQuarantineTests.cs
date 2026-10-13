using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>Even explicit legacy snapshot commands cannot overwrite maintained tuning.</summary>
public sealed class WeaponBalanceSnapshotQuarantineTests
{
    private sealed class Snapshot
    {
        public string Path;
        public string Json;
        public bool Dirty;
        public string AssetHash;
        public string MetaHash;
        public string Guid;
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RetiredSnapshotCommand_PreservesEveryAuthoredAssetAndReference(bool throughMenu)
    {
        string[] paths = AssetDatabase.FindAssets("t:WeaponData")
            .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path).ToArray();
        foreach (string name in new[] { "AutomaticCannon", "Flamethrower", "Mortar", "RocketLauncher", "RotatingBlade" })
            Assert.That(paths, Does.Contain("Assets/ScriptableObjects/WeaponSO/" + name + ".asset"));
        Snapshot[] before = paths.Select(Capture).ToArray();

        // Repeated calls must remain harmless, including the discoverable Editor command.
        for (int iteration = 0; iteration < 2; iteration++)
        {
            LogAssert.Expect(LogType.Warning, WeaponBalanceSheetApplier.QuarantineMessage);
            if (throughMenu)
                Assert.That(EditorApplication.ExecuteMenuItem(
                    "Tools/ScrapWaves/Weapon Stats/Legacy Snapshot Import (Disabled)"), Is.True);
            else
                WeaponBalanceSheetApplier.ApplyFromSheetSnapshot();

            foreach (Snapshot baseline in before)
            {
                Snapshot after = Capture(baseline.Path);
                Assert.That(after.Json, Is.EqualTo(baseline.Json), baseline.Path + " serialized fields/references");
                Assert.That(after.Dirty, Is.EqualTo(baseline.Dirty), baseline.Path + " dirty flag");
                Assert.That(after.AssetHash, Is.EqualTo(baseline.AssetHash), baseline.Path + " bytes");
                Assert.That(after.MetaHash, Is.EqualTo(baseline.MetaHash), baseline.Path + " metadata bytes");
                Assert.That(after.Guid, Is.EqualTo(baseline.Guid), baseline.Path + " GUID");
            }
        }
    }

    private static Snapshot Capture(string path)
    {
        WeaponData data = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
        Assert.That(data, Is.Not.Null, path);
        return new Snapshot
        {
            Path = path,
            Json = EditorJsonUtility.ToJson(data),
            Dirty = EditorUtility.IsDirty(data),
            AssetHash = Hash(path),
            MetaHash = Hash(path + ".meta"),
            Guid = AssetDatabase.AssetPathToGUID(path)
        };
    }

    private static string Hash(string path)
    {
        using var sha = SHA256.Create();
        return Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path)));
    }
}
