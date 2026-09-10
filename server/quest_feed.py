"""Read-only SQLite projection for the game's authenticated quest RPC."""
import asyncio
import json
import logging
import os
import time
from pathlib import Path

from store import level


class QuestFeed:
    def __init__(self, store, uid, path):
        self.store, self.uid, self.path = store, str(uid), Path(path)

    def snapshot(self):
        with self.store.connection() as c:
            c.execute('BEGIN')
            players = c.execute('SELECT b.player,b.game_id,p.class,p.renown FROM game_bindings b JOIN players p ON p.name=b.player ORDER BY b.player').fetchall()
            bound = {r['player']:json.loads(r['data']) for r in c.execute('SELECT b.player,p.data FROM game_bindings b JOIN game_players p ON p.id=b.game_id')}
            rules = {r['quest_id']:dict(r) for r in c.execute('SELECT * FROM game_rules')}
            has_notes = c.execute("SELECT 1 FROM sqlite_master WHERE name='quest_notes'").fetchone()
            notes = {r['quest_id']:dict(r) for r in c.execute('SELECT * FROM quest_notes')} if has_notes else {}
            quests = [dict(r) for r in c.execute('SELECT * FROM quests ORDER BY completed_at IS NOT NULL,id')]
            result = dict(schema=1,uid=self.uid,generated_at=int(time.time()),players=[])
            for player in players:
                lv,title = level(player['renown'])
                item = dict(account_id=player['game_id'],profile_name=player['player'],class_name=player['class'],renown=player['renown'],level=lv,rank=title,bound=True,quests=[])
                relevant = [q for q in quests if q['owner'] in (None, player['player'])]
                item['truncated'] = len(relevant)>256
                for q in relevant[:256]:
                    rule = rules.get(q['id'])
                    automatic = bool(rule and rule['title']==q['title'] and rule['owner']==q['owner'])
                    current,target = 0,0
                    if automatic:
                        subjects = [bound.get(q['owner'])] if q['owner'] else list(bound.values())
                        current = max((next((v['count'] for v in p['counters'] if v['kind']==rule['kind'] and v['key']==rule['key']),0) for p in subjects if p),default=0)
                        target = rule['target']
                        current = min(current,target)
                    note=notes.get(q['id'])
                    item['quests'].append(dict(id=q['id'],title=q['title'],chapter=q['chapter'],owner=q['owner'] or 'Tím',xp=q['xp'],completed=bool(q['completed_at']),automatic=automatic,current=current,target=target,note=note['note'] if note and note['title']==q['title'] else ''))
                result['players'].append(item)
            return result

    def export(self):
        data=json.dumps(self.snapshot(),ensure_ascii=False,separators=(',',':')).encode('utf-8')
        if len(data)>1024*1024:
            raise ValueError('Quest feed exceeds limit')
        self.path.parent.mkdir(parents=True,exist_ok=True)
        temporary=self.path.with_suffix('.tmp')
        fd=os.open(temporary,os.O_WRONLY|os.O_CREAT|os.O_TRUNC,0o600)
        with os.fdopen(fd,'wb') as f:
            f.write(data)
        os.replace(temporary,self.path)

    async def run(self, client):
        await client.wait_until_ready()
        failed=False
        while not client.is_closed():
            try:
                await asyncio.to_thread(self.export)
                failed=False
            except Exception as error:
                if not failed:
                    logging.getLogger(__name__).warning('Quest feed unavailable: %s',type(error).__name__)
                failed=True
            await asyncio.sleep(10)
