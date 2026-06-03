using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class FinishFlag : MonoBehaviour
{
    private void Reset() => GetComponent<Collider2D>().isTrigger = true;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other.GetComponentInParent<Player>() == null) return;

        LevelLoader.LoadNext();
    }
}
