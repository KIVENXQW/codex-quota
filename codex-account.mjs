import {spawn} from 'node:child_process';
import {createInterface} from 'node:readline';
import {readdirSync,existsSync,statSync} from 'node:fs';
import {join,dirname} from 'node:path';
import {homedir} from 'node:os';
import {fileURLToPath} from 'node:url';
import {normalizeLimits} from './usage-model.mjs';
import {ActivityTracker} from './activity-tracker.mjs';

const projectRoot=dirname(fileURLToPath(import.meta.url));
const children=new Set();
process.once('exit',()=>{for(const child of children)child.kill();});

export function findCodex() {
  const base = join(process.env.LOCALAPPDATA || join(homedir(),'AppData','Local'),'OpenAI','Codex','bin');
  const candidates = existsSync(base) ? readdirSync(base).map(folder => join(base,folder,'codex.exe')).filter(existsSync) : [];
  candidates.sort((a,b) => statSync(b).mtimeMs - statSync(a).mtimeMs);
  if (!candidates.length) throw new Error('未找到 Codex 桌面端运行程序，请先打开 Codex。');
  return candidates[0];
}

export async function connectCodex() {
  // The desktop runtime owns authentication. Never read, copy or expose credentials.
  const child = spawn(findCodex(), ['app-server'], {
    cwd:projectRoot,windowsHide:true,stdio:['pipe','pipe','ignore'],
    env:{...process.env,CODEX_HOME:join(homedir(),'.codex')}
  });
  children.add(child);
  const lines = createInterface({input:child.stdout});
  const pending = new Map();
  let nextId = 0;
  let closed=false;
  function request(method,params={}) {
    return new Promise((resolve,reject) => {
      if(closed){reject(new Error('账户连接已关闭。'));return;}
      const id=nextId++;
      const timer=setTimeout(()=>{pending.delete(id);reject(new Error('账户查询超时，请稍后刷新。'));},20000);
      pending.set(id,{resolve,reject,timer});
      child.stdin.write(JSON.stringify({id,method,params})+'\n');
    });
  }
  function failAll() {
    closed=true;
    for (const entry of pending.values()) {clearTimeout(entry.timer);entry.reject(new Error('无法读取账户，请确认 Codex 已登录后刷新。'));}
    pending.clear();
  }
  child.on('error',failAll);
  child.on('exit',()=>{children.delete(child);failAll();});
  child.stdin.on('error',failAll);
  lines.on('line',line => {
    let message;try{message=JSON.parse(line)}catch{return}
    if (message.method) {
      if (message.id !== undefined) child.stdin.write(JSON.stringify({id:message.id,error:{code:-32601,message:'Read-only client'}})+'\n');
      return;
    }
    const entry=pending.get(message.id);
    if(!entry)return;
    pending.delete(message.id);clearTimeout(entry.timer);
    if(message.error)entry.reject(new Error('账户接口暂不可用，请确认 Codex 已登录后刷新。'));
    else entry.resolve(message.result);
  });
  function close(){closed=true;failAll();lines.close();child.kill();children.delete(child);}
  try {
    await request('initialize',{clientInfo:{name:'codex_usage_panel',title:'Codex 额度面板',version:'1.0.0'},capabilities:{experimentalApi:true}});
    child.stdin.write(JSON.stringify({method:'initialized',params:{}})+'\n');
    return {request,close};
  }catch(error){close();throw error;}
}

export const activityTracker=new ActivityTracker({directory:join(projectRoot,'.data'),connect:connectCodex});

export async function readAccountUsage() {
  const client=await connectCodex();
  try {
    const [limits,usage,account]=await Promise.allSettled([client.request('account/rateLimits/read'),client.request('account/usage/read'),client.request('account/read')]);
    if(limits.status==='rejected')throw limits.reason;
    const login=account.status==='fulfilled'?account.value?.account:null;
    // The hash only separates caches; local history is not account-attributed.
    // Never persist or return an email, workspace identity, or credential.
    const identity=login?.type==='chatgpt' && login.email?JSON.stringify([login.type,login.email,login.workspaceName??null]):null;
    const activity=await activityTracker.getSnapshot(identity);
    return {
      ok:true,updatedAt:new Date().toISOString(),stale:false,...normalizeLimits(limits.value),
      usage:usage.status==='fulfilled'?usage.value:null,
      usageError:usage.status==='rejected'?'每日用量暂不可用':null,
      activity
    };
  } finally {
    client.close();
  }
}
