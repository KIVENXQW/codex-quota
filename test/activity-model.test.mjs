import test from 'node:test';
import assert from 'node:assert/strict';
import {aggregateActivity} from '../activity-model.mjs';

const sec = value => Date.parse(value) / 1000;
const turn = (id,start,end,status='completed') => ({id,startedAt:sec(start),completedAt:end===null?null:sec(end),status});
const options = {nowMs:Date.parse('2026-11-02T00:00:00Z'),cutoffMs:Date.parse('2026-11-01T00:00:00Z')};
const calculate = threads => aggregateActivity(threads,options);

test('overlapping parent and child work counts wall time once but completed turn IDs separately',()=>{
  const result=calculate([{id:'parent',turns:[turn('a','2026-11-01T01:00:00Z','2026-11-01T02:00:00Z')]},{id:'child',turns:[turn('b','2026-11-01T01:30:00Z','2026-11-01T02:30:00Z')]}]);
  assert.equal(result.totalWorkSeconds,5400);
  assert.equal(result.completedTasks,2);
});

test('UTC+8 midnight splits the union and assigns completion to the ending day',()=>{
  const result=calculate([{id:'a',turns:[turn('a','2026-11-01T15:30:00Z','2026-11-01T16:30:00Z')]}]);
  assert.deepEqual(result.dailyBuckets,[{date:'2026-11-01',workSeconds:1800,completedTasks:0},{date:'2026-11-02',workSeconds:1800,completedTasks:1}]);
});

test('copied turn IDs are deduplicated across forked threads',()=>{
  const item=turn('same','2026-11-01T01:00:00Z','2026-11-01T01:01:00Z');
  const result=calculate([{id:'a',turns:[item]},{id:'b',turns:[{...item}]}]);
  assert.equal(result.completedTasks,1);
  assert.equal(result.totalWorkSeconds,60);
});

test('zero-duration completions count while cutoff excludes old work and clips crossing work',()=>{
  const result=calculate([{id:'a',turns:[turn('old','2026-10-31T20:00:00Z','2026-10-31T21:00:00Z'),turn('cross','2026-10-31T23:59:00Z','2026-11-01T00:01:00Z'),turn('zero','2026-11-01T02:00:00Z','2026-11-01T02:00:00Z')]}]);
  assert.equal(result.totalWorkSeconds,60);
  assert.equal(result.completedTasks,2);
});

test('unfinished turns do not grow forever and gaps between turns are excluded',()=>{
  const result=calculate([{id:'a',turns:[turn('first','2026-11-01T01:00:00Z','2026-11-01T01:01:00Z'),turn('last','2026-11-01T04:00:00Z','2026-11-01T04:02:00Z'),turn('running','2026-10-01T00:00:00Z',null,'inProgress')]}]);
  assert.equal(result.totalWorkSeconds,180);
  assert.equal(result.completedTasks,2);
});

test('failed and interrupted turns contribute elapsed work but not successful tasks',()=>{
  const result=calculate([{id:'a',turns:[turn('failed','2026-11-01T01:00:00Z','2026-11-01T01:01:00Z','failed'),turn('cancelled','2026-11-01T01:01:00Z','2026-11-01T01:02:00Z','interrupted')]}]);
  assert.equal(result.totalWorkSeconds,120);
  assert.equal(result.completedTasks,0);
});

test('valid completion counts a task even when missing start prevents duration calculation',()=>{
  const result=calculate([{id:'a',turns:[{id:'missing-start',status:'completed',startedAt:null,completedAt:sec('2026-11-01T02:00:00Z')}]}]);
  assert.equal(result.completedTasks,1);assert.equal(result.totalWorkSeconds,0);assert.equal(result.missingTiming,1);
  assert.deepEqual(result.dailyBuckets,[{date:'2026-11-01',workSeconds:0,completedTasks:1}]);
});
