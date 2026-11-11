import test from 'node:test';
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { randomUUID } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import { selectWeekly } from '../weekly-model.mjs';

const now = 1800000000;
const week = (usedPercent = 25, resetsAt = now + 3600) => ({ usedPercent, resetsAt, windowDurationMins: 10080 });
const bucket = value => ({ secondary: value });
const fallback = value => ({ rateLimits: bucket(value) });
const unavailable = value => {
  assert.equal(value.available, false);
  assert.equal(value.remainingPercent, null);
  assert.equal(value.resetsAt, null);
  assert.equal(typeof value.error, 'string');
  assert.ok(value.error.length > 0);
};

test('reads a default weekly window and returns only quota fields', () => {
  assert.deepEqual(selectWeekly(fallback(week()), now), {
    available: true, remainingPercent: 75, resetsAt: now + 3600, error: null
  });
});

for (const [used, remaining] of [[0, 100], [100, 0], [62.5, 37.5]]) {
  test(`preserves ${remaining}% remaining`, () => assert.equal(selectWeekly(fallback(week(used)), now).remainingPercent, remaining));
}

test('prefers the explicit codex bucket to the default and other models', () => {
  const raw = { rateLimits: bucket(week(90)), rateLimitsByLimitId: { other: bucket(week(10)), codex: bucket(week(40)) } };
  assert.equal(selectWeekly(raw, now).remainingPercent, 60);
});

test('recognizes a codex limitId even when the map key differs', () => {
  const raw = { rateLimitsByLimitId: { alias: { limitId: 'codex', ...bucket(week(45)) } } };
  assert.equal(selectWeekly(raw, now).remainingPercent, 55);
});

test('does not replace a missing codex week with a different default', () => {
  unavailable(selectWeekly({ rateLimits: bucket(week()), rateLimitsByLimitId: { codex: { primary: { ...week(), windowDurationMins: 300 } } } }, now));
});

test('does not mistake a five-hour window for the week', () => {
  unavailable(selectWeekly(fallback({ ...week(), windowDurationMins: 300 }), now));
});

test('accepts the weekly window in the primary position', () => {
  assert.equal(selectWeekly({ rateLimits: { primary: week(35) } }, now).remainingPercent, 65);
});

for (const value of [undefined, null, {}, { usedPercent: 25, windowDurationMins: 10080 }, { resetsAt: now + 1, windowDurationMins: 10080 }, week(-1), week(101), week(NaN), week(Infinity), week('25'), week(25, NaN), week(25, 0), week(25, '1800000001')]) {
  test(`rejects missing or invalid quota ${JSON.stringify(value)}`, () => unavailable(selectWeekly(fallback(value), now)));
}

for (const resetsAt of [now - 1, now]) {
  test(`waits for fresh data once reset is reached (${resetsAt})`, () => {
    const value = selectWeekly(fallback(week(0, resetsAt)), now);
    unavailable(value);
    assert.match(value.error, /刷新/);
  });
}

test('rejects unrelated model buckets instead of picking a weekly value', () => {
  unavailable(selectWeekly({ rateLimitsByLimitId: { a: bucket(week(10)), b: bucket(week(20)) } }, now));
  unavailable(selectWeekly({ rateLimits: { limitId: 'other-model', ...bucket(week(10)) } }, now));
});

test('returns a safe unavailable state for a missing response', () => unavailable(selectWeekly(null, now)));

test('reader once emits one safe JSON line when Codex is absent, even with closed stdin', () => {
  const result = spawnSync(process.execPath, [fileURLToPath(new URL('../weekly-reader.mjs', import.meta.url)), '--once'], {
    env: { ...process.env, LOCALAPPDATA: join(tmpdir(), randomUUID()) }, encoding: 'utf8', timeout: 5000
  });
  assert.equal(result.status, 0);
  assert.equal(result.stderr, '');
  assert.equal(result.stdout.trim().split('\n').length, 1);
  const state = JSON.parse(result.stdout);
  unavailable(state);
  assert.equal(Number.isFinite(Date.parse(state.updatedAt)), true);
  assert.deepEqual(Object.keys(state).sort(), ['available', 'error', 'remainingPercent', 'resetsAt', 'updatedAt']);
});
