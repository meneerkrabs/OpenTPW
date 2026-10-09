# Optionele upscaling — ontwerp

Toegevoegd op verzoek op 9 oktober 2026. Status: **gepland, niet geïmplementeerd**.
Dit is een optionele presentatieverbetering, geen vervanging voor volledige offline
gameplay of originele graphics-fidelity. Geen nieuwe dependency is goedgekeurd.

## Doel en instellingen

- Standaard: `Native / uit`, renderschaal 100%, geen extra sharpening.
- Eerste portable modi: `Linear` en `Nearest` via de bestaande fullscreen-pass.
  Nearest is een bewuste retro-optie, geen kwaliteitsverbetering voor ieder beeld.
- Bij ingeschakelde upscaling: presets 77%, 67%, 59% en 50%; custom schaal 50–100%.
  Percentages gelden voor beide dimensies, niet voor het aantal pixels. Bij 50%
  breedte/hoogte rendert de wereld circa een kwart van de outputpixels.
  Deze presets zijn onze keuzes, geen vendor-kwaliteitslabels of originele waarden.
- Toon methode, gevraagde/effectieve schaal, interne/outputresolutie en eventuele
  fallbackreden. Schakel niet stilzwijgend een ander kwaliteitsalgoritme in.
- Bewaar voorkeuren in gebruikersconfiguratie, niet in park-/originele saves.
  Ongeldige configuratie valt met diagnostiek terug op Native; ontbrekende nieuwe
  velden behouden de bestaande native-renderroute.
- Sharpening blijft standaard uit; alleen beschikbaar bij een geïmplementeerde,
  gekwalificeerde methode. Geen slider zonder werkende achterliggende pass.

Dit betreft realtime opschaling van de 3D-wereld. Offline AI-texture-upscaling,
vervanging van originele assets, supersampling en frame generation vallen hier
niet onder en worden niet impliciet toegevoegd.

## Inpassing in de huidige renderer

`source/OpenTPW/Client/Renderer.cs` rendert nu de wereld naar een 4x-MSAA-framebuffer,
resolved naar `ResolveColorTexture`, blit met `Device.LinearSampler` naar de
swapchain en rendert daarna de editor/UI. `content/shaders/blit.shader` bevat
de fullscreen-texturesampling. De rendertargets gebruiken nu `Screen.Size`.
Die bestaande scheiding is het aanknopingspunt; geen renderer-rewrite nodig.

Gewenste volgorde:

1. Bepaal outputgrootte uit de echte swapchain/drawable-pixels, los van logische
   venstergrootte en DPI. Kies interne grootte uit outputgrootte × renderschaal;
   rond consistent af, clamp op minstens één pixel en op devicegrenzen.
2. Render alleen de 3D-wereld op die interne grootte. Houd bestaande MSAA-resolve
   intact; upscaling is geen antialiasingvervanger en MSAA is een aparte instelling.
3. Resolve, schaal naar outputgrootte en voer alleen expliciet gekozen sharpening
   uit. Native behoudt de huidige pass zonder nieuw filter.
4. Render tekst, menu's, cursors en ImGui op outputresolutie. UI wordt niet door
   de wereld-upscaler gehaald en blijft scherp bij lagere wereldresolutie.

Leg interne/outputafmetingen in één kleine renderconfiguratie vast; introduceer
geen pluginframework voor twee samplerkeuzes. Camera-aspect volgt outputaspect;
afronding van interne dimensies mag het beeld niet rekken of picking verschuiven.
Input/picking blijven in bestaande logische schermcoördinaten, met expliciete
DPI-conversie waar nodig; renderschaal mag nooit een tweede muisschaalfactor worden.

Resize, fullscreen, DPI- en schaalwijzigingen vervangen targets en resource-sets
op een veilige framegrens. Dispose alle eigen kleur-/dieptetextures en views,
niet alleen het framebufferobject; retire oude GPU-resources pas na voltooid gebruik.
Bij minimaliseren/zero-size pauzeert rendering zonder nulgrote GPU-allocaties.
Allocatie-/capabilityfalen geeft een native fallback met reden; als native ook
faalt volgt een duidelijke fout, geen eindeloze retry of zwarte nep-success.

Instellingen mogen **geen** invloed hebben op tickrate, RNG, simulatiestate,
saveformaat of replay. GPU-kwaliteit blijft strikt buiten de gamekern.

