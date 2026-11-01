'use strict';

const STATES = Object.freeze({ ACTIVE_EVASION: 'ACTIVE_EVASION', WANDER: 'WANDER',
  SELECT_ANCHOR: 'SELECT_ANCHOR', WEAVE: 'WEAVE', ESCAPE: 'ESCAPE', RECOVER: 'RECOVER',
  DETOUR: 'DETOUR', WAIT: 'WAIT', WAIT_AT_ANCHOR: 'WAIT_AT_ANCHOR' });
const EPS = 1e-6;
const distance = (ax, ay, bx, by) => Math.hypot(bx - ax, by - ay);
function finite(value, label) {
  if (typeof value !== 'number' || !Number.isFinite(value)) throw new TypeError(`${label} must be finite`);
  return value;
}
function nonnegative(value, label) {
  finite(value, label);
  if (value < 0) throw new RangeError(`${label} cannot be negative`);
  return value;
}
const configured = (config, name, fallback) => config[name] === undefined ? fallback : config[name];
function immutable(value) {
  if (value === null || typeof value !== 'object') return value;
  const copy = Array.isArray(value) ? value.map(immutable) : Object.fromEntries(Object.entries(value).map(([key, item]) => [key, immutable(item)]));
  return Object.freeze(copy);
}
function corridorDistance(sx, sy, tx, ty, px, py) {
  const vx = tx - sx, vy = ty - sy, square = vx * vx + vy * vy;
  const fraction = square === 0 ? 0 : Math.max(0, Math.min(1, ((px - sx) * vx + (py - sy) * vy) / square));
  return Math.hypot(px - sx - fraction * vx, py - sy - fraction * vy);
}

class SpiderHarness {
  constructor(config = {}) {
    this.threatRadius = nonnegative(configured(config, 'threatRadius', 150), 'threatRadius');
    this.safetyMargin = nonnegative(configured(config, 'safetyMargin', 12), 'safetyMargin');
    this.wanderThreshold = nonnegative(configured(config, 'wanderThreshold', 120), 'wanderThreshold');
    this.weaveThreshold = nonnegative(configured(config, 'weaveThreshold', 300), 'weaveThreshold');
    this.padding = nonnegative(configured(config, 'padding', 20), 'padding');
    this.anchorTolerance = nonnegative(configured(config, 'anchorTolerance', 1), 'anchorTolerance');
    this.x = finite(configured(config, 'startX', 100), 'startX');
    this.y = finite(configured(config, 'startY', 100), 'startY');
    this.targetX = this.x; this.targetY = this.y; this.speed = 0;
    this.state = STATES.ACTIVE_EVASION;
    this.currentWeb = null;
    this.activityMap = new Map(); this.lastCursor = null;
    this.wasThreatened = false;
    this.tickCount = 0; this.routeCounter = 0; this.decisionCounter = 0; this.sequenceCounter = 0;
    this.currentRouteId = null; this.currentDecisionId = null;
    this.ledger = []; this.receipts = [];
    this.authorization = null;
    this.elapsedTime = 0;
    this._clock = config.clock;
    if (this._clock !== undefined && typeof this._clock !== 'function') throw new TypeError('clock must be a function');
    this._timestamp = 0; this._context = null; this._route = null;
    this._webIds = new WeakMap(); this._webCounter = 0; this._webSignature = null;
    this._deferredKey = null; this._homeKey = null; this._waitContext = null;
    this._permissionDecisionId = null; this._lastProgressLog = -Infinity;
    this._previousCursor = null; this._homeLineage = null;
    this._loggingTick = false;
  }

  _webId(web = this.currentWeb) {
    if (!web || typeof web !== 'object') return null;
    if (!this._webIds.has(web)) this._webIds.set(web, `W-${++this._webCounter}`);
    return this._webIds.get(web);
  }

  _rectangle(bounds) {
    if (!bounds || typeof bounds !== 'object') throw new TypeError('bounds are required');
    const width = finite(bounds.width, 'bounds.width'), height = finite(bounds.height, 'bounds.height');
    if (width <= 0 || height <= 0) throw new RangeError('bounds dimensions must be positive');
    const left = finite(bounds.left === undefined ? 0 : bounds.left, 'bounds.left');
    const top = finite(bounds.top === undefined ? 0 : bounds.top, 'bounds.top');
    const right = finite(left + width, 'bounds.right'), bottom = finite(top + height, 'bounds.bottom');
    const px = Math.min(this.padding, width / 2), py = Math.min(this.padding, height / 2);
    return { left: left + px, top: top + py, right: right - px, bottom: bottom - py, width, height, originX: left, originY: top };
  }

