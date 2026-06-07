using UnityEngine;

/// <summary>
/// A heavy, cooperative crate: a free-body <see cref="Rigidbody2D"/> pushed deliberately by
/// characters. It takes <see cref="requiredPushers"/> worth of <see cref="pusherForce"/> to break
/// from rest (static threshold = (N − 0.5) × pusherForce), glides under a coded kinetic drag once
/// moving, and a fast bonk (solver-injected velocity) shoves it regardless of pusher count.
///
/// No runtime counting — <see cref="requiredPushers"/> is a CALIBRATION input only. Resolves in the
/// GameClock LATE phase (after every pusher has called <see cref="ApplyPush"/> this tick). Rewind-clean:
/// it stores no extra state — the regime is re-derived each tick from velocity + this tick's push, and
/// pose/velocity is captured by a RigidbodyChannel on the prefab (no new rewind channel).
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public sealed class PushableObstacle : MonoBehaviour, IPushable, ITickable
{
    [SerializeField] private float mass = 5f;
    [Tooltip("Calibration ONLY — how many deliberate pushers it takes to break from rest. Not counted at runtime.")]
    [SerializeField] private int requiredPushers = 2;
    [Tooltip("Force one deliberate pusher exerts. Match the character's pushForce for honest calibration.")]
    [SerializeField] private float pusherForce = 30f;
    [Tooltip("Kinetic drag coefficient once moving. Higher = stops sooner; lower = glides longer/faster.")]
    [SerializeField] private float pushResistance = 4f;

    private Rigidbody2D _rb;
    private Vector2 _netPush;                       // this tick's accumulated deliberate push, consumed in LATE
    private static PhysicsMaterial2D _frictionless; // coded resistance only; no contact friction

    private void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _rb.mass = Mathf.Max(0.01f, mass);          // guard: never 0 (a 0/negative mass NaNs the drive divisor)
        // Start HELD: X frozen so the physics solver can't shove it (a character walking into it just
        // stops, like a wall). Y stays free (gravity → rests on ground). Tick toggles X free/frozen.
        _rb.constraints = RigidbodyConstraints2D.FreezeRotation | RigidbodyConstraints2D.FreezePositionX;
        if (_frictionless == null)
            _frictionless = new PhysicsMaterial2D("PushableFrictionless") { friction = 0f, bounciness = 0f };
        if (TryGetComponent<Collider2D>(out var col)) col.sharedMaterial = _frictionless;
    }

    private void OnEnable() => GameClock.Instance.RegisterLate(this);

    private void OnDisable()
    {
        if (GameClock.HasInstance) GameClock.Instance.UnregisterLate(this);
    }

    /// <summary>Accumulate one pusher's force for this tick (called during the movers phase).</summary>
    public void ApplyPush(Vector2 force) => _netPush += force;

    // Runs in the LATE phase, after every pusher has called ApplyPush this tick.
    public void Tick(int tick, float dt)
    {
        // Recomputed each tick (not cached in Awake) so tuning pusherForce/requiredPushers in the
        // inspector takes effect live.
        float staticThreshold = (Mathf.Max(1, requiredPushers) - 0.5f) * pusherForce;
        float m = Mathf.Max(0.01f, mass);
        float vx = _rb.linearVelocity.x;
        float netX = _netPush.x;
        const float eps = 0.05f;

        // X-free means we're currently engaged (gliding); the constraint itself is the latch.
        bool xFree = (_rb.constraints & RigidbodyConstraints2D.FreezePositionX) == 0;
        // Break from rest only when the deliberate push meets the threshold; stay engaged while still
        // gliding under any push. Below that → held (X frozen, solver can't budge it → N pushers needed).
        bool engaged = Mathf.Abs(netX) >= staticThreshold || (xFree && Mathf.Abs(vx) > eps);

        if (engaged)
        {
            _rb.constraints = RigidbodyConstraints2D.FreezeRotation;   // X free → glide
            vx += (netX / m) * dt;                                     // pusher drive
            vx *= Mathf.Max(0f, 1f - pushResistance * dt);             // kinetic drag (clamped stable)
            _rb.linearVelocity = new Vector2(vx, _rb.linearVelocity.y);
        }
        else
        {
            // Held: freeze X so a character pressing into it (solver) can't move it below threshold.
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
            _rb.constraints = RigidbodyConstraints2D.FreezeRotation | RigidbodyConstraints2D.FreezePositionX;
        }
        _netPush = Vector2.zero;
    }
}
