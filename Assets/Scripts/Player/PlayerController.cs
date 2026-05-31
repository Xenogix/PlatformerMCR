using System;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float acceleration = 50f;
    [SerializeField] private float deceleration = 35f;
    [SerializeField] private float jumpForce = 10f;
    [SerializeField] private float jumpBufferTime = 0.1f;
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
    private float lastJumpRequestTime;

    // Events
    public event Action OnJumped;
    public event Action OnLanded;

    // Public properties
    public Vector3 Direction => direction;
    public bool IsOnGround => cc.isGrounded;
    public bool IsJumping => !cc.isGrounded && verticalVelocity > 0f;
    public Vector3 Velocity => horizontalVelocity + Vector3.up * verticalVelocity;
    public float MoveSpeed => moveSpeed;

    private void Start()
    {
        cc = GetComponent<CharacterController>();
    }

    private void Update()
    {
        ApplyMovement();
        ApplyJump();
        ApplyGravity();
        CheckLanding();

        // Apply the final movement to the character controller
        cc.Move((horizontalVelocity + Vector3.up * verticalVelocity) * Time.deltaTime);
    }

    // Public methods for input
    public void SetDirection(Vector3 newDirection) => direction = newDirection;
    public void RequestJump() { lastJumpRequestTime = Time.time; jumpRequested = true; }
    public void SetJumpHeld(bool held) => jumpHeld = held;

    private void ApplyMovement()
    {
        Vector3 targetVelocity = direction * moveSpeed;
        float rate = direction.sqrMagnitude > 0 ? acceleration : deceleration;
        horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, rate * Time.deltaTime);
    }

    private void ApplyJump()
    {
        if (jumpRequested && Time.time - lastJumpRequestTime > jumpBufferTime)
            jumpRequested = false;

        if (jumpRequested && cc.isGrounded)
        {
            verticalVelocity = jumpForce;
            jumpRequested = false;
            OnJumped?.Invoke();
        }
    }

    private void ApplyGravity()
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

        verticalVelocity += Physics.gravity.y * multiplier * Time.deltaTime;
    }

    private void CheckLanding()
    {
        bool grounded = cc.isGrounded;
        if (grounded && !wasGrounded) OnLanded?.Invoke();
        wasGrounded = grounded;
    }
}