  _clamp(x, y, bounds = this._context && this._context.rectangle) {
    if (!bounds) return { x, y };
    return { x: Math.max(bounds.left, Math.min(bounds.right, x)), y: Math.max(bounds.top, Math.min(bounds.bottom, y)) };
  }

  _inside(x, y, rectangle) {
    return x >= rectangle.left - EPS && x <= rectangle.right + EPS && y >= rectangle.top - EPS && y <= rectangle.bottom + EPS;
  }

  _atAnchor(web = this.currentWeb) {
    return Boolean(web && distance(this.x, this.y, web.anchorX, web.anchorY) <= this.anchorTolerance);
  }

  _evidence(extra = {}) {
    const context = this._context;
    const web = this.currentWeb;
    return {
      from: { x: this.x, y: this.y }, to: { x: this.x, y: this.y }, distanceMoved: 0,
      webId: this._webId(web), anchor: web ? { x: web.anchorX, y: web.anchorY } : null,
      distanceToAnchor: web ? distance(this.x, this.y, web.anchorX, web.anchorY) : null,
      anchorTolerance: this.anchorTolerance,
      idleTime: context ? context.idleTime : null, requiredIdle: this.weaveThreshold,
      safe: context ? distance(this.x, this.y, context.cursorX, context.cursorY) >= this.threatRadius : false,
      ...extra
    };
  }

  addReceipt(outcome, details = {}, timestamp = this._timestamp) {
    finite(timestamp, 'receipt timestamp');
    const record = immutable({ ...details, sequence: ++this.sequenceCounter, tick: this.tickCount,
      routeId: details.routeId === undefined ? this.currentRouteId : details.routeId,
      decisionId: details.decisionId === undefined ? this.currentDecisionId : details.decisionId,
      event: outcome, outcome, reason: details.reason || outcome,
      evidence: details.evidence || this._evidence(), timestamp });
    this.ledger.push(record);
    if (this.ledger.length > 200) this.ledger.splice(0, this.ledger.length - 200);
    return record;
  }

  _log(outcome, details = {}) {
    if (this._loggingTick && !this.receipts.includes(outcome)) this.receipts.push(outcome);
    return this.addReceipt(outcome, details, this._timestamp);
  }

  _decision(kind) { return `D-${++this.decisionCounter}-${kind}`; }

  assignRoute(state, targetX, targetY, speed, options = {}) {
    if (!Object.values(STATES).includes(state)) throw new RangeError('Unknown route state');
    finite(targetX, 'targetX'); finite(targetY, 'targetY'); nonnegative(speed, 'speed');
    const target = this._clamp(targetX, targetY);
    const webId = options.webId === undefined ? this._webId() : options.webId;
    const finalX = options.finalX === undefined ? target.x : finite(options.finalX, 'finalX');
    const finalY = options.finalY === undefined ? target.y : finite(options.finalY, 'finalY');
    const same = this._route && this._route.active && this._route.state === state &&
      distance(target.x, target.y, this._route.targetX, this._route.targetY) <= EPS &&
      distance(finalX, finalY, this._route.finalX, this._route.finalY) <= EPS && this._route.webId === webId && this._route.speed === speed;
    if (!same) {
      if (this._route && this._route.active) this._cancelRoute('GENUINE_REPLAN');
      this.currentRouteId = `R-${++this.routeCounter}-${state}`;
      this.currentDecisionId = options.decisionId || this._decision(state);
      this._route = { active: true, state, targetX: target.x, targetY: target.y, finalX, finalY, speed,
        routeId: this.currentRouteId, decisionId: this.currentDecisionId, webId, approved: false };
      const evidence = this._evidence({ from: { x: this.x, y: this.y }, to: { ...target }, final: { x: finalX, y: finalY }, webId });
      this._log('PLAN_SELECTED', { reason: 'APPROVAL_PENDING_CURRENT_EXECUTION_PREFLIGHT', evidence });
    }
    this.state = state; this.targetX = target.x; this.targetY = target.y; this.speed = speed;
    return this.currentRouteId;
  }

  _cancelRoute(reason) {
    if (this._route && this._route.active) this._log('ROUTE_CANCELLED', { reason, evidence: this._evidence({ target: { x: this.targetX, y: this.targetY } }) });
    if (this._route) this._route.active = false;
    this.speed = 0; this.targetX = this.x; this.targetY = this.y;
    this.currentRouteId = null; this.currentDecisionId = null;
  }

  _completeRoute(reason) {
    if (this._route && this._route.active) this._log('ROUTE_COMPLETED', { reason, evidence: this._evidence() });
    if (this._route) this._route.active = false;
    this.currentRouteId = null; this.currentDecisionId = null;
    this.speed = 0; this.targetX = this.x; this.targetY = this.y;
  }

