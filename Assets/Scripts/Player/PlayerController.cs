using System;
using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(Collider2D))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float acceleration = 50f;
    [SerializeField] private float deceleration = 35f;
    [SerializeField] private float jumpForce = 10f;
    [SerializeField] private float jumpBufferTime = 0.1f;
    [SerializeField] private float fallGravityMultiplier = 3.5f;
    [SerializeField] private float lowJumpGravityMultiplier = 2.5f;
    [SerializeField] private LayerMask groundLayers = ~0;
    [SerializeField] private float groundCheckDistance = 0.05f;

    private Rigidbody2D rb;
    private Collider2D col;

    private Vector2 direction;
    private Vector2 horizontalVelocity;
    private float verticalVelocity;
    private bool wasGrounded;
    private bool jumpRequested;
    private bool jumpHeld;
    private float lastJumpRequestTime;

    public event Action OnJumped;
    public event Action OnLanded;

    public Vector2 Direction => direction;
    public bool IsOnGround => CheckGrounded();
    public bool IsJumping => !IsOnGround && verticalVelocity > 0f;
    public Vector2 Velocity => horizontalVelocity + Vector2.up * verticalVelocity;
    public float MoveSpeed => moveSpeed;

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        col = GetComponent<Collider2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
    }

    private void Update()
    {
        ApplyMovement();
        ApplyJump();
        ApplyGravity();
        CheckLanding();

        rb.linearVelocity = horizontalVelocity + Vector2.up * verticalVelocity;
    }

    public void SetDirection(Vector2 newDirection) => direction = newDirection;
    public void RequestJump() { lastJumpRequestTime = Time.time; jumpRequested = true; }
    public void SetJumpHeld(bool held) => jumpHeld = held;

    private bool CheckGrounded()
    {
        if (col == null) return false;
        Bounds b = col.bounds;
        // Cast a thin box just below the collider — slightly inset on X to avoid
        // catching adjacent walls as ground.
        return Physics2D.BoxCast(
            origin: new Vector2(b.center.x, b.min.y),
            size: new Vector2(b.size.x * 0.95f, 0.05f),
            angle: 0f,
            direction: Vector2.down,
            distance: groundCheckDistance,
            layerMask: groundLayers);
    }

    private void ApplyMovement()
    {
        Vector2 targetVelocity = direction * moveSpeed;
        float rate = direction.sqrMagnitude > 0f ? acceleration : deceleration;
        horizontalVelocity = Vector2.MoveTowards(horizontalVelocity, targetVelocity, rate * Time.deltaTime);
    }

    private void ApplyJump()
    {
        if (jumpRequested && Time.time - lastJumpRequestTime > jumpBufferTime)
            jumpRequested = false;

        if (jumpRequested && CheckGrounded())
        {
            verticalVelocity = jumpForce;
            jumpRequested = false;
            OnJumped?.Invoke();
        }
    }

    private void ApplyGravity()
    {
        if (CheckGrounded() && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
            return;
        }

        float multiplier = fallGravityMultiplier;
        if (verticalVelocity > 0f && !jumpHeld)
            multiplier = lowJumpGravityMultiplier;

        verticalVelocity += Physics2D.gravity.y * multiplier * Time.deltaTime;
    }

    private void CheckLanding()
    {
        bool grounded = CheckGrounded();
        if (grounded && !wasGrounded) OnLanded?.Invoke();
        wasGrounded = grounded;
    }
}
