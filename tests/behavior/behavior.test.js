const test = require('node:test');
const assert = require('node:assert');
const SpiderHarness = require('../../src/behavior/behavior.js');

const bounds = { width: 1920, height: 1080 };
const createEnv = (overrides) => ({
  deltaTime: 0.1, idleTime: 0, cursorX: -1000, cursorY: -1000, bounds, randomFloat: () => 0.5, timestamp: 1700000000000, ...overrides
});

function injectWeb(harness, x, y) {
  harness.currentWeb = { anchorX: x, anchorY: y, progress: 10, interrupted: true };
}

test('1. Simulation Hardening: Negative deltaTime clamped', () => {
  const harness = new SpiderHarness({ startX: 100, startY: 100 });
  harness.assignRoute('ESCAPE', 500, 500, 100);

  // Inject negative time; spider should not move backward
  harness.evaluateTick(createEnv({ deltaTime: -1.5 }));

  assert.strictEqual(harness.x, 100);
  assert.strictEqual(harness.y, 100);
});

test('2. Decision Evidence: DETOUR_SELECTED and MOVE_CONFIRMED share routeId', () => {
  const harness = new SpiderHarness({ startX: 800, startY: 800 });
  injectWeb(harness, 100, 100);

  const result = harness.evaluateTick(createEnv({ idleTime: 300, cursorX: 450, cursorY: 450, deltaTime: 0.5 }));

  const detourReceipt = harness.ledger.find(r => r.outcome === 'DETOUR_SELECTED');
  const moveReceipt = harness.ledger.find(r => r.outcome === 'MOVE_CONFIRMED');

  assert.ok(detourReceipt.routeId.includes('DETOUR'), 'Decision missing stable ID');
  assert.strictEqual(detourReceipt.routeId, moveReceipt.routeId, 'Movement executed without linked authorization');
});

test('3. Governance Boundary: WEAVE_DEFERRED upon arriving home during active session', () => {
  const harness = new SpiderHarness({ startX: 100, startY: 100 });
  injectWeb(harness, 100, 100);
  harness.state = 'RECOVER';

  // Arrives home, but user is active (idleTime 50 < 300 required)
  const result = harness.evaluateTick(createEnv({ idleTime: 50, deltaTime: 1.0 }));

  assert.ok(result.receipts.includes('RETURN_CONFIRMED'), 'Physical arrival receipt missing');
  assert.ok(result.receipts.includes('WEAVE_DEFERRED'), 'Governance did not explicitly defer weaving');
  assert.strictEqual(harness.state, 'WAIT_AT_ANCHOR', 'Spider bypassed authorization');
});

test('4. Governance Boundary: WEAVE_AUTHORIZED triggers when idle', () => {
  const harness = new SpiderHarness({ startX: 100, startY: 100 });
  injectWeb(harness, 100, 100);
  harness.state = 'WAIT_AT_ANCHOR';

  // Spider is waiting, computer eventually hits 300s idle time
  const result = harness.evaluateTick(createEnv({ idleTime: 300, deltaTime: 0.1 }));

  assert.ok(result.receipts.includes('WEAVE_AUTHORIZED'), 'Missing governance authorization receipt');
  assert.strictEqual(harness.state, 'WEAVE', 'Spider failed to transition to weaving');
});
