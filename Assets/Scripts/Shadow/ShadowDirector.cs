using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Spawns the best-run shadows for the current level: on level load it reads the saved record (if
/// any) and instantiates one shadow body per track, each wired to retrace its path via
/// <see cref="ShadowPlayback"/>. No record → nothing spawns. Place one on each level (or the shared
/// Level prefab) and assign the visual-only <c>PlayerShadow</c> prefab.
/// </summary>
public sealed class ShadowDirector : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Visual-only shadow body to spawn per recorded track. Must carry a ShadowPlayback at " +
             "its root and have NO physics (no Rigidbody2D/Collider2D), so it retraces without drift.")]
    private GameObject shadowPrefab;

    private void Start()
    {
        if (shadowPrefab == null)
        {
            Debug.LogWarning("ShadowDirector: no shadow prefab assigned; no shadows will spawn.");
            return;
        }

        string levelKey = SceneManager.GetActiveScene().name;
        if (!ShadowStore.TryLoad(levelKey, out var record) || record == null) return;

        foreach (ShadowTrack track in record.Tracks)
        {
            if (track.Positions == null || track.Positions.Count == 0) continue;

            GameObject body = Instantiate(shadowPrefab); // keep the prefab's own transform/z-plane
            body.name = "Shadow";
            var playback = body.GetComponent<ShadowPlayback>();
            if (playback != null) playback.Play(track.SpawnTick, track.Positions);
        }
    }
}
