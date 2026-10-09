# Uitvoeringsstatus — 9 oktober 2026

Dit is een ontwikkelprototype, niet de complete offline game. Het volledige
plan en de eindacceptatie blijven ongewijzigd. Gebruiker heeft publicatie van plan,
voortgang en geverifieerde code naar de fork goedgekeurd. Git-history registreert
de gepubliceerde snapshots; originele assets, native binaries, captures en lokale
agent-runtimebestanden blijven buiten die publicatie.

## Geleverd en gecontroleerd

- Native macOS arm64: SDL2-windowing, Metal-rendering, ImGui-initialisatie en
  oorspronkelijke Jungle-grondtextuur en Totem-model. Geen Rosetta in deze test.
- Plaatsen, starten, procedurele beweging, stoppen/resetten, verwijderen en opnieuw
  plaatsen; geïsoleerd JSON-opslaan/laden van een lopende attractie en leeg park.
- Reproduceerbare native smoketest van 150 frames met GPU-readback. Controleert
  niet-zwarte beelden en textuurdetail; capture zonder UI, geen originele fidelitytest.
- Portable bestandspaden en assettests; oorspronkelijke bestanden blijven buiten git.
- SPIR-V eenmaal compileren met behouden resource-namen; dezelfde bytecode voedt
  backendshaders en reflectie. MSL/HLSL/GLSL-bindings hebben native regressietests.
- Eigen materiaalpipelines gebruiken Improved resource binding. Legacy Metal
  telde vertexbuffers bij uniformbindings op, terwijl SPIRV-Cross buffer 0 verwachtte.
  ImGui houdt zijn bestaande bindingcontract; culling blijft ongewijzigd.
- Mist gebruikt view-space afstand in plaats van geprojecteerde coördinaten.
  De gekozen mistafstand is een sandboxinstelling, geen gereconstrueerde gamewaarde.
- Engine-content wordt meegenomen in build/publish en shaderpaden zijn relatief
  aan de executable. Native smoketest slaagt gestart vanuit `/tmp`, buiten de repo.
- Framework-dependent pakketten voor `osx-arm64`, `win-x64` en `linux-x64` bouwen.
  Dit zijn lokale ontwikkelpakketten, geen gekwalificeerde releases.

## Simulatiefundament: beperkte M2-slice

De prototype-attractie draait op een gekozen 60 Hz fixed-step clock, los van
renderframes. CPU-tests vergelijken 30/60/144/240 fps met dezelfde 360 ticks en
dezelfde eindfase/hoogte. Framevertraging veroorzaakt maximaal 16 inhaalticks;
extra tijd wordt expliciet als `DroppedSeconds` geteld, niet oneindig ingehaald.
Renderer-timing gebruikt een monotone stopwatch in plaats van de systeemklok.

Dit bewijst alleen consistentie van onze prototypebeweging. Het bewijst niet de
oorspronkelijke tickrate, RSE-uitvoering of bit-identieke volledige simulatie op
alle platforms. RNG, eventreplay en volledige tick/RNG-save-state ontbreken nog.
Het huidige sandbox-saveformaat start fase en clock opnieuw bij laden.

## Verse verificatie

- Hele native assetsuite inclusief shader-, savecontainer- en fontregressies: **185 passed, 0 skipped,
  0 failed**, .NET SDK 8.0.425, `osx-arm64`.
- Zonder originele assets/native shader-opt-in: **165 passed, 20 skipped, 0 failed**.
- Savecontainer-slice: **31 passed**, inclusief de gehashte originele TPWI-fixture.
- Geïsoleerde clocktests: **12 passed**.
- Release-solutionbuild: **0 errors**, bestaande warnings blijven aanwezig.
- Native Metal-smoketest: exitcode 0, 150 frames, originele assets, save/load
  in een tijdelijke directory; gebruikers-sandboxsave blijft onaangeraakt.
- Drie RID-publishbuilds geslaagd; opnieuw uitvoeren na iedere bronwijziging.
- Opnieuw gepubliceerde Mac-build gestart vanuit `/tmp`: read-only saveinspectie
  en native Metal-smoketest beide exitcode 0. Ongeldig/truncated savebestand en
  ontbrekend inspectieargument geven exitcode 1; geen oorspronkelijke bestanden gewijzigd.
