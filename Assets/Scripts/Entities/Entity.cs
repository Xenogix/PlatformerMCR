using UnityEngine;

public class Entity : MonoBehaviour
{
    public virtual void Kill()
    {
        gameObject.SetActive(false);
    }
}
