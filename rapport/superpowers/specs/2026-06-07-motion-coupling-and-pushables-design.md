# Design: Motion Coupling, Coded Resistance & Pushable Obstacles

**Date:** 2026-06-07
**Branch:** `rewind-system`
**Status:** Draft for review

## 1. Context & Goals

The rewind/replay mechanic is now wired into the real level scenes (a `RewindSystems`
prefab — `RewindCaretaker` + `RewindDirector` — nested in the shared `Level` prefab; that
work is done and verified, and is **not** part of this spec).

This spec covers the next two pieces, designed together because they share one
architectural decision:

- **Carry / rider coupling** — a player or echo standing on (or pushed by) something
  inherits its motion: ride a moving carrier, jump in sync for a stacked "super-jump",
  shove an idle echo.
- **Cooperative pushable obstacles** — a heavy object that takes *N* deliberate pushers to
  budge, glides once moving, and can also be shoved by raw momentum (a fast bonk).

It also folds in two things that turned out to be the *same lever* as the above:

- **Frictionless body + coded resistance** — replaces Unity's indiscriminate
  `PhysicsMaterial2D` friction (which causes the wall-jump height bug) with targeted,
  code-driven resistance.
- **Removing the two echo hacks** — the spawn-time `mass × 0.2` and the prefab
  `gravityScale 2` (vs the player's `4`). Coupling provides the shove and the fair
  super-jump that those hacks were faking.

### Non-goals

- No new rewind channel. Everything here is re-derived from contacts each tick and is
  already captured by the existing `RigidbodyChannel`.
- No rewrite toward a force-based player controller. The controller stays velocity-driven;
  we change *how* it composes velocity, not its nature.
- The lever/`Use` + `StateChannel` work, animation derivation, and a `ControllerStateChannel`
  remain deferred (tracked separately).

## 2. Current state (the constraints that shape the design)

- **Tick spine.** `GameClock` advances one fixed tick per `FixedUpdate`, running *movers*
  (`PlayerCommandInvoker`, `ClonePlayback` → drive controllers) then *observers*
  (`RewindCaretaker` → capture post-move state). Nothing rewind-related uses Unity's
  `FixedUpdate` directly.
- **Velocity-driven controller.** `PlayerController.Tick()` *overwrites* velocity on the
  ground every tick: `rb.linearVelocity = tangent * newSpeed`. Airborne it sets X
  (`MoveTowards` toward target) and preserves Y. Jump sets Y absolutely.
- **This overwrite is the crux.** A rider on a moving carrier won't inherit its motion —
  the overwrite zeroes it. So carry/push can't be bolted on as post-hoc forces; they have
  to live *inside* the velocity computation.
- **Rewind is exact on jump-back, drifty forward.** `RigidbodyChannel` densely captures
  pos/rot/lin+ang velocity. Echoes replay their own recorded command stream by absolute
  tick; forward physics is allowed to diverge — that divergence is the point.
- **Hacks today.** `RewindDirector.SpawnEcho` sets `echoRb.mass = srcRb.mass * echoMassFactor`
  (0.2); `PlayerEcho.prefab` ships `gravityScale = 2` while `Player.prefab` is `4`. The
  player collider uses a friction-1 material, which drags the upward velocity off a jump
  taken against a wall (the known wall-jump bug).

## 3. Core principles (invariants every part must honor)

1. **Frictionless native, resistance in code.** The player and echo use a friction-0
   material. All "friction-like" deceleration is applied in code, *only along the ground
   tangent / to horizontal speed* — never to a vertical wall contact. This is what fixes
   the wall-jump bug: the resistance simply isn't applied where a jump lives.

2. **One coupling rule, classified by contact normal.** Everything is
   `velocity = V_base + own_intent`, where `V_base` is the velocity inherited from the
   contacts you're coupled to:
   - **Support contact** (normal ≈ up, beneath you) → inherit the carrier's **full**
     velocity (you ride it in every direction it moves).
   - **Side contact** (normal ≈ horizontal, something pressing into you) → inherit only the
     **into-you (normal) component** of its velocity.

   The "2× vs match-pusher" behavior is *not* two mechanisms — it's just whether the body
   adds its own intent: a rider who walks gets `carrier + walk` (2×); an idle ghost gets
   `pusher + 0` (matches). Head-on falls out: opposite side-contacts contribute `+x` and
   `−x`, summing to ~0 in `V_base`.

3. **Hybrid: coupling for controllers, physics for everything else.** The coupling layer
   only governs grounded *controller↔controller* (and controller↔moving-surface) cases —
   exactly where the overwrite would otherwise kill the interaction. Airborne collisions
   and free-body objects (pushable crates) are left to raw physics, preserving emergent
   momentum and bonks.

4. **Rewind-clean by re-derivation.** Coupling and obstacle state are recomputed from
   current contacts + velocities every tick — never stored. The *result* (a velocity) is
   captured by `RigidbodyChannel`; on jump-back everyone snaps to an exact pose and the
   next tick re-derives coupling from the restored state. No new channel. Echo command
   streams are never touched; being carried/pushed is emergent perturbation, just like a
   collision is today.

5. **Order-independent `V_base` via a start-of-tick snapshot.** `V_base` reads each contact
   body's velocity from a snapshot taken *before* any controller runs this tick, so the
   result doesn't depend on mover registration order (important for carry chains). The
   snapshot is transient (rebuilt every tick from current velocities) → inherently
   rewind-clean. Cost: a 1-tick (0.02 s) coupling latency, imperceptible.

## 4. Components & changes

### 4.1 `MovementSettings` (new ScriptableObject)

Single source of truth for tuning, shared by `PlayerController` (player **and** echo) and
`PushableObstacle`. Fixes the current problem that settings aren't shared across scenes and
lets the echo be physically identical to the player.

| Field | Purpose |
|---|---|
| `moveSpeed`, `acceleration`, `deceleration` | locomotion (the walking envelope) |
| `groundResistance` | gentle bleed rate applied to speed *above* `moveSpeed` (the slidey-ness) |
| `jumpForce`, `jumpBufferTime` | jump |
| `baseGravityScale`, `ascentGravityMultiplier`, `lowJumpGravityMultiplier`, `fallGravityMultiplier` | variable-gravity jump arc |
| `maxSlopeAngle` | slope/wall classification (feel) |
| `pushForce` | force a single deliberate pusher exerts on a pushable; also the obstacle's calibration unit |

Asset lives at `Assets/Settings/MovementSettings.asset`. Ground-check **geometry**
(`groundCheckSize`, `groundCheckDistance`, `groundLayer`) stays serialized on the controller
/ prefab, since it depends on the collider shape, not feel.

### 4.2 `PlayerController` — frame-relative velocity + coded resistance

Reads all tuning from a serialized `MovementSettings` reference. Sets `rb.gravityScale`
from the SO in `Awake` (so the echo, sharing the SO, matches — killing the gravity hack).

The **rewind-critical timing logic is preserved unchanged**: tick-stamp jump buffering,
post-jump ground-suppress, the backward-tick reset, the slope-tangent BoxCast, and variable
gravity all stay exactly as they are. Only the velocity *composition* changes.

**Grounded branch** (replaces the overwrite):

```
V_base   = Coupling.ResolveBase(this)         // §4.3; 0 on static ground → identical to today
tangent  = (groundNormal.y, -groundNormal.x)
v_rel    = rb.linearVelocity - V_base
relAlong = dot(v_rel, tangent)                // my tangential speed relative to the surface
target   = input.x * moveSpeed

if |relAlong| <= moveSpeed:                   // control zone — crisp, as today
    rate    = (|input.x| > 0) ? acceleration : deceleration
    newRel  = MoveTowards(relAlong, target, rate * dt)
else:                                         // excess zone — gentle bleed, preserves momentum
    newRel  = MoveTowards(relAlong, sign(relAlong) * moveSpeed, groundResistance * dt)

rb.linearVelocity = V_base + tangent * newRel  // ADD back; drop the relative perpendicular
                                               // component → pins to the (moving) surface
```

**Airborne branch:** `V_base` from side contacts only (no support); adjust X relative to
it, **preserve Y** (no clamp) so mid-air bonks/momentum survive. Once a body separates from
its carrier there's no support contact, so `V_base.y = 0` and it's a pure projectile — no
double-gravity.

**Jump (additive impulse):**

```
rb.linearVelocity.y += jumpForce
```

Additive, not an overwrite. In the normal case the body is at rest vertically when a
buffered jump fires (`velocity.y ≈ 0`), so this equals today's `= jumpForce`; additivity is
what lets a carrier's jump and the rider's own jump *stack*.

Two distinct vertical effects, handled by two different mechanisms:
- **Continuous vertical carry** (riding an upward-moving platform/body) → via `V_base.y`
  (the snapshot, §4.3). The grounded branch already yields `velocity.y = V_base.y`, so the
  rider tracks a moving surface.
- **Discrete super-jump** (carrier and rider jump the *same* tick) → a same-tick discrete
  event **cannot** travel through the start-of-tick snapshot (it would be one tick stale,
  and the bodies may have already separated). It is delivered by a **post-movers coupling
  pass** (§4.3, §5): after every controller has decided its jump for the tick, each jumping
  carrier adds `jumpForce` to the bodies currently riding it. Because both that propagated
  impulse and the rider's own jump are *additive*, order is irrelevant → reliable 2×, no
  double-counting. (For an echo, "jumps the same tick" = past-you jumped there, replayed.)
  Carrier jumps while the rider is idle → the pass still lifts the rider with it (1×).

`OnJumped`/`OnLanded` stay for animation/SFX only.

### 4.3 Contact coupling

A small resolver computes `V_base` for a controller from its current contacts, reading
velocities from the start-of-tick snapshot (§3.5).

- **Support velocity.** The grounded BoxCast already identifies the support collider; read
  its `attachedRigidbody`'s snapshot velocity (full vector). Static world → no rigidbody /
  zero velocity → `V_base = 0`.
- **Side pushes.** Enumerate the body's contacts (`Rigidbody2D.GetContacts`). For each
  contact whose normal is ≈ horizontal and whose other body is moving *into* us, add the
  inward (normal-component) part of that body's snapshot velocity.
- **Compose.** `V_base = supportVelocity + Σ side-push contributions`. Opposing side pushes
  cancel; a carrier + a sideways shove combine. (Edge cases settle naturally from the sum.)

**Snapshot mechanism.** `GameClock` gains a third group ticked *first* each `FixedUpdate`
(`RegisterPre`, mirroring `RegisterPost`). A tiny `CouplingFrame` registers there and
records `Rigidbody2D → velocity` for every body with a `RigidbodyChannel` (players, echoes,
obstacles). Controllers query `CouplingFrame.VelocityOf(rb)` during their mover tick.
Transient, rebuilt every tick → no rewind state.

**Post-movers coupling pass.** `GameClock` also gains a pass that runs *after* all movers
but *before* the observers (capture). It owns the cross-body effects that require every
controller's tick-decision to be final first: (a) **discrete jump propagation** — each
carrier that jumped this tick adds `jumpForce` to its current riders (§4.2); (b)
**`PushableObstacle` resolution** — each obstacle resolves its static/kinetic regime after
all pushers have applied their force this tick (§4.4). Running here makes both
order-independent and guarantees the result is captured by the observer pass that follows
(capture happens before Unity integrates physics). A rider finds its carrier from the
support rigidbody it cached during its own mover tick.

### 4.4 `PushableObstacle` (new)

A free-body `Rigidbody2D` (frictionless material, normal gravity — it rests on the ground
and is pushed horizontally) + a `RigidbodyChannel` so it rewinds like everything else.

Fields: `mass`, `requiredPushers` (int), `pushResistance` (kinetic glide drag), and a
`MovementSettings` reference for `pushForce`.

**Calibration (auto, in `Awake`):**

```
rb.mass         = mass
staticThreshold = (requiredPushers - 0.5) * settings.pushForce   // sits between N-1 and N
```

`requiredPushers` is a **calibration input, not a runtime counter** — there is no counting.

**Pusher side.** During a controller's tick, if its input points into a contacting
`PushableObstacle`, it calls `obstacle.ApplyPush(dir * settings.pushForce)`. The obstacle
accumulates `netPush` for the tick. (Only deliberate inward pressing pushes — a bystander
resting against it, or one sliding past, contributes nothing.)

**Resolution (in the post-movers pass, §4.3 — after all pushers applied this tick):**

```
if |horizontalSpeed| > ε:                      // moving (from push OR a bonk) → kinetic
    apply pushResistance as a decel opposing motion   // the "glide", not the "wall"
    // netPush keeps driving it; it coasts to rest when pushing stops
else:                                          // at rest → static gate
    if |netPush| >= staticThreshold:  release (let it accelerate)
    else:                             clamp horizontal velocity to 0 (held)
```

Why this hits every requirement:
- **Exactly N deliberate pushers** to break from rest — calibrated, reliable, no counting.
- **Fast bonk shoves it** — a collision impulse injects velocity directly, so the body is
  no longer "at rest", bypasses the static gate, and the kinetic regime glides it; a huge
  impulse sends it flying. Free, no special case.
- **Doesn't crawl once moving** — kinetic `pushResistance` is independent of (and lower
  than) the static threshold, emulating the static→kinetic split that 2D physics lacks.