- `git diff --check` hoort schoon te blijven.

Reproductie staat in RUNNING.md. Lokale logs staan onder `/tmp/opentpw-*.log`;
GPU-captures en publishoutput onder het genegeerde `artifacts/`. Captures bevatten
originele assets en worden niet als repositoryfixtures verspreid.

## Nog open / geen voltooiingsclaim

- M0: volledige native/transitieve dependency- en distributielicentie-inventaris,
  parsergrenzen, editie-identificatie en alle benodigde assetfixtures.
- M1: menselijke/automatische UI-inputkwalificatie, camera/zoom/focus en volledige
  lifecycle/resourcecontrole. Offscreen-captures tonen de knoppen niet.
- M2–M6: oorspronkelijke map/save-import, padennetwerk, bezoekers, queues,
  economie, personeel, alle rides/coasters en RSE-opcodes, werelden/scenario's,
  progression, audio/video, localization en oorspronkelijke gedragsreferenties.
- M7: daadwerkelijk uitvoeren op Windows/Linux, packaging/licenties,
  lange sessies, volledig native dependencybewijs en ondersteund release-runtimebeleid.
- Bestaande NuGet-auditwaarschuwingen voor ImageSharp en bestaande codewarnings
  zijn niet weggepoetst. Geen dependency-upgrades zonder expliciete autorisatie.
- Subagentvervolg werd geblokkeerd door een ingetrokken refresh-token. Werk is
  lokaal voortgezet; onafhankelijke architect-/review-signoff is niet geclaimd.

Eerstvolgende inhoudelijke gate: resterende MAP/TPWS-velden (zie TPWS-PAYLOAD.md) en een
legaal uitvoerbare originele offline gedragsreferentie onderzoeken. Geen
copy-protection-omzeiling en geen fictieve simulatieregels als vervanging voor bewijs.

## Ontwerpuitbreiding: optionele upscaling

Op gebruikersverzoek toegevoegd aan M6, PRD en testspecificatie op 9 oktober 2026:
`docs/UPSCALING-DESIGN.md`. Native/100% blijft standaard; portable Linear/Nearest
met instelbare wereldresolutie eerst, UI op outputresolutie. Simulatie/save/replay
blijven onaangeraakt. Vendor-/temporal-upscaling en dynamische resolutie pas na
afzonderlijk capability-, licentie-, input- en performancebewijs.

Save-/maponderzoek blijft de eerstvolgende gameplay-gate; upscaling verplaatst
dit kritieke pad niet.

Implementatie M6-U1 + hoge resoluties (9 oktober 2026, worktree `display`):
willekeurige venstergroottes (`--resolution`), borderless/exclusive fullscreen
(`--fullscreen`, `--fullscreen-exclusive`, Alt+Enter/F11), HiDPI-drawables, Native/
Linear/Nearest met presets 77/67/59/50 en custom 50–100 (`--upscale`, `--render-scale`),
geheeltallige BF4-UI-schaal, gebruikersinstellingen in `display.json`, ImGui-sectie
"Display" en de UI-agnostische `OpenTPW.IDisplaySettings`-API (met bevestigen/terugzetten).
Alleen de 3D-wereld schaalt; BF4-tekst, ImGui en films renderen op outputgrootte.
Bewijs (alleen macOS arm64/Metal, 1x-scherm 1920×1080): CPU-tests voor grootte,
afronding, fallbacks, DPI, picking en configuratie; native smoke voor sandbox en
`--load-original-level jungle` op 1280×720, 1920×1080 borderless (67%), 2560×1440
output via `OPENTPW_TEST_PIXEL_SCALE=2` met 50% Linear (wereld 1280×720, UI-schaal 2,
BF4-tekst exact, picking correct) en runtime schaal-/venster-/fullscreenwissels.
Niet bewezen: echte Retina, exclusive fullscreen, Windows/Linux-GPU, performance.
Zie `docs/UPSCALING-DESIGN.md`.

## Geleverde savecontainer-slice

