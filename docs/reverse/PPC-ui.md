# Feral PowerPC UI evidence

October 9, 2026. This lane supplies static evidence, bounded metadata readers and replacement specifications,
not original screen captures or gameplay changes. No original executable was run.
All addresses below are **section-relative**: `code:` is PEF section 0; `data:`
is instantiated section 1. Mac evidence is not automatically Windows Patch 2
evidence. Current OpenTPW pixels were never used as an original appearance oracle.

## Identities and reproducibility

| Private source | SHA-256 |
| --- | --- |
| `mac-feral/bin/SimThemePark.data` | `04809cd4ccee5433c7fb0b7c93d32f6a7aa629c1849181c0b7906415e5e295f5` |
| `libraries/macdoze_shared.data` | `ba11331a70bce77140ae6e7fe73145fe4e13cce5f042707a494cb77654b32f0d` |
| HFS `:Theme Park Data:data:ui.wad` | `5d710990242a9cc52656d282a50af77257dcc967e1d41985b130576150baa135` |
| HFS `:Theme Park Data:data:Language:American:residx.dat` | `02b937519e897a9fc7f36fec29197bc1abe40350979800306062c56f039174f0` |
| Windows baseline `Data/ui.wad` | `dd9cbf829d14c798be908b8910305d7ec04e9b449794806c0581293458bc9362` |
| Mac and Windows baseline `lobby.wad` | `b9afda6264961021aecaca882adf9ce25aa6973f48111415176ed45afd2c9ee7` |
| `engine_shared.data` | `c549123f647dcf33c3e2c3d515bffe2cfcb19d11680f743f02ca42bb09dbd73b` |
| Mac American `UITEXT.str` | `c3768d1f448c0f85952ae3edda41da750081e85996eb76750df53fad7688bdef` |
| Mac American `UIHELPTEXT.str` | `adc16efc04bdb8c605e916a23b7794342d507d0a90cc3f8f635dadf20cf59b75` |
| Mac American `MBToUni.dat` | `f682ed03507d6ea20f5f1689c321f32b52f8a21e838e9440fbc3207e7236b67d` |

HFS assets were read with the existing `hfsutils` tools and copied to `/tmp`.
No HFS write/copy-into-volume operation was used. Assets, raw instructions,
disassembly and extracted text remain outside Git. `witness.py` fails on a
different executable or Mac UI identity and emits only interpreted operands,
relocation identities, selected model metadata and hashes. It does not depend on
the heuristic `analyze.py` function boundaries. The latter's known traceback
bounds failure was reproduced; a local bounds-checked version was used only for
discovery, never as a checked-in dependency or proof of a function's name.

Run from the repository after extracting the three private inputs:

```sh
python3 tools/ppc-analysis/lanes/ui/witness.py \
  /path/to/mac-feral/bin /private/mac-ui.wad /private/mac-residx.dat \
  --pc-ui /path/to/windows/Data/ui.wad
```

In an isolated worktree without the shared PEF reader, set `PYTHONPATH` to the
root's `tools/ppc-analysis` directory. Tests accept explicit private paths:

```sh
UI_EVIDENCE_BIN_ROOT=/path/to/mac-feral/bin \
UI_EVIDENCE_MAC_UI=/private/mac-ui.wad \
UI_EVIDENCE_RESIDX=/private/mac-residx.dat \
UI_EVIDENCE_PC_UI=/path/to/windows/Data/ui.wad \
UI_EVIDENCE_MAC_UITEXT=/private/mac-uitext.str \
UI_EVIDENCE_MAC_UIHELP=/private/mac-uihelp.str \
UI_EVIDENCE_MAC_MBTOUNI=/private/mac-mbtouni.dat \
python3 -m unittest discover -s tools/ppc-analysis/lanes/ui -v
```

The first phase passed 12 tests; the expanded lane passes **24 tests, zero
skips**, including the identified original
corpus and negative/truncated corpus spans, RefPack expansion/reference checks,
relative resource offsets, node-table bounds and signed branch/operand decoding.
The embedded layout tests also reject unknown/truncated commands and preserve
signed coordinates and low/high control IDs.
No OpenTPW game runtime files changed, so this evidence lane does not claim a new game test
pass. Python compilation and `git diff --check` also pass.

## Asset identity and authored layout

Both UI WADs contain the same 1,202 member names. Decoding every member and
comparing their contents finds exactly two differences: `textures/tpw_logo.wct`
and `stexture/tpw_logo.wct`. **All 278 MD2 members are identical.** A Windows
baseline UI model therefore provides the same geometry as this Mac UI archive;
the logos and executable layouts still have edition-specific differences.

The authored 2048×1536 canvas is supported independently of OpenTPW rendering.
For example, the original `f_chat.MD2` transformed bounds have a 2048-pixel width
and 1536-pixel height, within floating-point rounding. The layout transform code
at `code:0x13e46c` reads constants 2048, 1536, 1024 and 768 from
`data:0x57c0`, `0x57bc`, `0x57d0`, `0x57e4`. It constructs matrices using the
requested rectangle dimensions, mesh extents and screen dimensions. This is a
code consumer, not merely a matching pair of asset bounds.

Selected unchanged member identities:

| Member | SHA-256 | Relevant authored data |
| --- | --- | --- |
| `b_buy.MD2` | `0fa4273da2b3ce5942f6c15be9aff3d951ced8168b0e64fa94cab567bd9e53b7` | root translation approximately `(229.2515, -1181.2289, -53.6473)`; six nodes `b_buy, disable, hilite, heldown, hidown, down` |
| `panel.MD2` | `b41107fd7de61be3089336467512a9b11ea25d688fa8148e1096b380969e3ee6` | root `pan_money` approximately `(668.3914, -1281.5582, 36.9877)`; alternatives `pan_info, pan_buy, pan_staff` |
| `b_door.MD2` | `fe7af68a58fac8818711ed571756cdb5fb5c5c192448bf8b5c6a028d3949571f` | six geometry nodes; root at origin; visual variants alone do not prove open/closed event semantics |
| `f_helpbg.MD2` | `da32a5efbfcb8af4c1747e4f1bdb292bad02dea3a06ceccd268523b15b931b1f` | small reusable mesh; executable supplies its final rectangle |
| `f_chat.MD2` | `80b0845a7998b00be337e814db5cabba7725be77cad8f6fe7a365a76815f6868` | full canvas extent |

