export const DAY_MS=86400000;
export const BEIJING_OFFSET_MS=8*3600000;
export const activityDate=ms=>new Date(ms+BEIJING_OFFSET_MS).toISOString().slice(0,10);
export const activityCutoff=nowMs=>Math.floor((nowMs+BEIJING_OFFSET_MS)/DAY_MS)*DAY_MS-BEIJING_OFFSET_MS-89*DAY_MS;
const finite=value=>typeof value==='number' && Number.isFinite(value);

// Account/turn timestamps are Unix seconds. Open turns are intentionally excluded:
// an abandoned in-progress record must never accrue time while the app is closed.
export function aggregateActivity(threads,{nowMs=Date.now(),cutoffMs=activityCutoff(nowMs)}={}) {
  const unique=new Map(),intervals=[],days=new Map();let completedTasks=0,missingTiming=0;
  for(const thread of threads)for(const turn of thread.turns||[]) {
    if(typeof turn.id!=='string')continue;
    const old=unique.get(turn.id);
    if(!old || (!finite(old.completedAt) && finite(turn.completedAt)))unique.set(turn.id,turn);
  }
  const day=date=>{if(!days.has(date))days.set(date,{date,workSeconds:0,completedTasks:0});return days.get(date);};
  for(const turn of unique.values()) {
    if(turn.status==='inProgress')continue;
    if(!finite(turn.completedAt)){missingTiming++;continue;}
    const end=turn.completedAt*1000;
    if(end<cutoffMs)continue;
    if(end>nowMs+1000){missingTiming++;continue;}
    if(turn.status==='completed' && end<=nowMs){completedTasks++;day(activityDate(end)).completedTasks++;}
    if(!finite(turn.startedAt)){missingTiming++;continue;}
    const start=turn.startedAt*1000;
    if(end<start){missingTiming++;continue;}
    if(start>nowMs)continue;
    const left=Math.max(start,cutoffMs),right=Math.min(end,nowMs);
    if(right>left)intervals.push([left,right]);
  }
  intervals.sort((a,b)=>a[0]-b[0]);const merged=[];
  for(const interval of intervals){const last=merged.at(-1);if(last && interval[0]<=last[1])last[1]=Math.max(last[1],interval[1]);else merged.push([...interval]);}
  let totalWorkSeconds=0;
  for(const [start,end] of merged){totalWorkSeconds+=(end-start)/1000;let cursor=start;while(cursor<end){const nextMidnight=(Math.floor((cursor+BEIJING_OFFSET_MS)/DAY_MS)+1)*DAY_MS-BEIJING_OFFSET_MS;const stop=Math.min(end,nextMidnight);day(activityDate(cursor)).workSeconds+=(stop-cursor)/1000;cursor=stop;}}
  return {completedTasks,totalWorkSeconds,dailyBuckets:[...days.values()].sort((a,b)=>a.date.localeCompare(b.date)),missingTiming};
}
