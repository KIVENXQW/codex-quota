import http from 'node:http';
import {readFile} from 'node:fs/promises';
import {dirname,join} from 'node:path';
import {fileURLToPath} from 'node:url';
import {readAccountUsage} from './codex-account.mjs';

const root=dirname(fileURLToPath(import.meta.url));
let appVersion='2.1.0';
try{appVersion=JSON.parse(await readFile(join(root,'version.json'),'utf8')).version||appVersion;}catch{}
const port=Number(process.env.CODEX_USAGE_PORT || 43761);
const origin=`http://127.0.0.1:${port}`;
let snapshot=null,inFlight=null;

async function snapshotNow() {
  if(snapshot && Date.now()-Date.parse(snapshot.updatedAt)<15000)return snapshot;
  if(!inFlight)inFlight=readAccountUsage().then(value=>{snapshot=value;return value;}).finally(()=>{inFlight=null});
  return inFlight;
}

const files=new Map([['/',['index.html','text/html; charset=utf-8']],['/app.js',['app.js','text/javascript; charset=utf-8']],['/style.css',['style.css','text/css; charset=utf-8']]]);
const server=http.createServer(async(req,res)=>{
  res.setHeader('Cache-Control','no-store');
  res.setHeader('X-Content-Type-Options','nosniff');
  res.setHeader('Content-Security-Policy',"default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; connect-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'none'; frame-ancestors 'none'");
  function json(status,data){res.writeHead(status,{'Content-Type':'application/json; charset=utf-8'});res.end(JSON.stringify(data));}
  if(req.headers.host!==`127.0.0.1:${port}` || (req.headers.origin && req.headers.origin!==origin) || req.headers['sec-fetch-site']==='cross-site')return json(403,{ok:false,error:'仅允许本机面板访问。'});
  if(req.method!=='GET')return json(405,{ok:false,error:'只支持读取。'});
  const path=new URL(req.url,origin).pathname;
  if(path==='/health')return json(200,{app:'codex-usage-panel',version:appVersion,pid:process.pid});
  if(path==='/api/usage'){
    try{return json(200,await snapshotNow());}
    catch(error){return json(snapshot?200:503,snapshot?{...snapshot,stale:true,error:error.message}:{ok:false,error:error.message});}
  }
  const file=files.get(path);
  if(!file)return json(404,{ok:false,error:'页面不存在。'});
  try{const content=await readFile(join(root,'public',file[0]));res.writeHead(200,{'Content-Type':file[1]});res.end(content);}
  catch{return json(503,{ok:false,error:'面板文件尚未就绪。'});}
});
server.on('error',error=>{console.error(error.code==='EADDRINUSE'?'端口已占用。请运行 Start-Panel.ps1 检查已启动的面板。':'面板启动失败。');process.exitCode=1;});
server.listen(port,'127.0.0.1',()=>console.log(`Codex 额度面板 ${origin}`));