`SaveReader` ondersteunt nu het geobserveerde offline version-133 TPWI-envelope
naast de oude synthetische 500-header. Beperkte binaire intake en zlib-decoding
vervangen one-shot reads en onbegrensde decompressie; caller-owned streams blijven
open en herhaalde reads werken. Header-/chunk-/decoded-lengtes, checksum,
streamcompletion en trailing data worden gecontroleerd, met harde 64 MiB-limieten.

`--inspect-save` geeft metadata en hashes zonder GPU of schrijftoegang tot assets.
Het echte Jungle Easymode-bestand decodeert naar 1.608.309 bytes met de verwachte
SHA-256; corpus en offsets staan in `REFERENCE-CORPUS.md` en `SAVE-CONTAINER.md`.
Geen parkpayloadschema, speelbare importer of bewijs van alle originele TPWS-layouts.
`MapFile` blijft onvolledig en `scape.omp`-semantiek is niet geraden.

Vervolgsubagents konden geen review leveren (model/account-beperking en een
geblokkeerde reviewoproep). Tests en lokale controles zijn uitgevoerd; onafhankelijke
review-signoff en voltooiing van de volledige game zijn niet geclaimd.

## Focus op ontbrekende/gedeeltelijke formaten

Op gebruikersverzoek krijgt de ❌/⚠️-lijst voorrang. `FORMAT-BACKLOG.md` scheidt
concrete fixtures, decoderwerk en integratie/fidelity; bestaande groene tekens
worden niet als volledige game- of platformkwalificatie geïnterpreteerd.

- Nieuwe `FontFile`: begrensde BF4-header/offset-/glyphparser, raw vier-bit,
  nibble-RLE en monochroom. Alle 33 originele Engelse fonts / 8.217 entries
  decoderen; drie gehashte fixtures pinnen metrics en onafhankelijk berekende samples.
- README: BF4 van ❌ naar ⚠️. Nog geen GPU-fontatlas/game-UI of oorspronkelijke
  visuele vergelijking; geen onterechte complete-fontclaim.
- `--inspect-font`: read-only CPU-diagnostiek zonder assets te wijzigen of GPU
  te starten. Gepubliceerde Mac-build vanuit `/tmp`: geldig font exit 0,
  ontbrekend argument exit 1. Drie RID-pakketten opnieuw gebouwd.
- BF4-tekst: deterministische glyphatlas (coverage × 17 → alpha), layout met
  originele advance/offsets/regelhoogte, regelafbreking en `?`-fallback; gepind
  op GAME8AA/SESHMED en UITEXT/OBJECT_NAMES. Sandboxpaneel tekent "Totem" en
  ride-labels; Metal-smoketest vergelijkt readback met CPU-composiet (max.
  verschil 0). Geen origineel UI-scherm/AA-vergelijking; D3D11/Vulkan niet gedraaid.
- Read-only metadata-inventaris van 312 DWFB-archives: 2.118 MD2-, 308 RSE- en
  7 MAP-members. Jungle terrain bevat `base.map` en `terrain.map`; dit bewijst
  nog geen kaartsemantiek/import. Negen TGQ-video's gevonden. Geen standalone
  MTR/LIPS/TQI-namen in deze locaties; ingebedde/differently named data blijft open.
- Upstream formaatnotities zijn via de GitHub-docsbron teruggevonden. MTR/LIPS
  blijven TODO; MAP-notitie gaat over sound maps, niet bewezen terreinrecords.

Parallelle formaat-slice (7 formaten, geïsoleerde worktrees, 9 oktober 2026):

- MD2: `ModelFile` herschreven als begrensde lezer; 2.116 van 2.118 corpusmembers
  parsen (838 geometrie, 1.278 animatiecontainers met opaque payload), 2
  versie-207.201-bestanden expliciet geweigerd. Ongebruikte stub
  `OpenTPW.Files/Public/ModelFile.cs` verwijderd.
- MD2-animatie: positie- (Bézier/lineair), rotatie- (slerp met easingcurves) en
  schaaltracks gedecodeerd en tegen alle 1.278 animaties gevalideerd; node-matrices
  zijn parent-relatief. De sandbox-Totem speelt `totemm1.MD2` af wanneer `Totem.RSE`
  ANIM_Main triggert (30 ticks/s is een eigen keuze); de smoketest eist veranderende nodes en pixels. Vertexanimatie en
  andere recordsoorten blijven ongedecodeerd ([MD2-MODELS.md](MD2-MODELS.md)).
