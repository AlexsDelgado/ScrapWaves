using UnityEngine;

/// <summary>
/// Al pararse arriba de este bloque, empuja al jugador en arco hasta el Land del mismo sufijo
/// (Jump floor → Land floor, Jump sky → Land sky).
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Collider))]
public sealed class MapLaunchPad : MonoBehaviour
{
    [SerializeField, Tooltip("Plataforma de llegada. Vacío = busca un Land con el mismo sufijo del nombre.")]
    private Transform _landing;

    [SerializeField, Min(0.5f), Tooltip("Metros que el arco sube por encima del punto más alto, para no rozar el borde.")]
    private float _apexClearance = 12f;

    private Collider _collider;
    private bool _busy;

    private void Awake()
    {
        _collider = GetComponent<Collider>();
        if (_landing == null)
            _landing = FindLanding(name, transform.position);
    }

    private void OnCollisionStay(Collision collision)
    {
        if (_busy || _landing == null)
            return;

        PlayerMovement movement = collision.collider.GetComponentInParent<PlayerMovement>();
        if (movement == null || movement.IsLaunching)
            return;

        Collider landingCollider = _landing.GetComponent<Collider>();
        if (landingCollider == null)
            return;

        float halfHeight = 1f;
        Collider body = movement.GetComponent<Collider>();
        if (body != null)
            halfHeight = body.bounds.extents.y;

        float feet = movement.transform.position.y - halfHeight;
        if (feet < _collider.bounds.max.y - 0.35f)
            return;

        Vector3 start = movement.transform.position;
        Bounds land = landingCollider.bounds;
        Vector3 target = new Vector3(land.center.x, land.max.y + halfHeight + 0.05f, land.center.z);

        if (!TryComputeLaunchVelocity(start, target, _apexClearance, out Vector3 velocity, out float flightTime))
            return;

        _busy = true;
        movement.LaunchWithVelocity(velocity, flightTime + 0.75f);
    }

    private void OnCollisionExit(Collision collision)
    {
        if (collision.collider.GetComponentInParent<PlayerMovement>() != null)
            _busy = false;
    }

    private void OnDrawGizmos()
    {
        Transform landing = _landing != null ? _landing : FindLanding(name, transform.position);
        if (landing == null)
            return;

        Collider from = GetComponent<Collider>();
        Collider to = landing.GetComponent<Collider>();
        if (from == null || to == null)
            return;

        Vector3 start = new Vector3(from.bounds.center.x, from.bounds.max.y + 1f, from.bounds.center.z);
        Vector3 target = new Vector3(to.bounds.center.x, to.bounds.max.y + 1f, to.bounds.center.z);
        if (!TryComputeLaunchVelocity(start, target, _apexClearance, out Vector3 velocity, out float flightTime))
            return;

        Gizmos.color = new Color(0.3f, 0.85f, 1f, 0.9f);
        Vector3 gravity = Physics.gravity;
        Vector3 previous = start;
        const int Steps = 24;
        for (int i = 1; i <= Steps; i++)
        {
            float t = flightTime * (i / (float)Steps);
            Vector3 point = start + velocity * t + 0.5f * gravity * (t * t);
            Gizmos.DrawLine(previous, point);
            previous = point;
        }
    }

    private static Transform FindLanding(string jumpName, Vector3 from)
    {
        if (!TryGetSuffix(jumpName, "jump", out string suffix))
            return null;

        Transform best = null;
        float bestDistance = float.PositiveInfinity;
        Transform[] all = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < all.Length; i++)
        {
            Transform candidate = all[i];
            if (!TryGetSuffix(candidate.name, "land", out string landSuffix))
                continue;
            if (!string.Equals(landSuffix, suffix, System.StringComparison.OrdinalIgnoreCase))
                continue;
            if (candidate.GetComponent<Collider>() == null)
                continue;

            float distance = (candidate.position - from).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    private static bool TryGetSuffix(string objectName, string prefix, out string suffix)
    {
        suffix = null;
        if (string.IsNullOrWhiteSpace(objectName))
            return false;

        string trimmed = objectName.Trim();
        if (trimmed.Length <= prefix.Length + 1)
            return false;
        if (!trimmed.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
            return false;
        if (!char.IsWhiteSpace(trimmed[prefix.Length]))
            return false;

        suffix = trimmed.Substring(prefix.Length + 1).Trim();
        return suffix.Length > 0;
    }

    public static bool TryComputeLaunchVelocity(Vector3 start, Vector3 target, float apexClearance, out Vector3 velocity, out float flightTime)
    {
        velocity = Vector3.zero;
        flightTime = 0f;

        float gravity = -Physics.gravity.y;
        if (gravity < 0.01f)
            return false;

        float clearance = Mathf.Max(0.5f, apexClearance);
        float apexY = Mathf.Max(start.y, target.y) + clearance;
        float rise = Mathf.Max(0.05f, apexY - start.y);
        float drop = Mathf.Max(0.05f, apexY - target.y);
        float timeUp = Mathf.Sqrt(2f * rise / gravity);
        float timeDown = Mathf.Sqrt(2f * drop / gravity);
        flightTime = timeUp + timeDown;
        if (flightTime < 0.05f)
            return false;

        Vector3 planar = new Vector3(target.x - start.x, 0f, target.z - start.z);
        velocity = planar / flightTime;
        velocity.y = Mathf.Sqrt(2f * gravity * rise);
        return true;
    }
}
