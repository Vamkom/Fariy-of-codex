"""Read completed replies from this one Codex chat and synthesize local audio."""
from __future__ import annotations
import collections,json,re,threading,time,secrets
from pathlib import Path


def spoken_text(text):
    text=re.sub(r'```[\s\S]*?```','',text)
    text=re.sub(r'!?\[([^\]]+)\]\([^\n)]*\)',r'\1',text)
    return re.sub(r'[*#>`|]','',text).strip()


def chunks(text,limit=240):
    result=[];current=''
    for part in re.split(r'(?<=[。！？.!?\n])',spoken_text(text)):
        if len(current)+len(part)>limit and current:result.append(current);current=''
        while len(part)>limit:result.append(part[:limit]);part=part[limit:]
        current+=part
    if current.strip():result.append(current.strip())
    return result


def completed_reply(record):
    item=record.get('payload',{})
    if record.get('type')!='response_item' or item.get('type')!='message' or item.get('role')!='assistant':return None
    if item.get('phase')!='final_answer' and item.get('channel')!='final':return None
    if item.get('status') not in (None,'completed'):return None
    text='\n'.join(c.get('text','') for c in item.get('content',[]) if c.get('type')=='output_text')
    return (item.get('id') or record.get('timestamp'),text)


class ReplyReader:
    def __init__(self,path,root,voice_lock,ready,synthesize):
        self.path=Path(path);self.root=Path(root);self.voice_lock=voice_lock
        self.ready=ready;self.synthesize=synthesize;self.lock=threading.Lock()
        self.messages=collections.deque();self.audio=collections.deque(maxlen=256)
        self.counter=0;self.generation=0;self.seen=set();self.error=None
        self.stream=secrets.token_hex(8);self.synthesizing=False
        self.offset=self.path.stat().st_size if self.path.exists() else 0
        self.stop_event=threading.Event()

    def start(self):
        threading.Thread(target=self.watch,daemon=True,name='fairy-reply-reader').start()
        threading.Thread(target=self.render,daemon=True,name='fairy-speech-worker').start()

    def items_after(self,after):
        with self.lock:return [x.copy() for x in self.audio if x['id']>after]

    def publish(self,text):
        with self.lock:
            self.generation+=1;self.messages.append((self.generation,text))

    def status(self):
        with self.lock:
            return {'bound':True,'fileExists':self.path.is_file(),'queued':len(self.messages),'synthesizing':self.synthesizing,'error':self.error}

    def watch(self):
        pending=b''
        while not self.stop_event.wait(.5):
            try:
                size=self.path.stat().st_size
                if size<self.offset:self.offset=size;pending=b''
                with self.path.open('rb') as source:
                    source.seek(self.offset);data=source.read();self.offset=source.tell()
                lines=(pending+data).split(b'\n');pending=lines.pop()
                for line in lines:self.consume(line)
            except (OSError,ValueError) as error:self.error=type(error).__name__

    def consume(self,line):
        try:record=json.loads(line)
        except ValueError:return
        reply=completed_reply(record)
        if reply and reply[0] not in self.seen and reply[1].strip():
            self.seen.add(reply[0]);self.publish(reply[1])

    def render(self):
        while not self.stop_event.wait(.25):
            if not self.ready():continue
            with self.lock:
                job=self.messages.popleft() if self.messages else None
            if job:
                self.synthesizing=True
                try:self.render_message(*job)
                finally:self.synthesizing=False

    def render_message(self,generation,text):
        for part in chunks(text):
            if self.stop_event.is_set():return
            try:
                # Retry transient engine restarts without dropping this sentence.
                for attempt in range(4):
                    try:
                        with self.voice_lock:data=self.synthesize(part)
                        break
                    except Exception:
                        if attempt==3:raise
                        if self.stop_event.wait(2*(attempt+1)):return
                with self.lock:
                    self.counter+=1;name=f'reply-{self.stream}-{self.counter}.wav'
                    target=self.root/'assets'/name;temporary=target.with_suffix('.tmp')
                    temporary.write_bytes(data);temporary.replace(target)
                    self.audio.append({'id':self.counter,'file':name})
                    self.error=None
                print('Fairy reply audio ready: '+str(self.counter),flush=True)
            except Exception as error:
                self.error=str(error);print('Fairy reply synthesis failed: '+str(error),flush=True);return