- MAP: TP2M-terreinkaarten (128×128 cellen, 5 gepinde fixtures); 64
  sound-catalog-`.map`-bestanden onderscheiden en geweigerd. Celbetekenis onbekend.
- RSE: `RideScriptFile` + statische analyse; alle 308 scripts parsen, 84 gebruikte
  opcodes, geen onbekende. `RideVM` voert ze nu uit (zie [RSE-VM.md](RSE-VM.md)):
  33 opcodes volledig in de VM, 51 via een hook die "unimplemented effect"
  registreert (bezoekers, animatie, geluid, objecten, ritcontrollers, parkklok).
  Alle 263 startbare scripts + 44 kinderen draaien 60 s zonder fouten; de
  sandbox-Totem draait `Totem.RSE`, dat bepaalt wanneer de originele
  `totemm1.MD2`-animatie start. Slicing, milliseconden en CRIT_LOCK zijn afgeleid, niet getraced.
- Bezoekers (benaderd, [GUESTS.md](GUESTS.md)): kinderen zijn originele sprites
  (`esprites.wad`, ESP/FPC/TPC gedecodeerd, 8 sets × 175 frames, 5 richtingen +
  spiegeling). Fixed-tick, geseede simulatie: aankomst via bushalte en kassa
  (`FixedItemInfo`), entree, padzoeken met flow fields over de save-verbindingsbits,
  behoeften, keuze met de `.sam` DecisionVar-gewichten, wachtrij en de
  LETMEON/LETMEOFF-hostprotocol + bezoekersopcodes via `RideVisitorBridge`; de
  Totem vult zich met echte bezoekers (start ≈3,3 s i.p.v. 11,3 s) en laat ze via
  HOP/WALKOFF/WALKGET weer uitstappen. Snelheden, behoeftetempo's, aankomsteenheid
  en scoreformule zijn eigen benaderingen. 600 bezoekers ≈0,06 ms per tick.
- TPWS/TPWI: 17 unieke sectiemarkers in de enige fixture gelokaliseerd; inhoud opaque.
  Er staat geen ander TPWI/TPWS-bestand op de ISO.
- LIPS: gevonden als `.LIP` (639 in `lips.wad` + 4 levelbestanden); strikte lezer,
  eenheid (waarschijnlijk µs) onbevestigd.
- MTR: alleen 11 ISO-bestanden (`Meshes/<taal>/*.mtr`); structurele lezer,
  betekenis en gebruik door de game onbekend. Tests via `OPENTPW_MTR_PATH`.
- TQI/TGQ: alle negen films; container en EA ADPCM-audio (bit-exact t.o.v. een
  externe referentie) en TQI-video (integer-IDCT, 56–61 dB, niet bit-exact).
  Afspelen via `--play-movie <naam>` (SDL2-audio, audioklok, frame drop/hold,
  GPU, native smoke-test); geen in-game trigger (geen bewijs in de data).

Verificatie na integratie: native assets + shaders **403 passed, 0 skipped**;
zonder assets 339 passed, 64 skipped; Release-solution 0 errors; Metal
smoke-test 150 frames geslaagd. Windows/Linux GPU niet gekwalificeerd.

GitHub CI van snapshot `5f23094`: macOS en Ubuntu build/CPU-tests slagen;
Windows had één assert-fout door `UnauthorizedAccessException` in plaats van
`IOException` bij vervangen van een directory. De test onderscheidt nu het
OS-specifieke exceptiontype; behoud van de directory en opruiming van tijdelijke
bestanden blijven verplicht. Windows-herverificatie blijft nodig voor deze wijziging;
CPU-CI is geen GPU-/audio-/volledige native releasekwalificatie.

Terrein/save-slice (9 oktober 2026, worktree `terrain`):

- MAP-assen bewezen: bestandsrij = spel-X, kolom = spel-Y, cel = 10 MD2-eenheden
  (Standard.sam fixed items, `bridge01`-node, rasterisatie van alle jungle-MD2-driehoeken).
