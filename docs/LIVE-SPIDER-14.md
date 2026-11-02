# LIVE-SPIDER-14 — Hanging Spider & Emergency Lifeline

Idle hanging is opt-in and disabled at launch. The ten-minute inactivity threshold is independent of the ordinary two-minute wander and five-minute web-construction thresholds. **Build a web now** does not satisfy the hanging threshold.

## Descent and suspension

The engine requires the same current web object, identity, and anchor; positive existing construction progress; a spider physically at that anchor; an upper-corner location; and an entire downward corridor clear of the pointer. Short, Medium, and Long depths correspond to 80, 160, and 240 pixels before the spider-size scale is applied. The entire proposed descent must fit the usable display bounds.

Preparation establishes a route and anchor. Actual descent then follows the native motion engine's approved route and bounded elapsed-time movement. A measured endpoint establishes hanging. The silk is a separate small, layered, click-through, nonactivating window joining the actual physical endpoints. There is no full-screen surface or replacement rendering system.

During a peaceful hang the physical spider center remains fixed. Gentle body rotation and small leg motion provide sway; animation does not move the physics position or conceal an unsafe route. Hanging can continue indefinitely until disturbed or canceled. Existing web progress is held throughout suspension and return.

## Return selection and safety

| Method | Conditions and behavior |
| --- | --- |
| `CLIMB_HOME` | A mild nearby pointer or renewed activity selects a gradual return through a currently clear tether corridor. |
| `SNAP_HOME` | A stronger nearby threat selects fast physical travel along the tether if the route is safe. The spider is never teleported. |
| `RELEASE_AND_ESCAPE` | An invalid identity, changed geometry, blocked route, unsafe home, or invalid next step releases the strand. The ordinary pointer-evasion/recovery rules regain control. |

The initial pointer danger radius remains 150 scaled pixels with the existing 12-pixel safety margin. Mild hanging disturbances are considered within 240 scaled pixels. Normal returns must clear the whole remaining corridor. A strong threat already inside the danger radius can permit a verified exit along the silk only when the home is safely clear and the geometry proves that travel continuously increases clearance. Every executed segment is checked again. A pointer between the spider and its home causes release rather than an unsafe retraction.

The core engine owns all physical position changes, route and decision identifiers, measured displacement, threat checks, and actual arrival. The suspension controller supplies intent and verifies effects; the renderer never overrides position.

After positive measured travel reaches the current safe anchor, the renderer may request a small damped vertical flex of the **web image only**. The first actually rendered flex is acknowledged before `WEB_IMPACT_CONFIRMED` is recorded. The physical spider position and anchor do not bounce. The effect settles quickly, followed by a 15-second rest cooldown. Normal construction can resume afterward; it cannot add progress during a tether-release update.

## States and receipts

The suspension state is separate from the underlying navigation state:

`IDLE_AT_WEB → PREPARE_DESCENT → DESCENDING → HANGING → THREAT_ASSESS → CLIMB_HOME / SNAP_HOME / RELEASE_AND_ESCAPE → WEB_IMPACT → SETTLING → REST_COOLDOWN`

Disabled or ineligible behavior may report `DISABLED`. Not every state persists for a whole rendered frame; threat assessment is also recorded as a decision receipt.

The separate bounded suspension ledger retains at most 100 immutable records. It links the measured spider positions, displacement, anchor distance, web identity, route/decision lineage and simulation time to outcomes. It does not inspect application content or add file-reading, clipboard, screenshot, network, input-injection or control-invocation capabilities.

| Receipt | What it establishes |
| --- | --- |
| `DESCENT_AUTHORIZED` | A current descent route passed geometry checks. |
| `SILK_ANCHOR_CONFIRMED` | The route refers to the current measured web anchor and identity. |
| `DESCENT_CONFIRMED` | Positive downward displacement actually occurred. |
| `HANGING_CONFIRMED` | The spider reached the measured suspension endpoint. |
| `THREAT_ASSESSED` | A disturbance was classified. |
| `RETURN_METHOD_SELECTED` | A currently approved return method was selected. |
| `RETURN_CONFIRMED` | Positive real travel reached the current safe home. |
| `WEB_IMPACT_CONFIRMED` | The host acknowledged an actual web-impact render after arrival. |
| `SETTLED_AT_HOME` | The acknowledged impact finished settling. |
| `TETHER_RELEASED` | Suspension authority was canceled; ordinary navigation can resume. |

## Lifecycle

Pause, hiding the pet, recall, depth/size changes, display/DPI/working-area changes, unavailable pointer polling, lock/disconnect, and shutdown cancel suspension and clear the tether. Cancellation preserves the last physical position and web progress, marks an away web interrupted, and defers another descent through cooldown. A display clamp is not reported as a successful homecoming.

The native host registers its own window for current-session Windows notifications. Reconnect and unlock are tracked separately, so a reconnect cannot clear a recorded lock. If registration is unavailable, the hanging option stays disabled. Normal spider behavior and exit controls remain available. [Windows session notifications](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsregistersessionnotification)

## Verification and preview

Run the pure regression suite with `./scripts/verify.ps1`. An interactive Windows session can additionally run `./scripts/verify.ps1 -Smoke` for the existing native overlays, owned Ghost Pointer fixture, and hanging overlays.

The hanging fixture runs two rendered sequences with controlled simulation-pointer positions: descent/hang/climb, then descent/hang/safe snap/bounce. It checks actual layer geometry, native click-through hit testing to an owned control, stationary real pointer, unchanged foreground window and owned control counters, retained web/progress, and active lifecycle cancellation. It injects no desktop input. Cancellation handlers are simulated without locking the user's session or reconfiguring displays; this is not a claim that a physical lock/unlock or monitor hot-unplug was performed.

Recorded verification for this build:

| Group | Result |
| --- | --- |
| Focused hanging engine | 28 assertions passed. |
| Independent hanging acceptance | 475 assertions passed across the ten acceptance groups. |
| Tether rendering and physical geometry | 96 assertions passed. |
| Lock/disconnect/reconnect ordering | 13 pure assertions passed. |
| Owned rendered hanging fixture | Passed both climb and snap/bounce sequences, including active cancellation. The recorded final run had 497 assertions, including per-frame checks. |
| Retained behavior and rendering | 38 JavaScript tests; 551 retained native and 749 v11 assertions; 135 escape scenarios; 432 artwork checks; existing web checks; 40 glow and 36 target validation assertions passed. |
| Ghost Pointer | 114 pure curiosity and 25 movement-priority assertions, plus 88 owned live checks passed. No external application window was approved. |

The runtime assertion count includes repeated physical-frame checks and is not a count of distinct user scenarios. The named groups use different units and should not be added into a single test-case total.

The user also tried the separate interactive preview with the real pointer and confirmed that the hanging and return behavior worked. That human observation is separate from the automated fixture's controlled simulation inputs.

For a human-controlled preview, use **Preview hanging in test window** or run `Halloweb.exe --hanging-demo`. The owned preview supplies a 600-second idle input and a separate test web. It reads the real pointer so an approach can select a safe return; it never moves that pointer. Close the preview to end its test spider. The production pet's web remains separate.

The known-good pre-upgrade desktop executable is retained locally as a rollback copy. The new feature is not enabled by installing the build; enable it explicitly from the tray.
