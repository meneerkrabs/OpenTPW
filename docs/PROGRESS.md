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

- Hele native assetsuite inclusief shader- en savecontainerregressies: **153 passed, 0 skipped,
  0 failed**, .NET SDK 8.0.425, `osx-arm64`.
- Zonder originele assets/native shader-opt-in: **137 passed, 16 skipped, 0 failed**.
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

Eerstvolgende inhoudelijke gate: oorspronkelijke MAP/TPWS-structuren en een
legaal uitvoerbare originele offline gedragsreferentie onderzoeken. Geen
copy-protection-omzeiling en geen fictieve simulatieregels als vervanging voor bewijs.

## Ontwerpuitbreiding: optionele upscaling

Op gebruikersverzoek toegevoegd aan M6, PRD en testspecificatie op 9 oktober 2026:
`docs/UPSCALING-DESIGN.md`. Native/100% blijft standaard; portable Linear/Nearest
met instelbare wereldresolutie eerst, UI op outputresolutie. Simulatie/save/replay
blijven onaangeraakt. Vendor-/temporal-upscaling en dynamische resolutie pas na
afzonderlijk capability-, licentie-, input- en performancebewijs.

Dit is alleen een ontwerpwijziging, geen nieuwe rendererfunctie of test-pass.
Save-/maponderzoek blijft de eerstvolgende gameplay-gate; upscaling verplaatst
dit kritieke pad niet. Eerdere build-/smoke-evidence betreft de bestaande renderer.

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
