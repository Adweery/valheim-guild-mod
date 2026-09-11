# Quest feed integration for an existing Discord bot

`quest_feed.py` adapts the existing Valheim Guild Bot to the in-game journal. It is not a standalone bot. It requires the bot's `store.Store`, `store.level` and the tables `players`, `quests`, `game_bindings`, `game_players`, `game_rules` and `monitor_state`; `quest_notes` is optional. Never commit databases, tokens or game saves.

## Wiring the feed

After creating the world monitor, construct `QuestFeed(store, world_uid, path, ready=monitor.initialized)`. Start `asyncio.create_task(feed.run(self))` in the bot's `setup_hook`. On shutdown, cancel the task and await it using `asyncio.gather(..., return_exceptions=True)`.

The feed is exported atomically every 10 seconds with permissions `0600`. Run the bot and dedicated server as the same local OS user. Set the plugin's `[Server]` `QuestFeedPath` to the absolute export path and configure the correct `WorldUid` for your world. Back up the DLL, configuration, world and `telemetry.json` before server updates. Update the dedicated server before clients when changing the protocol.

The client requests a snapshot through existing game RPC every five seconds. The server resolves the account from the authenticated game connection and returns only that account's personal and team quests. The response does not contain Steam or Discord IDs. No new ports or credentials are required.

## Data and freshness

The export reads a consistent SQLite snapshot without changing XP, quests or accounts. Completion comes from the database; the existing bot remains responsible for rewards. Exports older than 180 seconds are withheld as current data. The client marks stale cached data and does not replay old completion notifications on initial load.

## Progress reconciliation since 1.2.0

`bot.py`, `monitor.py` and `telemetry.py` are updated modules from the existing bot, not a complete distribution. Preserve its other modules and configuration.

`WorldMonitor.initialized` is set only after the first successful world poll, and `QuestFeed` waits for it. Initial reconciliation uses `notify=False` without deleting the existing message queue. Account linking re-evaluates recorded counters inside one SQLite transaction with `notify=False`. Subsequent live completions retain normal notifications.
