using UnityEngine;
using UnityEngine.Events;

public class Lever : MonoBehaviour, IInteractable
{
    public UnityEvent On;

    public UnityEvent Off;

    private bool isOn = false;

    public void Interact()
    {
        isOn = !isOn;

        if (isOn)
            On.Invoke();
        else
            Off.Invoke();
    }
}