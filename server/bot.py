"""Valheim Guild Bot: slash commands only, no privileged intents."""

import asyncio
import json
import logging
import os
import time
from pathlib import Path

import discord
from discord import app_commands
from dotenv import load_dotenv

import swamp_chapter
from link_view import LinkAccountView
from monitor import WorldMonitor
from progress_view import describe_progress
from quest_feed import QuestFeed
from store import ACHIEVEMENTS, PLAYERS, Store, level

logger = logging.getLogger(__name__)

ROOT = Path(__file__).resolve().parent
PLAYER_CHOICES = [app_commands.Choice(name=n, value=n) for n in PLAYERS]
OWNER_CHOICES = [app_commands.Choice(name="Celý tím", value="team"), *PLAYER_CHOICES]
ACHIEVEMENT_CHOICES = [app_commands.Choice(name=a[0], value=a[0]) for a in ACHIEVEMENTS]


def safe(text):
    return discord.utils.escape_markdown(str(text)).replace("@", "@\u200b")


def embed(title, description):
    result = discord.Embed(title=title, description=description, color=0xB58B42)
    result.set_footer(text="Valheim Guild • Renown • Quest log")
    return result


class GuildTree(app_commands.CommandTree):
    async def interaction_check(self, interaction):
        if interaction.guild_id != self.client.guild_id:
            await interaction.response.send_message(
                "Bot je určený iba pre svoj Valheim server.", ephemeral=True
            )
            return False
        return True

    async def on_error(self, interaction, error):
        original = getattr(error, "original", error)
        if isinstance(original, ValueError):
            message = str(original)
        elif isinstance(error, app_commands.CheckFailure):
            message = "Tento príkaz je dostupný iba správcovi servera (Manage Server)."
        else:
            logger.error("Command failed: %s", type(original).__name__)
            message = "Príkaz sa nepodarilo dokončiť. Skús ho zopakovať alebo kontaktuj správcu."
        if interaction.response.is_done():
            await interaction.followup.send(message, ephemeral=True)
        else:
            await interaction.response.send_message(message, ephemeral=True)


class GuildBot(discord.Client):
    def __init__(self, guild_id, db, monitor_config=None):
        intents = discord.Intents.none()
        intents.guilds = True
        super().__init__(intents=intents, allowed_mentions=discord.AllowedMentions.none())
        self.guild_id = guild_id
        self.db = db
        self.monitor = WorldMonitor(db, monitor_config) if monitor_config else None
        self.monitor_task = None
        self.feed_task = None
        self.feed = (
            QuestFeed(
                db,
                monitor_config["uid"],
                monitor_config["quest_feed_path"],
                self.monitor.initialized,
            )
            if monitor_config and monitor_config.get("quest_feed_path")
            else None
        )
        self.tree = GuildTree(self)
        register_commands(self)

    async def setup_hook(self):
        self.db.ensure_guild(self.guild_id)
        guild = discord.Object(id=self.guild_id)
        self.tree.copy_global_to(guild=guild)
        commands = await self.tree.sync(guild=guild)
        logger.info("Synchronized %s guild commands.", len(commands))
        if self.monitor:
            self.monitor_task = asyncio.create_task(self.monitor.run(self))

        if self.feed:
            self.feed_task = asyncio.create_task(self.feed.run(self))

    async def close(self):
        if self.feed_task:
            self.feed_task.cancel()
            await asyncio.gather(self.feed_task, return_exceptions=True)
        if self.monitor_task:
            self.monitor_task.cancel()
            await asyncio.gather(self.monitor_task, return_exceptions=True)
        await super().close()

    async def on_ready(self):
        logger.info("Bot connected and ready.")


