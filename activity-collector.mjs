import {activityCutoff,activityDate,aggregateActivity} from './activity-model.mjs';

const SOURCES=['cli','vscode','exec','appServer','subAgent','subAgentReview','subAgentCompact','subAgentThreadSpawn','subAgentOther','unknown'];
const finite=value=>typeof value==='number' && Number.isFinite(value);
const terminal=status=>['completed','interrupted','failed'].includes(status);
const cleanTurn=turn=>({id:turn.id,status:turn.status,startedAt:finite(turn.startedAt)?turn.startedAt:null,completedAt:finite(turn.completedAt)?turn.completedAt:null,durationMs:finite(turn.durationMs)?turn.durationMs:null});

// Bounded metadata-only backfill. Subsequent passes list only recently changed
// threads and reuse terminal turns already cached; no transcript/items are saved.
export async function collectActivity(client,previous=null,options={}) {
  const {nowMs=Date.now(),maxListPages=10,maxThreads=400,maxTurnPages=10,maxRequests=600,maxDurationMs=45000}=options;
  const cutoffMs=activityCutoff(nowMs),cutoffSec=cutoffMs/1000;
  const oldThreads=new Map((previous?.threads||[]).filter(t=>t.updatedAt>=cutoffSec || t.turns?.some(x=>x.completedAt>=cutoffSec)).map(t=>[t.id,t]));
  const discovered=new Map(),warnings=new Set();
  let discoveryComplete=true,requests=0,failed=false;
  const deadline=Date.now()+maxDurationMs;
  const threshold=previous?.discoveryComplete===true && previous.scanAt ? Math.max(cutoffSec,Date.parse(previous.scanAt)/1000-2) : cutoffSec;
  async function request(method,params){if(++requests>maxRequests || Date.now()>deadline)throw new Error('budget');return client.request(method,params);}
  for(const archived of [false,true]) {
    let cursor=null,done=false;
    try {
      for(let page=0;page<maxListPages;page++) {
        const result=await request('thread/list',{limit:100,sortKey:'updated_at',sortDirection:'desc',modelProviders:['openai'],sourceKinds:SOURCES,useStateDbOnly:true,archived,...(cursor?{cursor}:{})});
        const rows=Array.isArray(result.data)?result.data:[];
        for(const thread of rows){
          if(typeof thread.id!=='string')continue;
          if(!finite(thread.updatedAt)){discoveryComplete=false;warnings.add('部分聊天缺少更新时间，历史范围可能不完整。');continue;}
          if(thread.updatedAt<cutoffSec || thread.updatedAt<threshold)continue;
          if(discovered.size>=maxThreads && !discovered.has(thread.id)){discoveryComplete=false;warnings.add('已达到本次聊天采集上限，统计仅包含已读取部分。');break;}
          discovered.set(thread.id,{id:thread.id,updatedAt:thread.updatedAt});
        }
        if(!result.nextCursor || (rows.length && rows.every(t=>finite(t.updatedAt)&&t.updatedAt<threshold))){done=true;break;}
        if(discovered.size>=maxThreads)break;
        if(result.nextCursor===cursor)break;
        cursor=result.nextCursor;
      }
      if(!done){discoveryComplete=false;warnings.add('聊天分页达到采集上限，较早历史尚未完整读取。');}
    }catch{discoveryComplete=false;failed=true;warnings.add('部分聊天列表暂不可用，显示已采集的本机记录。');}
  }
  // A thread's updatedAt can remain unchanged when its active turn finishes.
  for(const thread of oldThreads.values())if((thread.hasIncomplete || thread.partial) && !discovered.has(thread.id))discovered.set(thread.id,{id:thread.id,updatedAt:thread.updatedAt});
  for(const thread of discovered.values()) {
    const old=oldThreads.get(thread.id);
    if(old && old.updatedAt>=thread.updatedAt && !old.hasIncomplete && !old.partial)continue;
    const turns=new Map((old?.turns||[]).map(t=>[t.id,t]));
    let cursor=null,done=false,threadPartial=false,partialReason=null;
    try {
      for(let page=0;page<maxTurnPages;page++) {
        const result=await request('thread/turns/list',{threadId:thread.id,limit:100,sortDirection:'desc',itemsView:'notLoaded',...(cursor?{cursor}:{})});
        const rows=Array.isArray(result.data)?result.data:[];
        let reachedKnown=false;
        for(const raw of rows){
          if(typeof raw.id!=='string')continue;
          const prior=turns.get(raw.id);
          if(prior && terminal(prior.status) && prior.completedAt!=null)reachedKnown=true;
          turns.set(raw.id,cleanTurn(raw));
        }
        const allOlder=rows.length>0 && rows.every(t=>finite(t.startedAt)&&finite(t.completedAt)&&t.startedAt<cutoffSec&&t.completedAt<cutoffSec);
        if(!result.nextCursor || allOlder || (reachedKnown && old && !old.partial)){done=true;break;}
        if(result.nextCursor===cursor)break;
        cursor=result.nextCursor;
      }
      if(!done){threadPartial=true;partialReason='limit';}
    }catch{threadPartial=true;partialReason='unavailable';failed=true;}
    const retained=[...turns.values()].filter(t=>t.completedAt==null || t.completedAt>=cutoffSec);
    oldThreads.set(thread.id,{id:thread.id,updatedAt:thread.updatedAt,hasIncomplete:retained.some(t=>!terminal(t.status)),partial:threadPartial,partialReason,turns:retained});
  }
  const threads=[...oldThreads.values()];
  let complete=discoveryComplete && !threads.some(t=>t.partial);
  for(const thread of threads)if(thread.partial)warnings.add(thread.partialReason==='limit'?'部分聊天的回合分页达到上限，时长统计尚不完整。':'部分回合元数据暂不可用，保留此前统计。');
  if(aggregateActivity(threads,{nowMs,cutoffMs}).missingTiming){complete=false;warnings.add('部分已结束回合没有有效起止时间，未计入时长。');}
  return {version:1,scanAt:new Date(nowMs).toISOString(),updatedAt:new Date(nowMs).toISOString(),cutoffMs,discoveryComplete,complete,failed,warnings:[...warnings],threads};
}

export function activitySnapshot(cache,{nowMs=Date.now(),collecting=false,error=null}={}) {
  const cutoffMs=activityCutoff(nowMs),threads=cache?.threads||[];
  const {completedTasks,totalWorkSeconds,dailyBuckets}=aggregateActivity(threads,{nowMs,cutoffMs});
  const status=collecting?'collecting':!cache?'unavailable':error?'partial':cache.complete?'ready':threads.length?'partial':'unavailable';
  const warnings=['仅统计本机 Codex 记录；本机历史不能证明全部属于当前账户，也不包含其他设备。','响应时长包含工具和网络等待；重叠回合已去重，未结束回合暂不计入。',...(cache?.warnings||[]),...(error?[error]:[])];
  return {status,scope:'local-codex',coveredFrom:cache?.complete?activityDate(Math.max(cutoffMs,cache.cutoffMs)):dailyBuckets[0]?.date??null,coveredTo:cache?.updatedAt?activityDate(Date.parse(cache.updatedAt)):null,updatedAt:cache?.updatedAt??null,threadCount:threads.length,completedTasks,totalWorkSeconds,dailyBuckets,warnings};
}
