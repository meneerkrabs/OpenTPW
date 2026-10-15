# Independent MPEG-1 Layer II joint reference pins

`Mpeg1JointReferenceTests` embeds 43 original synthetic four-frame packets, gzip-compressed only to avoid repeated zero-padding, and bounded independent signed-16 PCM excerpts. CI decompresses these embedded bytes using the existing .NET library; it needs no FFmpeg installation, private files, temporary reference archive or added package. Source SHA-256 covers all four frames. The complete external PCM hashes below identify provenance; excerpts do not prove bit-identical complete managed output.

An original independent standard-field writer generated table A/B/C/D packets at 44.1/48/32 kHz, all four mode extensions, clamped empty shared regions, unequal channel SCFSI/scalefactors, ordered grouped/ungrouped triplets, independent/shared coexistence, last active bands and valid protected packets. Three additional C/D packets pin low-row code 15 (class 16, 32767 levels/15 bits) below the intensity boundary together with a shared grouped signal. Frame-varying triplets exercise transients. The writer imports neither the implementation nor the author's separate fixture writer. CRC uses polynomial long division over explicitly emitted header/allocation/SCFSI fields. Numeric field facts follow ISO/IEC 11172-3 sections 2.4.1.6,2.4.2.3,2.4.3.3 and Annex B.5.

Installed FFmpeg 8.0.1 arm64 (GPLv3-or-later build) produced fixed-integer PCM with `ffmpeg -hide_banner -nostdin -v info -f mp3 -c:a mp2 -i SYNTHETIC.mp2 -map 0:a:0 -c:a pcm_s16le -f s16le -y OWNED.s16le`. No requested rate/channel conversion was used. Its executable SHA-256 is `0a96da2735695308d964e25fa6f4a0db2e9d24031390360f4c5ff96a4f8938e5`. No external source/binary or standard text is vendored.

Before excerpt selection, complete-vector comparison against exact source `d9673b369068f04fc67eb6a51aba7dc28cad1b7c` measured **maximum absolute error 1 LSB** across 172 original joint packets (1,585,152 samples), plus 3 low-endpoint packets (27,648 samples). Both managed and FFmpeg joint output matched separately packed duplicated-nonjoint companions exactly. Source/PCM hashes, actual rate/stereo logs, four complete frames, no trailing bytes, sample lengths and expected protected CRC counts were checked. The 1 LSB assertion is an observed envelope for these generated inputs, not an assumed codec-wide tolerance.

Original generator SHA-256: `7200b61708c5b6062aa212add1f943062447aa7dad3420a1b76711b8b4b7ad7c`; endpoint supplement: `76649fcb4bd91dc28dbe52e88d1b810c13819c4052065f6ce673a3871b0f82f0`. Full archive manifest SHA-256: `64cbb2ab7b583a7e4063a97f52320e6a43e67866c61aeb1f89cfd004f118fa3e`. The reviewed external measurement used decoder files extracted from immutable git source in an isolated dependency-free project, not mutable sibling build output.