  _approveCurrentRoute(context) {
    if (!this._route || !this._route.active) return false;
    if (!this._routeSafe(context)) return false;
    if (!this._route.approved) {
      const evidence = this._evidence({ from: { x: this.x, y: this.y }, to: { x: this.targetX, y: this.targetY },
        final: { x: this._route.finalX, y: this._route.finalY },
        cursor: { x: context.cursorX, y: context.cursorY },
        requiredPx: this.threatRadius + this.safetyMargin,
        routeSafe: this.state === STATES.ESCAPE ? null : true,
        approvalKind: this.state === STATES.ESCAPE ? 'ESCAPE_MANEUVER' : 'FULL_CLAMPED_REMAINING_ROUTE' });
      this._log('ROUTE_EVALUATED', { reason: 'CURRENT_EXECUTION_GEOMETRY_CHECKED', evidence });
      this._log('ROUTE_APPROVED', { reason: this.state === STATES.ESCAPE ? 'ESCAPE_MANEUVER_APPROVED' : 'FULL_CLAMPED_REMAINING_ROUTE_SAFE', evidence });
      this._route.approved = true;
    }
    return true;
  }

  _hold(state, reason) {
    this._cancelRoute(reason);
    this.state = state; this.speed = 0; this.targetX = this.x; this.targetY = this.y;
  }

  isCorridorBlocked(sx, sy, wx, wy, px, py) {
    for (const [key, value] of Object.entries({ sx, sy, wx, wy, px, py })) finite(value, key);
    return this.threatRadius > 0 && corridorDistance(sx, sy, wx, wy, px, py) + EPS < this.threatRadius + this.safetyMargin;
  }

  isCompleteRouteSafe(sx, sy, wx, wy, dx, dy, px, py) {
    return !this.isCorridorBlocked(sx, sy, wx, wy, px, py) && !this.isCorridorBlocked(wx, wy, dx, dy, px, py);
  }

  _routeSafe(context) {
    const waypoint = this._clamp(this.targetX, this.targetY, context.rectangle);
    if (this.state === STATES.ESCAPE) return true;
    if (this.isCorridorBlocked(this.x, this.y, waypoint.x, waypoint.y, context.cursorX, context.cursorY)) return false;
    if (this.state === STATES.DETOUR) {
      return Boolean(this.currentWeb && this._route && this._route.webId === this._webId() &&
        this._inside(this.currentWeb.anchorX, this.currentWeb.anchorY, context.rectangle) &&
        !this.isCorridorBlocked(waypoint.x, waypoint.y, this.currentWeb.anchorX, this.currentWeb.anchorY, context.cursorX, context.cursorY));
    }
    return true;
  }

  _rejected(kind, goal, context, reason) {
    const routeId = `C-${++this.routeCounter}-${kind}`;
    const decisionId = this._decision(kind);
    const evidence = this._evidence({ from: { x: this.x, y: this.y }, to: { ...goal },
      clearancePx: corridorDistance(this.x, this.y, goal.x, goal.y, context.cursorX, context.cursorY),
      requiredPx: this.threatRadius + this.safetyMargin });
    this._log('ROUTE_EVALUATED', { routeId, decisionId, reason: 'CANDIDATE_ROUTE_CHECKED', evidence });
    this._log('ROUTE_REJECTED', { routeId, decisionId, reason, evidence });
  }

  _corners(context, firstRight = false) {
    const r = context.rectangle;
    const x1 = r.originX + Math.min(100, r.width / 2), x2 = r.originX + r.width - Math.min(100, r.width / 2);
    const y1 = r.originY + Math.min(100, r.height / 2), y2 = r.originY + r.height - Math.min(100, r.height / 2);
    const corners = firstRight ? [[x2, y1], [x1, y1], [x2, y2], [x1, y2]] : [[x1, y1], [x2, y1], [x1, y2], [x2, y2]];
    return corners.map(([x, y]) => this._clamp(x, y, r));
  }