The Mac `_Resolution.sam` documents override codes 1–7 as 512×384, 640×480,
800×600, 1024×768, 1280×1024, 1600×1200 and 2048×1536, with code 0 selecting
the options value. Its shipping underscore disables it. Thus the shipped Mac
data acknowledges a 5:4 mode as well as 4:3 modes; it does not establish nearest
edge anchoring. Custom resolutions, upscaling and manual UI scale can remain
explicit extensions. Their policy need not be described as original behavior.

## Font allocation, selection and measurement

This is the strongest replacement for the scale-threshold part of UI-002.
The language initializer at `code:0x11e418` loads four banks of **13** font
resources. The accessor `code:0x1383c8` accepts indexes below 13 and returns
`base + 28 + bank * 312 + index * 24`. It reads the selected bank from the
resource manager's field `+4`. Each of the 52 resource-request ID operands was
checked and joined to the original `BFRI` index, whose filename offsets are
relative to byte 8. Names below are established by both code operands and the
actual American language index, not by guessing a naming convention.

| Slot | Bank 0 | Bank 1 | Bank 2 | Bank 3 |
| --- | --- | --- | --- | --- |
| 0 | MENUSMALL | MENUMED | MENUBIG | MENUBIG |
| 1 | CASHSMALL | CASHMED | CASHBIG | CASHBIG |
| 2 | SESHSMALL | SESHMED | SESHBIG | SESHBIG |
| 3 | DATETINY | DATESMALL | DATEMED | DATEBIG |
| 4 | GAME6AA | GAME7 | GAME10AA | GAME12 |
| 5 | TITLESMALL | TITLEMED | TITLEBIG | TITLEBIG |
| 6 | GAME5AA | GAME7 | GAME8 | GAME9 |
| 7 | GAME6AA | GAME8 | GAME9 | GAME10 |
| 8 | GAME5AA | GAME7 | GAME7 | GAME12 |
| 9 | GAME5AA | GAME7 | GAME8 | GAME9 |
| 10 | CONSOLE6 | CONSOLE6 | CONSOLE6 | CONSOLE6 |
| 11 | GAME6AA | GAME7 | GAME8 | GAME9 |
| 12 | POSTCARD | POSTCARD | POSTCARD | POSTCARD |

All names carry `.bf4`. Bank starts are `code:0x11f138`, `0x11f6c4`,
`0x11fc50`, `0x1201dc`. The selector at `code:0x11f0a0` reads the resolution
override field `+12`: positive values 1, 2, 3 map to banks 0, 1, 2; values
4 and above map to bank 3. With no positive override it reads the display
settings enum `+4`, mapping 0, 1, 2 to banks 0, 1, 2 and other values to bank 3.
The documented override therefore gives an explicit original mapping for the
four base resolutions and higher override modes. The display enum-to-size
consumer still needs separate verification before treating its integers as a
Windows configuration contract. The current universal `<0.36 / <0.6` rule
does not reproduce this table, particularly the fourth date tier and the
AA/non-AA choices of the generic fonts.

Identity proofs are relocation chains rather than heuristic labels:

| RTTI class | Vtable header | Slot | Code |
| --- | --- | --- | --- |
| InterfaceFont | `data:0x474e8` | 2 | `0x128164` |
| InterfaceFont | `data:0x474e8` | 3 | `0x128328` |
| InterfaceFontWrapper | `data:0x474c4` | 2 | `0x127e28` |
| InterfaceFontWrapper | `data:0x474c4` | 3 | `0x127f3c` |

The raw `InterfaceFont` width method indexes an eight-byte character record,
sign-extends its first byte, and adds the object's unsigned 16-bit spacing at
`+2`. The constructor defaults that spacing to 2; its initializer can replace
it. Its range method sums the same quantity per UTF-16 code unit. There is no
pair lookup in these methods. **This does not prove that every BF4 screen adds
two pixels.** The loaded bank uses wrappers: `InterfaceFontWrapper::slot2`
at `0x127e28` iterates UTF-16 code units and calls its character-width slot,
summing each result without pair context. Slot 3 at `0x127f3c` obtains a
character metric from the font resource and returns its signed width. These
paths establish per-character measurement, but the BF4-to-runtime metric
conversion, placement offsets, coverage-to-alpha mapping and draw blending
still need tracing. Do not promote the current linear alpha or greedy wrapping
policy merely because the asset records decode successfully.

Concrete consumers include popup help using slot 7 at `code:0x141ab0` and
ride status labels using slot 6 at `code:0x164f70`, `0x164fd4`. The ride status
constructor also binds text objects to original window IDs such as 15904,
15898 and 15900. Resolving those windows' allocation rectangles and associated
string/value indexes remains necessary; all ride labels are not interchangeable
with one generic font family.

## Button states, mesh selection and popup allocation

`InterfaceButton` is proven by RTTI header `data:0x4fc6c`; its refresh slot 5
points through a relocated transition vector to `code:0x1720e0`. That function
calls the classifier at `code:0x171f68` and sends its short result to attached
drawing objects. The classifier prioritizes attribute bit 1 at object `+68`,
then a byte at `+313`, then bits 0 and 1 of the byte at `+312`:

| Condition, in priority order | Returned index | Matching `b_buy` authored node |
| --- | --- | --- |
| attributes bit 1 | 1 | disable |
| `+313` set and `+312` bit 0 set | 3 | heldown |
| `+313` set | 2 | hilite |
| `+312` bit 1 set | 4 | hidown |
| `+312` bit 0 set | 5 | down |
| otherwise | 0 | b_buy |

This establishes the six-way state index layout. The labels “mouse held”,
“hover” and “latched down” for the two mutable bytes are still event-consumer
inferences until their input writers are traced. State setters at
`code:0x1722a0` and `0x172350` update these bytes and refresh attached renderers;
the ordinary constructor sets both bytes to zero.

`InterfaceDrawEngineMesh` RTTI header `data:0x47f28`, slot 3, points to
`code:0x13e83c`. Its helper `code:0x13e78c` walks a linked list from the
loaded mesh's `+120`, following link `+12`, using two short indexes from the
drawing object. It masks the old/new selected meshes and stores the selected
pointer at `+124`. The configuration method walks ancestor matrices, transforms
the selected mesh's bounds, derives a rectangle-fitting matrix, and calls the
imported `CMesh::SetLocalMatrix(int, sMatrix&)` at `code:0x13eb3c`.
The engine constructor at `0x13e26c` visits all alternative meshes and sets their
initial flags. This is real state/transform processing, not drawing all children
as ordinary composed scene parts. Exact flag meaning, the matrix helper's
composition convention and renderer depth order remain open. Consequently the
current “ignore child translations and reuse the root pose” rule cannot be
certified as equivalent solely from the six authored names.

