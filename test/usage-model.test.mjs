import test from 'node:test';
import assert from 'node:assert/strict';
import {normalizeLimits} from '../usage-model.mjs';

test('uses all returned buckets and converts consumed percentage to remaining', () => {
  const data = normalizeLimits({
    rateLimits: {limitId:'legacy',primary:{usedPercent:50}},
    rateLimitsByLimitId: {
      codex:{limitId:'codex',primary:{usedPercent:97,windowDurationMins:10080,resetsAt:1735689600},secondary:null,credits:{balance:'0',hasCredits:false,unlimited:false}},
      other:{limitId:'other',primary:{usedPercent:110,windowDurationMins:300},secondary:{usedPercent:-2,windowDurationMins:10080}}
    },rateLimitResetCredits:{availableCount:1}
  });
  assert.equal(data.limits.length,2);
  assert.equal(data.limits[0].windows[0].remainingPercent,3);
  assert.equal(data.limits[0].windows[0].label,'本周额度');
  assert.equal(data.limits[0].windows[0].resetsAt,1735689600);
  assert.equal(data.limits[0].windows.length,1);
  assert.equal(data.limits[1].windows[0].remainingPercent,0);
  assert.equal(data.limits[1].windows[1].remainingPercent,100);
  assert.equal(data.limits[0].credits.balance,'0');
  assert.equal(data.resetCreditsCount,1);
});

test('missing metrics stay unavailable and do not turn into zero usage', () => {
  const data=normalizeLimits({rateLimits:{limitId:'codex',primary:{usedPercent:null,windowDurationMins:null,resetsAt:null}}});
  assert.equal(data.limits[0].windows[0].usedPercent,null);
  assert.equal(data.limits[0].windows[0].remainingPercent,null);
  assert.equal(data.limits[0].credits,null);
  assert.equal(data.resetCreditsCount,null);
  assert.deepEqual(normalizeLimits({}).limits,[]);
});

test('public snapshot strips account identity and earned-reset redemption IDs', () => {
  const data=normalizeLimits({accountId:'private-account',rateLimitResetCredits:{availableCount:1,credits:[{id:'private-redemption-id'}]},rateLimits:{limitId:'codex',primary:{usedPercent:0,windowDurationMins:300}}});
  assert.equal(data.limits[0].windows[0].remainingPercent,100);
  assert.equal(data.limits[0].windows[0].label,'5 小时额度');
  assert.equal(JSON.stringify(data).includes('private'),false);
});
