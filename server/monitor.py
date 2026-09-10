"""Poll a read-only collector; award verified world milestones transactionally."""

import asyncio
import json
import logging
import subprocess
import time

from telemetry import Telemetry

BOSSES = {"defeated_eikthyr": (4, "Zabiť Eikthyra"), "defeated_gdking": (5, "Zabiť Eldera")}
logger = logging.getLogger(__name__)


class WorldMonitor:
    def __init__(self, db, config):
        self.db, self.config = db, config
        self.initialized = asyncio.Event()
        with db.connection() as connection:
            connection.execute(
                "CREATE TABLE IF NOT EXISTS monitor_state (id INTEGER PRIMARY KEY, data TEXT NOT NULL)"
            )
            connection.execute(
                "CREATE TABLE IF NOT EXISTS monitor_outbox (id INTEGER PRIMARY KEY, message TEXT NOT NULL, sent INTEGER NOT NULL DEFAULT 0)"
            )

        self.telemetry = Telemetry(db, config["world"], config["uid"])

    def ingest(self, snapshot, notify=True):
        if (
            snapshot.get("schema") != 1
            or snapshot.get("version") != 41
            or snapshot.get("world") != self.config["world"]
            or str(snapshot.get("uid")) != str(self.config["uid"])
        ):
            raise ValueError("World identity or format mismatch")
        if not isinstance(snapshot.get("keys"), list) or not all(
            isinstance(k, str) for k in snapshot["keys"]
        ):
            raise ValueError("Invalid world keys")
        for key in ["saved_at", "checked_at", "save_number"]:
            if type(snapshot.get(key)) not in (int, float) or snapshot[key] < 0:
                raise ValueError("Invalid world metadata")
        if type(snapshot.get("active")) is not bool:
            raise ValueError("Invalid service state")
        awarded = []
        with self.db.connection() as connection:
            connection.execute("BEGIN IMMEDIATE")
            previous = connection.execute("SELECT data FROM monitor_state WHERE id=1").fetchone()
            if previous:
                old = json.loads(previous["data"])
                if snapshot["save_number"] < old.get("save_number", 0):
                    raise ValueError("Save rolled back; manual review required")
            for key in snapshot["keys"]:
                if key not in BOSSES:
                    continue
                quest_id, title = BOSSES[key]
                quest = connection.execute(
                    "SELECT * FROM quests WHERE id=?", (quest_id,)
                ).fetchone()
                if not quest or quest["title"] != title or quest["owner"] or quest["completed_at"]:
                    continue
                for player in connection.execute("SELECT name FROM players").fetchall():
                    self.db.credit(
                        connection,
                        player["name"],
                        quest["xp"],
                        f"Server {snapshot['world']} / {key} / save {snapshot['save_number']}",
                        None,
                    )
                connection.execute(
                    "UPDATE quests SET completed_at=CURRENT_TIMESTAMP, completed_by=NULL WHERE id=?",
                    (quest_id,),
                )
                message = (
                    f"⚔️ **{title}** dokončený na RavensOath. **+{quest['xp']} Renown každému!**"
                )
                if notify:
                    connection.execute("INSERT INTO monitor_outbox(message) VALUES (?)", (message,))
                awarded.append(quest_id)
            self.telemetry.ingest(connection, snapshot.get("telemetry"), notify=notify)
            state = dict(snapshot, received_at=time.time(), error=None)
            connection.execute(
                "INSERT OR REPLACE INTO monitor_state(id,data) VALUES (1,?)", (json.dumps(state),)
            )
        return awarded

    def status(self):
        with self.db.connection() as connection:
            row = connection.execute("SELECT data FROM monitor_state WHERE id=1").fetchone()
            return json.loads(row["data"]) if row else {}

    def record_error(self, error):
        state = self.status()
        state.update(error=type(error).__name__, error_at=time.time())
        with self.db.connection() as connection:
            connection.execute(
                "INSERT OR REPLACE INTO monitor_state(id,data) VALUES (1,?)", (json.dumps(state),)
            )

    def poll(self, notify=True):
        result = subprocess.run(
            self.config["command"], capture_output=True, text=True, timeout=20, check=False
        )
        if result.returncode or len(result.stdout) > 2000000:
            raise ValueError("Collector unavailable")
        return self.ingest(json.loads(result.stdout), notify=notify)

    async def deliver(self, client):
        channel = client.get_channel(self.config["channel_id"])
        if channel is None:
            channel = await client.fetch_channel(self.config["channel_id"])
        if channel.guild.id != client.guild_id:
            raise ValueError("Notification channel belongs to another guild")
        with self.db.connection() as connection:
            rows = connection.execute(
                "SELECT * FROM monitor_outbox WHERE sent=0 ORDER BY id LIMIT 10"
            ).fetchall()
        for row in rows:
            await channel.send(row["message"])
            with self.db.connection() as connection:
                connection.execute("UPDATE monitor_outbox SET sent=1 WHERE id=?", (row["id"],))

    async def run(self, client):
        await client.wait_until_ready()
        while not client.is_closed():
            try:
                await asyncio.to_thread(self.poll, self.initialized.is_set())
                self.initialized.set()
                await self.deliver(client)
            except asyncio.CancelledError:
                raise
            except Exception as error:
                await asyncio.to_thread(self.record_error, error)
                logger.warning("World monitor check failed: %s", type(error).__name__)
            await asyncio.sleep(60)
