"""Cumulative game counters, explicit account binding and atomic quest rewards."""

import json

from store import PLAYERS


class Telemetry:
    def __init__(self, store, world, uid):
        self.store, self.world, self.uid = store, world, str(uid)
        with store.connection() as c:
            c.executescript("""
                CREATE TABLE IF NOT EXISTS game_players(id TEXT PRIMARY KEY, name TEXT, data TEXT);
                CREATE TABLE IF NOT EXISTS game_bindings(player TEXT PRIMARY KEY REFERENCES players(name), game_id TEXT UNIQUE REFERENCES game_players(id));
                CREATE TABLE IF NOT EXISTS game_rules(quest_id INTEGER PRIMARY KEY REFERENCES quests(id), title TEXT, owner TEXT, kind TEXT, key TEXT, target INTEGER);
            """)
            c.execute("BEGIN IMMEDIATE")
            if c.execute("SELECT 1 FROM settings WHERE key='telemetry_v1'").fetchone():
                return
            rules = [
                (6, "Nájsť Swamp", None, "biome", "Swamp", 1),
                (13, "Nájdi Black Forest", "Adam", "biome", "BlackForest", 1),
            ]
            for qid, old, new, owner, key, target in [
                (18, "Priprav arrows", "Vyrob 100 šípov", "Tomáš", "category:arrows", 100),
                (
                    22,
                    "Priprav food pre tím",
                    "Vyrob 20 porcií jedla pri craftingu",
                    "Pánik",
                    "category:food",
                    20,
                ),
            ]:
                changed = c.execute(
                    "UPDATE quests SET title=? WHERE id=? AND title=? AND owner=? AND completed_at IS NULL",
                    (new, qid, old, owner),
                ).rowcount
                if changed:
                    rules.append((qid, new, owner, "craft", key, target))
            for owner in PLAYERS:
                title = "Podieľaj sa na zabití 3 trollov"
                qid = c.execute(
                    "INSERT INTO quests(title,xp,owner,chapter) VALUES (?,20,?,?)",
                    (title, owner, "Bonus - RavensOath"),
                ).lastrowid
                rules.append((qid, title, owner, "kill", "$enemy_troll", 3))
            c.executemany("INSERT INTO game_rules VALUES (?,?,?,?,?,?)", rules)
            c.execute("INSERT INTO settings VALUES ('telemetry_v1','1')")

    def bind(self, player, game_id):
        with self.store.connection() as c:
            c.execute("BEGIN IMMEDIATE")
            if (
                player not in PLAYERS
                or not c.execute("SELECT 1 FROM game_players WHERE id=?", (game_id,)).fetchone()
            ):
                raise ValueError("Vyber známu postavu a herný účet zo zoznamu.")
            existing = c.execute(
                "SELECT player FROM game_bindings WHERE game_id=?", (game_id,)
            ).fetchone()
            if existing and existing["player"] != player:
                raise ValueError("Herný účet už patrí inej postave.")
            current = c.execute(
                "SELECT game_id FROM game_bindings WHERE player=?", (player,)
            ).fetchone()
            if current and current["game_id"] != game_id:
                raise ValueError("Zmena už prepojeného účtu vyžaduje kontrolu uloženého progresu.")
            c.execute("INSERT OR IGNORE INTO game_bindings VALUES (?,?)", (player, game_id))
            # Reconcile recorded evidence on linking; never replay historical announcements.
            saved = [json.loads(r["data"]) for r in c.execute("SELECT data FROM game_players")]
            self.ingest(
                c, dict(schema=1, world=self.world, uid=self.uid, players=saved), notify=False
            )

    def accounts(self):
        with self.store.connection() as c:
            return [dict(r) for r in c.execute("SELECT id,name FROM game_players ORDER BY name")]

    @staticmethod
    def count(data, kind, key):
        return next(
            (v["count"] for v in data["counters"] if v["kind"] == kind and v["key"] == key), 0
        )

    def ingest(self, c, snapshot, notify=True):
        if snapshot is None:
            return
        if (
            snapshot.get("schema") != 1
            or str(snapshot.get("uid")) != self.uid
            or snapshot.get("world") != self.world
        ):
            raise ValueError("Telemetry world mismatch")
        players = snapshot.get("players")
        if not isinstance(players, list) or len(players) > 100:
            raise ValueError("Invalid telemetry players")
        seen = set()
        for p in players:
            if not isinstance(p.get("id"), str) or not 1 <= len(p["id"]) <= 100 or p["id"] in seen:
                raise ValueError("Invalid account")
            seen.add(p["id"])
            if (
                not isinstance(p.get("name"), str)
                or len(p["name"]) > 80
                or type(p.get("last_seen")) is not int
            ):
                raise ValueError("Invalid player metadata")
            if not isinstance(p.get("counters"), list) or len(p["counters"]) > 2048:
                raise ValueError("Invalid counters")
            keys = set()
            for v in p["counters"]:
                pair = (v.get("kind"), v.get("key"))
                if (
                    pair[0] not in ("kill", "craft", "biome")
                    or not isinstance(pair[1], str)
                    or not 1 <= len(pair[1]) <= 100
                    or pair in keys
                    or type(v.get("count")) is not int
                    or not 0 <= v["count"] <= 10**12
                ):
                    raise ValueError("Invalid counter")
                keys.add(pair)
            old = c.execute("SELECT data FROM game_players WHERE id=?", (p["id"],)).fetchone()
            if old:
                for v in json.loads(old["data"])["counters"]:
                    if self.count(p, v["kind"], v["key"]) < v["count"]:
                        raise ValueError("Telemetry counters rolled back; review required")
            c.execute(
                "INSERT INTO game_players VALUES (?,?,?) ON CONFLICT(id) DO UPDATE SET name=excluded.name,data=excluded.data",
                (p["id"], p["name"], json.dumps(p)),
            )
        # A missing account may indicate a restored or reset telemetry file.
        if any(r["id"] not in seen for r in c.execute("SELECT id FROM game_players")):
            raise ValueError("Telemetry account disappeared")
        bound = {
            r["player"]: json.loads(r["data"])
            for r in c.execute(
                "SELECT b.player,p.data FROM game_bindings b JOIN game_players p ON p.id=b.game_id"
            )
        }
        for rule in c.execute("SELECT * FROM game_rules").fetchall():
            q = c.execute("SELECT * FROM quests WHERE id=?", (rule["quest_id"],)).fetchone()
            if (
                not q
                or q["completed_at"]
                or q["title"] != rule["title"]
                or q["owner"] != rule["owner"]
            ):
                continue
            subjects = (
                [bound[q["owner"]]]
                if q["owner"] in bound
                else ([] if q["owner"] else list(bound.values()))
            )
            if not any(
                self.count(p, rule["kind"], rule["key"]) >= rule["target"] for p in subjects
            ):
                continue
            recipients = [q["owner"]] if q["owner"] else list(PLAYERS)
            for name in recipients:
                self.store.credit(c, name, q["xp"], f"Game quest #{q['id']}: {q['title']}", None)
            c.execute("UPDATE quests SET completed_at=CURRENT_TIMESTAMP WHERE id=?", (q["id"],))
            if notify:
                c.execute(
                    "INSERT INTO monitor_outbox(message) VALUES (?)",
                    (f"✅ **{q['title']}** • {', '.join(recipients)} • +{q['xp']} Renown",),
                )
        if "Pánik" in bound and self.count(bound["Pánik"], "craft", "category:staff") >= 1:
            inserted = c.execute(
                "INSERT OR IGNORE INTO awards(player,achievement) VALUES ('Pánik','Arcane Awakening')"
            ).rowcount
            if inserted:
                self.store.credit(c, "Pánik", 50, "Game achievement: Arcane Awakening", None)
                if notify:
                    c.execute(
                        "INSERT INTO monitor_outbox(message) VALUES (?)",
                        ("✨ **Arcane Awakening** • Pánik • +50 Renown",),
                    )

    def progress(self, player):
        with self.store.connection() as c:
            row = c.execute(
                "SELECT p.data FROM game_players p JOIN game_bindings b ON p.id=b.game_id WHERE b.player=?",
                (player,),
            ).fetchone()
            return json.loads(row["data"]) if row else None

    def quest_progress(self):
        bound = {name: self.progress(name) for name in PLAYERS}
        with self.store.connection() as c:
            result = {}
            for r in c.execute(
                "SELECT r.* FROM game_rules r JOIN quests q ON q.id=r.quest_id WHERE q.title=r.title AND q.owner IS r.owner"
            ):
                subjects = [bound[r["owner"]]] if r["owner"] else list(bound.values())
                count = max((self.count(p, r["kind"], r["key"]) for p in subjects if p), default=0)
                result[r["quest_id"]] = f" • automaticky {min(count, r['target'])}/{r['target']}"
            return result

    def goals(self, player):
        """Active rules for this player, including progress supplied by the team."""
        with self.store.connection() as c:
            bound = {
                r["player"]: json.loads(r["data"])
                for r in c.execute(
                    "SELECT b.player,p.data FROM game_bindings b JOIN game_players p ON p.id=b.game_id"
                )
            }
            rows = c.execute(
                "SELECT r.*,q.xp FROM game_rules r JOIN quests q ON q.id=r.quest_id WHERE q.title=r.title AND q.owner IS r.owner AND q.completed_at IS NULL AND (q.owner=? OR q.owner IS NULL) ORDER BY q.id",
                (player,),
            ).fetchall()
            result = []
            for row in rows:
                r = dict(row)
                subjects = [bound.get(r["owner"])] if r["owner"] else list(bound.values())
                r["count"] = max(
                    (self.count(p, r["kind"], r["key"]) for p in subjects if p), default=0
                )
                result.append(r)
            return result

    def available_accounts(self, player):
        with self.store.connection() as c:
            return [
                dict(r)
                for r in c.execute(
                    "SELECT p.id,p.name FROM game_players p LEFT JOIN game_bindings b ON b.game_id=p.id WHERE b.player IS NULL OR b.player=? ORDER BY p.name,p.id",
                    (player,),
                )
            ]
