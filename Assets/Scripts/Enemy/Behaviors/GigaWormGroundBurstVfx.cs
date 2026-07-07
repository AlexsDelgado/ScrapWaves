using UnityEngine;

/// <summary>
/// Chatarra que salta del suelo cuando el Giga Worm se mueve bajo tierra.
/// </summary>
public static class GigaWormGroundBurstVfx
{
    private static readonly Color[] JunkColors =
    {
        new(0.45f, 0.42f, 0.38f),
        new(0.55f, 0.35f, 0.2f),
        new(0.3f, 0.32f, 0.34f)
    };

    public static void Spawn(Vector3 groundPoint, int pieceCount = 3)
    {
        pieceCount = Mathf.Clamp(pieceCount, 1, 6);
        for (int i = 0; i < pieceCount; i++)
        {
            Vector2 offset = Random.insideUnitCircle * 1.2f;
            Vector3 spawn = groundPoint + new Vector3(offset.x, 0.05f, offset.y);
            SpawnPiece(spawn);
        }
    }

    private static Material[] s_junkMaterials;

    private static Material GetJunkMaterial(int colorIndex)
    {
        if (s_junkMaterials == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            s_junkMaterials = new Material[JunkColors.Length];
            for (int i = 0; i < JunkColors.Length; i++)
            {
                s_junkMaterials[i] = new Material(shader)
                {
                    color = JunkColors[i],
                    hideFlags = HideFlags.HideAndDontSave
                };
            }
        }

        return s_junkMaterials[colorIndex % JunkColors.Length];
    }

    private static void SpawnPiece(Vector3 position)
    {
        PrimitiveType type = Random.value > 0.5f ? PrimitiveType.Cube : PrimitiveType.Cylinder;
        GameObject piece = GameObject.CreatePrimitive(type);
        piece.name = "GigaWormGroundBurst";
        piece.transform.position = position;
        piece.transform.localScale = Vector3.one * Random.Range(0.15f, 0.35f);
        piece.transform.rotation = Random.rotation;

        Collider col = piece.GetComponent<Collider>();
        if (col != null)
            Object.Destroy(col);

        Renderer renderer = piece.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = GetJunkMaterial(Random.Range(0, JunkColors.Length));

        Rigidbody rb = piece.AddComponent<Rigidbody>();
        rb.mass = Random.Range(0.2f, 0.8f);
        Vector3 impulse = new(
            Random.Range(-1.5f, 1.5f),
            Random.Range(3f, 6f),
            Random.Range(-1.5f, 1.5f));
        rb.AddForce(impulse, ForceMode.VelocityChange);
        rb.AddTorque(Random.insideUnitSphere * 8f, ForceMode.VelocityChange);

        GroundBurstPieceLifetime lifetime = piece.AddComponent<GroundBurstPieceLifetime>();
        lifetime.Configure(Random.Range(0.8f, 1.4f));
    }

    private sealed class GroundBurstPieceLifetime : MonoBehaviour
    {
        private float _lifetime;

        public void Configure(float lifetime)
        {
            _lifetime = lifetime;
            Destroy(gameObject, lifetime);
        }

        private void Update()
        {
            _lifetime -= Time.deltaTime;
            if (_lifetime <= 0f)
                return;

            float alpha = Mathf.Clamp01(_lifetime / 0.35f);
            Renderer renderer = GetComponent<Renderer>();
            if (renderer != null && renderer.material != null)
            {
                Color c = renderer.material.color;
                c.a = alpha;
                renderer.material.color = c;
            }
        }
    }
}
