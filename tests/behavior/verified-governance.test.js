'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const SpiderHarness = require('../../src/behavior/behavior.js');

const bounds = { width: 1920, height: 1080 };
const env = (overrides = {}) => ({ deltaTime: 0.1, idleTime: 0,
  cursorX: -1000, cursorY: -1000, bounds, randomFloat: () => 0.5,
  timestamp: 0, ...overrides });
const web = (anchorX = 100, anchorY = 100, interrupted = true, progress = 10) =>
  ({ anchorX, anchorY, interrupted, progress });
const event = receipt => receipt.event || receipt.outcome;
const at = (s, x, y) => Math.hypot(s.x - x, s.y - y) < 1e-6;
const moved = (from, to) => Math.hypot(to.x - from.x, to.y - from.y);
function segmentDistance(a, b, p) {
  const dx = b.x - a.x, dy = b.y - a.y, length = dx * dx + dy * dy;
  const t = length === 0 ? 0 : Math.max(0, Math.min(1,
    ((p.x - a.x) * dx + (p.y - a.y) * dy) / length));
  return Math.hypot(p.x - a.x - t * dx, p.y - a.y - t * dy);
}

test('V11: Measured away-to-home arrival precedes permission and has positive displacement evidence', () => {
  const s = new SpiderHarness({ startX: 400, startY: 100 });
  s.currentWeb = web();
  let arrived = null;
  for (let i = 0; i < 30; i++) {
    const from = { x: s.x, y: s.y };
    const r = s.evaluateTick(env({ idleTime: 40, timestamp: i * 100 }));
    if (!at(s, 100, 100)) {
      assert.ok(!r.receipts.includes('RETURN_CONFIRMED'));
      assert.ok(!r.receipts.includes('WEAVE_AUTHORIZED'));
    }
    if (r.receipts.includes('RETURN_CONFIRMED')) {
      assert.ok(at(s, 100, 100));
      assert.ok(moved(from, s) > 0, 'this arrival must contain real movement');
      arrived = s.ledger.findLast(row => event(row) === 'RETURN_CONFIRMED');
      assert.ok(arrived.evidence.distanceMoved > 0);
      assert.ok(arrived.evidence.distanceToAnchor <= 1e-6);
    }
    assert.equal(s.currentWeb.progress, 10, 'active session must not construct silk');
  }
  assert.ok(arrived, 'bounded physical return actually completes');
  assert.equal(s.state, 'WAIT_AT_ANCHOR');
  assert.equal(s.ledger.filter(row => event(row) === 'RETURN_CONFIRMED').length, 1);
});

test('V11: RECOVER or zero speed alone cannot confirm an arrival away from the actual anchor', () => {
  const s = new SpiderHarness({ startX: 600, startY: 600 });
  s.currentWeb = web(); s.state = 'RECOVER'; s.speed = 0;
  s.targetX = s.x; s.targetY = s.y;
  const r = s.evaluateTick(env({ idleTime: 300, deltaTime: 0 }));
  assert.ok(!r.receipts.includes('RETURN_CONFIRMED'));
  assert.ok(!r.receipts.includes('WEAVE_AUTHORIZED'));
  assert.equal(s.currentWeb.progress, 10);
});

test('V11: Already-at-anchor compatibility receipt admits zero movement and does not repeat', () => {
  const s = new SpiderHarness({ startX: 100, startY: 100 });
  s.currentWeb = web(); s.state = 'RECOVER'; s.speed = 0;
  for (let i = 0; i < 20; i++) {
    const r = s.evaluateTick(env({ idleTime: 40, timestamp: i * 100 }));
    assert.ok(!r.receipts.includes('MOVE_CONFIRMED'));
    assert.ok(at(s, 100, 100));
  }
  const confirmations = s.ledger.filter(row => event(row) === 'RETURN_CONFIRMED');
  assert.equal(confirmations.length, 1);
  assert.equal(confirmations[0].evidence.distanceMoved, 0);
  assert.equal(confirmations[0].evidence.distanceToAnchor, 0);
  assert.equal(confirmations[0].evidence.arrivalKind, 'ALREADY_AT_ANCHOR');
});

