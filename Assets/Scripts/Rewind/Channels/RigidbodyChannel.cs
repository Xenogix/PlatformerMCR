using UnityEngine;

// Dense channel for a physics-driven body: position, rotation, and BOTH velocities.
// Velocity lives on the Rigidbody2D, not the Transform — capturing only the
// transform would teleport a falling body with zero velocity on rewind.
public struct RigidbodyState
{
    public Vector2 Position;
    public float Rotation;
    public Vector2 LinearVelocity;
    public float AngularVelocity;
}

[RequireComponent(typeof(Rigidbody2D))]
public sealed class RigidbodyChannel : RewindChannel<RigidbodyState>
{
    private Rigidbody2D _rb;

    protected override void Awake()
    {
        base.Awake();                       // creates the history
        _rb = GetComponent<Rigidbody2D>();
    }

    protected override IHistory<RigidbodyState> NewHistory() => new DenseHistory<RigidbodyState>();

    protected override RigidbodyState Read() => new RigidbodyState
    {
        Position = _rb.position,
        Rotation = _rb.rotation,
        LinearVelocity = _rb.linearVelocity,
        AngularVelocity = _rb.angularVelocity,
    };

    protected override void Write(RigidbodyState s)
    {
        _rb.position = s.Position;
        _rb.rotation = s.Rotation;
        _rb.linearVelocity = s.LinearVelocity;
        _rb.angularVelocity = s.AngularVelocity;

        // Also push the pose onto the Transform. Rigidbody2D.position only reaches the Transform
        // on the next physics step, but while the timeline is open the clock is paused
        // (timeScale 0) so no step runs — without this, scrubbing would be invisible. Harmless
        // during normal play: the next physics step reconciles the same value.
        transform.position = new Vector3(s.Position.x, s.Position.y, transform.position.z);
        transform.rotation = Quaternion.Euler(0f, 0f, s.Rotation);
    }
}
