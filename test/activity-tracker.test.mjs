import test from 'node:test';
import assert from 'node:assert/strict';
import {mkdtemp,readFile,readdir,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join} from 'node:path';
import {ActivityTracker} from '../activity-tracker.mjs';

test('initial snapshot returns collecting before background client is available and saves no identity',async()=>{
  const directory=await mkdtemp(join(tmpdir(),'quota-activity-test-'));
  let release,closed=0;
  const waiting=new Promise(resolve=>{release=resolve;});
  const tracker=new ActivityTracker({directory,connect:async()=>{await waiting;return {request:async()=>({data:[],nextCursor:null}),close(){closed++;}};}});
  try {
    const snapshot=await tracker.getSnapshot('private@example.com');
    assert.equal(snapshot.status,'collecting');assert.equal(snapshot.updatedAt,null);
    release();await tracker.whenIdle();
    const cached=await tracker.getSnapshot('private@example.com');
    assert.equal(cached.status,'ready');assert.equal(closed,1);
    const names=await readdir(directory);assert.equal(names.length,1);assert.equal(names[0].includes('private'),false);
    const data=await readFile(join(directory,names[0]),'utf8');assert.equal(data.includes('private'),false);
  }finally{tracker.close();await rm(directory,{recursive:true,force:true});}
});

test('account change does not publish the previous accounts cached snapshot',async()=>{
  const directory=await mkdtemp(join(tmpdir(),'quota-activity-test-'));
  const tracker=new ActivityTracker({directory,connect:async()=>({request:async()=>({data:[],nextCursor:null}),close(){}})});
  try{await tracker.getSnapshot('one');await tracker.whenIdle();const second=await tracker.getSnapshot('two');assert.equal(second.status,'collecting');assert.equal(second.updatedAt,null);await tracker.whenIdle();}
  finally{tracker.close();await rm(directory,{recursive:true,force:true});}
});

test('normal refresh preserves cached readiness while background work is pending',async()=>{
  const directory=await mkdtemp(join(tmpdir(),'quota-activity-test-'));
  let connects=0,release;
  const waiting=new Promise(resolve=>{release=resolve;});
  const tracker=new ActivityTracker({directory,refreshMs:0,connect:async()=>{if(++connects>1)await waiting;return {request:async()=>({data:[],nextCursor:null}),close(){}};}});
  try{
    await tracker.getSnapshot('account');await tracker.whenIdle();
    const refreshing=await tracker.getSnapshot('account');
    assert.equal(refreshing.status,'ready');assert.ok(refreshing.updatedAt);
  }finally{release();await tracker.whenIdle();tracker.close();await rm(directory,{recursive:true,force:true});}
});