  _planReturn(context) {
    const web = this.currentWeb;
    const goal = { x: web.anchorX, y: web.anchorY };
    if (!this._inside(goal.x, goal.y, context.rectangle)) {
      this._hold(STATES.WAIT, 'ANCHOR_OUTSIDE_USABLE_BOUNDS'); return;
    }
    if (this._route && this._route.active && this._route.webId === this._webId() &&
      [STATES.RECOVER, STATES.DETOUR].includes(this.state) && this._routeSafe(context)) return;
    const wait = this._waitContext;
    if (wait && wait.webId === this._webId() && wait.until > this.elapsedTime &&
      distance(wait.cursorX, wait.cursorY, context.cursorX, context.cursorY) < 5 && wait.bounds === JSON.stringify(context.rectangle)) {
      this._hold(STATES.WAIT, 'REMEMBERED_BLOCKED_RETURN'); return;
    }
    if (!this.isCorridorBlocked(this.x, this.y, goal.x, goal.y, context.cursorX, context.cursorY)) {
      this._waitContext = null;
      this.assignRoute(STATES.RECOVER, goal.x, goal.y, 200); return;
    }
    this._rejected('DIRECT_RETURN', goal, context, 'THREAT_INTERSECTS_CORRIDOR');
    for (const corner of this._corners(context)) {
      if (distance(this.x, this.y, corner.x, corner.y) < 5) continue;
      if (this.isCompleteRouteSafe(this.x, this.y, corner.x, corner.y, goal.x, goal.y, context.cursorX, context.cursorY)) {
        this._waitContext = null;
        this.assignRoute(STATES.DETOUR, corner.x, corner.y, 150, { finalX: goal.x, finalY: goal.y });
        this._log('DETOUR_SELECTED', { reason: 'BOTH_CLAMPED_LEGS_SAFE', waypoint: { ...corner },
          evidence: this._evidence({ waypoint: { ...corner }, destination: { ...goal } }) });
        return;
      }
    }
    this._log('DETOUR_REJECTED', { reason: 'ALL_CLAMPED_WAYPOINTS_OBSTRUCTED', evidence: this._evidence({ destination: { ...goal } }) });
    this._hold(STATES.WAIT, 'NO_SAFE_RETURN_CORRIDOR');
    this._waitContext = { webId: this._webId(), cursorX: context.cursorX, cursorY: context.cursorY,
      until: this.elapsedTime + 1, bounds: JSON.stringify(context.rectangle) };
  }

  _weaveCheck(context, web = this.currentWeb) {
    if (!context || !web || web !== this.currentWeb) return 'NO_CURRENT_WEB';
    if (this._webSignature !== `${this._webId(web)}:${web.anchorX}:${web.anchorY}`) return 'WEB_IDENTITY_CHANGED';
    if (!this._inside(web.anchorX, web.anchorY, context.rectangle)) return 'ANCHOR_OUTSIDE_USABLE_BOUNDS';
    if (!this._atAnchor(web)) return 'NOT_AT_ANCHOR';
    if (distance(this.x, this.y, context.cursorX, context.cursorY) < this.threatRadius + this.safetyMargin) return 'THREAT_NEAR_ANCHOR';
    if (context.idleTime < this.weaveThreshold) return 'IDLE_THRESHOLD_NOT_MET';
    return null;
  }

  canWeave(context = this._context, web = this.currentWeb) {
    return this._weaveCheck(context, web) === null;
  }

  _revokeWeave(reason) {
    if (this.authorization) this._log('WEAVE_REVOKED', { reason,
      evidence: this._evidence({ previousWebId: this.authorization.webId, authorizationId: this.authorization.decisionId }) });
    this.authorization = null;
  }

  authorizeWeave(context = this._context, web = this.currentWeb) {
    const permitted = this.canWeave(context, web);
    const reason = permitted ? null : this._weaveCheck(context, web) || 'CENTRAL_GUARD_DENIED';
    if (!permitted) {
      this._revokeWeave(reason);
      const key = `${this._webId(web)}:${web ? `${web.anchorX}:${web.anchorY}` : 'none'}:${reason}`;
      if (key !== this._deferredKey) {
        if (!this._permissionDecisionId) this._permissionDecisionId = this._decision('WEAVE_PERMISSION');
        this._log('WEAVE_DEFERRED', { decisionId: this._permissionDecisionId, reason, evidence: this._evidence() });
        this._deferredKey = key;
      }
      if (this._atAnchor(web) && this.state !== STATES.ESCAPE) this._hold(STATES.WAIT_AT_ANCHOR, reason);
      return false;
    }
    const webId = this._webId(web);
    if (!this.authorization || this.authorization.webId !== webId) {
      this._permissionDecisionId = this._decision('WEAVE_PERMISSION');
      this.authorization = immutable({ webId, decisionId: this._permissionDecisionId, routeId: this.currentRouteId,
        anchorX: web.anchorX, anchorY: web.anchorY, tick: this.tickCount });
      this.authorization = immutable({ ...this.authorization, routeId: this._homeLineage ? this._homeLineage.routeId : null });
      this._log('WEAVE_AUTHORIZED', { routeId: this.authorization.routeId, decisionId: this._permissionDecisionId, reason: 'CURRENT_LOCATION_IDLE_AND_SAFETY_VERIFIED', evidence: this._evidence() });
    }
    this._deferredKey = null;
    this._hold(STATES.WEAVE, 'ROUTE_FINISHED_AT_AUTHORIZED_WEB');
    web.interrupted = false;
    return true;
  }

