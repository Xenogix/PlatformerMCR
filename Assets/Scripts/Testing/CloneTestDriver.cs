using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Throwaway test harness for the Command-pattern recording/replay, so you can press
/// Play and see a clone retrace your steps before the real rewind feature exists.
///
/// Setup (all in the editor):
///   1. Make a CLONE PREFAB: duplicate Player.prefab, remove its PlayerCommandInvoker
///      component and add a ClonePlayback component instead. (Keep its own
///      CharacterController so the live player can stand on the clone.)
///   2. Drop an empty GameObject in the scene, add this component.
///   3. Assign the live Player's PlayerCommandInvoker to "Live Player" (or leave empty
///      to auto-find), and the clone prefab to "Clone Prefab".
///   4. Press Play, move around, then press R: a clone spawns at your start point and
///      replays everything you did.
///
/// Delete this script once the real rewind/spawn task lands — it just calls
/// ClonePlayback.Play(timeline), which is the same hook that feature will use.
/// </summary>
public class CloneTestDriver : MonoBehaviour
{
    [SerializeField] private PlayerCommandInvoker livePlayer;
    [SerializeField] private GameObject clonePrefab;   // a Player copy carrying ClonePlayback
    [SerializeField] private Key spawnCloneKey = Key.R;

    // Where the recording began — the clone must start here to reproduce the path.
    private Vector3 recordStartPosition;
    private Quaternion recordStartRotation;

    private void Start()
    {
        if (livePlayer == null)
            livePlayer = FindFirstObjectByType<PlayerCommandInvoker>();

        if (livePlayer == null)
        {
            Debug.LogError("CloneTestDriver: no PlayerCommandInvoker found in the scene.");
            enabled = false;
            return;
        }

        Transform t = livePlayer.transform;
        recordStartPosition = t.position;
        recordStartRotation = t.rotation;
    }

    private void Update()
    {
        if (Keyboard.current != null && Keyboard.current[spawnCloneKey].wasPressedThisFrame)
            SpawnClone();
    }

    private void SpawnClone()
    {
        if (clonePrefab == null)
        {
            Debug.LogError("CloneTestDriver: assign a clone prefab (a Player with ClonePlayback).");
            return;
        }

        GameObject go = Instantiate(clonePrefab, recordStartPosition, recordStartRotation);

        var playback = go.GetComponent<ClonePlayback>();
        if (playback == null)
        {
            Debug.LogError("CloneTestDriver: clone prefab has no ClonePlayback component.");
            Destroy(go);
            return;
        }

        // Replays the recording captured so far. (The live player keeps recording, so
        // the clone will follow whatever you do up to the moment it catches up — fine
        // for a quick test. The real rewind feature would hand it a frozen snapshot.)
        playback.Play(livePlayer.Timeline);

        Debug.Log($"Spawned clone replaying {livePlayer.Timeline.Count} recorded ticks.");
    }
}
