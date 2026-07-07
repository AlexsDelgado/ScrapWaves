using UnityEngine;

public class MaterialDrop : MonoBehaviour
{
    [SerializeField, Min(1)] private int _defaultAmount = 1;
    [SerializeField, Min(0.01f)] private float _pickupRadius = 0.75f;
    [SerializeField, Min(0f)] private float _magnetRadius = 7f;
    [SerializeField, Min(0f)] private float _magnetSpeed = 14f;

    private MaterialPool _pool;
    private MaterialPoolMember _member;
    private MaterialType _material;
    private int _amount;

    private void Awake() => _member = GetComponent<MaterialPoolMember>();

    public void ActivateFromPool(MaterialPool pool, MaterialType material, int amount)
    {
        _pool = pool;
        _material = material;
        _amount = amount > 0 ? amount : _defaultAmount;
    }

    private void Update()
    {
        MaterialPickupReceiver receiver = MaterialPickupReceiver.Instance;
        if (receiver == null)
            return;

        Vector3 target = receiver.PickupPoint;
        float dist = Vector3.Distance(transform.position, target);
        if (dist <= _pickupRadius)
        {
            receiver.GrantMaterial(_material, _amount);
            if (_member != null)
                _member.Despawn();
            else
                _pool?.Release(gameObject);
            return;
        }

        if (_magnetRadius > 0f && _magnetSpeed > 0f && dist <= _magnetRadius)
            transform.position = Vector3.MoveTowards(transform.position, target, _magnetSpeed * Time.deltaTime);
    }
}
