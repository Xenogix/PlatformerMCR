using UnityEngine;

public class Door : MonoBehaviour
{
    [SerializeField] private Renderer doorRenderer;
    [SerializeField] private Collider2D doorCollider;
    [SerializeField] private Material closedMaterial;
    [SerializeField] private Material openMaterial;
    [SerializeField] private bool isOpen;

    private void Start()
    {
        UpdateComponents(isOpen);
    }

    private void OnValidate()
    {
        UpdateComponents(isOpen);
    }

    public bool IsOpen() => isOpen;

    public void SetOpen(bool open)
    {
        isOpen = open;
        UpdateComponents(open);
    }

    private void UpdateComponents(bool open)
    {
        if (doorCollider != null)
            doorCollider.enabled = !open;

        if (doorRenderer != null)
            doorRenderer.material = open ? openMaterial : closedMaterial;
    }
}