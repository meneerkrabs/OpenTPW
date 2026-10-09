# OpenTPW: plan naar een volledig werkende game

Peildatum: 9 oktober 2026. Status: goedgekeurd voor gefaseerde uitvoering.

## Doel en definitie van klaar

OpenTPW wordt een zelfstandig onderhouden herimplementatie waarmee een gebruiker
met originele gamebestanden de volledige offline Theme Park World-game kan spelen.
Apple Silicon/macOS is het eerste platform; Windows en Linux volgen via dezelfde
gamekern. Een demo, ronddraaiend model, succesvolle build of native executable is
niet gelijk aan een complete game.

Volledige offline acceptatie omvat de vier werelden, scenario's en ontgrendeling,
parkbouw, attracties en achtbanen, bezoekers, personeel, economie, research,
adviseur, audio, UI, camera/first-person en betrouwbaar opslaan/laden. Per onderdeel
wordt het verschil met de gekozen originele versie expliciet bijgehouden.
Herstel van de verdwenen onlinedienst is een afzonderlijk, later project: dit was
uitgesloten in de eerder goedgekeurde scope en wordt niet stilzwijgend toegevoegd.
Hetzelfde geldt voor Theme Park Inc (Sim Coaster) en de sandboxmodi met TPW-, TPI- of
gecombineerde content: doelen na de offline TPW-acceptatie, met eigen gates en
zonder invloed op het kritieke pad hierboven (zie README en docs/THEME-PARK-INC.md).

`docs/FEATURE-MATRIX.md` scheidt implementatie, macOS-/Windows-/Linux-kwalificatie
en originele VM-fidelity. Een tussengate mag een benoemde subset accepteren met
expliciete unsupported-diagnostiek. Eindacceptatie vereist alle offline functies,
attracties en benodigde opcodes van de geselecteerde editie; `missing`,
`approximated`, onbekende vereisten en unsupported vereisten blijven blockers.
Een passing build, zelfconsistente replay of prototype is geen reference-verificatie.

## Gecontroleerde uitgangssituatie

Dit is de inspectiebaseline van upstream `453e779`, niet de actuele status van
gelijktijdige M0/M1-edits. Nieuwe implementatie- of testclaims vereisen verse evidence.

- Fork: https://github.com/meneerkrabs/OpenTPW; upstream `main` op `453e779`.
- Upstream laatste commit op de peildatum: 7 september 2026, editor opnieuw aangesloten.
- .NET 8/C#, Veldrid, eigen bestandsparsers en bestaand MSTest-project.
- `source/OpenTPW/World/Level.cs` maakt lobby-eilanden, geen speelbaar park.
- `source/OpenTPW.Files/Public/MapFile.cs` reconstrueert geen kaart.
- `source/OpenTPW/World/Ride.cs` heeft geen zichtbaar attractiemodel; de VM heeft
  onvolledige parsing en animatie-opcodes. Geen functionerende originele ritcyclus.
- `source/OpenTPW/Client/Renderer.cs` forceert Vulkan en bevat twee buildfouten
  bij de huidige .NET 8-toolchain. Paden worden op Windows-separators vastgezet.
- Native SPIR-V-library in de huidige dependency is macOS x86_64, niet arm64.
- De oude `apple-silicon`-branch is een afwijkende architectuur uit maart 2024;
  geen veilige wholesale merge met `main`.
- De aangeleverde ISO is lokaal gedownload en SHA-1 gecontroleerd tegen de
  Archive.org-metadata: `e47675b295a958f82b9b21dee0546a5ad9eea1e8`.
  Assets blijven buiten git; integratietests gebruiken `OPENTPW_GAME_PATH`.
- Exact editielabel, locale en patchniveau zijn onbekend totdat asset-/binary-evidence
  deze identificeert; de ISO-naam of checksum bewijst die identificatie niet.
- Lokaal geverifieerde executable-hashes en PE-informatie staan in
  `docs/RECOMPILATION-ASSESSMENT.md`; dit bewijst geen runnable reference of fidelity.