**Popup help is near the bottom, not the top.** `CUIHelpWindow` RTTI header
`data:0x480e0`, slot 14, points to `code:0x141a94`. The allocator gets font
slot 7's height and forms integer height
`H = 2 * font_height * 1536 / drawable_height + 10`. It forwards the rectangle
`(574, 1520 - H, 1474, 1520)` through factory `code:0x17fe78`. Base-window
constructor `code:0x17dbb4` stores the four supplied coordinates at object
offsets 8, 10, 12 and 14, establishing their identity. The allocator installs
`f_helpbg`, selects font slot 7, and supplies RGBA `(255,255,255,255)` to the text
color setter. This replaces the top-center location and invented flat background
portion of UI-010 for the identified Mac build. Original help art's final alpha,
exact baseline and all dynamic resizing still need the drawing method. The
Windows manual independently advertises bottom help (PDF 6, printed 11);
that corroborates the region, not Mac pixel coordinates.

## Embedded layout interpreter and annual summary panel

Original allocation rectangles are also stored as a **short command stream in
the executable's data section**. They are not absent merely because no loose
screen layout file was found. Entry `code:0x181aac` resets a shared cursor and
calls recursive interpreter `0x181afc`. It reads signed big-endian halfwords,
checks command indexes 0–18 and dispatches through relocated table
`data:0x500d0`. Command 0 reads the window type, low/high attribute words,
low/high control ID words and four signed rectangle coordinates. It calls the
same window factory `0x17fe78` at `0x181c3c`, then recursively consumes that
window's child stream at `0x181c64`. Command 5 ends a stream. Commands 1/2 take
two-word resource references; 17/18 store two-word values at window fields
140/144. Their field meanings are not assigned invented names here.

The first phase decoded that established subset; phase two handles all 19
command shapes, with neutral geometry-property names. It parses the annual
summary table at `data:0x4f81c` completely: **27 windows, 478 halfwords**.
No inferred instruction length or guessed boundary is used to skip commands.

| Control | Parent | Original authored rectangle `(left, top, right, bottom)` |
| --- | --- | --- |
| 44444 root | none | `(248,30,1800,1007)` |
| 44449 information group | 44444 | `(437,220,1559,673)` |
| 44450 current rating value | 44449 | `(901,427,1136,472)` |
| 44451 same-row label | 44449 | `(462,427,859,472)` |
| 44461 prior-year value | 44449 | `(1244,427,1479,472)` |
| 44462 current column heading | 44449 | `(851,244,1187,355)` |
| 44463 prior-year column heading | 44449 | `(1189,244,1525,355)` |
| 44467 title | 44444 | `(809,74,1281,153)` |

The builder `code:0x16eeb0` constructs the annual summary and dynamically assigns labels: lookup ID 44451 at
`0x16f078/0x16f07c`, choose font slot 6 at `0x16f0b0/0x16f0b4`, request
UITEXT 190 through accessor `0x138504` at `0x16f0c4/0x16f0c8`, install that
text at `0x16f0d4`. Headers bind UITEXT 188 and 187 at `0x16ef8c` and
`0x16eff4`, both using slot 8; the title binds UITEXT 186. The economy lane independently decoded the baseline English BFST/BFMU corpus:
UITEXT 186 is End Of Year Summary, 187 Last Year, 188 This Year, and 190
Park rating. Its private UITEXT identity is
`3fe8b89c994bdd177b7226a51f24222621cc7e27beee94668942dc1f821137cf`;
its BFMU identity is
`69f23492ef61a27ed79dd4df67978536720d403f7e2733acb6b43e6f4f78c587`.
That is a Windows baseline text corpus linked to the Mac resource IDs, not
a Mac screen capture. The economy lane
traces current and prior-year monthly composite values to controls 44450 and
44461. The rectangles and resource indexes above can be combined with that
lane's formula evidence; neither screen title nor proximity alone proves the
formula. These are allocation rectangles, not a screenshot or a guarantee
that every child uses absolute screen coordinates after all transforms.

Phase two additionally identifies and decodes 54 other tables as described
below. This supplies original allocation metadata for UI-013/020/021/024/028;
it does not certify original pixels or close those IDs wholesale.

## Expanded layout corpus and root-name resource bindings

`phase2.py` independently verifies **60 caller paths to 55 distinct tables,
934 controls**. Fifty-one calls reach `0x181aac` directly; nine status-panel
initializers forward their table through `0x1471f8` to that entry. Each table
argument is proved by its TOC relocation and the actual load/call operands,
including copies from preserved registers. The report stores table ranges,
hashes, counts, root rectangles and selected controls; it emits no whole raw
table or disassembly. These are the identified caller set, not a claim that
every possible indirect table reference in the executable has been found.

Command shapes now include typed geometry blocks, bounded point arrays,
scrollbar/vary-box/list-control implied child rectangles, list column pairs,
light-panel indexes and the outer caller's rectangle properties. Counts are
limited to 512 entries, nesting to 64, controls to 512 per table and total
read words to 4096. Invalid subtypes, incompatible implied-child parent types,
unknown commands and truncated spans fail. Commands applying properties to an
existing caller-supplied window are retained as external-parent properties,
not attributed to the last decoded child. One concrete example is
`data:0x50172`, the city-lobby table, whose final command 3 targets the caller.

Resource references are **root-node name hashes**, not filename hashes.
Ordinary model loader `code:0x13c0ec` loads the model, reads its root mesh at
`0x13c1fc` and that mesh's name field `+84` at `0x13c200`, then registers the
drawing object through `0x173844`. Registration calls `0x1709b8` with multiplier
47, storing the result at registry-node `+16`. That helper's recurrence is
`h = ((h XOR byte) * 47) mod 2^32`, starting at zero. Layout commands 1/2
resolve their stored keys through `0x173a70`, which compares registry field
`+16`, follows link `+20`, and obtains the drawing object through its virtual
slot. This proves the name-to-table-to-renderer chain rather than merely finding
matching strings. The registry rejects duplicate keys; witness output retains
multiple asset candidates if an offline hash join is ambiguous.

