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
        _rb.freezeRotation = true;
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
        const float eps = 0.02f;

        if (Mathf.Abs(vx) < eps && Mathf.Abs(netX) < staticThreshold)
        {
            // Held: not enough deliberate push to break static friction, and no momentum.
            _rb.linearVelocity = new Vector2(0f, _rb.linearVelocity.y);
        }
        else
        {
            // Engaged (≥ threshold) OR already moving (sustained push, or a bonk the solver injected).
            // Pusher force drives; a velocity-proportional drag gives a terminal glide and bleeds a
            // bonk's momentum. Drag is clamped to [0,1) so a large pushResistance·dt stops it cleanly
            // instead of flipping the sign (explicit-Euler blow-up).
            vx += (netX / m) * dt;
            vx *= Mathf.Max(0f, 1f - pushResistance * dt);
            _rb.linearVelocity = new Vector2(vx, _rb.linearVelocity.y);
        }
        _netPush = Vector2.zero;
    }
}