## Routekeuze: herimplementatie plus gericht reverse engineering

De bestaande C#-engine blijft voorlopig de basis. Gebruik de originele executable
als gedragsreferentie, niet als ongemerkt meegeleverde runtime. Onderzoek originele
logica gericht bij onduidelijke formaten, simulatieberekeningen en scriptopcodes.
Vervang deze aanpak alleen na een meetbare proef die aantoonbaar werk bespaart.

| Route | Nut | Beperking / besluit |
| --- | --- | --- |
| OpenTPW C# herimplementatie | Bestaande parsers/rendering hergebruiken; snel verticale slices | Ontbrekende simulatie moet werkelijk worden geïmplementeerd; voorkeursroute |
| Matching decompilation met Ghidra/reccmp | Originele functies reconstrueren en vergelijken | Compiler/ABI en binary eerst verifiëren; gericht inzetten |
| Static recompilation originele executable | Originele machinetaal naar nieuwe native code | Windows PE/x86, imports, callbacks en graphics-runtime zijn niet opgelost door consoletools |
| Wine/compatibility | Originele game als referentie kunnen uitvoeren | Geen voltooide OpenTPW-port; apart van productacceptatie |
| Rosetta voor huidige Intel-Mac libraries | Snelle tijdelijke Mac-bootstrap | Niet native arm64; niet het eindproduct of langetermijnfundament |
| .NET NativeAOT | Later startup/distributie van eigen engine verbeteren | Herstelt geen originele gamecode; reflection/native dependencies eerst kwalificeren |

### Lessen uit ontwikkelingen sinds 2024

1. N64Recomp vertaalt MIPS/N64-code; XenonRecomp richt zich op PowerPC/XEX.
   Het succes van deze projecten is geen bewijs dat een Windows-x86-game met
   dezelfde tools automatisch werkt. Frontend, ABI en platform-runtime verschillen.
2. reccmp en LEGO Island laten een bruikbaar Windows-spoor zien: bron reconstrueren,
   functies tegen het origineel vergelijken, vervolgens platformonderdelen porten.
   Matching accuracy en functionele compleetheid blijven aparte kwaliteitsmaten.
3. isle-portable laat zien dat originele gameplay en vervangbare rendering/input/audio
   afzonderlijke werkzaamheden zijn. Neem die grens over; geen onnodige engine-rewrite.
4. SDL 3.2 kwam in januari 2025 uit, inclusief een moderne GPU-API. Dit is een kandidaat
   voor een toekomstige backend als de bestaande Veldrid-stack aantoonbaar blokkeert,
   geen reden om nu een functionerende parser/gamekern opnieuw te schrijven.
5. Apple's aangekondigde algemene Rosetta-ondersteuning loopt door macOS 27;
   daarna blijft beperktere ondersteuning voor bepaalde oudere games over.
   Plan daarom echte arm64-native dependencies en test die apart.
6. OpenTPW issue #31 meldt dat TGQ-video's EA TQI-payloads bevatten en met FFmpeg/VLC
   kunnen worden gelezen. Verifieer dit met de gekozen assets voordat een nieuwe
   videodecoder wordt gebouwd; de melding is geen reeds geleverde videofunctie.

### Beslissende recompilation-proef

Budget: maximaal 16 engineer-uren als beslisexperiment, geen leveringsbelofte.
Inventariseer de originele PE/executable en imports zonder installer/crack uit te
voeren. Selecteer één pure parser of simulatiefunctie met bekende input/output.
Vergelijk Ghidra-assisted reconstructie en, alleen indien aantoonbaar ondersteund,
static lifting met een implementatie in de bestaande C#-kern. Vereis reproduceerbare
outputs, een complete runtime-afhankelijkhedenlijst en minder verwacht totaalwerk.
Zonder dat bewijs geen pivot naar een nieuwe recompilation-toolchain.
Verwijder of omzeil zelf geen kopieerbeveiliging als onderdeel van deze proef; zie
"Herkomst van spelregels" voor de toegestane statische analyse.

