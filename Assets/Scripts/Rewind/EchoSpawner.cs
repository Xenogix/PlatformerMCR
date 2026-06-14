using UnityEngine;

// Spawns an echo (a translucent clone) for the RewindDirector and seeds it from the live player's
// restored state@target. Extracted from RewindDirector so that class stays focused on scrub input +
// orchestration; this owns the instantiate / seed / tint / register-capture mechanics and the echo
// numbering. No rewind math lives here — it just wires a freshly spawned body into the systems.
public sealed class EchoSpawner
{
    private int _seq;

    /// <summary>Instantiate and seed an echo. Returns the spawned GameObject, or null if the prefab
    /// is missing. <paramref name="source"/> is the live player to copy the restored pose/velocity from.</summary>
    public GameObject Spawn(GameObject echoPrefab, GameObject source, CommandTimeline script, int spawnTick, Color color)
    {
        if (echoPrefab == null)
        {
            Debug.LogError("EchoSpawner: echoPrefab is not assigned — cannot spawn an echo.");
            return null;
        }

        var srcRb = source.GetComponent<Rigidbody2D>();

        // Seed from the Rigidbody2D, NOT the Transform: right after a rewind the rigidbody holds
        // the restored state@target, but the transform doesn't sync until the next physics tick.
        Vector2 seedPos = srcRb != null ? srcRb.position : (Vector2)source.transform.position;
        float seedRot = srcRb != null ? srcRb.rotation : source.transform.eulerAngles.z;

        GameObject echo = Object.Instantiate(echoPrefab,
            new Vector3(seedPos.x, seedPos.y, source.transform.position.z),
            Quaternion.Euler(0f, 0f, seedRot));
        echo.name = $"Echo#{++_seq}";

        Tint(echo, color);

        // Seed the echo from the player's restored state@target: position, rotation, and velocity.
        var echoRb = echo.GetComponent<Rigidbody2D>();
        if (srcRb != null && echoRb != null)
        {
            echoRb.position = seedPos;
            echoRb.rotation = seedRot;
            echoRb.linearVelocity = srcRb.linearVelocity;
            echoRb.angularVelocity = srcRb.angularVelocity;
        }

        // The echo spawns deep inside the player (and maybe other echoes). Sync transforms so the
        // seeded pose is visible to physics queries; PlayerController.ResolveCharacterOverlaps then
        // suppresses those deep overlaps each tick (before the solver steps) until they separate.
        Physics2D.SyncTransforms();

        echo.GetComponent<ClonePlayback>().Play(script);

        // Register + capture NOW at spawnTick (a capture-cadence tick) so the echo has an alive record
        // from the moment it exists — otherwise an immediate second rewind to spawnTick would find no
        // record and deactivate the fresh echo.
        var echoEntity = echo.GetComponent<RewindableEntity>();
        if (echoEntity != null && RewindCaretaker.Instance != null)
        {
            RewindCaretaker.Instance.Register(echoEntity);
            echoEntity.Capture(spawnTick);
        }

        return echo;
    }

    // Tint the echo with its clone colour via a MaterialPropertyBlock — a per-renderer override, so NO
    // unique material instance is created (mr.material would, and that instance leaks when the echo is
    // later reclaimed). Keep the shared material's alpha so a translucent echo stays translucent.
    private static void Tint(GameObject echo, Color color)
    {
        var mr = echo.GetComponentInChildren<MeshRenderer>();
        if (mr == null) return;

        Material shared = mr.sharedMaterial;
        bool urp = shared != null && shared.HasProperty("_BaseColor");
        float alpha = shared == null ? 1f : (urp ? shared.GetColor("_BaseColor").a : shared.color.a);

        var mpb = new MaterialPropertyBlock();
        mr.GetPropertyBlock(mpb);
        mpb.SetColor(urp ? "_BaseColor" : "_Color", new Color(color.r, color.g, color.b, alpha));
        mr.SetPropertyBlock(mpb);
    }
}