For example, `mainpanel.MD2` has root name `base`, hash 468387477; hashing
`mainpanel` gives a different key. `panel.MD2` uses `pan_money`, and
`f_optpanel2.MD2` uses `optpanel2`. All selected primary references below have
one identified member/root candidate.

### Main HUD: `data:0x4ab38`, caller `code:0x156c24`

| Control | Model/root | Authored allocation rectangle |
| --- | --- | --- |
| 29 | mainpanel / base | `(37,984,439,1507)` |
| 32 | date / date | `(153,1044,404,1121)` |
| 33 | panel / pan_money | `(331,1069,1006,1495)` |
| 38 buy | b_buy | `(170,1122,288,1240)` |
| 40 info | b_info | `(287,1133,405,1252)` |
| 42 finance | b_money | `(156,1241,274,1360)` |
| 43 research | b_resrch | `(274,1254,392,1372)` |
| 41 map | b_map | `(81,1340,200,1458)` |
| 39 camera | b_camera | `(202,1355,320,1474)` |
| 47 cash text | text object installed by builder | `(258,60,720,260)` |
| 48 cash change text | text object installed by builder | `(458,273,720,363)` |

These six principal buttons have different original rectangles despite their
shared authored root pose. The ordinary HUD builder installs **cash font slot
1 with white RGBA** at `0x157030..0x157068`; **date font slot 3 with black
RGBA** at `0x1573ac..0x1573dc`. The cash-change text uses slot 2 with yellow
RGBA at `0x1570c8..0x157100`. Cash control 47 is additionally resized/repositioned
from measured font extents and drawable dimensions by `0x156ef4..0x157000`;
the table rectangle alone is not its final glyph box. Date text has its own
explicit drawing-region rectangle `(182,1061,383,1103)`. Number formatting and
the complete cash/date baseline conversion remain separate dependencies.

### Theme lobby and online world lobby are different tables

Theme table `data:0x50366`, loaded at `code:0x1839a4`, binds:

| Control | Model/root | Authored allocation rectangle |
| --- | --- | --- |
| 123111 main panel | islandlobby / islandlob | `(28,855,476,1521)` |
| 123112 enter | b_entpark | `(68,1114,221,1267)` |
| 123113 world view | b_worldview | `(68,946,221,1099)` |
| 123120 previous | b_lobleft | `(97,1299,250,1453)` |
| 123121 next | b_lobright | `(260,1299,413,1453)` |
| 123119 theme text | installed text object | `(477,1303,1571,1437)` |

The panel's secondary drawing reference binds `f_lobbutbg` and command 3 sets
region `(67,1273,438,1484)`; a 23-point polygon supplies its shaped hit region.
This is stronger evidence for UI-014 than putting the enter button between
previous/next based on modern frontend appearance. Caller font assignments and
mode-specific show/hide logic still need integration. `tpwlogo` also appears in
the separate login/profile layout `data:0x4bea8`, so its authored root alone
does not establish every lobby's logo position.

The **online world** table `data:0x523f0`, loaded at `code:0x1b90ac`, instead
binds `worldlob`, `f_wlobbg`, world-navigation arrows and find/back/zoom buttons.
Its root rectangle is `(9,620,510,1531)`. Applying these world-lobby allocations
to the offline island frontend would conflate two original screens.

### Options, buy-items and ride information

The options table at `data:0x4b0dc`, loaded at `code:0x158cd4`, has **57 controls**
and a full-canvas `f_screen` root `(0,0,2048,1536)`. It is not a generic `w_med`
window. A representative option row 120021 binds `f_optpanel3 / optpanel3`
at `(57,323,1289,472)`, with value region `(990,361,1248,432)`, a scroller child
at `(1023,364,1090,431)`, and label 120009 at `(117,375,706,420)`. Other rows
use `f_optpanel`, `f_optpanel2`, toggle buttons and distinct column groups;
the uniform 58%-split rule is not the original allocation scheme. OK/exit use
`b_okay` and `b_exit` at `(1763,1340,1846,1424)` and
`(1853,1340,1936,1424)`.

Buy-items table `data:0x4cd94`, loaded at `code:0x164528`, has a `w_big`
root `(186,30,2018,1007)`, a preview frame 490 `(408,179,822,593)` and catalogue
control 504 using `f_buyitem / buyitem` at `(1009,179,1813,902)`. Command 11
provides **three columns**, `(1039,1447)`, `(1460,1664)`, `(1673,1724)`;
its content region is `(1037,427,1727,871)`. It has actual scrolling children,
plus ride/shop/show/feature category buttons 507/509/506/508. Three columns
do not mean three rotating preview-icon slots: catalogue sorting, row contents,
selection and post-purchase continuation still require their controller.

Ride-status initializer `code:0x1673a4` forwards `data:0x4d1e8`. The table has
a `w_med / window2` root 15892 `(248,30,1800,1007)`, left preview frame 15908
`(348,162,762,576)` and text/stat frame 15893 `(853,162,1565,549)`. Original
label/value rows include users-last-month, age and excitement; labels use font
slot 6. The excitement label 15900 is `(876,279,1273,324)` and its value 15894
is `(1315,279,1550,324)`. This is an original separate information window,
not proof that the current build/info arm reproduces the same composition.
The delete button 15915 binds `b_erase` at `(300,800,454,953)`; door 15928
binds `b_door` at `(1430,784,1635,953)`.

### Platform text and door state semantics

The actual Mac American table contains **474 UITEXT entries**, versus 473 in
the baseline English PC table. IDs 17–193 used above agree, but late IDs shift:
Mac 315 is Game Options, 319 screen resolution, 320 audio quality, 321 effects
volume, 324 movie volume, 327 popup help. Mac 330 is **Ctrl-click scroll**;
the PC's corresponding option names a right-button operation. Do not copy Mac
late resource indexes into the current PC enumeration without an edition map.
Selected Mac labels are decoded with the Mac's own BFMU, with both identities
pinned; they do not depend on current OpenTPW fallback strings.

The door table sets field 140 to help ID 13 and field 144 to ID 12. Button
virtual slot 11, `code:0x172d90`, returns field 144 when its state byte `+312`
equals 1; otherwise it returns field 140. Actual Mac help 12 offers **open**,
help 13 offers **close**. Together with the state classifier, this establishes
the ordinary/open and latched-down/closed UI interpretation beyond the art.
The callback that mutates the ride's simulation open flag and the delete refund
remain untraced; the prompt alone cannot establish purchase/scrap arithmetic.