### Herkomst van spelregels (besluit 9 oktober 2026)

Een spelregel geldt alleen als origineel gedrag als hij herleid is naar logica in een
originele executable: voor Theme Park World de PowerPC-executable `SimTheme Park`
(Mac-cd, november 2000, PEF, onversleuteld) en haar gedeelde bibliotheken; voor
Theme Park Inc `Game.exe`. Een regel die alleen uit een handleiding, website,
community-bron of speltest komt, blijft `[APPROX]`, hoe aannemelijk ook. Databestanden
blijven `[DATA]`-herkomst: zij bewijzen waarden, niet de regel die ze gebruikt.

De Theme Park Inc-`Game.exe` van de cd is SafeDisc-versleuteld. Op besluit van de
projecteigenaar mag de ontsleutelde no-CD-`Game.exe` die op dezelfde cd meekomt
(`WIN10FIX+NOCDFIX/noCD Crack/tpinc_nocd/Game.exe`, zelfde sectie-indeling als het
origineel) voor statische analyse gebruikt worden. Grondslag: decompilatie voor
interoperabiliteit door een rechtmatige gebruiker (art. 6 Softwarerichtlijn
2009/24/EG, art. 45m Auteurswet). "Abandonware" is geen juridische grondslag: het
auteursrecht ligt bij EA. Voorwaarden:

- Geen executable, ontsleutelde code, disassembly-dumps of crack-bestanden in git,
  issues of artifacts; alleen eigen beschrijvingen met hash, functie-/adresverwijzing.
- Clean-room: regels worden in eigen woorden beschreven en opnieuw geïmplementeerd;
  geen gekopieerde of mechanisch vertaalde originele code.
- Alleen voor interoperabiliteit van OpenTPW; OpenTPW verspreidt of vereist geen
  no-CD-bestanden en spelers blijven hun eigen originele exemplaar nodig hebben.
- De Windows-TPW-executable (`TP.ICD`) valt hier niet onder zolang er geen
  vergelijkbaar besluit is.

Bewijsverwijzingen krijgen een eigen label, bijvoorbeeld
`[BIN:STP-PPC:<functie of adres>]` en `[BIN:TPI-EXE:<functie of adres>]`, met de
SHA-256 van de geanalyseerde executable in `RECOMPILATION-ASSESSMENT.md`. Het
fidelity-register (`tools/fidelity_register.py`) moet dat label nog leren voordat de
eerste regel zo wordt gemarkeerd.

### Evidence- en determinismecontract (vereisten, nog niet geleverd)

`docs/REFERENCE-CORPUS.md` specificeert het geselecteerde corpus en de tracevelden.
Vóór acceptatie van M3/M4-semantiek is een begrensde originele gedragstrace nodig,
gekoppeld aan executable-/assethashes, beginstaat, acties, waarnemingen en vooraf
bepaalde vergelijkingscriteria. Static-analysis evidence en een runtime-oracle
blijven aparte labels; zonder uitvoerbaar origineel blijft gedrag onverifieerd.

M2 moet een vaste simulatietick, stabiele update-/eventvolgorde en benoemd RNG-algoritme
met geserialiseerde volledige state vastleggen. Rendering/framerate mag de simulatie
niet sturen. Canonieke simulatiestate moet exact replaybaar zijn op alle doelplatforms;
eventuele numerieke toleranties voor vergelijking met het origineel worden vooraf
per veld gemotiveerd en mogen geen veranderde spelregels verbergen. Dit contract
beschrijft toekomstige acceptatie, niet de huidige prototype-motion of savefunctie.

### Runtime- en toolchainkwalificatie

