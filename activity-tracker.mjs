import {createHash} from 'node:crypto';
import {readFile,mkdir,writeFile,rename} from 'node:fs/promises';
import {join} from 'node:path';
import {collectActivity,activitySnapshot} from './activity-collector.mjs';

export class ActivityTracker {
  constructor({directory,connect,refreshMs=60000}) {
    this.directory=directory;this.connect=connect;this.refreshMs=refreshMs;
    this.key=null;this.cache=null;this.pending=null;this.client=null;this.generation=0;this.lastAttempt=0;this.error=null;
  }
  async getSnapshot(identity) {
    if(!identity){this.close();return activitySnapshot(null,{error:'无法确认当前登录范围，活动缓存暂不可用。'});}
    const key=createHash('sha256').update(identity).digest('hex');
    if(key!==this.key) {
      this.close();this.key=key;this.cache=null;this.error=null;this.lastAttempt=0;
      try{const cached=JSON.parse(await readFile(join(this.directory,`activity-${key}.json`),'utf8'));if(cached.version===1 && Array.isArray(cached.threads))this.cache=cached;}catch{}
    }
    if(!this.pending && Date.now()-this.lastAttempt>=this.refreshMs){
      this.lastAttempt=Date.now();
      const generation=this.generation;
      this.pending=this.update(generation,key).finally(()=>{if(generation===this.generation)this.pending=null;});
    }
    return activitySnapshot(this.cache,{collecting:!this.cache && !!this.pending,error:this.error});
  }
  async update(generation,key) {
    let client;
    try {
      client=await this.connect();
      if(generation!==this.generation){client.close();return;}
      this.client=client;
      const next=await collectActivity(client,this.cache);
      if(generation!==this.generation)return;
      this.cache=next;this.error=null;
      await mkdir(this.directory,{recursive:true});
      const file=join(this.directory,`activity-${key}.json`),temp=`${file}.tmp`;
      await writeFile(temp,JSON.stringify(next),'utf8');
      if(generation===this.generation)await rename(temp,file);
    }catch{if(generation===this.generation)this.error='本机活动更新暂不可用，保留此前已读取记录。';}
    finally{client?.close();if(generation===this.generation)this.client=null;}
  }
  whenIdle(){return this.pending||Promise.resolve();}
  close(){this.generation++;this.client?.close();this.client=null;this.pending=null;this.key=null;this.cache=null;}
}