### Keyboard and button event consumers

Keyboard group matching at `code:0x114ac0` and `0x114b9c` scans 20-byte records
using signed-short key/modifier fields `+2/+4`. The first calls callback `+16`
and sets record `+6=1`; the second calls callback `+12` and clears that field.
The original game group has 15 records. Slow/fast scalar callbacks are in the
second slot: records `data:0x452c4`/`0x452d8` have encoded keys `0x6d00`/`0x6b00`,
modifiers 0, vectors `0x7508`/`0x7510` and no first-slot callback. These are
release-side speed actions. Their hardware key translation still requires the
platform input producer; no physical key name is inferred from an encoded short.

The button event handler `code:0x1724f4` sets state bit 1 on protocol message
`0x10005`, then clears/toggles state on `0x10004`, depending on attributes.
After checking its stored press/capture record, it emits parent message 256
with window ID and state (`code:0x172a34/0x172a38`). It also maintains hover
byte `+313`, checks disabled/repeat attributes, and has constructor repeat
intervals **500/125 milliseconds** at `+316/+320`. The clock lane traced widget
getter `0x171ef4` through current clock object `data:0x4fc24`. The default
callback `0x171e9c` divides GetAbsolute by 1000; application UI setup at
`0x13cb18` selects a callback through object `data:0x120e74` to
`LbTimeClockTickFunction` at `0x13ca44`, which calls `LbTime_GetClock`.
Both are raw milliseconds, distinct from the scaled park scheduling clock.
This supports release activation and latched buttons within the original UI
protocol; top-level pointer production and keyboard focus navigation remain open.

## Sign effects: RGB assumption contradicted

The expanded sign witness identifies `engine_shared` as well as the app.
`Parameters[2]`, `[3]`, `[4]` are **not an RGB triplet**. Effect helper
`code:0xab128` loads them from effect offsets 12/16/20 into three scalar
registers. At `0xab484/0xab488/0xab48c`, all three normalized source image
color channels are multiplied by the **same** first coefficient. A positive
lighting term scales the second coefficient and affects all channels; a
specular term raises a value to the exponent at offset 24 (`Parameters[5]`)
and adds the third coefficient as a white highlight. Each channel is converted
back to 0–255 and clamped. These are ambient/diffuse/specular-style coefficients,
with the labels inferred from the arithmetic, not official field names.
Parameters 6/7 additionally feed angle-to-radian/sine/cosine calculations in
this effect path; calling them x/y/width is not supported.

The effect output's byte 0 is copied from the mask; computed color bytes occupy
1/2/3 (`code:0xab54c`, `0xab564`, `0xab56c`, `0xab570`). Engine export
`Bitmap::swizzle_for_gimex()` resolves to `engine code:0x3e6c8`; its complete
loop reverses each four-byte pixel to **B,G,R,A**, preserving alpha and dimensions.
It does not resize the 256-square intermediate before the output copy/pack
loops. Those loops fill two caller-supplied 128-square destinations. Therefore
OpenTPW's two 256-square destinations are an enlarged policy, not original
texture sizing. Exact mesh UV orientation, all compositing operations and
the packed 16-bit mode's consumer still need verification.

The source SGN interpretation also needs repair before naming every field:
the original loader reads two effect/style integers at source offsets 9 and
13 before reading the first font record at 17. Its effect reader consumes
**11 words / 44 bytes** after each 392-byte font record. The second font record
therefore starts at 453. Current `SignFile` reaches the same face/LOGFONT
offsets, but its `HeaderCount` / per-slot `StyleId` interpretation cuts the
record boundaries differently. Four SGNs from the identified Mac/PC-identical
lobby archive confirm these offsets and have nonzero effect extents. This is
an evidence-backed semantic correction dependency, not permission to silently
reinterpret the public reader or every field's units.

Reproduce the expanded metadata without storing it in Git:

```sh
python3 tools/ppc-analysis/lanes/ui/phase2.py \
  /path/to/mac-feral/bin /private/mac-ui.wad \
  --mac-uitext /private/mac-uitext.str \
  --mac-uihelp /private/mac-uihelp.str \
  --mac-mbtouni /private/mac-mbtouni.dat
```

## Standalone C# metadata reader

`tools/ppc-analysis/lanes/ui/csharp/OriginalLayoutReader.csproj` targets the
repository's .NET 8 SDK and uses only framework libraries. It has no package or
project dependencies and is not connected to the OpenTPW renderer. Public
reader/metadata types live in `OpenTPW.Reverse.Ui`; the executable entry point
is a dependency-free validation runner.

`OriginalLayoutReader.Read` returns an ordered command AST, flat allocation-order
control list, source addresses, signed rectangles/control IDs, unsigned attribute
bits, drawing keys, neutral geometry/style parameters, pairs and nested scopes.
Parent indexes identify actual allocations even when different controls reuse
the same numeric ID. Repeated property assignments remain ordered in the AST;
their last values can be inspected without deleting the earlier commands.
Properties of an existing external caller remain in the outer scope. The reader
does not manufacture that caller's control type or apply those properties to an
unrelated child.

All 19 proved command shapes are supported on the fresh-allocation path. Unknown
commands/subtypes, incompatible implied-child parents, duplicate implied children,
unknown external-parent types for typed commands, invalid light indexes and
truncated spans have explicit diagnostics with command and section-relative
address. Type 0 is rejected: original factory jump-table entry 0 reaches
`code:0x18020c`, which returns null rather than an allocated control. Other types
outside 1–13 are also rejected. Geometry subtypes 1–3 preserve their scalar
arguments; exact paint/hit-test semantics are not invented. This is a metadata
reader, not an emulator of state-dependent window lookup or allocation failure.

Hard caps are 32 MiB input, 4096 consumed halfwords, 512 controls, 64 nested scopes
and 512 entries per geometry/column array. Caller limits can tighten these caps.
Source-address overflow, trailing input and missing end commands fail; explicitly
requested prefix mode reports exact consumption when reading from a larger local
buffer. `OriginalNodeNameHash.Compute` takes nonzero name bytes and reproduces
the signed-byte XOR/multiply-47 arithmetic, including wraparound. It rejects
embedded C-string terminators and names over 255 bytes.

