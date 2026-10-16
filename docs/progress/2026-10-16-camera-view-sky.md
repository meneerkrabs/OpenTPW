## The park's sky is no longer black in the low video camera (October 16, 2026)

The video camera placed with the HUD's camera button looks over the horizon, and the sky showed pure black there: the park
world pass never drew a backdrop (the sky box entity was removed long ago), so everything not covered by terrain or objects
stayed at the framebuffer's black clear. This is not a regression of today's merges: the overhead park camera simply never
looked at the horizon. The park now draws the original's backdrop colour, the fog colour 0x44DDFF that the engine's viewport
clear uses (`0x5d8b50`, `0x5737aa`), before the level (UI-107). The engine sky's own layers (`sky_cyl.tga` cylinder, `sky.tga`
clouds, moon, sky objects; `0x5852b0`, `0x5860d0`) are still not drawn and stay open under UI-107.