for (const path of ['first-web', 'resumed-web', 'waiting-at-anchor', 'physical-return', 'ongoing-effect']) {
  test(`V11: Central canWeave denial governs ${path}`, () => {
    const first = path === 'first-web';
    const s = new SpiderHarness({ startX: first ? 1820 : path === 'physical-return' ? 120 : 100, startY: 100 });
    if (!first) s.currentWeb = web(100, 100, path !== 'resumed-web' && path !== 'ongoing-effect');
    if (path === 'waiting-at-anchor') s.state = 'WAIT_AT_ANCHOR';
    if (path === 'physical-return') s.state = 'RECOVER';
    if (path === 'ongoing-effect') {
      s.currentWeb.interrupted = false;
      s.evaluateTick(env({ idleTime: 300 }));
      assert.equal(s.state, 'WEAVE');
    }
    const progress = s.currentWeb ? s.currentWeb.progress : 0;
    let calls = 0;
    s.canWeave = () => { calls++; return false; };
    const r = s.evaluateTick(env({ idleTime: 300, timestamp: 100 }));
    assert.ok(calls > 0, 'authorization and construction must use the central guard');
    assert.notEqual(r.state, 'WEAVE');
    assert.ok(!r.receipts.includes('WEAVE_AUTHORIZED'));
    if (s.currentWeb) assert.equal(s.currentWeb.progress, progress);
  });
}

test('V11: User activity revokes an earlier weaving authorization before the construction effect', () => {
  const s = new SpiderHarness({ startX: 100, startY: 100 });
  s.currentWeb = web(100, 100, false);
  s.evaluateTick(env({ idleTime: 300 }));
  assert.equal(s.state, 'WEAVE');
  const progress = s.currentWeb.progress;
  const r = s.evaluateTick(env({ idleTime: 40, timestamp: 100 }));
  assert.notEqual(r.state, 'WEAVE');
  assert.ok(!r.receipts.includes('WEAVE_AUTHORIZED'));
  assert.equal(s.currentWeb.progress, progress);
});

test('V11: Pointer entry revokes weaving even if idle time remains sufficient', () => {
  const s = new SpiderHarness({ startX: 100, startY: 100 });
  s.currentWeb = web(100, 100, false);
  s.evaluateTick(env({ idleTime: 300 }));
  const progress = s.currentWeb.progress;
  const r = s.evaluateTick(env({ idleTime: 300, cursorX: 100, cursorY: 100, timestamp: 100 }));
  assert.notEqual(r.state, 'WEAVE');
  assert.ok(!r.receipts.includes('WEAVE_AUTHORIZED'));
  assert.equal(s.currentWeb.progress, progress);
  assert.equal(s.currentWeb.interrupted, true);
});

test('V11: Web replacement at another anchor cannot inherit a previous permission', () => {
  const s = new SpiderHarness({ startX: 100, startY: 100 });
  s.currentWeb = web(100, 100, false);
  s.evaluateTick(env({ idleTime: 300 }));
  s.currentWeb = web(600, 600, false, 25);
  const r = s.evaluateTick(env({ idleTime: 300, timestamp: 100 }));
  assert.notEqual(r.state, 'WEAVE');
  assert.ok(!r.receipts.includes('WEAVE_AUTHORIZED'));
  assert.equal(s.currentWeb.progress, 25);
});

test('V11: Same-location replacement requires a fresh web-scoped authorization', () => {
  const s = new SpiderHarness({ startX: 100, startY: 100 });
  s.currentWeb = web(100, 100, false);
  s.evaluateTick(env({ idleTime: 300 }));
  const first = s.ledger.findLast(row => event(row) === 'WEAVE_AUTHORIZED');
  assert.ok(first);
  s.currentWeb = web(100, 100, false, 25);
  const r = s.evaluateTick(env({ idleTime: 300, timestamp: 100 }));
  assert.ok(r.receipts.includes('WEAVE_AUTHORIZED'));
  const second = s.ledger.findLast(row => event(row) === 'WEAVE_AUTHORIZED');
  assert.ok(first.evidence.webId && second.evidence.webId);
  assert.notEqual(second.evidence.webId, first.evidence.webId);
});

