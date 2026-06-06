using UnityEngine;

public class Entity : MonoBehaviour
{
    public virtual void Kill()
    {
        if (TryGetComponent<RewindableEntity>(out var rewindable))
            // Rewindable entities are despawned (deactivated, but not destroyed) to allow
            // them to be brought back on rewind.
            rewindable.Despawn();
        else
            gameObject.SetActive(false);
    }
}
