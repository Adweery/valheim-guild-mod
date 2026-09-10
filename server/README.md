# Quest feed pre existujúceho Discord bota

`quest_feed.py` je adaptér k existujúcemu Valheim Guild Bot, nie samostatný bot. Vyžaduje jeho `store.Store`, `store.level` a tabuľky `players`, `quests`, `game_bindings`, `game_players`, `game_rules`; voliteľne `quest_notes`. Nedávaj sem databázu, token ani herné uložené dáta.

V bote po vytvorení WorldMonitor vytvor `QuestFeed(store, world_uid, path)`. V `setup_hook` spusti `asyncio.create_task(feed.run(self))`. Pri zatváraní bota task zruš a počkaj naň pomocou `asyncio.gather(..., return_exceptions=True)`.

Export sa zapisuje každých 10 sekúnd atomicky s právami 0600. Bot a dedikovaný server majú bežať pod rovnakým lokálnym používateľom. Modu nastav v sekcii `[Server]` parameter `QuestFeedPath` na absolútnu cestu exportu a správny `WorldUid`. Pred aktualizáciou modu zálohuj DLL, konfiguráciu, svet a telemetry.json. Aktualizuj dedikovaný server pred klientmi.

Klient žiada snapshot cez existujúce herné RPC každých 5 sekúnd. Server určuje účet z autentifikovaného herného spojenia a posiela iba príslušné osobné a tímové úlohy. Odpoveď neobsahuje Steam ani Discord ID. Nie sú potrebné nové sieťové porty alebo prihlasovacie údaje.

Export používa konzistentné čítanie SQLite a nemení XP, questy ani účty. Príznak dokončenia pochádza z databázy; existujúci bot ostáva jediným správcom odmien. Súbor starší než 180 sekúnd sa neposiela ako aktuálny. Klient označuje neaktuálnu cache a neprehráva staré oznámenia pri prvom načítaní.