def register_commands(client):
    tree, db = client.tree, client.db

    async def run(method, *args):
        return await asyncio.to_thread(method, *args)

    async def selected(interaction, player):
        result = (
            await run(db.player, player) if player else await run(db.identity, interaction.user.id)
        )
        if not result:
            raise ValueError(
                "Účet ešte nie je prepojený. Vyber parameter player alebo požiadaj admina o /bind."
            )
        return result

    @tree.command(description="Stav RavensOath a automatického sledovania boss questov.")
    async def server(interaction: discord.Interaction):
        if not client.monitor:
            await interaction.response.send_message(
                "Sledovanie servera nie je nastavené.", ephemeral=True
            )
            return
        state = await run(client.monitor.status)
        fresh = state.get("received_at", 0) > time.time() - 180 and not state.get("error")
        status = (
            ("Online" if state.get("active") else "Zastavený")
            if fresh
            else "Spojenie sa overuje / údaje nie sú aktuálne"
        )
        saved = f"<t:{int(state['saved_at'])}:R>" if state.get("saved_at") else "zatiaľ neoverené"
        bosses = (
            ", ".join(k.replace("defeated_", "") for k in state.get("keys", [])) or "zatiaľ žiadny"
        )
        telemetry = state.get("telemetry")
        mod_status = (
            f"pripojený, zachytené účty: {len(telemetry.get('players', []))}"
            if telemetry
            else "zatiaľ bez údajov"
        )
        await interaction.response.send_message(
            embed=embed(
                "RavensOath • server",
                f"**{status}**\nPosledný save: {saved}\nBoss progres: {bosses}\nHerný mod: {mod_status}\n\nEikthyr a Elder sa potvrdia automaticky po uložení sveta. Kontrola každých 60 sekúnd. Herný mod sleduje kill credit, crafting a biomy. /gameprogress zobrazí údaje prepojeného účtu. Subjektívne questy ostávajú ručné.",
            )
        )

    @tree.command(description="Progres z herného modu; výsledok vidíš iba ty.")
    @app_commands.choices(player=PLAYER_CHOICES)
    async def gameprogress(interaction: discord.Interaction, player: str | None = None):
        p = await selected(interaction, player)
        data = await run(client.monitor.telemetry.progress, p["name"]) if client.monitor else None
        if not data:
            await interaction.response.send_message(
                "Zatiaľ bez prepojeného herného účtu. Nainštaluj mod, pripoj sa na RavensOath a admin použije /linkgame.",
                ephemeral=True,
            )
            return
        goals = await run(client.monitor.telemetry.goals, p["name"])
        await interaction.response.send_message(
            embed=embed(f"{p['name']} • herný progres", describe_progress(data, goals)),
            ephemeral=True,
        )

    @tree.command(description="Admin: vyber hernú postavu zo zoznamu a prepoj ju s hráčom.")
    @app_commands.default_permissions(manage_guild=True)
    @app_commands.checks.has_permissions(manage_guild=True)
    @app_commands.choices(player=PLAYER_CHOICES)
    @app_commands.describe(player="Komu chceš priradiť hernú postavu?")
    async def linkgame(interaction: discord.Interaction, player: str):
        if not client.monitor:
            raise ValueError("Sledovanie servera nie je nastavené.")
        linked = await run(client.monitor.telemetry.progress, player)
        if linked:
            await interaction.response.send_message(
                f"✅ **{player} → {safe(linked['name'])}** už je prepojený. Použi /gameprogress.",
                ephemeral=True,
            )
            return
        accounts = await run(client.monitor.telemetry.available_accounts, player)
        if not accounts:
            await interaction.response.send_message(
                "Zatiaľ nemám žiadnu voľnú hernú postavu. Hráč musí spustiť Valheim s modom, pripojiť sa na RavensOath a počkať približne minútu. Potom zopakuj /linkgame.",
                ephemeral=True,
            )
            return
        await interaction.response.send_message(
            f"Vyber hernú postavu hráča **{player}**. Meno vidíš aj pri výbere postavy vo Valheime.",
            view=LinkAccountView(
                client.monitor.telemetry, player, accounts, interaction.user.id, client.guild_id
            ),
            ephemeral=True,
        )

    @tree.command(description="Profil hráča, classa, level a Renown.")
    @app_commands.choices(player=PLAYER_CHOICES)
    async def profile(interaction: discord.Interaction, player: str | None = None):
        p = await selected(interaction, player)
        lv, title = level(p["renown"])
        awards = await run(db.awards, p["name"])
        await interaction.response.send_message(
            embed=embed(
                f"{p['name']} • {p['class']}",
                f"**Level {lv}: {title}**\n**{p['renown']} Renown**\n"
                f"Achievementy: {', '.join(sorted(awards)) or 'Zatiaľ žiadne'}",
            )
        )

    @tree.command(description="Čo pripraviť na Swamp a aktuálny stav Chapter II.")
    async def swamp(interaction: discord.Interaction):
        content = await run(swamp_chapter.overview, db)
        await interaction.response.send_message(
            embed=embed(swamp_chapter.CHAPTER, content), ephemeral=True
        )

    @tree.command(description="Osobné a tímové questy. Stránkovanie po 5 questoch.")
    @app_commands.choices(player=PLAYER_CHOICES)
    @app_commands.choices(
        chapter=[
            app_commands.Choice(name="Swamp", value=swamp_chapter.CHAPTER),
            app_commands.Choice(name="A New World", value="Chapter I - A New World"),
            app_commands.Choice(name="Bonus", value="Bonus - RavensOath"),
        ]
    )
    async def quests(
        interaction: discord.Interaction,
        player: str | None = None,
        completed: bool = False,
        page: app_commands.Range[int, 1, 10000] = 1,
        chapter: str | None = None,
    ):
        rows = await run(db.quests)
        rows = [
            q
            for q in rows
            if bool(q["completed_at"]) == completed
            and (not player or q["owner"] in (None, player))
            and (chapter is None or q["chapter"] == chapter)
        ]
        pages = max(1, (len(rows) + 4) // 5)
        if page > pages:
            raise ValueError(f"Posledná stránka je {pages}.")
        progress = await run(client.monitor.telemetry.quest_progress) if client.monitor else {}
        notes = await run(swamp_chapter.notes, db)
        content = "\n\n".join(
            f"**#{q['id']} {safe(q['title'])}**\n"
            f"{safe(q['chapter'])} • {q['owner'] or 'Celý tím'} • +{q['xp']} Renown"
            + (" každému" if not q["owner"] else "")
            + progress.get(q["id"], "")
            + ("\n" + safe(notes[q["id"]]) if q["id"] in notes else "")
            for q in rows[(page - 1) * 5 : page * 5]
        )
        await interaction.response.send_message(
            embed=embed(
                f"{'Dokončené' if completed else 'Aktívne'} questy • {page}/{pages}",
                content or "Žiadne questy v tomto výbere.",
            )
        )

    @tree.command(description="Dokonči svoj alebo tímový quest a pripíš Renown iba raz.")
    async def complete(interaction: discord.Interaction, quest_id: int):
        await interaction.response.defer()
        q, recipients = await run(
            db.complete,
            quest_id,
            interaction.user.id,
            interaction.permissions.manage_guild,
        )
        await interaction.followup.send(
            embed=embed(
                "Quest dokončený",
                f"**{safe(q['title'])}**\n+{q['xp']} Renown: {', '.join(recipients)}",
            )
        )

    @tree.command(description="Poradie hráčov podľa Renown.")
    async def leaderboard(interaction: discord.Interaction):
        players = sorted(await run(db.party), key=lambda p: -p["renown"])
        await interaction.response.send_message(
            embed=embed(
                "Renown leaderboard",
                "\n".join(
                    f"{i}. **{p['name']}** • {p['renown']} Renown • {level(p['renown'])[1]}"
                    for i, p in enumerate(players, 1)
                ),
            )
        )

    @tree.command(description="Vaša štvorčlenná Valheim partia.")
    async def party(interaction: discord.Interaction):
        await interaction.response.send_message(
            embed=embed(
                "Valheim party",
                "\n\n".join(
                    f"**{p['name']} • {p['class']}**\n{p['renown']} Renown • {level(p['renown'])[1]}"
                    + (" • účet prepojený" if p["discord_id"] else " • čaká na /bind")
                    for p in await run(db.party)
                ),
            )
        )

    @tree.command(description="Class-specific achievementy a ich odmeny.")
    @app_commands.choices(player=PLAYER_CHOICES)
    async def achievements(interaction: discord.Interaction, player: str | None = None):
        earned = await run(db.awards, player) if player else set()
        content = "\n\n".join(
            f"{'✅' if a[0] in earned else '▫️'} **{a[0]}** • {a[1]} • +{a[3]}\n{a[2]}"
            for a in ACHIEVEMENTS
            if not player or a[1] == PLAYERS[player]
        )
        await interaction.response.send_message(embed=embed("Achievementy", content))

    @tree.command(description="Admin: prepoj postavu s Discord členom servera.")
    @app_commands.default_permissions(manage_guild=True)
    @app_commands.checks.has_permissions(manage_guild=True)
    @app_commands.choices(player=PLAYER_CHOICES)
    async def bind(interaction: discord.Interaction, player: str, member: discord.Member):
        if member.bot:
            raise ValueError("Postavu nemožno prepojiť s botom.")
        await run(db.bind, player, member.id)
        await interaction.response.send_message(
            f"{player} je prepojený s účtom {safe(member.display_name)}.",
            ephemeral=True,
        )

    @tree.command(description="Admin: vytvor osobný alebo tímový quest.")
    @app_commands.default_permissions(manage_guild=True)
    @app_commands.checks.has_permissions(manage_guild=True)
    @app_commands.choices(owner=OWNER_CHOICES)
    async def addquest(
        interaction: discord.Interaction,
        title: app_commands.Range[str, 1, 150],
        renown: app_commands.Range[int, 0, 10000],
        owner: str = "team",
        chapter: app_commands.Range[str, 1, 100] = "Chapter I - A New World",
    ):
        qid = await run(db.add_quest, title, renown, None if owner == "team" else owner, chapter)
        await interaction.response.send_message(f"Quest #{qid} bol vytvorený.", ephemeral=True)

    @tree.command(description="Admin: uprav názov, odmenu a vlastníka nedokončeného questu.")
    @app_commands.default_permissions(manage_guild=True)
    @app_commands.checks.has_permissions(manage_guild=True)
    @app_commands.choices(owner=OWNER_CHOICES)
    async def editquest(
        interaction: discord.Interaction,
        quest_id: int,
        title: app_commands.Range[str, 1, 150],
        renown: app_commands.Range[int, 0, 10000],
        owner: str,
    ):
        await run(db.edit_quest, quest_id, title, renown, None if owner == "team" else owner)
        await interaction.response.send_message(f"Quest #{quest_id} bol upravený.", ephemeral=True)

    @tree.command(description="Admin: pridaj alebo odober Renown (záporné amount odoberá).")
    @app_commands.default_permissions(manage_guild=True)
    @app_commands.checks.has_permissions(manage_guild=True)
    @app_commands.choices(player=PLAYER_CHOICES)
    async def give_xp(
        interaction: discord.Interaction,
        player: str,
        amount: app_commands.Range[int, -10000, 10000],
        reason: app_commands.Range[str, 1, 300],
    ):
        await run(db.adjust, player, amount, reason, interaction.user.id)
        await interaction.response.send_message(
            f"{player}: {amount:+} Renown. Zmena je zapísaná v histórii.",
            ephemeral=True,
        )

    @tree.command(description="Admin: potvrď class achievement a pripíš odmenu iba raz.")
    @app_commands.default_permissions(manage_guild=True)
    @app_commands.checks.has_permissions(manage_guild=True)
    @app_commands.choices(player=PLAYER_CHOICES, achievement=ACHIEVEMENT_CHOICES)
    async def award(interaction: discord.Interaction, player: str, achievement: str):
        xp = await run(db.award, player, achievement, interaction.user.id)
        await interaction.response.send_message(
            embed=embed("Achievement získaný", f"**{player}: {achievement}**\n+{xp} Renown")
        )


def main():
    if (ROOT / "oracle-deployment.json").exists():
        raise SystemExit("Bot je nasadený na Oracle; táto lokálna kópia je archív.")
    os.umask(0o077)
    load_dotenv(ROOT / ".env")
    token = os.getenv("DISCORD_TOKEN", "").strip()
    guild = os.getenv("DISCORD_GUILD_ID", "").strip()
    if not token or not guild.isdigit():
        raise SystemExit("Doplň DISCORD_TOKEN a DISCORD_GUILD_ID do lokálneho .env.")
    path = Path(os.getenv("DATABASE_PATH", "data/valheim.sqlite3"))
    if not path.is_absolute():
        path = ROOT / path
    logging.basicConfig(level=logging.INFO, format="%(asctime)s %(levelname)s %(message)s")
    # Never log exceptions containing HTTP headers or credentials.
    try:
        config_path = ROOT / "server-monitor.json"
        monitor_config = json.loads(config_path.read_text()) if config_path.exists() else None
        db = Store(path)
        swamp_chapter.install(db)
        GuildBot(int(guild), db, monitor_config).run(token, log_handler=None)
    except discord.LoginFailure:
        raise SystemExit("Discord token nie je platný. Obnov ho v Developer Portali.") from None
    except discord.HTTPException as exc:
        raise SystemExit(
            f"Discord odmietol pripojenie (HTTP {exc.status}). Skontroluj invite a server ID."
        ) from None


if __name__ == "__main__":
    main()
