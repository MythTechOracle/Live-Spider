# LIVE-SPIDER-12 — Ghost Pointer / Curious Observer

The spider's independent screen coordinates can trigger an observation and an effect on its own overlay. They never become an input device for the application underneath.

## Observation boundary

One dedicated MTA worker starts point lookups at most once per 150 ms. Requests coalesce; there is no growing queue or replacement worker when a provider hangs. Results expire after 300 ms and must still match the current point, approved root window, process, and approval generation.

Approval defaults to none. **Ctrl+Alt+G** approves the foreground window after the user selects it; only one window is approved at a time. Revocation, replacement, closure, or detected identity changes invalidate pending results. No window names, titles, executable paths, or document names are retrieved or stored for approval. The app does not switch focus to grant permission.

The worker performs one direct `AutomationElement.FromPoint` lookup. It caches only:

- `ControlType`, accepting **Button** and **MenuItem** only;
- `BoundingRectangle`, in physical screen pixels;
- `IsEnabled` and `IsOffscreen`, for eligibility;
- `IsPassword`, solely to reject marked sensitive hits;
- `ProcessId`, to keep the hit inside the approved process.

No element escapes the worker. Cached-only automation elements cannot be used to invoke actions. There is no `FromHandle`, tree traversal, parent search, fallback subtree search, content pattern, name/value lookup, or screenshot-based detection. An own-overlay hit or unsupported target declines; the app does not hide overlays to probe through them.

Known shell, common-dialog, security, lock-screen, and unsupported classes are excluded. This class filter and the hit's password flag cannot classify every sensitive application or page without examining more information. The explicit window approval is therefore essential. Secure/elevated or inaccessible providers can simply fail to produce a target.

## Curiosity and movement

`EXPLORE → TARGET_DETECTED → OBSERVE → GHOST_HOVER → DEPART`

Detection begins a brief hold of an existing safe wander. Observation lasts about 180 ms; the synthetic glow lasts about 600 ms; departure releases movement and applies a short revisit cooldown. The glow is a separate layered, click-through, nonactivating bitmap.

The native motion engine continues to process real elapsed time, threats, current route safety, web identity and weaving authorization during a hold. Only the physical effect of a safe `WANDER` step can pause. Escape, recovery, anchor selection, and web construction cannot be held by curiosity. Leg animation stops during the dwell and resumes with movement.

The five states do not authorize external actions. No mouse messages, click/drag injection, keyboard input, focus requests, UIA invocation, native hot-state changes, or cross-process hooks exist in the curiosity path. The historical native-hover experiment is a separate executable and is not part of the production build.

## Receipts

The separate curiosity ledger holds at most **64** immutable records. Each contains only a sequence number, simulation tick, curiosity state, outcome, fixed reason classification and `Button`/`MenuItem`/`None` kind. It stores no names, coordinates, window handles, process identifiers, app content, or wall-clock timestamps.

| Outcome | Evidence |
| --- | --- |
| `TARGET_OBSERVED` | The host supplied current approved geometry and role. |
| `GHOST_HOVER_RENDERED` | The host acknowledged an actually visible synthetic overlay, after the request. |
| `OBSERVATION_DECLINED` | A visit was denied or interrupted; unchanged declines are deduplicated. |
| `NO_INPUT_DISPATCHED` | The curiosity engine has no input-dispatch capability. This describes its behavior, not all possible external app effects. |

Ghost Pointer performs no filesystem or network operations. Its worker and state engine never read documents, directories, file metadata, clipboard contents, screenshots, or application text. Existing explicitly requested local diagnostics and build/test reports remain separate from the observer; approval grants no file-reading capability.

## Verification

Pure checks cover the full state sequence, render acknowledgement, target loss/change, invalid geometry/time, reset, threat preemption, cooldown, deduplication and bounded receipts. Native motion checks cover held route identity, resumed movement, advancing simulation time, immediate escape, recovery, anchor selection, weaving, and idle-threshold priority.

The owned-control fixture uses a named kernel memory channel rather than disk files for coordination. It checks passive detection, appearance, current geometry, disabled/unsupported/covered targets, approval and revocation, native state/event counters, and unchanged physical pointer and foreground focus. It does not inspect user documents or take screenshots. Windows hit testing and overlay styles are checked without injecting mouse input.

On **1 November 2026**, **88 live owned-fixture assertions passed**. The rendered spider completed `EXPLORE → TARGET_DETECTED → OBSERVE → GHOST_HOVER → DEPART`, physically held its position, acknowledged a visible glow and continued walking. A second encounter verified immediate threat preemption. Unapproved and revoked requests made no new UIA lookups; native `BST_HOT` and hover/down/up/click/focus counters stayed unchanged. The real pointer and foreground stayed unchanged across sampled ticks. `MenuItem` classification was verified in pure checks; the live fixture exercised `Button`.

The pure suites passed **114 curiosity assertions** and **25 native movement-priority assertions**, alongside all **38 JavaScript** tests, **551 retained native** assertions, **749 v11 native** assertions, existing escape/artwork/silk checks, and glow validation. These groups use different units and should not be combined into one test-case total.

A separate **human physical click-through check passed** after the passive run. The user clicked the owned test button through the visible spider and glow; the fixture recorded down/up/click after glow rendering, and Windows hit testing resolved to that same button. No click was injected. This manual phase permits the user's pointer movement and button action; it is distinct from the passive encounter's stationary-pointer and zero-action assertions.

To repeat that optional manual check, run `Halloweb.exe --hover-manual-check` in a writable test folder. It opens only an owned fixture and waits up to three minutes for one human click; its report is `manual-click-test-results.txt`. It is not part of unattended verification.

Testing another application requires explicit approval for that window. Owned-fixture results must not be presented as universal verification across external applications. A retained copy of the working desktop executable provides the rollback point before installing a new build.

This verification run is intentionally limited to owned test controls. No external application window is approved or claimed as verified.

The installed 0.3.0 build was checked for live pointer polling, a working quit shortcut, and **zero UIA lookups** while no window was approved. The normal spider/silk overlay smoke check also passed. The pre-upgrade executable is retained locally as the rollback copy.

Document reading remains deferred to a separate future capability with its own permission and verification requirements.