Primary references: [ISO/IEC 11172-3](https://previewnorm.com/iso/ISO%20IEC%2011172-3-1993%20PDF.pdf), [official FFmpeg decoder documentation](https://www.ffmpeg.org/doxygen/9.0/mpegaudiodec__template_8c_source.html). This generated subset does not certify all ISO inputs, original audio, native game behavior, playback/GPU, or MPEG-2 LSF joint support.

The actual permanent test method was also run against isolated exact decoder source:
43 unmodified cases passed. Moving the intensity boundary one band group, using
the left factor for the right shared channel, or reading both allocations above
the boundary each failed 34 cases while 9 clamped controls passed. These deliberate
mutations were confined to a temporary copy and are absent from production. They
show targeted sensitivity; the excerpts are not a complete-vector conformance test.

| Fixture | Source SHA-256 | Full reference PCM SHA-256 |
|---|---|---|
| A-44100-ext0-mixed-multiple-shared | 22355402869710223a81df9c0b07de5addf72137bfaf6b008fc81f72a61ba3ad | 1a39719eaef6e94e7ae7e89f7a51a3b27f22c424633f7598eb5d1b0dd15be0a4 |
| A-44100-ext1-mixed-multiple-shared | 606e596bfd6cf51816626bd184d239c5139f3dd91cb7dd1dd2e520afb240fc02 | c69266585f10fef475423fc17f54a81a3c48e414fda720c456cb9d0d1d21b1d3 |
| A-44100-ext2-mixed-multiple-shared | c699742e0355c1f56c48287e6056413fe32f8764d3c5d465db72089b6ef1b36b | f38b00e19668ebdcfcc66abf51a1e2b476266314ee7158ff53e7b03a0c9fce6f |
| A-44100-ext3-mixed-multiple-shared | 0404496f73e5c65a766dc5b383a465d276b8cd555acfe15771562ef224ec21af | 471460e13709583580500edd7fdf7b47fbe45572e932db9ee7456d6415506868 |
| A-48000-ext0-mixed-multiple-shared | 6b7d834f21d490113f19f36a33df64607c6b20bac5db80335eb689c28141fbd9 | 1a39719eaef6e94e7ae7e89f7a51a3b27f22c424633f7598eb5d1b0dd15be0a4 |
| A-48000-ext1-mixed-multiple-shared | 52586ce3d54098e91ce4fbb8d62c03a7ce927d8f140fcaf1f81d6449e33a336c | c69266585f10fef475423fc17f54a81a3c48e414fda720c456cb9d0d1d21b1d3 |
| A-48000-ext2-mixed-multiple-shared | 0f11deb26032c1788b2042b571fba2f5648cc47f3fd291573b859dbe5e181a39 | f38b00e19668ebdcfcc66abf51a1e2b476266314ee7158ff53e7b03a0c9fce6f |
| A-48000-ext3-mixed-multiple-shared | b552015b0ce157758d1ef5ab63f334d660a91ea9c0dbd983ffdbbb016f6f1495 | 471460e13709583580500edd7fdf7b47fbe45572e932db9ee7456d6415506868 |
| B-44100-ext0-mixed-multiple-shared | 1b9fcd4867ad1874b1a3d90e52ede104b2611c593931a7e7cea18a6569db773b | 78740afc3f3a0e447d5e7e6da362bfb7cbbedf56394788b06ff1fbf15a0a7131 |
| B-44100-ext1-mixed-multiple-shared | 56b28d1169870e29f17383a776b8073bdf2abe1278ea90946eb1fa22bdcc5c2b | 7b050ca63c0f44f991a8f12a562a388c927515d037d01e156b9d532a4910370a |
| B-44100-ext2-mixed-multiple-shared | 3c8a4b8fd8d572a7d9b9be730878806b6eacea41ff893fb54ddfc6a2e1fd6fa4 | a7541aa5d1d16a50d424a78140274d8e7ae947adae5ec4bfe357f3f35f1dc5e1 |
| B-44100-ext3-mixed-multiple-shared | f81fde3ae5ad2733ece1a23acab2e3a832a74e1ccf24aca2cc2bf2ee6f81f4d6 | 55fa07123dee0c8b1a172ea801996223ae34e5e8589284100421a1a26c42221d |
| B-32000-ext0-mixed-multiple-shared | 3d206e3dcb72c4967e9f26c9f72fdcddd33160d00237c3df96f04bd8b653ff69 | 78740afc3f3a0e447d5e7e6da362bfb7cbbedf56394788b06ff1fbf15a0a7131 |
| B-32000-ext1-mixed-multiple-shared | 4d25d26de6c91d7000fd5a7c08dcfb046b1abe4ca104e96b92a4ca69993180c8 | 7b050ca63c0f44f991a8f12a562a388c927515d037d01e156b9d532a4910370a |
| B-32000-ext2-mixed-multiple-shared | 26cb2ca1e530151235d0a746014a4c0f28e9560d85b87ca2bdae917737634774 | a7541aa5d1d16a50d424a78140274d8e7ae947adae5ec4bfe357f3f35f1dc5e1 |
| B-32000-ext3-mixed-multiple-shared | 349acca13391839e1322ea152586b56855971990a2f2504b251a4ae1b156e55a | 55fa07123dee0c8b1a172ea801996223ae34e5e8589284100421a1a26c42221d |
| C-44100-ext0-mixed-multiple-shared | 99973dc47e2ec0175834fc467e4e8189138166a44efba90b8cdf92992155c73d | 89926e253e587922a986a6ba85f7d7817ad264dd565f7cb1561d35f93a8dc0d3 |
| C-44100-ext1-clamped-empty-shared | dd505b26f36bcd321d077aca8b28ce1ab90dd6de8f0fcabbbb9badbe57b58c54 | ccbda56bf4be9681cb0d3f13fd1bee61ef0009a6877f410e8f259cd7a7b1b458 |
| C-44100-ext2-clamped-empty-shared | aa0793992798a672e12807225c0048779a424c4b9e5ef6056d3bcbe3f4e792ed | ccbda56bf4be9681cb0d3f13fd1bee61ef0009a6877f410e8f259cd7a7b1b458 |
| C-44100-ext3-clamped-empty-shared | 533a0e0ad9e3ce1b9f157db9f5b796c3eafc466befa7de1171b6da18a7035eb2 | ccbda56bf4be9681cb0d3f13fd1bee61ef0009a6877f410e8f259cd7a7b1b458 |
| C-48000-ext0-mixed-multiple-shared | 031d253ba6f4bffe98374e127e1ba814671005457b78f2c61dba87e4238ec568 | 89926e253e587922a986a6ba85f7d7817ad264dd565f7cb1561d35f93a8dc0d3 |
| C-48000-ext1-clamped-empty-shared | 17cf812ff30c3a3c78b3fc874eab80fbfc6c3d6d8c2b8d04d6d0ef0aba6ca45d | ccbda56bf4be9681cb0d3f13fd1bee61ef0009a6877f410e8f259cd7a7b1b458 |
| C-48000-ext2-clamped-empty-shared | a9e45e2c123d77573b333f6e7cceef367601dc47c9f9fa312655df570e8af96f | ccbda56bf4be9681cb0d3f13fd1bee61ef0009a6877f410e8f259cd7a7b1b458 |
| C-48000-ext3-clamped-empty-shared | 20dcabb8009b384abf44167182711091a4b3520c686dc6f9aa767acb75ae755b | ccbda56bf4be9681cb0d3f13fd1bee61ef0009a6877f410e8f259cd7a7b1b458 |
| D-32000-ext0-mixed-multiple-shared | af39b70f8deae69fa5f8327a12e72072b4e92b83eb425d63f7dd2f2b145f56ae | 2303a8c128228ab45ab12cefcc25bf172b27df82b52e5ab7967b053480cdfa2e |
| D-32000-ext1-mixed-multiple-shared | 532c35106e81a340f03acd6f7c7545e0c1cdd236ded287d2d52da2a9ea394418 | 66e318a6082ad3be33f57303ae04be123cadbc5a7472a00c8baddd02d7b1ace0 |
| D-32000-ext2-clamped-empty-shared | 132f7429446f3dd7832c3a31452a8bfbb9b855bd1d76079ea7612f41c90ac1e5 | 9c76e18bb1287c7a3bf3e904277342f9a17a16791f2b289292991b4bbb90485c |
| D-32000-ext3-clamped-empty-shared | e0aa426add8a3307fd54b525fdb3ed718fdc0b2f11fc0ecf8e38874b140d0ed3 | 9c76e18bb1287c7a3bf3e904277342f9a17a16791f2b289292991b4bbb90485c |
| A-44100-ext0-shared-grouped | 4486b5543938f0d652a23c11bb9628db71c9f94e70e6bb4801f8b1e121100b57 | b477c74126d008c058f19ef584e4ec964feef00b91550b7cd30c71539d71d9ea |
| A-44100-ext0-shared-ungrouped | 7f393f51dd7ca4bdff57e065e7d68e9a9bcd930a5b15609992bbce4e094f982d | 8df29467234eeaf66605d1745aebae6906ab9e7c679ec959ffb9b9c457792605 |
| B-32000-ext0-shared-grouped | 5076e3da63d59ff2530d8cb22afc3be02a22791ffb97361970a9844b030cd792 | b477c74126d008c058f19ef584e4ec964feef00b91550b7cd30c71539d71d9ea |
| B-32000-ext0-shared-ungrouped | f8cf19d5d74ac88211cea736cd7b9dfc7ceadf3ceef3cb8bb48b0f164c54a37e | 8df29467234eeaf66605d1745aebae6906ab9e7c679ec959ffb9b9c457792605 |
| C-48000-ext0-shared-grouped | 767ab611320ec03161f4192f11a8a84c6e4d4938c4cc83a4d9157fea600210ff | b477c74126d008c058f19ef584e4ec964feef00b91550b7cd30c71539d71d9ea |
| C-48000-ext0-shared-ungrouped | 4f2eec50e257220ff0e6326952c4b0a391470b173bc0e7d0f8aaa68234e6cb52 | bcab9986f5640b21cf41ac58f195afbf79ad6d5108f51a2e2aa8e502656e2c8f |
| D-32000-ext0-shared-grouped | 695065974a6248cfa4cc8c8b122725e0ee9aa36f705fb5a81149ecec3dd4c511 | b477c74126d008c058f19ef584e4ec964feef00b91550b7cd30c71539d71d9ea |
| D-32000-ext0-shared-ungrouped | b775eaebea2807542ccdc20d87ca5b60bdf82686bbedb903d6e9447328f54852 | bcab9986f5640b21cf41ac58f195afbf79ad6d5108f51a2e2aa8e502656e2c8f |
| A-48000-ext1-all-allocation-crc | 6059a09d1fa160d4fef891a2ebddefa9018d5aafd21243f18fc3793851d8b9a9 | ee58f48c5b835fd4d9016b9c5f718d96bf5000336090bca015c0a7b97ef73a2b |
| B-44100-ext2-all-allocation-crc | 3772157a96c63380c7bbf5b1a93e7fdfa40ed11ec38c687d998f51136ccb9fed | 0bd24f270f003d7d9024b8b821aa92eaf9483acab444f10b5dcb59e54028b556 |
| C-44100-ext3-all-allocation-crc | ed604f47ec9ce95e86b258f259aa04115276a0419ee64c2656329ea5d44681c4 | 79772320ecb4616ab4e0e0f52b3d7d984a7713e425c16dbd9ee6ee639e7e1055 |
| D-32000-ext0-all-allocation-crc | eb04f5119a770128d27f4c7d8e2e67007a1fc9566aacdcf6101ee996e852e4e5 | 53e3f6fb66ff78e9804777d982ae272de9747027bc48b64a8f9795542e075fee |
| C-44100-ext0-low-code 15-shared | 846424cf0a589e3673fc599a937ae43d27c80a7b7c9e63778e80ddd88b51f8ff | d201dea914d57034abb291f36e77562c533df5974c11adc72d55e6cee3ba916e |
| C-48000-ext0-low-code 15-shared | 9f1042320951287e6aaecc54998ba62f338749e043d163561a730a6e0682b423 | d201dea914d57034abb291f36e77562c533df5974c11adc72d55e6cee3ba916e |
| D-32000-ext0-low-code 15-shared | c9d22a5554388d85383e8c5fe0f68e3d109e7606812781446c0b52a5ffcff185 | d201dea914d57034abb291f36e77562c533df5974c11adc72d55e6cee3ba916e |