M0 inventariseert versies, transitieve native libraries, laadpad, OS/architectuur,
licentie/distributie en teststatus voor NAudio/Media Foundation/WaveOut, SDL2,
SPIR-V-cross-compilation en ImGui/fonts. De bestaande Windows-audiopaden zijn geen
macOS-audiobackend. Metal kiezen bewijst niet dat shaders, fonts, input, audio en
editor-launch werken. M1 kwalificeert alle launch-kritieke paden; optionele paden
mogen tijdelijk expliciet unsupported blijven, maar blokkeren latere eindacceptatie.
Bij Rosetta moeten processarchitectuur én iedere geladen native library overeenkomen.
M7 vereist echte arm64-kwalificatie zonder Rosetta.

Leg SDK-versie, restore-inputs en reproduceercommando's vast voor M0. .NET 8-support
eindigt op 10 november 2026 volgens Microsoft. Besluit van de projecteigenaar (9 oktober
2026): over naar .NET 10 (LTS tot 14 november 2028), SDK 10.0.401 in `global.json`;
NuGet-pakketten blijven ongewijzigd en zijn geen onderdeel van dit besluit. NativeAOT
blijft een afzonderlijke, optionele kwalificatie.

## Uitvoeringsfasen en harde gates

### M0 — Reproduceerbare basis en inventarisatie
- Leg editie/assetmanifest/hashes vast; identificeer ontbrekende bestanden.
- Houd exact editielabel/patch onbekend zolang bewijs ontbreekt; leg locale en
  executable-identiteit vast via `docs/RECOMPILATION-ASSESSMENT.md`.
- Zorg voor een schone .NET-build, portable assetpaden en unit-/assettestscheiding.
- Maak een startcommando met expliciet gamepad, logging en assetdiagnostiek.
- Registreer compiler/binary-informatie voor het beslisexperiment.
- Inventariseer de runtime-dependencies en leg SDK-/restore-inputs vast.
- Gate: schone checkout bouwt; CPU-tests slagen; assettests lezen echte WAD-data;
  ontbrekende assets geven een duidelijke fout zonder nep-success.
- Evidence-pakket: revision/toolchain, geselecteerd manifest, benoemde texture-/model-
  assertions en runtime-matrix. Optionele tests zonder assets zijn inconclusive,
  geen assetbewijs. De expliciete `--validate-assets`-gate moet falen bij ontbrekende
  vereiste bestanden, onleesbare archives of ongeldige geselecteerde fixtures.

### M1 — Eerste zichtbare Mac-park-slice
- Gebruik Metal op macOS; repareer cross-compilation en backendvoorwaarden.
- Bootstrap desnoods x86_64/Rosetta, maar label dit expliciet als tijdelijk.
- Laad een begrensde Jungle-sandbox met echte terrein- en attractie-assets.
- Voeg camerabediening, gridplaatsing, footprintvalidatie en start/stop toe.
- Houd eventuele tijdelijke modelanimatie zichtbaar gescheiden van originele VM-fidelity.
- Gate: app start op deze Mac; precies één attractie plaatsbaar; beweging stopt/start;
  UI-clicks plaatsen geen tweede attractie; screenshots/logs en tests als bewijs.
- Bewijs backend/process/native-library-architectuur en shader-/font-/editor-/input-
  launchpaden. Een begrensd terrein/model en procedurele beweging zijn een prototype,
  niet gevalideerde originele MAP-semantiek, ritcyclus of VM-fidelity.

### M2 — Datamodel, kaart en persistence
- Werk MAP/TPWS-schema's uit op basis van echte bestanden en gerichte binary-analyse.
- Introduceer een deterministische simulatietick die onafhankelijk van rendering werkt.
- Leg tickfrequentie, updatevolgorde, RNG-algoritme/state en canonieke replayvelden vast
  volgens het evidencecontract; verifieer save/reload inclusief tick en RNG-state.
- Maak eigen geversioneerd saveformaat met atomische writes en migratietests.
- Originele saves eerst read-only import; writeback alleen na roundtrip-bewijs.
- Gate: hetzelfde park na herstart; seed/replay reproduceren resultaten;
  beschadigde bestanden falen gecontroleerd zonder saveverlies.

