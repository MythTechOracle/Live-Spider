# Behavior and verification

Halloweb separates a proposed action from its actual effect. A target is an intent; changed coordinates establish movement; measured clearance establishes safety. Reaching the web is then a separate fact from having permission to weave.

## The behavior boundary

The native Windows pet follows this sequence:

1. **Intent:** select an escape, wandering, anchor, or recovery target.
2. **Approval:** check the usable display bounds and the current route geometry. A normal return corridor must clear the pointer's danger region and safety margin.
3. **Execution:** move by a bounded amount using elapsed time. The executed target must still match the approved route.
4. **Verification:** measure actual displacement, final pointer distance, and distance to the current web anchor.
5. **Authorization:** permit weaving only at that web, with its current identity, sufficient idle time, current safety, and no active movement route.
6. **Construction:** increase web progress only through that same authorization boundary.

A changed target, display bounds, web, anchor, or invalidated route retires the old plan as appropriate. A state name such as `RECOVER` or `WEAVE` is never sufficient evidence that movement, arrival, or construction occurred.

An escape can briefly move closer to a threat while maneuvering away from a wall. Clearance is checked after movement; neither moving away from a wall nor reaching an escape target certifies safety on its own.

## Returning home

The **Windows app waits** when the straight return corridor is blocked. Rejected corridors have bounded, expiring session memory: at most 16 entries, with a three-second lifetime. Current pointer geometry is reconsidered rather than blindly executing the rejected path. A stopped wait has zero execution speed.

Moving the pointer clear permits a new safe return plan. Interrupted silk progress is retained. Arrival is verified independently of idle time: the spider can get home while construction remains deferred.

The native return contract requires prior absence and actual displacement back to the measured anchor. Recalling the spider is an explicit reset that clears its old web and route memory; it is not recorded as a successful homecoming.

## Weaving permission

One native `CanWeave` boundary governs the first web, resumed construction, waiting at home, and every progress update. It checks the actual position, current web identity and anchor, pointer safety, idle threshold, and route status.

Pointer entry, renewed activity, web replacement, anchor changes, or new movement can revoke permission. Web progress cannot inherit permission granted to a different web. **Build a web now** temporarily satisfies the idle condition for 90 seconds; it does not waive the position, identity, or safety checks.

## Receipts and lineage

Per-update receipts are short outcome names. A separate structured ledger records sequence, tick, route and decision identifiers where applicable, event/outcome, reason, evidence, and timestamp.

| Outcome | What it establishes |
| --- | --- |
| `MOVE_CONFIRMED` | Measured displacement occurred under an approved movement plan. |
| `THREAT_CLEARED` | In the native app, a threat episode ended at a safe measured position. |
| `POINTER_WITHDREW` | Pointer withdrawal cleared danger without requiring spider displacement. It may coexist with native `THREAT_CLEARED`. |
| `ROUTE_BLOCKED` | A proposed corridor failed its current safety check; this does not claim a movement happened. |
| `RETURN_CONFIRMED` | The native spider physically returned to the current safe web anchor after being away. |
| `WEAVE_DEFERRED` | Current conditions do not authorize construction. |
| `WEAVE_AUTHORIZED` | The current web, actual location, idle time, and safety conditions authorize weaving. |
| `WEAVE_PROGRESS_CONFIRMED` | A positive, authorized construction effect was measured. Progress evidence is throttled rather than logged every frame. |

An unchanged journey retains its route and decision identifiers. Replanning, cancellation, completion, or invalidation ends that lineage. Later motion requires its own current approval. Rejected candidates remain distinct from approved plans and executed movements.

The ledger keeps **at most 200 records in RAM**. Oldest records are evicted while sequence numbers continue increasing. Unchanged deferrals are deduplicated so waiting does not fill the ledger with the same refusal every frame. Movement can still consume the bounded history; this is neither a permanent audit archive nor restart persistence.

Evidence is captured when a record is created, rather than retaining a live reference to a mutable web. The native timestamps use seconds; JavaScript timestamps use the caller's declared units. Injected timestamp zero is valid. Comparing exports from different hosts requires agreeing on units and clock origin first.

## Local diagnostics

Run these commands from the folder containing the executable:

```powershell
./Halloweb.exe --status
./Halloweb.exe --ledger
./Halloweb.exe --quit
```

`--status` writes an on-demand state and authorization summary to `running-status.txt`. `--ledger` writes the current session buffer to `session-ledger.json` beside the executable. This is an explicit local snapshot, not automatic history storage or an upload. `--quit` closes the running normal instance.

Ledger evidence can include the spider's simulation coordinates, pointer-distance measurements, and current web information. The app reads the global pointer locally and uses Windows' last-input idle information; it does not record the contents of keystrokes. Activity heat and temporary route memory are also session-local.

