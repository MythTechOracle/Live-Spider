'use strict';
const test = require('node:test');
const assert = require('node:assert/strict');
const SpiderHarness = require('../../src/behavior/behavior.js');
const bounds = { width: 1920, height: 1080 };
const env = (o={}) => ({ deltaTime:0.1, idleTime:0, cursorX:-1000, cursorY:-1000, bounds, randomFloat:()=>0.5, timestamp:1700000000000, ...o });
const web = (anchorX=100,anchorY=100,interrupted=true) => ({anchorX,anchorY,progress:10,interrupted});

test('A1: Recovery route ID remains stable while traveling the same route', () => {
  const s=new SpiderHarness({startX:800,startY:800});
  s.currentWeb=web();
  s.evaluateTick(env({ idleTime:300 }));
  const assigned=s.currentRouteId;
  assert.match(assigned, /RECOVER/);
  s.evaluateTick(env({ idleTime:300 }));
  assert.equal(s.currentRouteId, assigned, 'Route ID changed despite no replanning, threatening traceability');
});

test('A2: WAIT_AT_ANCHOR cannot authorize weaving when physically away from anchor', () => {
  const s=new SpiderHarness({startX:600,startY:600});
  s.currentWeb=web(); s.state='WAIT_AT_ANCHOR'; s.speed=0;
  const r=s.evaluateTick(env({idleTime:300}));
  assert.notEqual(r.state,'WEAVE','Weaving authorized away from the web anchor');
  assert.ok(!r.receipts.includes('WEAVE_AUTHORIZED'));
});

test('A3: Existing web does not silently resume weaving away from its anchor', () => {
  const s=new SpiderHarness({startX:800,startY:800});
  s.currentWeb=web(100,100,false); s.state='ACTIVE_EVASION';
  const r=s.evaluateTick(env({idleTime:300}));
  assert.notEqual(r.state,'WEAVE', 'Web construction was resumed at a position far from its anchor');
  assert.equal(s.currentWeb.progress,10);
});

test('A4: Starting a new web emits authorization receipt before weaving', () => {
  const s=new SpiderHarness({startX:1820,startY:100});
  const r=s.evaluateTick(env({idleTime:300}));
  assert.equal(r.state,'WEAVE');
  assert.ok(r.receipts.includes('WEAVE_AUTHORIZED'),'New web bypassed authorization receipt');
});

test('A5: Invalid random coordinate generator cannot poison the spider position', () => {
  const s=new SpiderHarness({startX:500,startY:500});
  s.evaluateTick(env({idleTime:120,randomFloat:()=>Number.NaN}));
  assert.ok(Number.isFinite(s.x) && Number.isFinite(s.y),'Nonfinite positions propagated from randomFloat');
  assert.ok(Number.isFinite(s.targetX) && Number.isFinite(s.targetY));
});

test('A6: Injected timestamp 0 is honored rather than replaced by wall clock', () => {
  const s=new SpiderHarness({startX:100,startY:100});
  s.currentWeb=web(); s.state='WAIT_AT_ANCHOR';s.speed=0;
  s.evaluateTick(env({idleTime:50,timestamp:0}));
  assert.equal(s.ledger.at(-1).timestamp,0);
});

test('A7: Unchanged WAIT_AT_ANCHOR state does not flood duplicate deferral receipts', () => {
  const s=new SpiderHarness({startX:100,startY:100});
  s.currentWeb=web();s.state='WAIT_AT_ANCHOR';s.speed=0;
  for (let i=0;i<5;i++) s.evaluateTick(env({idleTime:50,timestamp:1000+i*100}));
  const receipts=s.ledger.filter(r=>r.outcome==='WEAVE_DEFERRED');
  assert.equal(receipts.length,1,'Unchanged refusal was logged every tick and will evict audit history');
});
