## UI depth and render state decoder (November 4, 2026)

Work in progress (round 11, package K). Read so far in tp.exe: every UI control model vertex has depth 0 (the control matrix `0x476C50` stores m22 = 0 at `0x476CBF`; control models carry part flag 0x2000000, so `0x579690` maps z' = 0.1 x z); the state frame of a control is the k-th node down the first-child chain (`0x476E80`); the option table behind TEXTUREFILTERING and TEXTURE32 is read from the registration (record base `0x7442C0`, values from `0x785874`).