test('V11: Unchanged traveling route keeps route and decision identities across many updates', () => {
  const s = new SpiderHarness({ startX: 800, startY: 800 });
  s.currentWeb = web();
  s.evaluateTick(env({ idleTime: 300 }));
  const routeId = s.currentRouteId, decisionId = s.currentDecisionId;
  assert.ok(routeId && decisionId);
  for (let i = 0; i < 20; i++) {
    s.evaluateTick(env({ idleTime: 300, timestamp: (i + 1) * 100 }));
    assert.equal(s.currentRouteId, routeId);
    assert.equal(s.currentDecisionId, decisionId);
  }
  assert.equal(s.ledger.filter(row => event(row) === 'ROUTE_APPROVED' && row.routeId === routeId).length, 1);
});

test('V11: Genuine replanning creates new identities while equivalent assignment retains them', () => {
  const s = new SpiderHarness({ startX: 800, startY: 800 });
  s.currentWeb = web();
  s.assignRoute('RECOVER', 100, 100, 200);
  const firstRoute = s.currentRouteId, firstDecision = s.currentDecisionId;
  s.assignRoute('RECOVER', 100, 100, 200);
  assert.equal(s.currentRouteId, firstRoute);
  assert.equal(s.currentDecisionId, firstDecision);
  s.assignRoute('RECOVER', 120, 100, 200);
  assert.notEqual(s.currentRouteId, firstRoute);
  assert.notEqual(s.currentDecisionId, firstDecision);
});

test('V11: Every recorded movement links measured displacement to an earlier approved decision', () => {
  const s = new SpiderHarness({ startX: 800, startY: 800 });
  s.currentWeb = web();
  for (let i = 0; i < 12; i++) s.evaluateTick(env({ idleTime: 300,
    cursorX: 450, cursorY: 450, timestamp: i * 100 }));
  const moves = s.ledger.filter(row => event(row) === 'MOVE_CONFIRMED');
  assert.ok(moves.length > 0);
  for (const receipt of moves) {
    assert.ok(receipt.routeId && receipt.decisionId);
    const decision = s.ledger.find(row => event(row) === 'ROUTE_APPROVED' &&
      row.routeId === receipt.routeId && row.decisionId === receipt.decisionId && row.sequence < receipt.sequence);
    assert.ok(decision, 'an evaluated or rejected candidate alone cannot authorize movement');
    assert.ok(receipt.evidence.distanceMoved > 0);
    assert.ok(Math.abs(moved(receipt.evidence.from, receipt.evidence.to) - receipt.evidence.distanceMoved) < 1e-6);
  }
});

test('V11: Fully blocked WAIT has zero execution speed and no residual displacement, then resumes when clear', () => {
  const s = new SpiderHarness({ startX: 60, startY: 40 });
  s.currentWeb = web(340, 360);
  s.assignRoute('DETOUR', 340, 40, 150);
  const start = { x: s.x, y: s.y };
  const r = s.evaluateTick(env({ bounds: { width: 400, height: 400 },
    cursorX: 200, cursorY: 200, idleTime: 300 }));
  assert.equal(r.state, 'WAIT');
  assert.equal(s.speed, 0);
  assert.equal(moved(start, s), 0);
  assert.ok(!r.receipts.includes('MOVE_CONFIRMED'));
  let returned = false;
  for (let i = 0; i < 100; i++) {
    const next = s.evaluateTick(env({ bounds: { width: 400, height: 400 },
      idleTime: 300, timestamp: (i + 1) * 100 }));
    returned ||= next.receipts.includes('RETURN_CONFIRMED');
  }
  assert.ok(returned && at(s, 340, 360));
});

test('V11: Invalid detour cannot execute stale waypoint when a different direct route is safe', () => {
  const s = new SpiderHarness({ startX: 800, startY: 800 });
  s.currentWeb = web(800, 100);
  s.assignRoute('DETOUR', 100, 100, 150);
  const oldRoute = s.currentRouteId;
  const from = { x: s.x, y: s.y }, pointer = { x: 450, y: 450 };
  const r = s.evaluateTick(env({ idleTime: 300, cursorX: pointer.x, cursorY: pointer.y }));
  assert.equal(s.x, 800, 'a safe direct route is vertical; old diagonal residual speed must not execute');
  if (r.state === 'WAIT') assert.equal(moved(from, s), 0);
  else {
    assert.notEqual(s.currentRouteId, oldRoute);
    assert.ok(segmentDistance(from, s, pointer) >= s.threatRadius);
  }
});