Font assignments are separate constructor metadata. `OriginalLayoutFontMetadata`
returns the three known HUD slot/color bindings only when the declared Mac
executable identity, section/address and **computed table SHA-256** all match.
A synthetic table carrying a claimed Mac source identity receives no bindings.
The layout grammar itself does not choose a BF4 bank or decode glyphs.

`OriginalUiLabelResolver` identifies actual UITEXT/BFMU pairs by both SHA-256
values and entry count. It describes explicitly observed semantic/index pairs
for the identified Mac American and Windows baseline English editions. It can
read selected labels only from the matching resource identity; unknown variants,
mixed character tables and mismatched raw indexes fail. It does not alter the
PC enum or infer a universal +1 rule. Mac Ctrl-click and PC right-button option
descriptions remain edition-specific. Other languages/editions need their own
verified mappings.

Validation:

- **19 synthetic C# cases passed, zero skips**, covering every command shape,
  all geometry arities, signed/full-width fields, parent indexes, ordered/outer
  properties, unsupported corners, budgets, diagnostics and identity guards.
- A temporary, off-Git corpus manifest supplies the 55 original table slices
  plus independently generated Python metadata. C# matches **all 934 controls**,
  consumed words, source offsets, IDs/types/attribute bits, parent IDs, every
  effective property, geometry/column payload and table hash. It also verifies
  the three known font bindings and both label editions.
- Build/static analyzers: **zero warnings/errors**. SDK formatting verification
  passes. Python compilation and `git diff --check` pass.

Reproduce synthetic validation:

```sh
dotnet build tools/ppc-analysis/lanes/ui/csharp/OriginalLayoutReader.csproj
dotnet run --project tools/ppc-analysis/lanes/ui/csharp/OriginalLayoutReader.csproj \
  --no-build -- --self-test
```

Generate private inputs **outside every Git worktree**, then verify them:

```sh
python3 tools/ppc-analysis/lanes/ui/export_private_layouts.py \
  /path/to/mac-feral/bin /private/off-git/ui-layout-corpus \
  --mac-uitext /private/mac-uitext.str --mac-mbtouni /private/mac-mbtouni.dat \
  --pc-uitext /path/to/pc/English/UITEXT.str --pc-mbtouni /path/to/pc/English/MBToUni.dat
dotnet run --project tools/ppc-analysis/lanes/ui/csharp/OriginalLayoutReader.csproj \
  --no-build -- --verify-private /private/off-git/ui-layout-corpus/manifest.private.json
```

The exporter checks the identified executable and refuses a destination beneath
a Git worktree before reading/copying original table data. This guard was tested
with a repository destination and no output was created. Private table slices,
language resources and generated expectations are not checked-in fixtures.
Synthetic fixture construction is confined to the validation runner.

Independent review is required before renderer/controller integration. Remaining
gates include conditional lookup/allocation behavior, dynamic rectangles, BF4
coverage/baselines, control routing, full locale identity maps and original pixels.
The reader supplies evidence-grounded metadata; it does not close UI fidelity tags.

## Sign/font compatibility path

`code:0xaba40` reads the SGN header, two text slots and subsequent effect blocks;
its slot reader `0xaa5a0` reads 64-byte face and 260-byte filename fields, two
integers and 60-byte LOGFONT records, swapping numeric fields for PowerPC.
`code:0xabf14` calls text-mask helper `0xa9f9c` twice. A park sign caller at
`code:0x18e728` supplies base texture dimensions **128×128**; the renderer
allocates intermediate buffers at twice both base dimensions. These are two
128-square texture destinations, not evidence for OpenTPW's chosen pair of
256-square destinations. Full compositing/packing and output orientation must
be carried through before replacing the complete sign canvas contract.

The text-mask helper has concrete behavior beyond an imported symbol name:

- Creates an uncompressed, top-down **8-bit** DIB at twice the text-mask width
  and height; supplies a 256-entry grey palette.
- Creates/selects the slot LOGFONT and measures text through
  `GetTextExtentPoint` (`code:0xaa29c`). If too wide, it searches a narrower
  LOGFONT **width**, remeasuring candidates. It does not use an eight-texel
  padding rule or rescale every glyph's em height.
- Calls `TextOut` at `code:0xaa410` with horizontal origin
  `256 - measured_width / 2` and vertical origin taken from the slot offset.
- Averages each 2×2 block of DIB samples using integer division by 4 to create
  the mask (`code:0xaa480..0xaa4bc`). It scans mask rows to record first/last ink.

All four app call targets resolve through imported transition-vector glue;
the witness verifies `CreateDIBSection`, `CreateFontIndirect`,
`GetTextExtentPoint`, `TextOut`. On this **Mac** the `TextOut` export resolves
to `macdoze code:0x64bc`, whose wrapper calls `0x9a58`. That implementation
saves/sets a GWorld, sets color, moves the pen and calls QuickDraw **StdText**
at `0x9b1c`; measurement calls **StdTxMeas** at `0x9bf8`. This is not a captured
Windows GDI rasterization oracle. The 2×2 mask reduction is proven; the precise
OS text rendering, hinting, kerning or antialiasing policy is not.

## Speed, cursor and camera handoffs

UI callbacks at `code:0x11315c` and `0x113184` call the scalar routines
`0x127c88` and `0x127c48`. Their transition vectors are `data:0x7508` and
`0x7510`, referenced by records at `data:0x452d0` and `0x452e4`. The scalar
initializes to 1, divides/multiplies by 1.25 and clamps to **0.25..2.0**.
The clock lane traced `0x127cd0` to the main scheduling clock via
`0x10e844 -> 0x11a588 -> 0x10ed54 -> 0x117d74`, ordinary animation and RSE
waits. Advisor LIP has a separate unscaled clock. UI-022's ×1/×2/×4 economy-only
control is therefore a different policy. The records' event/key dispatch,
displayed speed widget and pause interaction remain unresolved; do not infer
their key meanings merely from packed numeric default fields.

The Mac UI loader explicitly names 22 TGA cursor resources, from `CNor.tga`
through `CCro.tga`, at `code:0x1d9b56..0x1d9c13`. The Mac HFS directory
ships those TGAs plus 22 ANI/CUR members. `InterfaceCursor` maintains visibility
and two byte fields; `InterfaceBFCursor` has its own draw/position slots at
`code:0x199dd4`, `0x199e18`, `0x199e5c`. A system cursor alone therefore does
not implement the original cursor path. Hotspots, selected mode, frame timing,
clipping and mouse-focus ownership remain consumer-tracing dependencies.

