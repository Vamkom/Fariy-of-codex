'use strict';
const fs=require('fs');
const path=require('path');
const http=require('http');
const crypto=require('crypto');
const ROOT=__dirname;
const config=JSON.parse(fs.readFileSync(path.join(ROOT,'config.json'),'utf8').replace(/^\uFEFF/,''));
const instance=crypto.createHash('sha256').update(ROOT.toLowerCase()).digest('hex').slice(0,16);
let enabled=true,busy=false,server=null;
let state={installed:false,status:'starting',instance};
function connect(target,port) {
  if(target.url!=='app://-/index.html'||!target.webSocketDebuggerUrl.startsWith(`ws://127.0.0.1:${port}/devtools/page/`))throw Error('Refused non-native target');
  return new Promise((resolve,reject)=>{
    const ws=new WebSocket(target.webSocketDebuggerUrl);let nextId=1;
    const pending=new Map();
    ws.onerror=()=>reject(Error('Native renderer connection failed'));
    ws.onopen=()=>resolve({
      call(method,params={}) {
        return new Promise((res,rej)=>{
          const id=nextId++;
          const timeout=setTimeout(()=>{pending.delete(id);rej(Error('Native renderer timed out'));},7000);
          pending.set(id,{res,rej,timeout});ws.send(JSON.stringify({id,method,params}));
        });
      },
      close(){ws.close();}
    });
    ws.onmessage=event=>{
      const message=JSON.parse(event.data);const waiting=pending.get(message.id);if(!waiting)return;
      clearTimeout(waiting.timeout);pending.delete(message.id);
      if(message.error)waiting.rej(Error(message.error.message));else waiting.res(message.result);
    };
    ws.onclose=()=>{for(const waiting of pending.values()){clearTimeout(waiting.timeout);waiting.rej(Error('Renderer closed'));}pending.clear();};
  });
}
function paint(css,revision) {
  if(location.protocol!=='app:'||location.pathname!=='/index.html')return {installed:false,status:'wrong-target'};
  const id='fairy-hdd-codex-theme';let style=document.getElementById(id);
  if(css===null&&style?.dataset.revision!==revision)return {needsCss:true};
  if(!style){style=document.createElement('style');style.id=id;document.head.appendChild(style);}
  if(style.dataset.revision!==revision){style.textContent=css;style.dataset.revision=revision;}
  document.documentElement.setAttribute('data-fairy-hdd','v2');
  return {installed:true,status:'applied',revision,stylePresent:!!document.getElementById(id),
    nativeComposer:!!document.querySelector('[data-codex-composer-root]'),
    nativeSidebar:!!document.querySelector('aside.app-shell-left-panel'),
    panelColor:getComputedStyle(document.querySelector('aside.app-shell-left-panel')||document.body).backgroundColor};
}
function remove(){document.getElementById('fairy-hdd-codex-theme')?.remove();document.documentElement.removeAttribute('data-fairy-hdd');return {installed:false,status:'restored'};}
async function tick() {
  if(busy)return;busy=true;let session;
  try {
    const native=JSON.parse(fs.readFileSync(path.join(process.env.LOCALAPPDATA,'CodexDreamSkin','native-session.json'),'utf8'));
    const port=Number(native.port);
    if(!Number.isInteger(port)||port<1024||port>65535)throw Error('No active local skin port');
    const targets=await (await fetch(`http://127.0.0.1:${port}/json/list`,{signal:AbortSignal.timeout(2000)})).json();
    const target=targets.find(t=>t.type==='page'&&t.url==='app://-/index.html');
    if(!target)throw Error('No native Codex renderer');
    session=await connect(target,port);
    let css=fs.readFileSync(path.join(ROOT,'codex-hdd.css'),'utf8');
    const artwork=path.join(ROOT,'skin','hdd-background.png');
    if(fs.existsSync(artwork)) {
      const bytes=fs.readFileSync(artwork);
      if(bytes.length>8*1024*1024)throw Error('HDD background exceeds 8 MiB limit');
      css+='\nhtml[data-fairy-hdd="v2"]{--fairy-hdd-art:url("data:image/png;base64,'+bytes.toString('base64')+'") !important;}';
    }
    const revision=crypto.createHash('sha256').update(css).digest('hex').slice(0,16);
    const expression=enabled?`(${paint.toString()})(null,${JSON.stringify(revision)})`:`(${remove.toString()})()`;
    let result=await session.call('Runtime.evaluate',{expression,returnByValue:true});
    if(result.result?.value?.needsCss)result=await session.call('Runtime.evaluate',{expression:`(${paint.toString()})(${JSON.stringify(css)},${JSON.stringify(revision)})`,returnByValue:true});
    if(result.exceptionDetails)throw Error('Native skin paint failed');
    state={...result.result.value,port};
  } catch(error) {state={installed:false,status:'awaiting-native-runtime',reason:error.message};}
  finally {state.instance=instance;session?.close();busy=false;fs.writeFileSync(path.join(ROOT,'skin-runtime-state.json'),JSON.stringify(state,null,2));}
}
async function main(){
  if(process.argv.includes('--once')){await tick();console.log(JSON.stringify(state));return;}
  server=http.createServer(async(req,res)=>{
    if(!['127.0.0.1:18743','localhost:18743'].includes(req.headers.host)||req.headers.origin){res.writeHead(403);return res.end();}
    if(req.url==='/status'&&req.method==='GET'){res.writeHead(200,{'Content-Type':'application/json'});return res.end(JSON.stringify(state));}
    if(['/restore','/enable'].includes(req.url)&&req.method==='POST'&&req.headers['x-fairy-token']===config.token){enabled=req.url==='/enable';await tick();res.writeHead(200);return res.end(JSON.stringify(state));}
    res.writeHead(403);res.end();
  });
  server.on('error',()=>{process.exit(1);});server.listen(18743,'127.0.0.1');
  await tick();const timer=setInterval(tick,3000);
  for(const signal of ['SIGINT','SIGTERM'])process.on(signal,async()=>{clearInterval(timer);enabled=false;await tick();server.close();});
}
main().catch(error=>{console.error(error.message);process.exitCode=1;});
