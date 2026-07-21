using UnityEngine;

public class MaterialPoolMember : MonoBehaviour
{
    private MaterialPool _owner;

    public void Bind(MaterialPool owner) => _owner = owner;
    public bool BelongsTo(MaterialPool owner) => _owner == owner;

    public void Despawn()
    {
        if (_owner != null)
            _owner.Release(gameObject);
        else
            gameObject.SetActive(false);
    }
}
