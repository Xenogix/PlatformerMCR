using UnityEngine;

public class Door : MonoBehaviour
{
    public void SetOpen(bool open)
    {
        gameObject.SetActive(!open);
    }
}