Rewind-clean: `netPush` re-derived from contacts each tick; velocity captured; regime
re-derived from velocity.

### 4.5 Removing the hacks + frictionless material

- `RewindDirector.SpawnEcho`: drop `echoRb.mass = srcRb.mass * echoMassFactor`; remove the
  `echoMassFactor` field. The echo keeps the player's real mass.
- `PlayerEcho.prefab`: `gravityScale` now comes from the shared SO (= player's), not the
  prefab's `2`. Mass = player's.
- `Player.prefab` + `PlayerEcho.prefab` collider material → `Slippery` (friction 0).
- **Keep** `IgnoreCollisionUntilClear` — it solves spawn-overlap depenetration, not the mass
  hack, and matters more now that the echo is full-mass.

## 5. Tick ordering & data flow

One fixed tick, after the changes:

```
GameClock.FixedUpdate:
  PRE     CouplingFrame snapshots every rewindable body's velocity      (§4.3)
  MOVERS  invoker / clonePlayback → controller.Tick():
            - V_base = Coupling.ResolveBase(snapshot, contacts)         (§4.3)
            - compose grounded/airborne velocity; additive jump impulse (§4.2)
            - cache support rigidbody; if pressing into a
              PushableObstacle → obstacle.ApplyPush                     (§4.4)
  COUPLE  post-movers pass — all tick-decisions now final:             (§4.3)
            - jumping carriers add jumpForce to current riders (super-jump)
            - each PushableObstacle resolves its static/kinetic regime  (§4.4)
  (Unity integrates physics after FixedUpdate — solver handles bonks, depenetration)
  OBSERVERS RewindCaretaker captures post-move state                    (unchanged)
```

On a rewind, `RigidbodyChannel.Restore` snaps every body's pose+velocity; the next tick's
`CouplingFrame` rebuilds from the restored velocities and coupling re-derives — no stored
coupling/obstacle state to get out of sync.

## 6. Determinism & rewind argument

- **No new channel.** Coupling and obstacle regime are pure functions of (contacts,
  snapshot velocities, input) each tick. The only persisted thing is rigidbody state, which
  `RigidbodyChannel` already captures.
- **Echo streams untouched.** Echoes still replay recorded `Move`/`Jump`/`JumpHeld`
  commands. Carry/push/bonk perturb the echo's *pose*, exactly like a collision does today;
  forward drift is expected, jump-back is exact.
- **Worked example (echo on echo).** Echo A replays a jump at tick T while echo B stands on
  it. In the post-movers pass at T, A (a jumping carrier) adds `jumpForce` to B: if B's
  stream is idle, B is lifted with A (1×); if B's stream also jumps at T, B gets A's
  propagated impulse plus its own → super-jump (2×). None of this is recorded for B — it's
  emergent. Rewind past T: both snap to captured poses; replaying forward re-derives the
  same coupling from the same restored state, so it reproduces (modulo the intended forward
  physics drift).
- **Order independence.** `V_base` reads the pre-tick snapshot, so a carry chain
  (A carries B carries C) doesn't depend on which controller ticks first; it just settles
  over a couple of ticks.

## 7. Tunable parameters (feel, dialed in play)

`MovementSettings`: `groundResistance` (slidey-ness), `pushForce`. Per-obstacle:
`requiredPushers`, `pushResistance`, `mass`. Per `RewindDirector`: existing keys/offsets
unchanged.

Open feel questions, deferred to tuning (not architecture): exact `groundResistance` rate;
whether very steep slopes (where gravity pushes you past `moveSpeed`) are allowed to slide
(current model: yes, honestly).

## 8. Testing plan

**Edit-mode unit tests** (pure logic, no scene):
- Velocity composition: `V_base = 0` reproduces today's grounded MoveTowards exactly;
  rider on `+moveSpeed` carrier walking `+moveSpeed` → world `2×`; excess above `moveSpeed`
  bleeds at `groundResistance`; release within envelope stops crisply.
- Additive jump: `V_base.y + jumpForce`; carrier-jump + own-jump ≈ 2× impulse.
- Obstacle calibration: `staticThreshold` lands between (N-1) and N × `pushForce`; at rest,
  `netPush` just under threshold holds, just over releases; injected velocity (bonk) moves
  it regardless of `netPush`.
- Side-push composition: opposing pushers cancel; same-direction don't double the *base*.

**Play-mode / in-editor (via UnityMCP) smokes:**
- Round-trip determinism: record a run with carry + a push, rewind, confirm bodies snap to
  captured poses (no drift on the jump-back).
- Level01 scenario: spawn an echo, stand on it and jump (super-jump), shove an idle echo,
  push a 2-required crate with one echo (held) then two (moves), bonk a crate with a fall
  (moves). Assert no console errors and expected gross outcomes.

**Manual playtest checklist:** wall-jump apex equal to open-jump apex (bug fixed); no
slope-slide on normal slopes; shove/super-jump feel; crate cooperation + glide feel.

## 9. Risks & mitigations

- **Controller rewrite risk.** It's a carefully tuned, rewind-critical class. Mitigation:
  preserve all timing logic verbatim; gate the change behind the `V_base = 0 ⇒ identical`
  property and an edit-mode test asserting it.
- **Side-contact detection quality.** `GetContacts` normals can be noisy on corners.
  Mitigation: reuse the same `maxSlopeAngle` classification as ground detection; start with
  support-only coupling (carry/super-jump) and layer side-push on once carry is solid.
- **Kinetic/static obstacle edge cases** (jitter at the rest threshold). Mitigation: a small
  `ε` dead-band and hysteresis around the rest/move transition.

## 10. Suggested sequencing (one ID per PR)

1. `MovementSettings` SO + migrate `PlayerController` to read it (behavior-preserving) +
   frictionless material + kill the two echo hacks. *(Refactor; fixes wall-jump.)*
2. Coded resistance + frame-relative velocity (`V_base` plumbing + `CouplingFrame`
   pre-phase), support-only coupling (carry + super-jump).
3. Side-push coupling (idle-ghost shove, head-on void, 2×).
4. `PushableObstacle` + a demo crate prefab.
5. Parameter polish pass (jump, speed, resistance, push feel).
```

