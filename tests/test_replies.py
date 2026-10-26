import json,tempfile,time,unittest,threading,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'app'))
from replies import ReplyReader,completed_reply,chunks

def record(phase,text='completed',role='assistant'):
    return {'type':'response_item','payload':{'type':'message','id':phase+role,'role':role,'phase':phase,'status':'completed','content':[{'type':'output_text','text':text}]}}

class ReplyTests(unittest.TestCase):
    def test_only_completed_final_replies(self):
        self.assertIsNone(completed_reply(record('commentary')))
        self.assertIsNone(completed_reply(record('final_answer',role='user')))
        self.assertEqual(completed_reply(record('final_answer'))[1],'completed')
    def test_history_and_partial_record(self):
        with tempfile.TemporaryDirectory() as directory:
            path=Path(directory)/'chat.jsonl'
            path.write_text(json.dumps(record('final_answer','old history'))+'\n')
            reader=ReplyReader(path,directory,threading.Lock(),lambda:False,lambda _:b'')
            reader.start()
            try:
                with path.open('a') as f:f.write(json.dumps(record('commentary'))+'\n');f.write(json.dumps(record('final_answer','new reply')))
                time.sleep(.8)
                self.assertFalse(reader.messages)
                with path.open('a') as f:f.write('\n')
                time.sleep(.8)
                with reader.lock:self.assertEqual(list(reader.messages),[(1,'new reply')])
                with path.open('a') as f:f.write(json.dumps(record('final_answer','new reply'))+'\n')
                time.sleep(.8)
                self.assertEqual(reader.generation,1)
            finally:reader.stop_event.set()
    def test_chunk_limit(self):
        result=chunks('Hello. '*200+'\n[Details](https://example.invalid)\n```\nexample()\n```')
        self.assertTrue(all(len(x)<=240 for x in result))
        self.assertIn('Details',''.join(result))
        self.assertNotIn('https://',''.join(result))
        self.assertNotIn('example()',''.join(result))
if __name__=='__main__':unittest.main(verbosity=2)
