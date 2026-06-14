# CCTV Try-Timers (RT / TC)

Two on-screen counters per level try, styled as the CCTV overlay the HUD already uses
(channel label, blinking REC, transport icons), plus a final-times splash on the
level-transition snow.

## The two counters

| Label | Meaning | Source | Behavior |
|---|---|---|---|
| `RT 00:01:23.4` | Real time of the try (speedrun / RTA) | `Time.realtimeSinceStartup` − level-start snapshot | Runs always — including timeline pause/scrub (decided: thinking time costs real time) |
| `TC 00:00:58:12` | Tape timecode (game time) | `GameClock.Tick × Time.fixedDeltaTime` | Freezes on pause, winds back on rewind — automatic, since `Tick` itself moves back |

- TC format is authentic CCTV `HH:MM:SS:FF` where `FF` = tick within the second — tick-precise.
- RT format `HH:MM:SS.t` (tenths) for speedrun readability.
- The tension is the point: rewinding saves TC but costs RT — two distinct optimization targets.

## Components

**`TryTimers`** (new, `Assets/Scripts/UI/`) — one job: track and render the counters.
- Two `TMP_Text` refs (RT line, TC line), same font/styling as the channel label.
- `Update()` reads the clocks and formats both lines each frame. No events, no state
  beyond the start snapshot. `GameClock` absent → TC shows `00:00:00:00`.
- Exposes the formatted strings (`RtText` / `TcText`) for the transition splash.
- Lives on `HUD.prefab`, placed as a stacked block under the REC/transport indicator
  (top-right), like a camera's timestamp overlay.

**`LevelTransition`** (existing, extended) — final-times splash on the outro snow.
- Optional `TMP_Text` final-times field, child of the noise object (renders above the
  static; appears/disappears with it — no extra lifecycle code).
- `PlayOutroThenLoad`: fills the text from the scene's `TryTimers` at the moment of
  completion, then holds the snow for a separate, longer `resultHoldDuration`
  (~2 s, serialized) before loading — the 0.2 s intro snap stays untouched.
- No `TryTimers` found or field unset → behaves exactly as today.

## Scope cuts (YAGNI)

- No freeze-at-goal results screen beyond the snow splash; the HUD dies with the scene.
- No skip input for the splash; no best-time persistence/leaderboard.

## Manual test protocol

1. RT and TC advance together during normal play.
2. Open timeline → TC freezes, RT keeps counting.
3. Scrub back → TC visibly winds backward.
4. Commit rewind, replay → TC re-advances from the rewound point; RT never blipped.
5. Finish the level → snow shows both final times for ~2 s, then next level loads.
