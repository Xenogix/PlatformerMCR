using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Routes the rewind input and orchestrates echoes — the production replacement for
/// bene's throwaway CloneTestDriver. On the rewind key it asks the Caretaker to jump the
/// world back (Memento), then splits the live player's recorded command stream at the
/// target tick T: the frozen [T, now] slice drives a freshly spawned echo (Command
/// replay), while the live player re-records forward from T.
///
/// The echo is a copy of the just-restored player, seeded at the player's exact state@T,
/// and made weightless (low mass) so the player can shove it around; collisions with the
/// player/other echoes it spawned inside are ignored until they separate, to avoid the
/// physics pop. The echo carries a RewindableEntity, so a later rewind snaps it back.
/// </summary>
public sealed class RewindDirector : MonoBehaviour
{
    [Tooltip("Key that triggers the instant jump-back and spawns an echo.")]
    [SerializeField] private Key rewindKey = Key.R;

    [Tooltip("Echo mass as a fraction of the player's, so the player shoves echoes around weightlessly.")]
    [SerializeField, Range(0.01f, 1f)] private float echoMassFactor = 0.2f;

    [Tooltip("Echo prefab — a Player variant carrying ClonePlayback + RigidbodyChannel + RewindableEntity and NO PlayerCommandInvoker (translucent). If unset, falls back to cloning the live player.")]
    [SerializeField] private GameObject echoPrefab;

    private PlayerCommandInvoker livePlayer;

    private void Start() => livePlayer = FindAnyObjectByType<PlayerCommandInvoker>();

    private void Update()
    {
        var kb = Keyboard.current;
        if (kb != null && kb[rewindKey].wasPressedThisFrame)
            Rewind();
    }

    private void Rewind()
    {
        // Re-resolve in case the player was respawned or the scene reloaded.
        if (livePlayer == null) livePlayer = FindAnyObjectByType<PlayerCommandInvoker>();
        var caretaker = RewindCaretaker.Instance;
        if (caretaker == null || livePlayer == null) return;

        int target = caretaker.Rewind();
        if (target < 0) return; // nothing captured yet

        // Command-side split, addressed by ABSOLUTE tick (mirror of the caretaker's state
        // DiscardAfter, opposite retention): the echo keeps a frozen [target, now] copy; the
        // live player keeps [.., target-1] and re-records forward from target.
        CommandTimeline echoScript = livePlayer.Timeline.SliceFromTick(target);
        livePlayer.Timeline.TruncateAfterTick(target - 1);

        SpawnEcho(echoScript, target);
    }

    private void SpawnEcho(CommandTimeline script, int spawnTick)
    {
        GameObject src = livePlayer.gameObject;
        var srcRb = src.GetComponent<Rigidbody2D>();

        // Seed from the Rigidbody2D, NOT the Transform: right after a rewind the rigidbody
        // holds the restored state@target, but the transform doesn't sync until the next
        // physics step.
        Vector2 seedPos = srcRb != null ? srcRb.position : (Vector2)src.transform.position;
        float seedRot = srcRb != null ? srcRb.rotation : src.transform.eulerAngles.z;

        var pos = new Vector3(seedPos.x, seedPos.y, src.transform.position.z);
        var rot = Quaternion.Euler(0f, 0f, seedRot);
        GameObject echo = echoPrefab != null ? Instantiate(echoPrefab, pos, rot) : Instantiate(src, pos, rot);
        echo.name = "Echo";

        // An echo must not read live input. The dedicated prefab has no invoker; if we fell
        // back to cloning the live player, strip it here.
        var invoker = echo.GetComponent<PlayerCommandInvoker>();
        if (invoker != null) Destroy(invoker);

        var echoRb = echo.GetComponent<Rigidbody2D>();
        if (srcRb != null && echoRb != null)
        {
            echoRb.position = seedPos;
            echoRb.rotation = seedRot;
            echoRb.linearVelocity = srcRb.linearVelocity;
            echoRb.angularVelocity = srcRb.angularVelocity;
            echoRb.mass = srcRb.mass * echoMassFactor; // weightless-ish: the player shoves it around
        }

        var playback = echo.GetComponent<ClonePlayback>();
        if (playback == null) playback = echo.AddComponent<ClonePlayback>();
        playback.Play(script);

        // Register + capture NOW at spawnTick (a capture-cadence tick) so the echo has an
        // alive record from the moment it exists — otherwise an immediate second rewind to
        // spawnTick would find no record and deactivate the fresh echo.
        var echoEntity = echo.GetComponent<RewindableEntity>();
        if (echoEntity != null)
        {
            RewindCaretaker.Instance.Register(echoEntity);
            echoEntity.Capture(spawnTick);
        }

        SetupSpawnOverlapIgnore(echo);
    }

    // Don't let the spawn-overlap with the player (and any echo it spawned inside) pop them
    // apart: ignore those collisions until they separate.
    private void SetupSpawnOverlapIgnore(GameObject echo)
    {
        var echoCol = echo.GetComponent<Collider2D>();
        if (echoCol == null) return;

        var peers = new List<Collider2D>();
        var playerCol = livePlayer.GetComponent<Collider2D>();
        if (playerCol != null) peers.Add(playerCol);
        foreach (var other in FindObjectsByType<ClonePlayback>(FindObjectsSortMode.None))
        {
            if (other.gameObject == echo) continue;
            var c = other.GetComponent<Collider2D>();
            if (c != null) peers.Add(c);
        }

        echo.AddComponent<IgnoreCollisionUntilClear>().IgnoreWhileOverlapping(peers);
    }
}