## Visual hover glow

The glow is a separate small amber overlay around an accessible control under the spider's body. It does not set the app's own hover state. No pointer movement, synthetic input, control invocation, focus request, or hover message is sent to the app underneath.

Supported roles are buttons, hyperlinks, checkboxes, radio buttons, tabs, and menu items. Disabled, offscreen, excessively large, unavailable, or ambiguous targets produce no glow. Desktop files and text fields are excluded. Some applications expose little accessibility geometry, so this effect will not appear on every button.

One background MTA worker reads only control role, bounding rectangle, enabled/offscreen state, and process identity. It reads no names or text and retains no automation element outside that worker. Requests coalesce into one pending point, start at most ten times per second, and expire after 300 milliseconds. Provider calls cannot delay the spider's animation thread. A hung provider occupies that one background worker; the app does not create replacement threads to chase it.

The detector excludes Halloweb's own windows and rechecks the visible external window beneath the spider before displaying a result. A bounded fallback handles its transparent overlays without hiding them or moving the pointer. Geometry and window identity checks are conservative; they do not reproduce every application's private hit-testing rules. Missing or uncertain geometry means no glow.

The tray's **Spider hover glow** option is enabled at launch. Switching it off, pausing, hiding the spider, recalling it, or closing the app clears the glow. All three visual overlays use layered, click-through, nonactivating windows.

## JavaScript reference engine

The source under `src/behavior/` is a separately tested JavaScript behavior engine. It retains complete two-leg geometric detours, revalidates the remaining clamped route, and stops an invalidated plan before stale movement can occur. Its detour tests do not establish a native Windows detour feature.

Two compatibility differences are explicit:

- The JavaScript suite permits a one-time `RETURN_CONFIRMED` when the spider is already at its measured anchor. That record says `ALREADY_AT_ANCHOR`, reports zero displacement, and produces no movement receipt. The native app retains its stricter away-to-home contract.
- JavaScript uses `THREAT_CLEARED` for spider-caused clearance and `POINTER_WITHDREW` for pointer-caused clearance. Native `THREAT_CLEARED` records verified safety and can accompany a withdrawal receipt.

JavaScript display relocation is reported separately as `BOUNDS_REPOSITIONED`; it cannot impersonate voluntary movement on an approved route or claim that a stationary pointer withdrew.

## Verification record

The recorded verification on **31 October 2026** distinguishes behavior tests from real Windows overlay checks:

| Check group | Recorded result |
| --- | --- |
| JavaScript v11 | **38 passed, 0 failed** on Node.js 24.14.0: four original supplied tests, seven supplied adversarial tests, and 27 independent acceptance cases. |
| Retained native behavior checks | **551 assertions passed**. |
| Native v11 behavior checks | **749 assertions passed**. |
| Existing desktop self-tests | Retain 135 escape scenarios, 432 artwork rotation/size/animation checks, and progressive silk checks. |
| Real Windows smoke checks | Exercise both layered overlays, click-through and no-activation styles, retained keyboard focus, screen placement, pause/resume, and ledger JSON serialization. |
| Hover glow checks (1 November 2026) | **40** pure rendering/geometry assertions and **27** target validation/freshness assertions passed. **19** separate real Windows fixture checks verified detection through both overlays, disabled/text/covered/closed controls, no input events during probing, and unchanged pointer/focus. |

These are different groups and units of verification, not a single combined test-case count. The two original JavaScript test files were preserved without weakening their assertions.

Fresh cases cover central denial across first-web, resumed-web, waiting, recovery, and ongoing-effect paths; current web identity; actual homecoming while weaving remains unauthorized; stable route and decision lineage; stale-target cancellation; current-geometry approval before movement; immutable evidence snapshots; timestamp zero; bounded history; and deduplicated deferrals.

The Windows runtime checks are separate from Node.js success. An initial tool-sandbox launch could not poll the global pointer; repeating the same verified executable outside that sandbox confirmed live pointer polling and engine receipts. That result describes the execution context of those checks, not a claim of broad testing across every Windows installation. No Electron runtime is used or verified by the native build.

From a Windows checkout, run `./scripts/verify.ps1` for the packaged checks. The native executable also supports `--self-test` and a temporary `--smoke-test` launch.

`./scripts/verify.ps1 -Smoke` additionally launches an isolated hover fixture. It checks actual detection beneath the spider and glow, physical alignment, text/disabled/covered targets, clear behavior, and the absence of fixture mouse-enter, hover, click, focus, and activation events. It also verifies that the real pointer and foreground window remain unchanged. Its controls belong to a separate test process; it does not interact with buttons in the user's apps. The fixture closes automatically. Keep the pointer still during this opt-in check.
