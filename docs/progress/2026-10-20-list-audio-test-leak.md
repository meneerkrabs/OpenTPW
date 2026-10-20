## A list audio test no longer leaves text entry on (October 20, 2026)

`FindParksEnterClicksOnceThroughTheSearchButton` (#712) focused the Find Parks search field, which turns the global text entry on, and never turned it off. Any later test in the same run that opens the wave machine window then failed, because the window refuses to open while text is being typed. The test now restores the previous state, as the rename tests next to it already do. CI's test order did not hit this; a local with-data run did (found in the post-merge review of #712).
