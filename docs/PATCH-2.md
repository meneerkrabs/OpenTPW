# Official Patch 2 data evidence

On 2026-10-09 the original EuroAmer patch was applied to a private installation copy on a Windows 2022 GitHub runner. [Run 37948688404](https://github.com/meneerkrabs/OpenTPW/actions/runs/37948688404) completed the English (`2057`), common and EuroAmer stages with native result 0, no diagnostic callbacks and no unsupported requests. The patch engine and patch files were pinned to the original package hashes in [the runner](OFFICIAL-PATCH-RUNNER.md). The game was never launched.

This report compares the existing original Data baseline to that verified output. It establishes exact asset changes; it does not verify the original executable’s runtime version, gameplay fixes, or OpenTPW compatibility with every patch/edition. The original baseline already contains Jelly, Phantom and Bouncy archives: here the patch replaces their contents rather than adding previously absent rides.

## Comparison coverage

- Original Data files: **801**; patched Data files: **808**.
- **780** physical files remain byte-identical; **21** changed and **7** were added; none were removed.
- **13** changed WADs were fully decompressed with the existing reader; **24** members changed. No WAD members were added or removed in these 13 archives.
- The 28 Data differences, including before/after byte lengths and SHA256, independently match the native runner metadata. Its full installation report contains 43 changed/added files, including root binaries and support files.
- An original-versus-itself comparison reports all 801 files unchanged. Generated fixtures verify WAD member identity/change, SAM token normalization/change and RSE operand identity/change.

Analysis uses [tools/patch-analysis](../tools/patch-analysis/README.md), with no added external dependencies. Metadata contains hashes and derived descriptions; original bytes, strings, and instruction/disassembly listings are kept out of Git.

## Settings changes

Numeric tokens are compared numerically and boolean tokens canonically; these inferred token types do not establish the original game’s complete settings schema. Duplicate-key value ordering is preserved.

| File/member | Observed change |
|---|---|
| `Data/high.sam`, `Data/med.sam` | All 26 parsed entries are identical. Only commented line 3 differs; removing comments and normalizing whitespace yields identical text. |
| Added `Data/_Resolution.sam` | Parsed numeric setting `Res.RESOLUTION = 5`. |
| `Data/levels/fantasy/Standard.sam` | `ThemeEngine.AmbientLightLevel`: 4,288,716,976 → 4,283,782,504 (`0xFFA0A0B0` → `0xFF555568`). |
| Hallow `coasta.wad/coaster.sam`; Jungle `minecart.wad/Coaster.sam` | Add `asCarTypes[0].fAnimSpeedMultiplier = 0.09`; 138 → 139 parsed entries. |
| Space `bouncy.wad/Bouncy.sam` | `UsageInfo.MaxCapacity`, `Upgrades[2].InitCapacity` and `Upgrades[2].RedLineCapacity`: 12 → 10. |
| Space `moonshot.wad/moonshot.sam` | Entry X: 0.5 → 0.1; entry Y: 0.1 → 0.9; exit X: 0.5 → 0.1; exit Y: 0.9 → 0.1; add `UsageInfo.RequiresTeleport = 1`. The four position keys are `UsageInfo.EntryCellStandPosX/Y` and `UsageInfo.ExitCellAppearPosX/Y`. |
| Space `shocker.wad/shocker.sam` | `UsageInfo.EntryCellStandPosY`: 0.1 → 0.9; `UsageInfo.ExitCellAppearPosY`: 0.9 → 0.1; add `UsageInfo.RequiresTeleport = 1`. |
| Hallow `haunt.wad/haunt.sam` | The parser reports removal of nonnumeric key `[._.]` and addition of `L._.J`, with identical value hash. Other 40 parsed entries are unchanged. No gameplay meaning is assigned to this syntax. |

## RSE structural changes

All six changed RSE members pass the strict reader. String blobs and variable names are unchanged in each. Addresses below are code-word indices, not native machine addresses. No original opcode meaning or behavioral fix is inferred.

| Member | Exact observed structural change |
|---|---|
| Fantasy `b_drip.wad/EventMap.RSE` | Four literal changes in operand index 1 of opcode 3, at words 0/15/18/24: 145→140, 18→0, 19→0, 25→0. Header and instruction count remain unchanged. |
| Hallow `c_hade.wad/EventMap.RSE` | Same four word positions: 175→171, 17→0, 20→0, 22→0. |
| Jungle `coaster1.wad/EventMap.RSE` | Same four word positions: 204→200, 17→0, 20→0, 22→0. |
| Fantasy `jelly.wad/Jelly.RSE` | 159→157 code words and 75→73 instructions. Shape alignment removes two operand-free opcode-15 instructions at old words 27 and 33, with 14 branch operands retargeted. Other header fields, strings and variable names remain unchanged. Alignment matches opcode/operand kinds and is a structural correspondence, not proof of behavior. |
| Hallow `phantom.wad/Phantom.RSE` | StackSize header field 15→17; code words (227), instructions (96), operands, strings and variable names are identical. |
| Space `bouncy.wad/Bouncy.RSE` | BounceSize header field 10→12; code words (209), instructions (90), operands, strings and variable names are identical. |

Jelly’s branch changes under that alignment:

| Old instruction word → new word | Old target → new target |
|---|---|
| 19 → 19 | 144 → 142 |
| 25 → 25 | 33 → 32 |
| 31 → 30 | 37 → 35 |
| 41 → 39 | 76 → 74 |
| 45 → 43 | 101 → 99 |
| 52 → 50 | 56 → 54 |
| 54 → 52 | 67 → 65 |
| 59 → 57 | 67 → 65 |
| 85 → 83 | 144 → 142 |
| 91 → 89 | 95 → 93 |
| 99 → 97 | 85 → 83 |
| 123 → 121 | 127 → 125 |
| 129 → 127 | 116 → 114 |
| 149 → 147 | 158 → 156 |

## Changed decompressed WAD members

Names and hashes below identify exact differences. HMP, MD2 and WCT changes are identified by bytes and hashes here; their visual/gameplay implications were not tested. All other members in these WADs are byte-identical.

| WAD | Changed members |
|---|---|
| `Data/levels/fantasy/hoarding.wad` | `ho_fos4.hmp`, `ho_fos4.MD2` |
| `Data/levels/fantasy/rides/b_drip.wad` | `EventMap.RSE` |
| `Data/levels/fantasy/rides/jelly.wad` | `Jelly.RSE` |
| `Data/levels/hallow/hoarding.wad` | `ho_fob3.hmp`, `ho_fob3.MD2` |
| `Data/levels/hallow/rides/coasta.wad` | `cart.MD2`, `cartm.MD2`, `coaster.sam`, `stexture/mc_wheel.wct`, `textures/mc_wheel.wct` |
| `Data/levels/hallow/rides/c_hade.wad` | `EventMap.RSE` |
| `Data/levels/hallow/rides/haunt.wad` | `haunt.sam` |
| `Data/levels/hallow/rides/phantom.wad` | `Phantom.RSE` |
| `Data/levels/jungle/rides/coaster1.wad` | `EventMap.RSE` |
| `Data/levels/jungle/rides/minecart.wad` | `cart.MD2`, `cartm.MD2`, `Coaster.sam`, `stexture/mc_wheel.wct`, `textures/mc_wheel.wct` |
| `Data/levels/space/rides/bouncy.wad` | `Bouncy.RSE`, `Bouncy.sam` |
| `Data/levels/space/rides/moonshot.wad` | `moonshot.sam` |
| `Data/levels/space/rides/shocker.wad` | `shocker.sam` |

## Physical Data file fingerprints

| File | Old bytes → new bytes | Old SHA256 → new SHA256 |
|---|---|---|
| `Data/generic/defaulttexture/NotFound.tga` | 49691 → 12332 | `c4d85bd4c57ee3ebefa215525742563c2a307c4dc91d4500945e81a27a0f44d6` → `a4060947603f4d31e7def1bed746963f5f54834afde05d5f5f9b2d401f42a082` |
| `Data/high.sam` | 2122 → 2123 | `dab8e2d43028feb06008d9bf171dbc360a9279864a6ced322e2520b2d5525ca8` → `707eeef1168e77e9cf10b168ef8959419dba8a72bb2581a98be3d7ef08ad23c6` |
| `Data/Language/English/CHAT_COMMANDS.str` | 5536 → 5820 | `827109169d1bdfe841a5328b8fd186218adab2b9c8acb03be50787f3a3c61198` → `80b704f2cded9d92095e03b675939a8aa3110b00757a0f3d6914c309c44663ec` |
| `Data/Language/English/DATEBIG.bf4` | absent → 17116 | `absent` → `1e5bab6c50447002638aac6c636d12f9fd49493018dbf214adb32fc070d0951d` |
| `Data/Language/English/DATEMED.bf4` | absent → 14492 | `absent` → `a9b6ee073567675f9b1799d8cc354e6a5bb65e525afbadb26f275b634f7f36bc` |
| `Data/Language/English/DATESMALL.bf4` | absent → 12268 | `absent` → `15eb42cd95898a7cd87f7b4e15b4fabdfb0f321c092be257df13a23f71dd3fed` |
| `Data/Language/English/DATETINY.bf4` | absent → 10612 | `absent` → `92211a2c337f8ad56fb2132181035c2c77a75e1f425eaa887341a6e7748886b5` |
| `Data/Language/English/residx.dat` | 1343 → 1442 | `945ef16f13c0ad97b8396951f05e55b76a99f1ea8c5baab56ffddc9e1a7373a7` → `02b937519e897a9fc7f36fec29197bc1abe40350979800306062c56f039174f0` |
| `Data/Language/English/UITEXT.str` | 16980 → 17076 | `3fe8b89c994bdd177b7226a51f24222621cc7e27beee94668942dc1f821137cf` → `ca238a10cb31736cc68374378a996b3bcc9b65ed4edd70bb78adf39ac06381ab` |
| `Data/levels/fantasy/hoarding.wad` | 278700 → 278714 | `a0f448c00ec7cc6e9ca3f8214b428b1715f511c293e851e574e64b93ce3b3279` → `9845cf09d0653b1978dea8a0d79468b8a3800e09bac0a202ebd1a9fd2214e8a7` |
| `Data/levels/fantasy/rides/b_drip.wad` | 252276 → 252273 | `4713398fc235697ad3f6a6996c8b9e48350c64a22b5cc7181a2b896341ed04fa` → `22b4917f70e8b4cfcd0f2353a4b7766072cf5353345b8d1348787f761ccd329b` |
| `Data/levels/fantasy/rides/jelly.wad` | 104329 → 104324 | `fee4f6c56284dc4649ac0cc67359ff5881dcb435a0463fa8e7883f91f6dff536` → `0cfd85b64a3a3ca4a33c8694367e3bd8ad0a7a991822ab6ef91e419786fb764b` |
| `Data/levels/fantasy/Standard.sam` | 4970 → 4970 | `38d6903f10d021bc263ae2ac48ab68e1b4cee543060728c8e08b9c96c502e2e3` → `f90fc9b7dc26d80f48c2cd50c18d7d12fafc85779fb4d22b5597e4b3a9d4d104` |
| `Data/levels/hallow/hoarding.wad` | 133032 → 133029 | `0ba1c13d405b11d0f6e7b60aa25b7bb210f84ad3dddc491aadb8d73cc8b73192` → `332cd0115bdd7413e971b2c7e019bf46ebbcfc9730c045657ccf4c8efe8043d3` |
| `Data/levels/hallow/rides/coasta.wad` | 180193 → 179912 | `4afbcd9ccac8f8f3a5f0c98891cc86c681eac6bd5642e850dc5551b58b8690cf` → `8b08cf45ad95e1d64041036b51a26954513eb410e3c6f647be5bae746add420a` |
| `Data/levels/hallow/rides/c_hade.wad` | 240765 → 240765 | `c44cd72d14cbf6023bcca7625dd8f1707e9dac2f67dddf780c991efddb2ff764` → `afba96b507eee15dc7ab2fc894b930a94538c79ba8aec5a0019a5be3b51d820a` |
| `Data/levels/hallow/rides/haunt.wad` | 139105 → 139108 | `efad9da5b7bbe1d79e9158ae60f453421d6f9c642f75488dbb6fd1a38954820c` → `eae300bd3e27b91c7dccdef946925d065b104293c50d79d11926b1f4acc0cc96` |
| `Data/levels/hallow/rides/phantom.wad` | 217839 → 217840 | `66b7ef4283b1a23787bbbde4147ee53688e7c5032c6328153db237f17eb50a25` → `90a0867707206a76d29e7229c65ea4756bbb55397d7a5a7c3076e2f2bee59792` |
| `Data/levels/jungle/rides/coaster1.wad` | 335179 → 335179 | `673471d5e151028d8743d901aec1c00b400643bb0a09bb0bca6c8c36da329977` → `f6867d29c998aba2f8755102a079242fcb6f96773a7667ebf5d07db41275f61c` |
| `Data/levels/jungle/rides/minecart.wad` | 300709 → 300458 | `184402eabbd90039f095d4879b63ddb2dfc89d82623fd0bf211ff88315e76333` → `c3bd8da541d10e1d26ab46a8fd188387e427ce8557c5466fbfecc306cc903ef1` |
| `Data/levels/space/rides/bouncy.wad` | 190392 → 190393 | `a7c35dacbe57764daba8ce62d5aa07da9ac53d7df5bd1602096d7f33b8e93d9f` → `071c966ba78007c134039562b1304ce4f5837636d22b77090a143c7d031c2b17` |
| `Data/levels/space/rides/moonshot.wad` | 169997 → 170057 | `fa427d03bcb498a2ffcb3a2a41aa2c61963c07458489cfa0fd6d263793045338` → `dec8cebb7d3a57278a99ffb0375b78d5cd1bac8dd702a0c6838ceb91a0ac240f` |
| `Data/levels/space/rides/shocker.wad` | 231942 → 232002 | `c46e391b58061027a76dbd926e1fa2e921a700393240be5851e774aa49f3750d` → `afba22ea9eed278911c83b2102a7b87c021ece227efe2d47a9fb70420d836d60` |
| `Data/med.sam` | 2123 → 2126 | `4bcae054974b85c1dc53f758b470a3e2ce95cd18757ce78b3aa78871defe450b` → `9a27a931716a55048c497ee178eff52911f410845cb35beab1e1479a4480a689` |
| `Data/ui/cursors/Ccar.tga` | 12332 → 12332 | `ca64c922f1ea86f58be886fcd45f1569ff3638f672343207a7e76cc555aff4f9` → `c4355480b54ec4606e5fb5ee397514dbcbab2168f2f6d34d9a799ee885073f12` |
| `Data/ui/cursors/Ccro.tga` | absent → 12332 | `absent` → `81de379dc42e4b76e1b63f0f8b88319f5d8216166e958af61230322e90519acc` |
| `Data/ui/cursors/c_crosshair.ani` | absent → 1094 | `absent` → `2d22656235fe43be3f26f9819be528d70655f7b85aab2ed7b2a8feaea8776d14` |
| `Data/_Resolution.sam` | absent → 424 | `absent` → `30e98c3c013f825a22f2198f79a002f60b4eb454f54ee47c1786de158e27763c` |

## Decompressed changed-member fingerprints

| WAD/member | Old bytes → new bytes | Old SHA256 → new SHA256 |
|---|---|---|
| `Data/levels/fantasy/hoarding.wad/ho_fos4.hmp` | 156 → 156 | `844608e36eaf73be83a3f439ed667913f37a9bbf2b34c3f14aa9497df1d190f3` → `b0023a9ed3348dd960ea5bdfde10d9614487cdca128fac3d26720654c8a8a83d` |
| `Data/levels/fantasy/hoarding.wad/ho_fos4.MD2` | 7568 → 7568 | `a648eab3dc11081e8906ab02788f76fe01ce70ce3a3d2b7d5c212af6ab3fb20c` → `bde808b320e75fe6f371d325bbc2238adb6dece4b5b326909377552dfa5fb098` |
| `Data/levels/fantasy/rides/b_drip.wad/EventMap.RSE` | 318 → 318 | `7a9f27fc9985447fe10218f7f6936f2555ae4362c72cf38259aeb15c832806a8` → `c29ec99abb6b135d51d6f9ac92eb521cf4d34c08063984907bf5de9ba8ee22ec` |
| `Data/levels/fantasy/rides/jelly.wad/Jelly.RSE` | 939 → 931 | `c0150775537d5076759a4b61f1cd067e91256712a8d15f20e6138d34505865e9` → `cada92dd3051305aaa0bdac0a1893b318d01346e9c049405a29699e600d42bc8` |
| `Data/levels/hallow/hoarding.wad/ho_fob3.hmp` | 156 → 156 | `7db5efb6b3d8501c131b9798608da0f1cc141da759a52d3a73e3ffcb18454550` → `4d8d9011896ef5654506067df63a2e6979c78c9aa0653877b2928aa69101ec10` |
| `Data/levels/hallow/hoarding.wad/ho_fob3.MD2` | 5944 → 5944 | `c0a1606e3097b905e46be8cf207ffeeebe0ecaf8b5278e346924c3a20bce0233` → `7e8161ddf088c50959247f2b3728325861bb4427896f44d4730ffd29b29489f6` |
| `Data/levels/hallow/rides/coasta.wad/cart.MD2` | 5136 → 5136 | `caf337c9b04d2f18ef17b51eea1b02042f31fbca377efedc341d50d8528254dd` → `e6adac76d5b7c6e5a978bcf3433fb80acad9fd164ef8ea107383f3f8ef6e3853` |
| `Data/levels/hallow/rides/coasta.wad/cartm.MD2` | 7456 → 7456 | `4fa007747f27ae13a4c471b0d005fe2aa2bb29c282ac299ecfc5c5c71e9f5bdc` → `6ed7095470cf15dd660fb95ae208978f272c5557aff14fb0cfece7657097af9d` |
| `Data/levels/hallow/rides/coasta.wad/coaster.sam` | 6080 → 6123 | `f4fb6c7099505215df6ea303ad8acfef259106773002b8af3eba7077767613aa` → `86878637366708914861653e65dba396638605525cb3bc88492bd0bd1bdb7018` |
| `Data/levels/hallow/rides/coasta.wad/stexture/mc_wheel.wct` | 360 → 357 | `1e0afcf3941408b92d0993c23b303b0de4427f644d4f23384c1f9c67276d2cd1` → `720c2d4639eb7d3a6df1bf324231791151a71e7599fef6fe1d4dd5c87663e932` |
| `Data/levels/hallow/rides/coasta.wad/textures/mc_wheel.wct` | 1838 → 1842 | `a9fa8e854f5211aef9b29e5b32043d6bd7f12a1f5d61d17bc6d536a5091437d9` → `5afc7d9bc6412030ef6a31d84511a4724d7a6ef167357389098caef570bc12ea` |
| `Data/levels/hallow/rides/c_hade.wad/EventMap.RSE` | 318 → 318 | `a95869817d3bfa2bc0a667902b7a9e476501e8936cb948db7949b01beaaf85a7` → `18918869b44f275d68f96f6afa39a9a8f1085075796008b707e2c4a3d14b567d` |
| `Data/levels/hallow/rides/haunt.wad/haunt.sam` | 1300 → 1300 | `90bea4dc24ae16c0ead87bf79b78c0908cb799838077d5a7da7ba1f5b898a795` → `ae8bb8715165b8f3ea8544a07c98df6c51bb0c01aa0a87d5862e388d6451d9c3` |
| `Data/levels/hallow/rides/phantom.wad/Phantom.RSE` | 1223 → 1223 | `50894f9b11e2ce2232732462fbb97b954ee05f13292453ff3ac3b2a161f430a5` → `3b285b19aed3b171e46ed9d8b975091a0892382cc8669039af56cfb8d7058730` |
| `Data/levels/jungle/rides/coaster1.wad/EventMap.RSE` | 318 → 318 | `c2d33b651803bc1429b806085e03b78c4f9146246eec5d0160826c265ae0d501` → `709c70450458ad2fba03845718071467dc352dabf1f8f73459d5941e8a8a9faa` |
| `Data/levels/jungle/rides/minecart.wad/cart.MD2` | 4368 → 4368 | `32154b6f208657b0dac4de67608c41b941fbdcbe5e33bfebdaffe42aea5104cb` → `063c7036e164e08021d5994b21662ed0a9bb71003ab6269409b9e5d1a0e8577d` |
| `Data/levels/jungle/rides/minecart.wad/cartm.MD2` | 7456 → 7456 | `854705b8c656103b1b396ecb15c83b34c8fe561761c8a61f81c76540553980c4` → `1d59d7bbc9bfdefd75c9f26837d7ddd4784159050d051a27871f78c7078a9d76` |
| `Data/levels/jungle/rides/minecart.wad/Coaster.sam` | 6041 → 6084 | `68ca7c591088babe1ebac59a46e45154302fc70684995667be6f0a48304efe83` → `747b00f3732b6d8f7a083ca43805e9cc68411983d82128a3d4785909dc0c60f7` |
| `Data/levels/jungle/rides/minecart.wad/stexture/mc_wheel.wct` | 360 → 357 | `1e0afcf3941408b92d0993c23b303b0de4427f644d4f23384c1f9c67276d2cd1` → `720c2d4639eb7d3a6df1bf324231791151a71e7599fef6fe1d4dd5c87663e932` |
| `Data/levels/jungle/rides/minecart.wad/textures/mc_wheel.wct` | 1838 → 1842 | `a9fa8e854f5211aef9b29e5b32043d6bd7f12a1f5d61d17bc6d536a5091437d9` → `5afc7d9bc6412030ef6a31d84511a4724d7a6ef167357389098caef570bc12ea` |
| `Data/levels/space/rides/bouncy.wad/Bouncy.RSE` | 1125 → 1125 | `4fd934f796328f25e3702b87bfd67dc2b9a42946cf39d6a025f6fe98e7a8ad87` → `9002195fc92ea36e0bcf377dadac89bb0fb023091e293ac8ad2d66bf957896b4` |
| `Data/levels/space/rides/bouncy.wad/Bouncy.sam` | 1641 → 1641 | `6523c3354b87ec5b5863b5913263465107302ab83fa17729666745c91fc7c384` → `ea778a5430cffa4feca4eba82fd489ad576b52c63ffd11da71aba562755eea2c` |
| `Data/levels/space/rides/moonshot.wad/moonshot.sam` | 1759 → 1855 | `49dc67b6ee77d43fb319b06aa34025bc91ee80d835f8621379144a938d496fda` → `94cee36c57ae0f105fa2e69e678633dd95ff32b861a0420c26bccb19a5cf3ad2` |
| `Data/levels/space/rides/shocker.wad/shocker.sam` | 1761 → 1857 | `2f94e58f84db1b66c9576dd5fe4c7ac67563fd36b303dc1d8d3590b79b2d1669` → `04c382b96660ca689aa5a8290cc3c5a781e2dab7e13a17c5851abf73a078c1e3` |

## Verification limits

No rendering, native gameplay, save compatibility, network service, or patch-specific OpenTPW runtime behavior was verified by this asset comparison. STR, DAT, BF4 and TGA additions/changes are fingerprinted, without copying their original contents or assigning an unverified fix description. Comment-only changes do not constitute new gameplay behavior. The existing reader projects emit pre-existing nullable/member warnings and the existing Zio 0.17.0 low-severity NU1901 audit warning; this analysis adds no packages.
