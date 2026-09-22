using UnityEngine;

/// <summary>
/// Desfasa el clip al activarse (spawn o salida del pool). Un Play, sin Update.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class EnemyAnimPhaseOffset : MonoBehaviour
{
    private static readonly int StateHash = Animator.StringToHash("Hop");

    private void OnEnable()
    {
        Animator animator = GetComponent<Animator>();
        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        animator.Play(StateHash, 0, Random.value);
    }
}
