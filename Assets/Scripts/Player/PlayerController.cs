using System;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float acceleration = 50f;
    [SerializeField] private float deceleration = 35f;
    [SerializeField] private float jumpForce = 10f;
    [SerializeField] private int jumpBufferTicks = 5;
    [SerializeField] private float fallGravityMultiplier = 3.5f;
    [SerializeField] private float lowJumpGravityMultiplier = 2.5f;

    // Component references
    private CharacterController cc;

    // Private state
    private Vector3 direction;
    private Vector3 horizontalVelocity;
    private float verticalVelocity;
    private bool wasGrounded;
    private bool jumpRequested;
    private bool jumpHeld;
    private int jumpRequestedTick;
    private int currentTick;

    // Events
    public event Action OnJumped;
    public event Action OnLanded;

    // Public properties
    public Vector3 Direction => direction;
    public bool IsOnGround => cc.isGrounded;
    public bool IsJumping => !cc.isGrounded && verticalVelocity > 0f;
    public Vector3 Velocity => horizontalVelocity + Vector3.up * verticalVelocity;
    public float MoveSpeed => moveSpeed;

    private void Awake()
    {
        cc = GetComponent<CharacterController>();
    }

    /// <summary>
    /// Advances the controller by one fixed step. Driven by the player's invoker (or a
    /// clone's playback) AFTER this tick's input commands have set direction/jump, so
    /// the same tick index always produces the same movement — that determinism is what
    /// makes clone replay land exactly where past-you did. Replaces the old Update().
    /// </summary>
    public void Tick(int tick, float dt)
    {
        currentTick = tick;

        ApplyMovement(dt);
        ApplyJump();
        ApplyGravity(dt);
        CheckLanding();

        // Apply the final movement to the character controller
        cc.Move((horizontalVelocity + Vector3.up * verticalVelocity) * dt);
    }

    // Public methods for input (called by commands via the Player façade)
    public void SetDirection(Vector3 newDirection) => direction = newDirection;
    public void RequestJump() { jumpRequestedTick = currentTick; jumpRequested = true; }
    public void SetJumpHeld(bool held) => jumpHeld = held;

    private void ApplyMovement(float dt)
    {
        Vector3 targetVelocity = direction * moveSpeed;
        float rate = direction.sqrMagnitude > 0 ? acceleration : deceleration;
        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, rate * dt);
    }

    private void ApplyJump()
    {
        // Jump buffering: a request stays valid for a few ticks, so a press just
        // before landing still triggers a jump on touchdown. Measured in ticks (not
        // Time.time) so it reproduces identically on a replaying clone.
        if (jumpRequested && currentTick - jumpRequestedTick > jumpBufferTicks)
            jumpRequested = false;

        if (jumpRequested && cc.isGrounded)
        {
            verticalVelocity = jumpForce;
            jumpRequested = false;
            OnJumped?.Invoke();
        }
    }

    private void ApplyGravity(float dt)
    {
        if (cc.isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
            return;
        }

        // Apply variable gravity multipliers based on whether the player is ascending, descending, or has released the jump button
        // This allows for variable jump heights and quicker falls
        float multiplier = fallGravityMultiplier;
        if (verticalVelocity > 0f && !jumpHeld)
            multiplier = lowJumpGravityMultiplier;

        verticalVelocity += Physics.gravity.y * multiplier * dt;
    }

    private void CheckLanding()
    {
        bool grounded = cc.isGrounded;
        if (grounded && !wasGrounded) OnLanded?.Invoke();
        wasGrounded = grounded;
    }
}
