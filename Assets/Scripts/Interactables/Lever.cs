using UnityEngine;

public class Lever : MonoBehaviour, IInteractable
{
    [SerializeField] private Door door;

    private bool isOn = false;

    public void Interact()
    {
        print("Lever toggled");
        isOn = !isOn;
        door.SetOpen(isOn);
    }
}