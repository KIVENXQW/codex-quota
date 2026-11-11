import { spawn } from 'node:child_process';
import { existsSync, readdirSync, statSync } from 'node:fs';
import { homedir } from 'node:os';
import { join } from 'node:path';
import { createInterface } from 'node:readline';
import { fileURLToPath } from 'node:url';
import { selectWeekly } from './weekly-model.mjs';

const failure = message => Object.assign(new Error(), { safeMessage: message });
function connect() {
  const base = join(process.env.LOCALAPPDATA || join(homedir(), 'AppData', 'Local'), 'OpenAI', 'Codex', 'bin');
  const paths = existsSync(base) ? readdirSync(base).map(name => join(base, name, 'codex.exe')).filter(existsSync) : [];
  paths.sort((a, b) => statSync(b).mtimeMs - statSync(a).mtimeMs);
  if (!paths.length) throw failure('未找到 Codex，请先打开客户端');
  const child = spawn(paths[0], ['app-server'], {
    cwd: fileURLToPath(new URL('.', import.meta.url)), windowsHide: true, stdio: ['pipe', 'pipe', 'ignore'],
    env: { ...process.env, CODEX_HOME: join(homedir(), '.codex') }
  });
  const lines = createInterface({ input: child.stdout });
  const pending = new Map();
  let nextId = 0, closed = false;
  const rejectAll = () => {
    closed = true;
    for (const { reject, timer } of pending.values()) { clearTimeout(timer); reject(failure('无法读取额度，请确认已登录')); }
    pending.clear();
  };
  child.on('error', rejectAll);
  child.on('exit', rejectAll);
  child.stdin.on('error', rejectAll);
  const notify = message => { if (!closed) child.stdin.write(JSON.stringify(message) + '\n'); };
  lines.on('line', line => {
    let message;
    try { message = JSON.parse(line); } catch { return; }
    if (message.method) {
      if (message.id !== undefined) notify({ id: message.id, error: { code: -32601, message: 'Read-only client' } });
      return;
    }
    const entry = pending.get(message.id);
    if (!entry) return;
    pending.delete(message.id); clearTimeout(entry.timer);
    if (message.error) entry.reject(failure('无法读取额度，请确认已登录'));
    else entry.resolve(message.result);
  });
  return {
    notify,
    request(method, deadline) {
      return new Promise((resolve, reject) => {
        if (closed) { reject(failure('无法读取额度，请确认已登录')); return; }
        const id = nextId++;
        const timer = setTimeout(() => { pending.delete(id); reject(failure('读取超时，请稍后刷新')); }, Math.max(1, deadline - Date.now()));
        pending.set(id, { resolve, reject, timer });
        notify({ id, method, params: method === 'initialize' ? {
          clientInfo: { name: 'codex_weekly_minimal', title: '周额度', version: '1.0.0' }, capabilities: { experimentalApi: true }
        } : {} });
      });
    },
    close() { rejectAll(); lines.close(); child.stdin.end(); child.kill(); }
  };
}

let client = null, timer = null, stopped = false, busy = false;
function stop() {
  if (stopped) return;
  stopped = true; clearInterval(timer); client?.close(); process.stdin.pause();
}
async function refresh() {
  if (busy || stopped) return;
  busy = true;
  let state;
  try {
    const deadline = Date.now() + 20000;
    if (!client) {
      client = connect();
      await client.request('initialize', deadline);
      client.notify({ method: 'initialized', params: {} });
    }
    state = selectWeekly(await client.request('account/rateLimits/read', deadline));
  } catch (error) {
    client?.close(); client = null;
    state = { available: false, remainingPercent: null, resetsAt: null, error: error?.safeMessage || '读取失败，请稍后刷新' };
  } finally { busy = false; }
  if (!stopped) process.stdout.write(JSON.stringify({ ...state, updatedAt: new Date().toISOString() }) + '\n');
}
process.on('SIGINT', stop); process.on('SIGTERM', stop); process.on('SIGHUP', stop); process.on('exit', stop);
process.stdout.on('error', stop);
if (process.argv.includes('--managed')) { process.stdin.on('end', stop); process.stdin.resume(); }
await refresh();
if (process.argv.includes('--once')) stop();
else if (!stopped) timer = setInterval(refresh, 60000);
