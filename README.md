# Valheim Guild - inštalácia a aktualizácie

Pre partiu RavensOath. Inštalátor vždy sťahuje aktuálne vydanie z tohto repozitára. GitHub účet hráča nie je potrebný.

## Okolité truhly od verzie 1.3.1

Obsah prístupných truhlíc v okruhu **30 metrov** sa obnovuje automaticky, aj keď do nich predmety pridá alebo z nich odoberie iný hráč. Mod číta aktuálny stav, ktorý už hra synchronizovala klientovi; netreba truhly otvárať. Aktualizácia závisí aj od bežného sieťového oneskorenia hry.

`Supplies.ScanRadius` v konfigurácii nastaví rádius od 5 do 100 metrov. Skener používa načítané objekty hry, rešpektuje osobné truhly a wardy a nepreberá vlastníctvo objektov. Spracuje 32 truhlíc každé dve sekundy, najviac 100 najbližších. Pri plnom limite trvá celý cyklus približne osem sekúnd. Vzdialené truhly ostávajú výslovne starším záznamom. Pri zistenej strate prístupu alebo zničení truhlice sa jej obsah zo zoznamu odstráni.

Integračné testy simulujú pridanie, odobratie a vyprázdnenie truhly druhým hráčom, zmenu oprávnení, wardy, dosah a dávkové obnovovanie. Reálny test dvoch pripojených hráčov ešte čaká na manuálne overenie.

## Sken zásob od verzie 1.3.0

Vo **F8 > Zásoby a výbava** uvidíš inventár, nasadené predmety s kvalitou a obsah truhlíc, ktoré si otvoril. Inventár a otvorená truhla sa obnovujú každé dve sekundy; konečný stav sa načíta aj pri zatvorení truhly. Zamknuté a vzdialené truhly sa neprehľadávajú. Od 1.3.1 sa načítajú aj prístupné truhly v nastavenom rádiuse.

Pri predmete vidíš oddelene počet pri sebe a naposledy videný počet v truhlách. Truhla má herné súradnice a čas overenia. Aj nedávny záznam môže byť neaktuálny, ak s truhlou medzičasom pracoval niekto iný. Pred odchodom ju znova otvor.

Tlačidlami −, + a +5 nastavíš vlastný cieľový počet do batoha. Zásoby v truhle ho nesplnia: panel odporučí najprv overiť zásoby a pribaliť chýbajúce kusy. Cieľ zostáva uložený aj keď inventár a truhla ostanú prázdne. Toto je osobný zoznam prípravy, nie automatická zmena crafting questov alebo odmena XP.

Cache je lokálna pre postavu a svet v `BepInEx/config/ValheimGuildSupplies`. Obsahuje najviac 100 naposledy overených truhlíc a 4000 záznamov predmetov. Nezdieľa sa medzi hráčmi ani medzi Macom a Windows. Plánovanie je zatiaľ ručné; automatické odporúčania podľa akcie a bojového štýlu nie sú súčasťou tohto vydania.

Sken nevyžaduje aktualizáciu servera 1.2.0 ani nové sieťové porty. Hru treba pred klientskou aktualizáciou vypnúť. Načítanie pluginu a modelové testy sú overené; otvorenie truhly a vykreslenie novej karty v pripojenej hre ešte vyžadujú manuálnu skúšku.

## Automatické pokračovanie od verzie 1.2.0

Pri štarte bota sa najprv overí uložený svet a zaznamenaná telemetria. Denník čaká na úspešné zosúladenie. Pri prepojení účtu sa okamžite uznajú už zaznamenané splnené ciele. Odmeny sa nepridelia opakovane a historické dokončenia sa neoznamujú do chatu.

Panel automaticky vyberá najviac päť nesplnených cieľov. Uprednostní rozrobené počítadlá a aktuálnu kapitolu; po dokončení doplní ďalšie ciele. Ručné pripnutie prepne panel na vlastný výber. V denníku je tlačidlo na opätovné zapnutie automatického výberu (`Quests.AutoTrackNext`).

