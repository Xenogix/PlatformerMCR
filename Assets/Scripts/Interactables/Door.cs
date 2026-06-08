using UnityEngine;

public class Door : MonoBehaviour
{
    public bool IsOpen { get; private set; }

    public void SetOpen(bool open)
    {
        IsOpen = open;
        gameObject.SetActive(!open);
    }
}