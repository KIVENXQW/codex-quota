import test from 'node:test';
import assert from 'node:assert/strict';
import {collectActivity,activitySnapshot} from '../activity-collector.mjs';

const nowMs=Date.parse('2026-11-02T00:00:00Z');
const turn={id:'turn-a',status:'completed',startedAt:nowMs/1000-120,completedAt:nowMs/1000-60,items:[{text:'DO NOT SAVE'}]};
function api({pages=1}={}) {return {async request(method,params){
  if(method==='thread/list'){assert.equal(params.useStateDbOnly,true);assert.deepEqual(params.modelProviders,['openai']);if(params.archived)return {data:[],nextCursor:null};return {data:[{id:'thread-a',updatedAt:nowMs/1000,preview:'DO NOT SAVE'}],nextCursor:pages>1?'more':null};}
  if(method==='thread/turns/list'){assert.equal(params.itemsView,'notLoaded');return {data:[turn],nextCursor:null};}
  throw new Error('unexpected API');
}};}

test('collector stores only metadata and marks fully inspected local window ready',async()=>{
  const cache=await collectActivity(api(),null,{nowMs});
  assert.equal(JSON.stringify(cache).includes('DO NOT SAVE'),false);
  const view=activitySnapshot(cache,{nowMs});
  assert.equal(view.status,'ready');assert.equal(view.totalWorkSeconds,60);assert.equal(view.completedTasks,1);
  assert.equal(view.coveredFrom,'2026-08-05');assert.equal(view.scope,'local-codex');
});

test('bounded pagination cannot claim complete coverage',async()=>{
  const cache=await collectActivity(api({pages:2}),null,{nowMs,maxListPages:1});
  const view=activitySnapshot(cache,{nowMs});
  assert.equal(view.status,'partial');assert.ok(view.warnings.length>0);
});

test('unchanged completed threads reuse metadata; unfinished turns are refreshed',async()=>{
  const first=await collectActivity(api(),null,{nowMs});
  let turnsRead=0;
  const nextApi=api(),request=nextApi.request;
  nextApi.request=async(method,params)=>{if(method==='thread/turns/list')turnsRead++;return request(method,params);};
  await collectActivity(nextApi,first,{nowMs:nowMs+60000});
  assert.equal(turnsRead,0);
  first.threads[0].hasIncomplete=true;
  await collectActivity(nextApi,first,{nowMs:nowMs+60000});
  assert.equal(turnsRead,1);
});

test('old terminal records missing timestamps stay partial without being reread every poll',async()=>{
  const source=api(),request=source.request;let turnsRead=0;
  source.request=async(method,params)=>{if(method==='thread/turns/list'){turnsRead++;return {data:[{id:'legacy',status:'completed',startedAt:null,completedAt:null}],nextCursor:null};}return request(method,params);};
  const cache=await collectActivity(source,null,{nowMs});
  assert.equal(activitySnapshot(cache,{nowMs}).status,'partial');
  await collectActivity(source,cache,{nowMs:nowMs+60000});
  assert.equal(turnsRead,1);
});

test('a transient turn read failure is retried despite unchanged updatedAt and can recover ready',async()=>{
  const source=api(),request=source.request;let fail=true;
  source.request=async(method,params)=>{if(method==='thread/turns/list' && fail)throw new Error('temporary');return request(method,params);};
  const first=await collectActivity(source,null,{nowMs});
  assert.equal(activitySnapshot(first,{nowMs}).status,'partial');
  fail=false;
  const recovered=await collectActivity(source,first,{nowMs:nowMs+60000});
  const view=activitySnapshot(recovered,{nowMs:nowMs+60000});
  assert.equal(view.status,'ready');assert.equal(view.completedTasks,1);assert.equal(recovered.threads[0].partial,false);
  assert.equal(recovered.warnings.length,0);
});

test('a failed discovery pass retries the missing historical range before declaring coverage ready',async()=>{
  const source=api(),request=source.request;let fail=true;
  source.request=async(method,params)=>{if(method==='thread/list' && !params.archived && fail)throw new Error('temporary');return request(method,params);};
  const first=await collectActivity(source,null,{nowMs});
  assert.equal(first.complete,false);
  fail=false;
  const recovered=await collectActivity(source,first,{nowMs:nowMs+60000});
  const view=activitySnapshot(recovered,{nowMs:nowMs+60000});
  assert.equal(view.status,'ready');assert.equal(view.completedTasks,1);assert.equal(recovered.warnings.length,0);
});