test('V11: Timestamp zero and repeated injected inputs reproduce identical receipt evidence', () => {
  function simulate() {
    const s = new SpiderHarness({ startX: 100, startY: 100 });
    s.currentWeb = web(); s.state = 'WAIT_AT_ANCHOR';
    for (const timestamp of [0, 100, 200, 300]) s.evaluateTick(env({ idleTime: 40, timestamp }));
    return s.ledger;
  }
  const first = simulate(), second = simulate();
  assert.deepEqual(first, second);
  assert.ok(first.some(row => row.timestamp === 0));
});

test('V11: Unchanged waiting does not bury decisions in duplicate deferrals', () => {
  const s = new SpiderHarness({ startX: 100, startY: 100 });
  s.currentWeb = web(); s.state = 'WAIT_AT_ANCHOR';
  for (let i = 0; i < 500; i++) s.evaluateTick(env({ idleTime: 40, timestamp: i * 100 }));
  assert.equal(s.ledger.filter(row => event(row) === 'WEAVE_DEFERRED').length, 1);
  assert.ok(s.ledger.length < 20, 'unchanged waiting should retain consequential history');
});

test('V11: Bounded ledger retains strictly ordered global sequence numbers after eviction', () => {
  const s = new SpiderHarness({ startX: 100, startY: 100 });
  for (let i = 0; i < 150; i++) {
    s.assignRoute('ESCAPE', 500 + i, 500, 100);
    s.evaluateTick(env({ timestamp: i * 100, deltaTime: 0.01 }));
  }
  assert.ok(s.sequenceCounter > 200, 'the scenario must actually exceed the history capacity');
  assert.ok(s.ledger.length <= 200);
  assert.ok(s.ledger[0].sequence > 1, 'old entries were evicted rather than renumbered');
  for (let i = 1; i < s.ledger.length; i++) assert.ok(s.ledger[i].sequence > s.ledger[i - 1].sequence);
  assert.equal(s.ledger.at(-1).sequence, s.sequenceCounter);
});

test('V11: Stored evidence is an immutable snapshot rather than a live web or coordinate reference', () => {
  const s = new SpiderHarness({ startX: 120, startY: 100 });
  s.currentWeb = web();
  s.evaluateTick(env({ idleTime: 40 }));
  const receipt = s.ledger.find(row => event(row) === 'RETURN_CONFIRMED');
  assert.ok(receipt && receipt.evidence);
  const snapshot = structuredClone(receipt);
  s.currentWeb.anchorX = 700; s.currentWeb.progress = 99;
  s.x = 500; s.y = 500;
  assert.deepEqual(receipt, snapshot);
  assert.ok(Object.isFrozen(receipt) && Object.isFrozen(receipt.evidence));
  assert.throws(() => { receipt.evidence.distanceMoved = 999; }, TypeError);
  if (receipt.evidence.anchor) assert.ok(Object.isFrozen(receipt.evidence.anchor));
});

test('V11: Nonfinite environmental coordinates and bounds reject before physical mutation', () => {
  for (const overrides of [{ cursorX: NaN }, { cursorY: Infinity }, { idleTime: Infinity },
    { bounds: { width: 0, height: 1080 } }, { bounds: { width: 1920, height: NaN } }]) {
    const s = new SpiderHarness({ startX: 500, startY: 500 });
    const original = { x: s.x, y: s.y };
    assert.throws(() => s.evaluateTick(env(overrides)), /finite|positive|bounds|dimension|cursor|idle/i);
    assert.deepEqual({ x: s.x, y: s.y }, original);
  }
});