Každá úloha uvádza, či je stav doložený svetom, zaznamenanými počítadlami alebo potrebuje ručné potvrdenie. Starý crafting či zabitia pred inštaláciou sa neodhadujú z výbavy. Vo verzii 1.2.0 sa inventár ani truhly ešte neskenovali. Neoveriteľné stavebné a organizačné úlohy zostávajú otvorené.

## Herný denník od verzie 1.1.0

- **F8** otvorí denník osobných a tímových questov. Na Macu môže byť potrebné **Fn + F8** podľa nastavenia funkčných klávesov.
- **Sledovať** pripne najviac päť questov do bočného panela. Dokončené questy uvoľnia miesto.
- **F9** zobrazí alebo skryje panel. Klávesy a veľkosť sa dajú zmeniť v `BepInEx/config/adwery.valheim.guildtelemetry.cfg`.
- Denník zobrazuje kapitoly, aktuálny počet pri automatických úlohách, Renown a prípravu na Swamp. Prepínač Aktívne/Dokončené mení zoznam.
- Dokončenie počas hrania zobrazí krátke oznámenie. Pri pripojení sa staré dokončenia znovu neoznamujú.
- Účet musí byť prepojený cez Discord `/linkgame`. Nepotrebuješ nový token ani ďalšie prihlásenie.

Údaje sa obnovujú približne do 15 sekúnd. Pri výpadku zostane posledný stav označený ako starší. Manuálne questy stále potvrdzuješ cez Discord `/complete`; samotné otvorenie denníka nepridáva XP. Do Discord chatu sa pri obnovovaní panela neposielajú správy.

Server potrebuje mod 1.2.0 a export z bota. Staršie klienty naďalej posielajú progres. Táto verzia neobsahuje mapové šípky ani waypointy.

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

`python build_release.py --version 1.3.1 --dll /cesta/ValheimGuildTelemetry.dll --loader /cesta/BepInExPack.zip --output /cesta/vydanie` pripraví manifest a ZIPy. Pred vydaním treba overiť zostavenie a funkčnosť daného DLL. Skript nevytvára vydanie na GitHube automaticky.

Vydanie vytvor najprv ako draft, nahraj všetky štyri súbory a až potom ho zverejni. Tým je manifest a jeho obsah dostupný spolu. Verejný manifest cez HTTPS a kontrolné súčty chránia konzistenciu prenosu; nejde o nezávislý digitálny podpis autora. Dôvera v aktualizácie závisí od prístupu správcu do tohto repozitára.

Testy používajú izolované dočasné priečinky. Mac: `python3 -m unittest discover -s tests -v`. Windows PowerShell sa testuje na štandardnom Windows runneri v GitHub Actions.

Závislosť a jej zdroje/licencie: [BepInExPack Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/) a [BepInEx](https://github.com/AzumattDev/BepInEx). Balík BepInEx sa v tomto repozitári nedistribuuje.

## Zdrojový kód a zostavenie

V `src/ValheimGuildTelemetry` je mod a v `src/StateTests` testy protokolu. Použi .NET SDK 8. Referenčné DLL hry získaj zo svojej legálnej inštalácie z priečinka Managed, BepInEx a 0Harmony z BepInEx/core. Herné DLL sa nesmú pribaliť do vydania. Pre serverový build používame `assembly_valheim`, `assembly_utils`, `UnityEngine` a `UnityEngine.CoreModule` z dedikovaného servera a `UnityEngine.IMGUIModule`, `UnityEngine.TextRenderingModule`, `UnityEngine.InputLegacyModule` z klienta rovnakej verzie hry.

```sh
dotnet run --project src/StateTests
dotnet build src/ValheimGuildTelemetry -c Release -p:GameReferences=/cesta/referencie -p:LoaderReferences=/cesta/BepInEx/core
```

Serverový adaptér a jeho zapojenie opisuje [server/README.md](server/README.md). UI v reálnej hernej relácii na Macu a Windows ešte čaká na manuálne overenie; zostavenie, modelové testy a načítanie na dedikovanom serveri sú overené.

Zostavenie 1.3.0 navyše vyžaduje vlastné `assembly_guiutils.dll` z Managed priečinka rovnakej verzie hry. Toto DLL sa nedistribuuje.
