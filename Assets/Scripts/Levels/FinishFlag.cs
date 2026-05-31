using UnityEngine;

[RequireComponent(typeof(Collider))]
public class FinishFlag : MonoBehaviour
{
    [SerializeField] private Level level;

    private void Reset() => GetComponent<Collider>().isTrigger = true;

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<Player>() == null) return;

        if (level != null) level.Complete();
        else Debug.LogWarning("FinishFlag: no Level assigned.", this);
    }
}