The lobby parser at `code:0x97bac` converts `ISLANDFOV`, `SPINSPEED`,
`SPINRADIUS`, `VERTICALOFFSET` into float fields at offsets 4, 8, 12, 16 of the
parsed lobby object. The stores for the first three are `0x97d20`, `0x97dbc`,
`0x97e58`. Mac and baseline lobby archives match, so their literals are data,
but interpreting the spin value as radians per 0.1 second is not yet established.
`CLBLobbyView` RTTI header `data:0x3e840` supplies view entry points, including
initializer `0x8ef50` and camera vector/matrix update `0x8f1f4`; tracing the
parsed fields through the lobby controller and interpolation is still needed.
No 60° FOV, 3/s glide or focus behavior is certified by those class identities.

## Bounded production model-binding integration

The current UI model cache no longer treats a filename as the original registry
identity. `UiModel` retains `RootNodeName` from `ModelFile.RootNodeIndex` and
computes `DrawingKey` with the evidenced signed-byte recurrence. `UiModels`
builds the actual root-key registry and exposes strict drawing-key/root-name
lookups. Existing authored widget calls can still use explicit filename aliases;
exact-case aliases take priority over an unambiguous case-insensitive fallback.
Root names themselves are not case-folded or trimmed. Colliding drawing hashes
and ambiguous asset aliases produce explicit diagnostics, not first-file selection.

The all-278 corpus revealed a real duplicate: `shadow1.MD2` and
`w_small_shadow.MD2` both have root `wshadow1`, key 1248416976. The ordinary
loader's filename comparison at `code:0x13c170` resolves to imported `strcmp`
and its matching branch returns without registration. The production registry
keeps that specific shadow-file exclusion, while inventory/explicit asset lookup
still exposes all 278 files. This yields 277 ordinary bindings and avoids
pretending both files have distinct original keys. Upstream filename canonicalization
is not generalized into root-name case folding.

Verification locks the actual bug: a synthetic asset whose filename differs
from its stored root could not be resolved before the fix. Tests additionally
cover a sparse dummy root distinct from the first drawable frame, case-sensitive
root variants, ambiguous filename variants, deliberate distinct-name hash
collisions, the special shadow asset and collision detection after custom loads.
The corpus test checks every asset's stored root against its independent ModelFile
record and all ordinary key lookups. **56 UI tests pass, zero skips**, including
the existing custom display/interface scaling tests. Build reports existing
repository/package warnings; no dependency versions or unrelated sources changed.

This integrates the root binding rule only. The 55-screen metadata reader stays
standalone pending peer review; controller/layout/paint integration and original
pixel verification remain open.

## Current sign renderer correction plan

The production call path was independently checked:
`SignTextRenderer.RenderSign -> SignCanvas.Compose -> SignCanvas.SlotColor`.
`SlotColor` clamps `Parameters[2..4]` into independent RGB channels and passes
them to `SignTextLayout.DrawLine`. `SignFileTests.ReadsHeaderTextSlotsAndLogFont`
currently asserts that RGB interpretation. This tests the existing implementation,
not the original behavior, and contradicts the established effect arithmetic.

A correction must remove that interpretation rather than rename its output:

1. Preserve the slot floats as effect/material coefficients and correct the SGN
   effect/header record boundaries with a bounded reader. Parse the original
   per-effect source image/color blocks before assigning a final sign color.
2. Keep glyph coverage separate from surface color. The original raster helper's
   white text produces an 8-bit mask with integer 2×2 averaging; it does not
   establish a flat white final sign material or a Windows hinting oracle.
3. Apply shared base/diffuse/specular terms to the decoded source RGB, preserve
   mask alpha, then perform the identified channel swizzle and output split/pack.
   Validate flat and enabled-effect paths, boundary coefficients, missing blocks,
   clipping and the 16-bit consumer separately.
4. Replace the RGB assertion with meaningful regressions: modifying a material
   coefficient affects the same lighting term across channels, not an arbitrarily
   selected color channel. Use synthetic image/mask fixtures plus privately
   identified original records; keep raw assets outside Git.

No new guessed RGB or neutral-color replacement is proposed. Until the complete
surface path is available, any mask-only preview must be explicit presentation
policy with diagnostics; it cannot claim original final color. The binder's write
scope does not include the sign compositor, so this plan is handed to the sign
owner and the current contradictory runtime path remains identified as a required
follow-up rather than being silently certified.

## Every UI approximation: result and concrete remaining dependency

“Partial” means a replaceable subclaim is established; it does not close the
whole register ID. This lane changes no gameplay/register tags, and reports
**zero fully resolved IDs**. The requested extensions need no invented original
values to stay available.

