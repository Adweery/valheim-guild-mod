# Valheim Guild - inštalácia a aktualizácie

Pre partiu RavensOath. Inštalátor vždy sťahuje aktuálne vydanie z tohto repozitára. GitHub účet hráča nie je potrebný.

## Windows

1. Stiahni [inštalátor pre Windows](https://github.com/Adweery/valheim-guild-mod/releases/latest/download/Valheim-Guild-Installer-Windows.zip) a rozbaľ celý ZIP.
2. Vypni Valheim a otvor `Install-Valheim.cmd`. Súbor `.ps1` nechaj vedľa neho.
3. Po hlásení „Hotovo“ spusti hru normálne cez Steam.

## Mac

1. Stiahni [inštalátor pre Mac](https://github.com/Adweery/valheim-guild-mod/releases/latest/download/Valheim-Guild-Installer-Mac.zip) a rozbaľ ZIP.
2. Vypni Valheim a otvor `Install-Valheim.command`.
3. Po inštalácii spúšťaj hru cez `Start-Valheim-Guild-Mac.command` v priečinku hry, prípadne cez už nastavený launcher Valheim Guild. Na Apple Silicon je potrebná Rosetta.

Ak systém nedovolí spustiť stiahnutý skript, pošli Adamovi presnú hlášku. Inštalátor nie je podpísaný komerčným certifikátom. Nevypínaj systémovú ochranu. Windows spúšťa PowerShell s dočasnou politikou len pre tento proces, systémové nastavenie nemení.

## Ďalšia aktualizácia

Vypni hru a znova spusti ten istý inštalátor. Sám si stiahne aktuálny mod. Nepotrebuješ nový ZIP pri každej verzii modu. Ak sa niekedy zmení samotný formát inštalácie, Adam oznámi potrebu nového inštalátora.

- Nájde Steam knižnicu; ak nájde viac inštalácií alebo žiadnu, vyžiada priečinok hry.
- Každé sťahovanie overí pomocou SHA-256 z manifestu vydania.
- Pri bežiacej hre zastaví inštaláciu. Hru sám nevypína ani nespúšťa.
- Pred zmenou uloží pôvodné súbory do `ValheimGuildBackups` v priečinku hry. Pri chybe zápisu skúsi pôvodné súbory obnoviť; zálohu zachová aj pre ručnú obnovu.
- Nezasahuje do svetov, postáv, XP ani Discord účtov. Existujúce cudzie mody a konfiguráciu BepInEx zachová.
- Pri chýbajúcom BepInEx stiahne pevne určený BepInExPack Valheim 5.4.2350 priamo od vydavateľa na Thunderstore. Pri inej alebo nekompletnej existujúcej verzii sa zastaví, aby ju neprepísal.
- Dočasné odpojenie internetu alebo nedostupné vydanie zastaví inštaláciu pred zmenou hry.

Po prvom pripojení na RavensOath prepoj herný účet cez správcu bota a skús `/gameprogress` v súkromnom testovacom kanáli.

## Pre správcu vydaní

Zdroj pravdy pre klientov: `releases/latest/download/latest.json`. Manifest odkazuje na konkrétne nemenné vydanie modu, nie na plávajúci ZIP. Vydávaj nové tagy; nikdy nenahrádzaj balík pod existujúcim tagom.

Inštalátor a mod sú samostatné časti. Oprava bota v Discorde nevyžaduje klientsku aktualizáciu. Zmenu sieťového protokolu treba zladiť so serverom pred nastavením nového vydania ako latest.

`python build_release.py --version 1.0.1 --dll /cesta/ValheimGuildTelemetry.dll --loader /cesta/BepInExPack.zip --output /cesta/vydanie` pripraví manifest a ZIPy. Pred vydaním treba overiť zostavenie a funkčnosť daného DLL. Skript nevytvára vydanie na GitHube automaticky.

Vydanie vytvor najprv ako draft, nahraj všetky štyri súbory a až potom ho zverejni. Tým je manifest a jeho obsah dostupný spolu. Verejný manifest cez HTTPS a kontrolné súčty chránia konzistenciu prenosu; nejde o nezávislý digitálny podpis autora. Dôvera v aktualizácie závisí od prístupu správcu do tohto repozitára.

Testy používajú izolované dočasné priečinky. Mac: `python3 -m unittest discover -s tests -v`. Windows PowerShell sa testuje na štandardnom Windows runneri v GitHub Actions.

Závislosť a jej zdroje/licencie: [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) a [BepInEx](https://github.com/AzumattDev/BepInEx). Balík BepInEx sa v tomto repozitári nedistribuuje.
