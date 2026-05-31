using UnityEngine;

[RequireComponent(typeof(Collider))]
public class FinishFlag : MonoBehaviour
{
    private void Reset() => GetComponent<Collider>().isTrigger = true;

    private void OnTriggerEnter(Collider other)
    {
        if (other.GetComponentInParent<Player>() == null) return;

        LevelLoader.LoadNext();
    }
}