| ID | Result | Concrete remaining dependency |
| --- | --- | --- |
| UI-001 | Authored canvas proven; custom output anchoring is extension policy. | Trace rectangle transforms under 1280×1024/other original modes before claiming original nearest-edge behavior. |
| UI-002 | Partial: four 13-slot font banks and override mapping established. | Bind each screen/window to its font slot; trace display enum size mapping and final font draw coverage. |
| UI-003 | Partial: linked state selection and fitted mesh matrix path established. | Decode `SetLocalMatrix` and ancestor composition, show how child translations cancel/participate for each frame. |
| UI-004 | Open; no triangle-depth order proof. | Follow selected mesh to the engine render queue, texture ordering and Z/depth/blend flags. |
| UI-005 | Pink unused texels are asset data; filter/bleed remains OpenTPW policy. | Trace original texture conversion and sampler settings at UI mesh rendering. |
| UI-006 | Partial: popup white, HUD cash white, cash-change yellow and date black supplied by original constructors. | Trace other controls and disabled/highlight paths; establish final blend/backdrop colors. |
| UI-007 | Open. | Trace text drawing flags and secondary draw offset; one framebuffer/UI-scaled pixel is not proved. |
| UI-008 | Partial: generic six-way state classifier is proved. | Find the purple text-button resource allocation and UV/state binding; do not infer halves from generic buttons. |
| UI-009 | Partial replacement: complete 57-control options table, row-specific frames/label/value regions and scroller children decoded. | Bind every row's dynamic value/label and handler; verify clipping and final transformed text bounds. |
| UI-010 | Partial replacement: bottom rectangle and `f_helpbg` identified. | Follow help draw object baseline, alpha, multi-line resize and show/hide timing. |
| UI-011 | Open. | Trace dialog stack draw order and background overlay/disable flags; a modal input lock does not prove dimming. |
| UI-012 | Partial: release-side activation, keyboard callback records, latched states and unscaled 500/125 ms repeat traced. | Trace top-level physical input conversion, arrow/Enter/Escape navigation, Mac Ctrl-click versus PC right button and focus ownership. |
| UI-013 | Partial: 55 tables/60 caller paths, 934 controls, all 19 command shapes, root-name bindings decoded. | Trace indirect/custom allocations, mode-specific visibility and final transforms; qualify each named screen's controller. |
| UI-014 | Partial: actual island panel/enter/previous/next rectangles and model keys identified; world lobby kept separate. | Bind theme/name text, logo and player/menu state; account for different logo pixels and dynamic visibility. |
| UI-015 | Contradicted by Windows manual's player-creation mode flow. | Trace Mac player creation/select/continue state machine and save handoff; distinguish mode-specific park initialization. |
| UI-016 | Angle/height literals remain data, usage open. | Trace ISLAND parser writes to island transform/camera-target consumers, including degree/radian conversion. |
| UI-017 | Parsed camera fields identified; current unit/FOV/glide rules open. | Follow float fields 4/8/12/16 through lobby controller to view interpolation and camera projection. |
| UI-018 | Partial asset equality; static sky is incomplete behavior. | Trace lobby flying mesh, rain/lightning and animation consumers plus camera scene render stages. |
| UI-019 | Current fallback only. | Establish completeness of every original camera-position record and original absent-record branch; no invented default from gaps. |
| UI-020 | Partial replacement: six distinct original HUD button rectangles, model keys and Mac help-role bindings established. | Apply original transforms and compare original runtime pixels; button state effects and clipping must stay consistent. |
| UI-021 | Partial replacement: cash/date rectangles, font slots and constructor colors established, including DATETINY tier. | Complete measured cash repositioning, date baseline and locale grouping/formatting; table bounds alone are insufficient. |
| UI-022 | Contradicted subclaims: scalar uses 1.25 steps, .25..2 and affects scheduler/animation/scripts. | Resolve callbacks' input dispatch, displayed control and pause behavior; integrate clock-lane consumer proofs. |
| UI-023 | Test-only value, no original evidence required for game path. | Keep fixture calendar separate and audit all runtime consumers for accidental use. |
| UI-024 | Partial replacement: original buy window with preview/three-column scrolling catalogue and separate ride-status window decoded. | Trace catalogue row contents/sort/selection/category controller, coordinate inheritance, translated labels and final arm/window state. |
| UI-025 | Open. | Identify tag queue enqueue/dequeue, capacity/expiry clock and `msgtag`/`f_tag*` allocation; distinguish advisor from UI queue. |
| UI-026 | Preview meshes are original data; projection/turn/sort open. | Follow catalogue preview draw object's model rotation, animation selection, camera projection and queue ordering. |
| UI-027 | Open. | Trace original hit/pick mode and geometry/grid query before selection notification; ground-cell occupancy alone is insufficient. |
| UI-028 | Open, simulation dependency remains. | Resolve RideStatusPanel value bindings and formatter for excitement/reliability/repair/life; render advertised values from real simulation. |
| UI-029 | Partial: door state 1 selects actual Mac open prompt, ordinary state close prompt; erase/door IDs and rectangles identified. | Trace ride-open flag mutation and erase event into deletion/accounting; prompts do not establish refund behavior. |
| UI-030 | Open. | Trace options initialization from registry/defaults and normalized audio gains, help toggle persisted state and missing-key branch. |
| UI-031 | Windows manual contradicts ride-only single placement: queue follows ride placement. | Trace Mac placement mode transition after purchase, cancellation, shop/feature repetition and scrap ownership; implement a real queue tool. |
| UI-032 | Partial: per-character measurement established. | Trace original wrap/truncation/font-switch decisions for each control and localized text; current greedy wrapping is not established. |
| UI-033 | Test-only fallback, no original evidence required with corpus. | Keep missing Totem fixture price out of ObjectCatalog runtime path and verify actual asset availability. |

Relevant compatibility IDs:

| ID | Result and remaining dependency |
| --- | --- |
| COMPAT-001 | Partial replacement: two 128×128 destinations, larger masks/DIBs and dimension-preserving channel swizzle established; geometry UV orientation and full compositing still require integration. |
| COMPAT-002 | Partial replacement: OS-measured centering and LOGFONT-width search, no evidenced eight-texel margin; trace empty/overlong/multi-line text and final texture coordinates. |
| COMPAT-003 | Contradicted: parameters 2–4 are shared material/lighting coefficients, not RGB. Preserve source image colors and implement the evidenced effect arithmetic after correcting SGN field meanings/compositing. |
| COMPAT-004 | Partial: SGN reader handles effect/pixel blocks and optional image path; trace/decode the background/effects and verify byte-consistent output instead of flat dark board. |
| COMPAT-005 | Open: SGN scale integer is read separately from LOGFONT width; width-search proof does not assign the 85..141 field a unit or prove it is ignored. Trace its consumers. |
| COMPAT-006 | Partial: identified Mac sign call reaches QuickDraw StdText/StdTxMeas; no Windows GDI pair-kerning conclusion follows. Inspect backend/font settings or obtain original-platform glyph-spacing oracle. |
| COMPAT-007 | Open: trace saved park name and resource/object-name selection into both sign text inputs. |
| COMPAT-008 | Open: mesh/textured face render depth and offsets need engine consumer proof; arbitrary normal lift is not supported by text raster helper. |
| COMPAT-009 | Partial replacement: two-times DIB dimensions and integer 2×2 mask averaging are proved; OS hinting/AA coverage and later sign filtering remain unverified. |
| COMPAT-011 | Partial resource accessor identity only; missing string/empty currency fallback branch needs the original resource implementation, not current six-language success. |
| COMPAT-012 | Open: follow UniToMB conversion failures into text edit/sign naming/serialization; no `?` replacement rule established. |

The [Windows manual evidence](../REFERENCE-MANUAL.md) supplies player/mode flow,
bottom-help and ride-to-queue continuation statements with page locators. They
must remain labeled Windows manual evidence. Identical Mac assets, static
Mac instructions and advertised Windows controls are three distinct kinds of
evidence; none proves exact original pixels or Windows Patch 2 timing.