  setEscapeTarget(cursorX, cursorY, bounds) {
    const rectangle = this._rectangle(bounds);
    let dx = this.x - cursorX, dy = this.y - cursorY;
    if (Math.abs(dx) < 1 && Math.abs(dy) < 1) { dx = (rectangle.left + rectangle.right) / 2 - this.x; dy = (rectangle.top + rectangle.bottom) / 2 - this.y; }
    const margin = Math.min(100, rectangle.width / 3, rectangle.height / 3);
    if (this.x > rectangle.right - margin) dx -= this.x - (rectangle.right - margin);
    if (this.x < rectangle.left + margin) dx += rectangle.left + margin - this.x;
    if (this.y > rectangle.bottom - margin) dy -= this.y - (rectangle.bottom - margin);
    if (this.y < rectangle.top + margin) dy += rectangle.top + margin - this.y;
    if (Math.abs(dx) < 1 && Math.abs(dy) < 1) { dx = (rectangle.left + rectangle.right) / 2 - this.x; dy = (rectangle.top + rectangle.bottom) / 2 - this.y; }
    if (Math.abs(dx) < EPS && Math.abs(dy) < EPS) dx = 1;
    const angle = Math.atan2(dy, dx);
    let target = this._clamp(this.x + Math.cos(angle) * 160, this.y + Math.sin(angle) * 160, rectangle);
    if (distance(this.x, this.y, target.x, target.y) < 1) {
      let best = -Infinity;
      for (let i = 0; i < 32; i++) {
        const p = this._clamp(this.x + Math.cos(i * Math.PI / 16) * 240, this.y + Math.sin(i * Math.PI / 16) * 240, rectangle);
        const score = distance(p.x, p.y, cursorX, cursorY);
        if (score > best) { target = p; best = score; }
      }
    }
    this.assignRoute(STATES.ESCAPE, target.x, target.y, 600);
  }

  _validatedContext(env) {
    if (!env || typeof env !== 'object') throw new TypeError('tick environment is required');
    const rectangle = this._rectangle(env.bounds);
    const cursorX = finite(env.cursorX, 'cursorX'), cursorY = finite(env.cursorY, 'cursorY');
    const idleTime = Math.max(0, finite(env.idleTime, 'idleTime'));
    // Invalid elapsed time deliberately becomes a zero-duration, nonmoving frame.
    const deltaTime = typeof env.deltaTime === 'number' && Number.isFinite(env.deltaTime) ? Math.max(0, Math.min(0.5, env.deltaTime)) : 0;
    finite(this.x, 'x'); finite(this.y, 'y'); finite(this.targetX, 'targetX'); finite(this.targetY, 'targetY'); nonnegative(this.speed, 'speed');
    for (const key of ['threatRadius', 'safetyMargin', 'wanderThreshold', 'weaveThreshold', 'padding', 'anchorTolerance']) nonnegative(this[key], key);
    if (this.currentWeb) {
      if (typeof this.currentWeb !== 'object') throw new TypeError('web must be an object');
      finite(this.currentWeb.anchorX, 'web.anchorX'); finite(this.currentWeb.anchorY, 'web.anchorY'); nonnegative(this.currentWeb.progress, 'web.progress');
    }
    if (env.randomFloat !== undefined && typeof env.randomFloat !== 'function') throw new TypeError('randomFloat must be a function');
    const timestamp = finite(env.timestamp === undefined ? (this._clock ? this._clock() : (this.elapsedTime + deltaTime) * 1000) : env.timestamp, 'timestamp');
    return { ...env, rectangle, cursorX, cursorY, idleTime, deltaTime, timestamp };
  }