test('V11: Invalid random outputs normalize without creating nonfinite effects', () => {
  for (const value of [NaN, Infinity, -Infinity, undefined, 'invalid', -5, 5]) {
    const s = new SpiderHarness({ startX: 500, startY: 500 });
    s.evaluateTick(env({ idleTime: 120, randomFloat: () => value }));
    for (const coordinate of [s.x, s.y, s.targetX, s.targetY]) assert.ok(Number.isFinite(coordinate));
    assert.ok(s.x >= 0 && s.x <= bounds.width && s.y >= 0 && s.y <= bounds.height);
  }
});

test('V11: Display relocation cannot impersonate movement on an unapproved route', () => {
  const s = new SpiderHarness({ startX: 500, startY: 500 });
  s.evaluateTick(env({ bounds: { left: 1000, top: 1000, width: 500, height: 500 }, deltaTime: 0 }));
  assert.ok(s.x >= 1000 && s.y >= 1000, 'display correction keeps the spider visible');
  for (const receipt of s.ledger.filter(row => event(row) === 'MOVE_CONFIRMED')) {
    assert.ok(receipt.routeId && receipt.decisionId, 'ordinary movement needs actual plan lineage');
    const approval = s.ledger.find(row => event(row) === 'ROUTE_APPROVED' && row.sequence < receipt.sequence &&
      row.routeId === receipt.routeId && row.decisionId === receipt.decisionId);
    assert.ok(approval, 'mandatory display relocation must have its own truthful classification or approved placement plan');
  }
});

test('V11: Display relocation cannot claim the stationary pointer withdrew', () => {
  const s = new SpiderHarness({ startX: 500, startY: 500 });
  s.evaluateTick(env({ cursorX: 500, cursorY: 500, deltaTime: 0 }));
  const previousSequence = s.sequenceCounter;
  const r = s.evaluateTick(env({ cursorX: 500, cursorY: 500, deltaTime: 0, timestamp: 100,
    bounds: { left: 1000, top: 1000, width: 500, height: 500 } }));
  assert.ok(!r.receipts.includes('POINTER_WITHDREW'));
  assert.ok(!s.ledger.some(row => row.sequence > previousSequence && row.reason === 'POINTER_WITHDREW'),
    'cancellation reasons must also distinguish pointer withdrawal from display placement');
});

test('V11: An externally selected plan remains unapproved until current execution geometry is checked', () => {
  const s = new SpiderHarness({ startX: 800, startY: 800 });
  s.currentWeb = web();
  const candidate = s.assignRoute('RECOVER', 100, 100, 200);
  assert.ok(!s.ledger.some(row => event(row) === 'ROUTE_APPROVED' && row.routeId === candidate),
    'a route assignment without current environment evidence expresses intent only');
  const from = { x: s.x, y: s.y };
  s.evaluateTick(env({ idleTime: 300, cursorX: 450, cursorY: 450 }));
  assert.equal(moved(from, s), 0, 'newly observed obstruction prevents execution');
  assert.ok(!s.ledger.some(row => event(row) === 'MOVE_CONFIRMED' && row.routeId === candidate));
  assert.ok(!s.ledger.some(row => event(row) === 'ROUTE_APPROVED' && row.routeId === candidate));
});

test('V11: Mutating a public target cannot execute a different destination under stale approval', () => {
  const s = new SpiderHarness({ startX: 800, startY: 800 });
  s.currentWeb = web();
  s.evaluateTick(env({ idleTime: 300 }));
  const oldRoute = s.currentRouteId;
  const oldApproval = s.ledger.find(row => event(row) === 'ROUTE_APPROVED' && row.routeId === oldRoute);
  assert.ok(oldApproval);
  const previousSequence = s.sequenceCounter;
  s.targetX = 700; s.targetY = 100;
  s.evaluateTick(env({ idleTime: 300, timestamp: 100 }));
  for (const receipt of s.ledger.filter(row => event(row) === 'MOVE_CONFIRMED' && row.sequence > previousSequence)) {
    if (receipt.routeId === oldRoute) assert.deepEqual(receipt.evidence.intendedTarget, oldApproval.evidence.to,
      'the old approval cannot be reused for a changed destination');
    else assert.ok(s.ledger.some(row => event(row) === 'ROUTE_APPROVED' && row.sequence < receipt.sequence &&
      row.routeId === receipt.routeId && row.decisionId === receipt.decisionId));
  }
});
