using UnityEngine;

// Collider2D may live on this GameObject or on a child. When the collider is on
// a child, a Kinematic Rigidbody2D on this GameObject is required for triggers
// to bubble up.
public class FinishFlag : MonoBehaviour
{
    private bool hasTriggered = false;
    private void Reset()
    {
        var col = GetComponentInChildren<Collider2D>();
        if (col != null) col.isTrigger = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (hasTriggered) return;

        if (other.GetComponentInParent<Player>() == null) return;
        hasTriggered = true;

        // Route through the level transition (static "channel change" outro) if present;
        // otherwise fall back to loading the next level directly.
        var transition = FindAnyObjectByType<LevelTransition>();
        if (transition != null)
            transition.PlayOutroThenLoad(LevelLoader.LoadNext);
        else
            LevelLoader.LoadNext();
    }
}
