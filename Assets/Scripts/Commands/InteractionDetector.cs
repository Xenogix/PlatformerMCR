using UnityEngine;

public class InteractionDetector : MonoBehaviour
{
    [SerializeField] private float interactRadius = 1.5f;
    [SerializeField] private LayerMask interactableLayer;

    private readonly Collider2D[] _hits = new Collider2D[4];

    public IInteractable GetClosest()
    {
int count = Physics2D.OverlapCircle(transform.position, interactRadius, new ContactFilter2D { useLayerMask = true, layerMask = interactableLayer }, _hits);
         Debug.Log($"OverlapCircle hit count: {count} at position {transform.position} radius {interactRadius}");
        IInteractable closest = null;
        float closestDist = float.MaxValue;

for (int i = 0; i < count; i++)
{
    var interactable = _hits[i].GetComponentInParent<IInteractable>();
    if (interactable == null) continue;
    float dist = Vector2.Distance(transform.position, _hits[i].transform.position);
    if (dist < closestDist)
    {
        closestDist = dist;
        closest = interactable;
    }
}

        return closest;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, interactRadius);
    }
}