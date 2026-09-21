using NUnit.Framework;
using UnityEngine;

public sealed class PickupGroundFallTests
{
    [Test]
    public void Tick_FallsAndSnapsToGroundHit()
    {
        // Plane at y=0 covering a wide area.
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.position = Vector3.zero;
        ground.layer = LayerMask.NameToLayer("Default");

        try
        {
            Vector3 position = new Vector3(0f, 8f, 0f);
            float velocity = 0f;
            LayerMask mask = LayerMask.GetMask("Default");
            bool landed = false;

            for (int i = 0; i < 300 && !landed; i++)
                landed = PickupGroundFall.Tick(ref position, ref velocity, 1f / 60f, 0.35f, mask);

            Assert.That(landed, Is.True);
            Assert.That(position.y, Is.EqualTo(0.35f).Within(0.05f));
            Assert.That(velocity, Is.EqualTo(0f));
        }
        finally
        {
            Object.DestroyImmediate(ground);
        }
    }
}