## Gefaseerde algoritmekeuze

| Stap | Keuze | Gate / beperking |
| --- | --- | --- |
| M6-U1 | Native, Linear, Nearest + vaste/custom schaal | Bestaande Veldrid/shaders/samplers; eerst resize/DPI/input/resources kwalificeren |
| M6-U2, optioneel | Onderzoek ruimtelijke edge-aware upscaling, bijvoorbeeld FSR 1 EASU/RCAS | Vooraf bron/licentie, gepinde versie, shadervertaling en werkelijke Metal/D3D11/Vulkan-kosten bewijzen; geen automatische dependencytoevoeging |
| Later, apart besluit | Temporal upscaling, bijvoorbeeld FSR 2 of MetalFX | Eerst motion vectors, depth/jitter/history, camera-cut/reset en transparantie/UI-contracten onderzoeken; huidige renderer levert deze inputs niet als upscalingcontract |

Een vendornaam in dit ontwerp is geen integratie- of supportclaim. MetalFX wordt
niet de verplichte Mac-route; een platformspecifieke uitbreiding moet naast de
portable baseline werken. DLSS/XeSS en andere vendorintegraties zijn geen
releasevereiste. Frame generation valt buiten deze slice.

Automatische/dynamische resolutie komt pas na GPU-timing en vaste-schaalbewijs:
begrensde schaal, hysterese, aanpasinterval en expliciet target-framebudget.
Geen auto-schaal op basis van alleen totale frametijd: een CPU-/simulatiebottleneck
wordt niet opgelost door minder pixels. Geen performancewinst claimen zonder meting;
filteroverhead kan bij kleine resoluties zwaarder zijn dan de besparing.

## Acceptatie en bewijs

- CPU-tests voor presets/custom grenzen, ongeldige waarden, afronding/devicegrenzen,
  Native=100%, DPI/output versus logische input en configuratieroundtrip.
- Shadercompilatie/bindings voor Metal/MSL, D3D11/HLSL en Vulkan/SPIR-V; werkelijke
  pakketuitvoering apart op elk platform. Vertaalde shaders zijn geen runtime-pass.
- Zelfde scène/camera/state: native- en geschaalde captures van terrain, attractie,
  transparantie, UI/tekst en first-person. Controleer UV-orientatie, kleurruimte,
  aspect, scherpe UI en afwezigheid van extra picking-offsets.
- Native-uitmodus reproduceert de baseline; tests mogen geen verschil wegpoetsen
  door de native baseline via een nieuw filter te laten lopen.
- Wissel modi/schaal herhaaldelijk; resize, minimaliseer/herstel, fullscreen en
  DPI/focuswissels. Geen zwarte frames, crashes of groeiende eigen GPU-resources.
- Meet GPU-passkosten/framepercentielen/geheugen per resolutie en backend, met
  vaste assetidentiteit/camera/parkbelasting. CPU- en GPU-kosten apart rapporteren.
- Identieke acties en simulatieticks leveren dezelfde canonieke replaystate op
  voor elke beeldmodus; rendering beïnvloedt gameplay niet.
- Fallback voor unsupported methodes/allocatiefalen expliciet testen. Optionele
  vendoralgoritmes blokkeren native gameplay niet; de aangeboden baseline-modi
  moeten wel op alle drie doelplatforms gekwalificeerd zijn voor release.

De huidige GPU-smoketest leest de interne resolve-texture en sluit UI uit.
Die is daarom alleen wereld-renderbewijs. Voeg voor upscaling een capture van de
uiteindelijke output ná scaling én UI toe; noem de bestaande capture geen
end-to-end upscaling-/UI-test.

## Primaire onderzoeksbronnen

- AMD FidelityFX Super Resolution: https://gpuopen.com/fidelityfx-superresolution/
- AMD FSR 1 bron: https://github.com/GPUOpen-Effects/FidelityFX-FSR
- AMD FSR 2 bron/inputs: https://github.com/GPUOpen-Effects/FidelityFX-FSR2
- Apple MetalFX: https://developer.apple.com/documentation/metalfx

Controleer API/capabilities/licentie tegen de gekozen gepinde implementatie vóór
integratie; dit ontwerp kiest bewust geen onbewezen nieuwste vendor-SDK.