- `base.MD2` 0x6C-blok gedecodeerd als 96×85-heightfield: hoeken, gaten
  (= MAP water/entree/vaste looppaden in alle vier thema's) en grondtextuurslots.
- Vijf MAP-bits getypeerd (`MapCellFlags`); bit 0x04 en headerwaarden blijven opaque.
- Easymode.TPWI: per-celgrid (16.384 records, MAP-byte identiek aan `base.map`,
  pad/verbindingen/bezetting) en SYSG-objectrecords (Info.Id → .sam-namen)
  gedecodeerd; read-only `OriginalParkImport` (78 padcellen, 11 objecten, 3 fixed items).
  Geld/tijd/gasten niet geïmporteerd (niet verifieerbaar met één fixture).
- `--load-original-level jungle`: originele terreinmeshes + heightfield, geïmporteerde
  paden/footprints, bouwregels op bewezen bits.
- Na rebase op `55b1b93` (RSE-VM, BF4-tekst, TGQ, MD2-animatie): native assets
  479 passed, 10 skipped (opt-in MTR/native-shadertests); zonder assets 397 passed,
  92 skipped. Metal smoke-test geslaagd voor sandbox (script, animatie, tekst,
  save/load) en `--load-original-level jungle`/`fantasy`.

## Talen (9 oktober 2026)

- Alle vijf meegeleverde talen (Engels, Deens, Frans, Duits, Zweeds) decoderen:
  `.str` gebruikt nu `MBToUni.dat` uit de eigen taalmap (Deens/Zweeds 248 tekens,
  overige 249). Eerder waren 2.254/2.358 Deense en 2.268/2.358 Zweedse strings
  verminkt, plus 3 Franse (œ) en 1 Duitse (š). De 24-bit stringlengte wordt volledig
  gelezen; UITEXT-strings > 255 tekens werden afgekapt (ook Engels). Tekentabel
  wordt eenmaal opgebouwd in plaats van per teken.
- Taalkeuze: `--language`, `OPENTPW_LANGUAGE` of instelling `Language`; standaard
  Engels als geïnstalleerd, anders de geïnstalleerde taal. Andere talen via
  `--language-data` / `OPENTPW_LANGUAGE_DATA`: een read-only, hoofdletterongevoelige
  overlay van de uitgepakte CD-mappen `<Lang>/data` en `<Lang>/Meshes`; er wordt
  niets naar de gamemap of git gekopieerd. Strings en fonts (Localization,
  sandboxpaneel) volgen de keuze; spraak/LIP/banners zijn alleen resolvebaar,
  niet afgespeeld/getekend.
- Tests: synthetische reader- en resolutietests; met de CD-data per taal 21
  stringtabellen / 2.358 strings, gepinde voorbeelden ("Gå Online", "Koppla upp",
  "d'œuvre", "Unfuhg Gibsniš"), glyphdekking in alle 33 fonts per taal, LIP/banners.
  Native Metal-smoketest slaagt in Engels en Duits (readbackverschil 0).
  Volledige suite: zonder assets 394 passed / 98 skipped; met `OPENTPW_GAME_PATH`
  466 / 26; plus `OPENTPW_LANGUAGE_DATA` 482 / 10 (de 10 zijn de opt-in native
  shader- en MTR-tests); 0 failed.
- Open: geen originele menu-/UI-schermen, geen taalwissel tijdens het spel,
  `UniToMB.dat` (tekstinvoer) ongelezen, andere edities niet onderzocht. Zie
  [LANGUAGES.md](LANGUAGES.md).

## Parkeconomie (9 oktober 2026, worktree `economy`)

- Gelaagde `.sam`-instellingen (`levels/Standard.sam` → thema-`Standard.sam` → `Easy_Standard.sam`;
  categorie- → object- → `Easy_`-objectbestanden) met bronvermelding per waarde. Inventaris van alle
  economie-, personeel-, research- en doelinstellingen met betekenis en gebruik in
  [ECONOMY.md](ECONOMY.md).
- `ParkEconomy`: deterministische parkklok (dag = 240 vaste ticks, 30-dagenmaanden: benadering),
  pauze/snelheden, grootboek met de originele UI-categorieën, entreeprijs, winkel- en
  sideshowprijzen, bouw-, upgrade-, cel- en grondkosten, schrootwaarde, leningen (LOANNAMES),
  faillissement na zes maanden rood (TAG_SYSTEM-teksten), personeelspool, lonen
  (BaseWage × PayMultiplier), training, monteurs/schoonmakers (benaderd), research, uitdagingen,
  golden tickets en sleutels. Benaderingen staan per regel gedocumenteerd.
- Easymode.TPWI: leningtabel (8 × 32 bytes; aflossing floor(bedrag/maanden) = 0 % rente, dus
  `Easy_Standard.sam`) en uitdagingslijst (8 × 45 bytes = jungle `ChallengesInThisLevel`) gevonden en
  gecontroleerd. 87.987 / −12.013 blijft onverklaard (oneven, terwijl alle bouwkosten veelvouden van 5
  zijn); het saldo start daarom op `InitialCash`.
- Eigen geversioneerd parksaveformaat (JSON, atomisch, strikt gevalideerd) met klok, RNG, geld,
  personeel, research en doelen; roundtrip en identieke voortzetting na laden getest.
- `--load-original-level <thema>`: economie loopt mee op de vaste klok en logt datum/saldo per
  speldag; de smoketest betaalt een maand loon en doet een parksave-roundtrip (alle vier thema's
  geslaagd, macOS arm64 Metal). Volledige suite: zonder assets 427 passed / 122 skipped; met
  `OPENTPW_GAME_PATH` 519 / 30; plus taaldata 539 / 10; 0 failed.
- Open: bezoekersuitgaven, personeelsgedrag/stakingen, adviseur, HUD-schermen (andere slices), en
  originele formules voor dagduur, rente, parkwaardering en sleutels.

## Originele UI: front-end en HUD (9 oktober 2026, worktree `frontend`)

- Bewijs (UI.md): de 278 `ui.wad`-modellen zijn geschreven in een virtueel scherm van
  2048×1536 (volledige-schermframes beslaan precies 0..2048 × 0..−1536); HUD-modellen staan
  op hun schermpositie, generieke knoppen/vensters op de oorsprong. Kindnodes zijn
  alternatieve toestandsframes (normaal/uitgeschakeld/oplichten/ingedrukt), geen onderdelen;
  dit verklaart de "ongecomponeerde" bounds uit MD2-MODELS.md. Texture-V loopt van onder naar
  boven; puur roze is transparant. `lobby.wad` beschrijft de 3D-lobby (vier eilanden,
  cameraposities, luchtkleur).
- Standaardstart is nu een front-end in originele stijl: 3D-lobby met de vier eilanden, menu
  met originele knoppen/teksten/tooltips (UITEXT/UIHELPTEXT), spelmodus, laden, opties en
  afsluiten; daarna het originele level met een HUD (hoofdpaneel, datum, saldo, bouwarm met
  categorieën en draaiende preview-modellen, infoarm, berichten, pauzemenu). `--sandbox`,
  `--load-original-level` en een kale `--smoke-test` slaan de front-end over zoals voorheen.
- Opties: originele rijen (resolutie, volumes, popup-hulp) plus OpenTPW-rijen (venstermodus,
  upscaling, renderschaal, interfaceschaal, taal) in dezelfde stijl, met de originele
  bevestig/herstel/herstart-meldingen; ontbrekende labels komen uit een eigen, in zes talen
  vertaalde aanvullende tabel. Weergave via een stub van `IDisplaySettings` (display-slice).
- Benaderingen (gedocumenteerd): posities van door code geplaatste elementen, lettertype-
  niveau per resolutie, snelheidsknoppen, kalender en startsaldo-stub (startsaldo zelf uit
  `Easy_Standard.sam`), Totem-prijs uit `Totem.sam`. Interfaces voor economie, catalogus en
  display staan in UI.md.
- Tests: zonder assets 433 geslaagd / 129 overgeslagen; met `OPENTPW_GAME_PATH` en
  `OPENTPW_LANGUAGE_DATA` 552 geslaagd / 10 overgeslagen; 0 mislukt. Metal-smoketest
  `--front-end --smoke-test` geslaagd in alle zes talen (tekst in readback texel voor texel
  gecontroleerd); bestaande sandbox-, jungle- en fantasy-smoketests blijven slagen.
- Open: originele schermposities, profielen/online, cursor, animaties en geluid van knoppen,
  vergelijking met originele captures, D3D11/Vulkan.
