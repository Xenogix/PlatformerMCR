using UnityEngine;

// Collider2D may live on this GameObject or on a child (e.g., a rotation-canceling
// child added by the physics migration). When the collider is on a child, a
// Kinematic Rigidbody2D on this GameObject is required for triggers to bubble up.
public class Hazard : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.TryGetComponent<Entity>(out var entity))
            entity.Kill();
    }
}
