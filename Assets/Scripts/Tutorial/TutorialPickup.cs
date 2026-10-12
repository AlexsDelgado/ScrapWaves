using UnityEngine;

/// <summary>Authored, guaranteed lesson loot. Material credit is separate from XP and meta credit.</summary>
public sealed class TutorialPickup : MonoBehaviour
{
    [SerializeField] private MaterialType _material;
    [SerializeField] private Transform _visual;
    private TutorialDirector _owner;
    private int _amount;
    private Vector3 _safePosition;
    public MaterialType Material => _material;
    public bool IsCollected { get; private set; }
    public int Amount => _amount;
    public void Arm(TutorialDirector owner, int amount, Vector3 position)
    {
        _owner = owner; _amount = amount; IsCollected = false;
        _safePosition = position; transform.position = position;
        gameObject.SetActive(amount > 0);
    }
    private void Update()
    {
        if (_owner == null || !TutorialDirector.IsActive || GameplayPause.IsUiPaused || Time.timeScale <= 0 || _amount <= 0) return;
        var player = PlayerMovement.PlayerTransform;
        if (player == null) return;
        if (transform.position.y < _safePosition.y - 4f) transform.position = _safePosition;
        if (_visual != null) _visual.Rotate(Vector3.up, 45f * Time.deltaTime);
        Vector3 target = player.position + Vector3.up * .2f;
        float distance = Vector3.Distance(transform.position, target);
        if (distance < 1.6f) Collect();
        else if (distance < 7f) transform.position = Vector3.MoveTowards(transform.position, target, 12f * Time.deltaTime);
    }
    public void Collect()
    {
        if (IsCollected || _amount <= 0 || _owner == null || !TutorialDirector.IsActive) return;
        IsCollected = true;
        _owner.Inventory.Add(_material, _amount);
        gameObject.SetActive(false);
        _owner.NotifyCollected(this);
    }
}