  evaluateTick(env) {
    const context = this._validatedContext(env); // No physical state changes precede validation.
    const originalX = this.x, originalY = this.y, originalState = this.state;
    this.tickCount++; this.elapsedTime += context.deltaTime; this._timestamp = context.timestamp;
    this._context = context; this.receipts = []; this._loggingTick = true;
    const location = this._clamp(this.x, this.y, context.rectangle);
    this.x = location.x; this.y = location.y;
    const boundsDistanceMoved = distance(originalX, originalY, this.x, this.y);
    if (boundsDistanceMoved > EPS) {
      this._cancelRoute('BOUNDS_PLACEMENT_REQUIRES_NEW_PLAN');
      this._log('BOUNDS_REPOSITIONED', { routeId: null, decisionId: this._decision('DISPLAY_PLACEMENT'),
        reason: 'MANDATORY_USABLE_DISPLAY_PLACEMENT', evidence: this._evidence({
          from: { x: originalX, y: originalY }, to: { x: this.x, y: this.y }, distanceMoved: boundsDistanceMoved }) });
    }
    const signature = this.currentWeb ? `${this._webId()}:${this.currentWeb.anchorX}:${this.currentWeb.anchorY}` : null;
    if (signature !== this._webSignature) {
      this._revokeWeave('WEB_IDENTITY_CHANGED');
      if (this._webSignature !== null) this._cancelRoute('WEB_IDENTITY_CHANGED');
      this._webSignature = signature; this._deferredKey = null; this._homeKey = null;
      this._permissionDecisionId = null; this._waitContext = null;
      this._homeLineage = null;
    }
    if (this.authorization && !this.canWeave(context, this.currentWeb)) this._revokeWeave(this._weaveCheck(context) || 'CENTRAL_GUARD_DENIED');
    if (!this.lastCursor || distance(context.cursorX, context.cursorY, this.lastCursor.x, this.lastCursor.y) > 5) {
      const cell = `${Math.floor(context.cursorX / 100)},${Math.floor(context.cursorY / 100)}`;
      this.activityMap.set(cell, Math.min((this.activityMap.get(cell) || 0) + 1, 100));
      while (this.activityMap.size > 256) this.activityMap.delete(this.activityMap.keys().next().value);
      this.lastCursor = { x: context.cursorX, y: context.cursorY };
    }
    const startX = this.x, startY = this.y;
    const preThreatDistance = distance(this.x, this.y, context.cursorX, context.cursorY);
    const threatened = preThreatDistance < this.threatRadius;
    const pointerChanged = this._previousCursor && distance(context.cursorX, context.cursorY, this._previousCursor.x, this._previousCursor.y) > EPS;
    const pointerWithdrew = this.wasThreatened && !threatened && pointerChanged && boundsDistanceMoved <= EPS;
    let invalidated = false;
    if (!this._atAnchor()) this._homeKey = null;
    if ([STATES.RECOVER, STATES.DETOUR, STATES.WANDER, STATES.SELECT_ANCHOR].includes(this.state) && this.speed > 0 && !this._routeSafe(context)) {
      this._hold(STATES.WAIT, 'REMAINING_CLAMPED_ROUTE_INVALIDATED');
      this._log('ROUTE_REJECTED', { reason: 'REMAINING_CLAMPED_ROUTE_INVALIDATED', evidence: this._evidence() });
      this._waitContext = null; invalidated = true;
    }

    // Intent chooses a route; every non-escape route is checked after clamping.
    if (threatened) {
      this._revokeWeave('THREAT_NEAR_ANCHOR');
      if (this.currentWeb) this.currentWeb.interrupted = true;
      if (this.state !== STATES.ESCAPE || distance(this.x, this.y, this.targetX, this.targetY) <= EPS) this.setEscapeTarget(context.cursorX, context.cursorY, context.bounds);
    } else if (pointerWithdrew) {
      this._hold(STATES.ACTIVE_EVASION, 'POINTER_WITHDREW');
    } else if (invalidated) {
      // Hold this frame; a fresh complete plan may be selected on the next tick.
    } else if (this.state === STATES.ESCAPE) {
      if (this.isCorridorBlocked(this.x, this.y, this.targetX, this.targetY, context.cursorX, context.cursorY)) this._hold(STATES.ACTIVE_EVASION, 'STALE_ESCAPE_ROUTE_UNSAFE');
    } else if (this.currentWeb && !this._atAnchor() && (this.currentWeb.interrupted || context.idleTime >= this.weaveThreshold || [STATES.RECOVER, STATES.DETOUR, STATES.WAIT_AT_ANCHOR, STATES.WAIT].includes(this.state))) {
      this.currentWeb.interrupted = true;
      this._planReturn(context);
    } else if (this.currentWeb && this._atAnchor() && (this.currentWeb.interrupted || context.idleTime >= this.weaveThreshold || [STATES.RECOVER, STATES.WAIT_AT_ANCHOR, STATES.WEAVE].includes(this.state))) {
      this._hold(STATES.WAIT_AT_ANCHOR, 'AWAITING_CURRENT_WEAVE_PERMISSION');
    } else if (!this.currentWeb && context.idleTime >= this.weaveThreshold) {
      const existing = this._route && this._route.active && this.state === STATES.SELECT_ANCHOR;
      const corners = this._corners(context, true);
      corners.sort((a, b) => (this.activityMap.get(`${Math.floor(a.x / 100)},${Math.floor(a.y / 100)}`) || 0) - (this.activityMap.get(`${Math.floor(b.x / 100)},${Math.floor(b.y / 100)}`) || 0));
      const anchor = existing ? this._clamp(this.targetX, this.targetY) : corners.find(p => !this.isCorridorBlocked(this.x, this.y, p.x, p.y, context.cursorX, context.cursorY)) || corners[0];
      if (this.isCorridorBlocked(this.x, this.y, anchor.x, anchor.y, context.cursorX, context.cursorY)) this._hold(STATES.WAIT, 'NEW_ANCHOR_ROUTE_BLOCKED');
      else this.assignRoute(STATES.SELECT_ANCHOR, anchor.x, anchor.y, 150, { webId: null });
    } else if (context.idleTime >= this.wanderThreshold) {
      if (this.state !== STATES.WANDER || distance(this.x, this.y, this.targetX, this.targetY) < 5) {
        const generator = context.randomFloat || Math.random;
        const sample = () => { let n; try { n = generator(); } catch (_) { n = NaN; } return typeof n === 'number' && Number.isFinite(n) ? Math.max(0, Math.min(1, n)) : 0.5; };
        const target = this._clamp(context.rectangle.originX + sample() * context.rectangle.width, context.rectangle.originY + sample() * context.rectangle.height);
        if (this.isCorridorBlocked(this.x, this.y, target.x, target.y, context.cursorX, context.cursorY)) this._hold(STATES.WAIT, 'WANDER_ROUTE_BLOCKED');
        else this.assignRoute(STATES.WANDER, target.x, target.y, 100);
      }
    } else this._hold(STATES.ACTIVE_EVASION, 'ACTIVITY_BELOW_WANDER_THRESHOLD');

    const target = this._clamp(this.targetX, this.targetY);
    if (distance(target.x, target.y, this.targetX, this.targetY) > EPS && this._route && this._route.active) {
      this.assignRoute(this.state, target.x, target.y, this.speed, { finalX: this._route.finalX, finalY: this._route.finalY });
    }
    this.targetX = target.x; this.targetY = target.y;
    if (this.speed > 0 && this.state !== STATES.ESCAPE && !this._routeSafe(context)) this._hold(STATES.WAIT, 'EXECUTION_ROUTE_NOT_SAFE');
    if (this.speed > 0 && (!this._route || !this._route.active || this._route.state !== this.state ||
      distance(this._route.targetX, this._route.targetY, this.targetX, this.targetY) > EPS)) {
      const final = this.state === STATES.DETOUR && this.currentWeb ? { finalX: this.currentWeb.anchorX, finalY: this.currentWeb.anchorY } : {};
      this.assignRoute(this.state, this.targetX, this.targetY, this.speed, final);
    }
    if (this.speed > 0 && !this._approveCurrentRoute(context)) this._hold(STATES.WAIT, 'EXECUTION_APPROVAL_DENIED');
    const movementState = this.state, movementRouteId = this.currentRouteId, movementDecisionId = this.currentDecisionId;
    const intendedTarget = { x: this.targetX, y: this.targetY };
    const remaining = distance(this.x, this.y, this.targetX, this.targetY);
    if (this.speed > 0 && remaining > EPS) {
      const travel = Math.min(remaining, this.speed * context.deltaTime);
      this.x += ((this.targetX - this.x) / remaining) * travel;
      this.y += ((this.targetY - this.y) / remaining) * travel;
      if (travel >= remaining - EPS) { this.x = this.targetX; this.y = this.targetY; }
    }
    const distanceMoved = distance(startX, startY, this.x, this.y);
    const postThreatDistance = distance(this.x, this.y, context.cursorX, context.cursorY);
    const safe = postThreatDistance >= this.threatRadius;
    const movementEvidence = this._evidence({ from: { x: startX, y: startY }, to: { x: this.x, y: this.y }, distanceMoved,
      intendedTarget, preThreatDistance, postThreatDistance });
    if (distanceMoved > EPS) this._log('MOVE_CONFIRMED', { routeId: movementRouteId, decisionId: movementDecisionId, reason: 'MEASURED_DISPLACEMENT', evidence: movementEvidence });
    if (safe && pointerWithdrew && distanceMoved <= EPS) this._log('POINTER_WITHDREW', { reason: 'SAFE_WITHOUT_SPIDER_DISPLACEMENT', evidence: movementEvidence });
    else if (safe && threatened && distanceMoved > EPS) this._log('THREAT_CLEARED', { reason: 'MEASURED_MOVEMENT_CLEARED_THREAT', evidence: movementEvidence });

    // Verification measures the actual destination, independently of permission.
    const arrived = distance(this.x, this.y, this.targetX, this.targetY) <= EPS;
    if (movementState === STATES.ESCAPE && safe && arrived) { this._completeRoute('ESCAPE_TARGET_REACHED_AND_SAFE'); this.state = STATES.ACTIVE_EVASION; }
    else if (movementState === STATES.ESCAPE && !safe && arrived) this.setEscapeTarget(context.cursorX, context.cursorY, context.bounds);
    if (movementState === STATES.DETOUR && arrived && safe && this.currentWeb) {
      this._log('WAYPOINT_CONFIRMED', { routeId: movementRouteId, decisionId: movementDecisionId, reason: 'WAYPOINT_IS_NOT_HOME', evidence: movementEvidence });
      this._completeRoute('MEASURED_WAYPOINT_REACHED');
      this._planReturn(context);
    }
    if (movementState === STATES.SELECT_ANCHOR && arrived && safe) {
      this.currentWeb = { anchorX: this.x, anchorY: this.y, progress: 0, interrupted: false };
      this._webSignature = `${this._webId()}:${this.currentWeb.anchorX}:${this.currentWeb.anchorY}`;
      this._deferredKey = null; this._permissionDecisionId = null;
      this._homeLineage = movementRouteId ? { routeId: movementRouteId, decisionId: movementDecisionId } : null;
      this._completeRoute('NEW_WEB_ANCHOR_REACHED');
    }
    const atAnchor = this._atAnchor();
    if (atAnchor && safe && (movementState === STATES.RECOVER || originalState === STATES.RECOVER)) {
      const key = `${this._webId()}:${this.currentWeb.anchorX}:${this.currentWeb.anchorY}`;
      if (key !== this._homeKey) {
        const arrivalKind = distanceMoved > EPS ? 'PHYSICAL_ARRIVAL' : 'ALREADY_AT_ANCHOR';
        this._log('RETURN_CONFIRMED', { routeId: movementRouteId, decisionId: movementDecisionId, reason: arrivalKind,
          evidence: { ...movementEvidence, arrivalKind } });
        this._homeKey = key;
      }
      if (movementRouteId) this._homeLineage = { routeId: movementRouteId, decisionId: movementDecisionId };
      this._completeRoute('MEASURED_WEB_ANCHOR_REACHED');
    }
    const seeksPermission = this.currentWeb && atAnchor && (context.idleTime >= this.weaveThreshold || this.currentWeb.interrupted ||
      [STATES.WAIT_AT_ANCHOR, STATES.RECOVER, STATES.WEAVE, STATES.SELECT_ANCHOR].includes(this.state) || movementState === STATES.RECOVER || movementState === STATES.SELECT_ANCHOR);
    if (seeksPermission && this.state !== STATES.ESCAPE) {
      if (this.authorizeWeave(context, this.currentWeb) && this.canWeave(context, this.currentWeb) && context.deltaTime > 0) {
        const previousProgress = this.currentWeb.progress;
        this.currentWeb.progress += context.deltaTime;
        if (this.elapsedTime - this._lastProgressLog >= 1) {
          this._log('WEAVE_PROGRESS_CONFIRMED', { routeId: this.authorization.routeId, decisionId: this.authorization.decisionId, reason: 'AUTHORIZED_CONSTRUCTION_EFFECT',
            evidence: this._evidence({ previousProgress, progress: this.currentWeb.progress, progressAdded: context.deltaTime }) });
          this._lastProgressLog = this.elapsedTime;
        }
      }
    }
    this.wasThreatened = !safe;
    this._previousCursor = { x: context.cursorX, y: context.cursorY };
    this._loggingTick = false;
    return { x: this.x, y: this.y, state: this.state, web: this.currentWeb, receipts: this.receipts.slice(),
      routeId: this.currentRouteId, decisionId: this.currentDecisionId, distanceMoved, boundsDistanceMoved, preThreatDistance, postThreatDistance,
      authorization: this.authorization, intent: { state: movementState, from: { x: startX, y: startY }, to: intendedTarget },
      verification: { safe, atAnchor: this._atAnchor(), canWeave: this.canWeave(context, this.currentWeb) } };
  }
}

SpiderHarness.STATES = STATES;
if (typeof module !== 'undefined' && module.exports) module.exports = SpiderHarness;
else if (typeof window !== 'undefined') window.SpiderHarness = SpiderHarness;
