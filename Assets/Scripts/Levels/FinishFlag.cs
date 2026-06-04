using UnityEngine;

// Collider2D may live on this GameObject or on a child. When the collider is on
// a child, a Kinematic Rigidbody2D on this GameObject is required for triggers
// to bubble up.
public class FinishFlag : MonoBehaviour
{
    private void Reset()
    {
        var col = GetComponentInChildren<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponentInParent<Player>() == null) return;

        LevelLoader.LoadNext();
    }
}