### M3 — Eén complete gameplay-loop
- Bouw paden, ingang, wachtrij, één attractie, winkel/toilet en personeel.
- Bezoekers: doelen, padzoeken, wachtrij, rit, behoefteverandering en vertrek.
- Economie: kosten, opbrengst, ticketprijs, personeel en onderhoud.
- Gate: 30 minuten versneld headless draaien met inkomsten/uitgaven en bezoekers;
  geen vastgelopen wachtrijen, onbereikbare doelen of negatief tijdverloop.
- Invariants bewijzen stabiliteit, geen originele economie/bezoekerssemantiek:
  accepteer die semantiek pas met gekoppelde originele traces en vergelijking.

### M4 — Attracties, scripts en achtbanen
- Corpus van RSE-bestanden, opcode-inventaris, VM-disassembly en golden tests.
- Uitvoeringsbudget, foutdiagnostiek, objecthiërarchie en animatie/eventbinding.
- Hergebruik dezelfde VM voor vaste attracties; geen aparte hardcoded hack per ride.
- Achtbaanbouw, segmenten, terrein-/footprintconstraints, ritcamera en ritbeoordeling.
- Gate: iedere ondersteunde attractie doorloopt load/build/run/stop/delete;
  alle gebruikte opcodes hebben tests; onbekende opcodes worden expliciet gemeld.
- Dit is alleen subset-acceptatie. Volledige M4-acceptatie vereist alle verplichte
  attracties/achtbanen en benodigde opcodes, reference-traces en geen unsupported
  vereisten; registreer originele VM-fidelity apart van procedurele animatie.

### M5 — Volledige offline progression
- Alle vier werelden, scenario's/doelen, research, unlocks en adviseur.
- Personeelsrollen, onderhoud/storingen, behoeften, shops en decoratie-effecten.
- Gate: elk scenario start, doelen zijn haalbaar en voortgang overleeft save/load;
  featurematrix heeft geen open blockers voor de afgesproken originele editie.

### M6 — Audiovisuele en UX-pariteit
- Fonts, lokalisatie, menu's, shortcuts, geluid/muziek, video's en first-person.
- Reuse bestaande bewezen decoders waar mogelijk; geen eigen codec zonder noodzaak.
- Meet rendering/resources en memory; verbeter hotspots pas na profiling.
- Neem optionele, instelbare upscaling mee volgens `docs/UPSCALING-DESIGN.md`:
  Native standaard, eerst portable Linear/Nearest met vaste/custom renderschaal;
  alleen de 3D-wereld schalen, UI op outputresolutie en simulatie onaangeraakt.
- Vendor-/temporal-upscalers en dynamische resolutie zijn latere optionele proeven,
  geen dependencytoestemming of blocker voor native gameplay. Kwalificeer aangeboden
  baseline-modi per backend op beeld, DPI/picking, fallback, resources en gemeten kosten.
- Gate: referentiecaptures en replay-tests; geen ontbrekende primaire UI-functies;
  audio/input/rendering blijven werken na resize, alt-tab en lange sessies.

### M7 — Native en platformkwalificatie
- Reproduceerbare native arm64-builds voor alle dependencies; Rosetta niet vereist.
- Windows-/Linux-backends en distributiepakketten; CI met CPU- en assetgates.
- Drie afzonderlijke verplichte releasegates: native macOS-arm64, native Windows
  en native Linux. Leg voor Windows/Linux de ondersteunde OS-/CPU-targets vast;
  verifieer op ieder doelplatform passende process- en native-library-architecturen.
  Een Wine-/Rosetta-/andere compatibility-run vervangt geen native releasegate.
- NativeAOT alleen behouden bij meetbare startup/geheugen/distributiewinst en groene tests.
- Gate: macOS-arm64/Windows/Linux dezelfde simulation replay; lange sessies en
  save/load-corpus zonder regressies; packages bevatten geen originele assets.
