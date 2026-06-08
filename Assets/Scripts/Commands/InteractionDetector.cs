using UnityEngine;

/// <summary>
/// Finds the nearest <see cref="IInteractable"/> within <see cref="interactRadius"/>. Lives on a
/// Player/echo; <see cref="Player.Use"/> calls <see cref="GetClosest"/>().Interact(), so a replaying
/// clone interacts with whatever its replayed position puts it next to — for free.
/// </summary>
public class InteractionDetector : MonoBehaviour
{
    [SerializeField] private float interactRadius = 1.5f;
    [Tooltip("Layers searched for interactables. Default Everything works (the IInteractable lookup " +
             "filters anyway); narrow it to an 'Interactable' layer for efficiency.")]
    [SerializeField] private LayerMask interactableLayer = ~0;

    private readonly Collider2D[] _hits = new Collider2D[8];

    public IInteractable GetClosest()
    {
        var filter = new ContactFilter2D { useLayerMask = true, layerMask = interactableLayer };
        int count = Physics2D.OverlapCircle(transform.position, interactRadius, filter, _hits);

        IInteractable closest = null;
        float closestDist = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var interactable = _hits[i].GetComponentInParent<IInteractable>();
            if (interactable == null) continue;
            float dist = Vector2.Distance(transform.position, _hits[i].transform.position);
            if (dist < closestDist) { closestDist = dist; closest = interactable; }
        }
        return closest;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactRadius);
    }
}
