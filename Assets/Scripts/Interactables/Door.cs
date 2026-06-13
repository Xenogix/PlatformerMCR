using UnityEngine;

public class Door : MonoBehaviour
{
    [Tooltip("Open = invisible and non-blocking. Applied at startup, so ticking this makes the door open by default.")]
    [SerializeField] private bool isOpen;

    private Renderer[] _renderers;
    private Collider2D[] _colliders;

    public bool IsOpen => isOpen;

    private void Awake()
    {
        // "Open" hides the renderers and disables the colliders instead of deactivating the GameObject.
        // The object (and its DoorChannel / RewindableEntity) must stay active to keep registering and
        // capturing: deactivating it collided with RewindableEntity's OWN SetActive-based dormancy
        // (Capture() early-returns while dormant), which desynced and froze the door's recorded state —
        // the source of the impossible rewind states. Renderer/collider are owned solely by this script.
        _renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        _colliders = GetComponentsInChildren<Collider2D>(includeInactive: true);
        ApplyOpen(); // honour the serialized default at load, before the first rewind capture
    }

    public void SetOpen(bool open)
    {
        isOpen = open;
        ApplyOpen();
    }

    private void ApplyOpen()
    {
        for (int i = 0; i < _renderers.Length; i++) _renderers[i].enabled = !isOpen;
        for (int i = 0; i < _colliders.Length; i++) _colliders[i].enabled = !isOpen;
    }
}