- Alle vereiste matrixregels moeten reference-verified zijn met platformbewijs;
  bevestig het supported-runtime-besluit en distributie-/native-library-inventaris.
- Iedere native releasegate vereist package-launch, rendering/input/audio, strikte
  assetvalidatie, dezelfde canonieke simulation replay, save/load en lange-sessietests.
  Smoke-tests alleen zijn onvoldoende; releaseacceptatie blijft open zolang één
  platform ongekwalificeerd is. Native betekent OS-/CPU-passende runtime/dependencies,
  niet verplicht NativeAOT; een ondersteunde .NET JIT-runtime is toegestaan.

## Efficiëntie en werkwijze

- Kritieke pad: assets/build → rendering → datamodel → simulatie → fidelity.
- Parallel: parsers/corpus-tests, platformlaag, documentatie; geen gedeelde schrijfscope.
- Kleine patches per subsystem; bestaande helpers eerst hergebruiken.
- Geen dependency toevoegen of upgraden zonder expliciete toestemming.
- Geen generieke ECS, multiplayer, complete renderer-rewrite of AI-bezoekerlaag vooraf.
- Bij reverse engineering: bron/evidence per onbekende functie bewaren en golden tests
  schrijven; AI-output telt nooit als bewijs zonder runtime-/reference-verificatie.
- Houd demo-animatie, compatibility-uitvoering, native rendering en complete gameplay
  als vier verschillende statussen in de voortgang bij.
- Prioriteer regressies en bewezen decoder-/scriptkennis boven aantallen gegenereerde regels.

## Risico's en inschatting

De meeste kosten liggen in ontbrekende simulation semantics en originele formaten,
niet in C# naar native compileren. De volledige game is geen verantwoord uur-/dagen-
commitment. Eerst M0/M1 meten, dan capaciteit en throughput per subsystem bepalen.
Belangrijkste risico's: versieverschillen, gedeeltelijke VM/modelparser, shader/native
interop, fidelity zonder werkend origineel als oracle, en savecompatibiliteit.
Als een gate faalt, blijft die fase open; geen vervanging door een screenshot-only demo.

## Primaire bronnen voor routekeuze

- https://github.com/OpenTPW/OpenTPW en issue https://github.com/OpenTPW/OpenTPW/issues/31
- https://github.com/N64Recomp/N64Recomp
- https://github.com/hedge-dev/XenonRecomp
- https://github.com/isledecomp/reccmp
- https://github.com/isledecomp/isle en https://github.com/isledecomp/isle-portable
- https://github.com/NationalSecurityAgency/ghidra
- https://github.com/libsdl-org/SDL/releases/tag/release-3.2.0
- https://developer.apple.com/documentation/apple-silicon/about-the-rosetta-translation-environment
- https://support.apple.com/en-us/102527 (Rosetta door macOS 27; beperkt vanaf macOS 28)
- https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/
- https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core (.NET 8: 10 november 2026)
- https://github.com/naudio/NAudio/tree/v2.2.1 (Windows-audio, gepinde generatie)

Bronclaims hebben peildatum 9 oktober 2026. Issue #31 rapporteert een North-American
retail `roll.tgq`-sample; generaliseer deze melding niet naar alle video's/edities.
Live upstream-documentatie is geen bewijs voor de lokaal gepinde runtime-versies.

## Voortgang bij aanvang

- [x] Fork en lokale clone.
- [x] Broninspectie en scopebevestiging.
- [x] ISO-download en checksum; assets buiten repository.
- [x] Lokaal .NET 8 arm64 SDK; x64 bootstrap SDK wordt gecontroleerd.
- [ ] M0 build/assetdiagnostiek/tests.
- [ ] M1 zichtbare Mac-park-slice.
- [ ] Recompilation-beslisexperiment met originele executable.
- [ ] M2–M7; niet geleverd en niet als gereed rapporteren.
