using UnityEngine;

public sealed partial class CombatTextDirector
{
    private struct HealingSlot
    {
        public CombatTextView View;
        public Transform Target;
        public Vector3 AnchorOffset;
        public long Total;
        public float Started;
        public float Scale;
    }

    // A separate, bounded channel keeps healing out of damage totals and prevents
    // rapid lifesteal/regeneration from exhausting enemy damage-number capacity.
    private const float HealingMergeWindow = .2f;
    private readonly HealingSlot[] _healingSlots = new HealingSlot[4];
    private CombatTextPool _healingPool;
    public int ActiveHealingTextCount => _healingPool?.ActiveCount ?? 0;

    public bool TryEmitHealing(int restored, Transform player, float now)
    {
        if (_disposed || restored <= 0 || player == null || !player.gameObject.activeInHierarchy)
            return false;
        SyncAccessibilityFromOptions();
        // Health recovery remains important in ImportantOnly; Off hides every number.
        if (!_profile.Enabled || _accessibility.CombatText == CombatTextMode.Off)
            return false;
        Vector3 anchor = ResolveHealingAnchor(player);
        if (!CombatTextVisibilityPolicy.TryEvaluateWorld(_camera, anchor,
                CombatTextPriority.MajorAbility, _profile, out _, out _))
            return false;

        int free = -1;
        int latest = -1;
        for (int i = 0; i < _healingSlots.Length; i++)
        {
            ref HealingSlot slot = ref _healingSlots[i];
            if (slot.View == null) { if (free < 0) free = i; continue; }
            if (slot.Target != player) continue;
            if (latest < 0 || slot.Started > _healingSlots[latest].Started) latest = i;
            if (now - slot.Started < HealingMergeWindow)
            {
                MergeHealing(ref slot, restored);
                return true;
            }
        }
        if (_healingPool == null)
        {
            var root = new GameObject("Player Healing Text");
            root.transform.SetParent(_viewRoot, false);
            _healingPool = new CombatTextPool(root.transform, _profile, _healingSlots.Length);
        }
        if (free < 0 || !_healingPool.TryAcquire(out CombatTextView view))
        {
            if (latest < 0) return false;
            MergeHealing(ref _healingSlots[latest], restored);
            return true;
        }
        float scale = .8f * _profile.Normal.BaseScale * _accessibility.CombatTextScale;
        CombatTextPresentation presentation = new(restored, CombatTextStyleId.Normal,
            CombatTextPriority.MajorAbility, default, false, false, false, false,
            false, _accessibility.ReducedFlash, false, anchor, scale,
            player.GetInstanceID() ^ free, _profile.GetMotion(CombatTextStyleId.Normal,
                false, _accessibility.ReducedMotion));
        view.PlayHealing(in presentation, _camera);
        _healingSlots[free] = new HealingSlot { View = view, Target = player,
            AnchorOffset = anchor - player.position, Total = restored, Started = now, Scale = scale };
        return true;
    }

    private Vector3 ResolveHealingAnchor(Transform player)
    {
        // Start at chest height. Sample the body shape, not pickup triggers or
        // enemy text offsets, and place the text in front of the visible torso.
        Vector3 anchor = player.TransformPoint(Vector3.up * .4f);
        float surfaceOffset = .35f;
        CharacterController body = player.GetComponent<CharacterController>();
        if (body != null)
        {
            anchor = body.transform.TransformPoint(body.center + Vector3.up * (body.height * .2f));
            Vector3 scale = body.transform.lossyScale;
            surfaceOffset = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) + .06f;
        }
        if (_camera != null)
            anchor += (_camera.transform.position - anchor).normalized * surfaceOffset;
        return anchor;
    }

    private void MergeHealing(ref HealingSlot slot, int restored)
    {
        slot.Total = SaturatingAdd(slot.Total, restored);
        CombatTextMergePresentation merge = new(slot.Total, CombatTextStyleId.Normal,
            CombatTextPriority.MajorAbility, default, false, false, false, false,
            _accessibility.ReducedFlash, slot.Scale,
            _accessibility.ReducedMotion ? 0f : _profile.DirectRePunchScale,
            _profile.RePunchDuration, 0f);
        slot.View.MergeHealing(in merge);
    }

    private void TickHealing(float deltaTime)
    {
        for (int i = 0; i < _healingSlots.Length; i++)
        {
            ref HealingSlot slot = ref _healingSlots[i];
            if (slot.View == null) continue;
            bool missing = slot.Target == null || !slot.Target.gameObject.activeInHierarchy;
            if (!missing && slot.View.IsAnchored)
                slot.View.SetAnchorPosition(slot.Target.position + slot.AnchorOffset, false);
            if (missing || slot.View.Tick(deltaTime, _camera))
            {
                _healingPool.Release(slot.View);
                slot = default;
            }
        }
    }

    public void StopHealing()
    {
        _healingPool?.ReleaseAll();
        for (int i = 0; i < _healingSlots.Length; i++) _healingSlots[i] = default;
    }
}
