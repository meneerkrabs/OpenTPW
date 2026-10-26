namespace OpenTPW;

/// <summary>
/// The path builder's approximation register (docs/PATHS.md; docs/reverse/PATH-plan.md §10). Each entry is
/// tagged <c>[APPROX:PATH-NNN]</c> at its code site and names the plan's unknown in its first word. Everything
/// else in the path builder is traced from the Mac binary (BIN labels at the code sites).
/// </summary>
public static class PathApproximations
{
	public static readonly IReadOnlyList<(string Id, string Rule)> Entries = new[]
	{
		("PATH-007", "PATH-LAND: the land follows the original (tp.exe map init 0x536490 sets flag 0x40 on every non-InitialPath cell, the fence of the theme's Hoardings.sam is laid from the gate posts (0x5323f0), an imported save keeps its saved mFlags and hoardings (0x4d0b30), Buy Land validates (0x531a50), prices (0x532303) and merges (0x5327e0) a rectangle), except that the fence fill's cell outcome is modelled (the cells inside the ring closed along the gate, plus the four cells round each corner that turns away from the land; cell for cell equal to the Jungle Easymode save's flags) instead of its 2 x 2 scan (0x5311c0); the six-click fence laid when bit 0x400 of the level word 0x79d86c is set is not modelled; a purchase covering the open side of the gate, or whose merged fence cannot be walked from post A to post B, is refused (the original's validator accepts some such rectangles, e.g. Jungle (45, 11)-(49, 17); its ring walk 0x52ba50 then likely keeps the old ring and the refill drops the new hoardings, charging for no land, or may not terminate)"),
		("PATH-015", "QUEUE-MODELS: queue cells carry the theme's queue models as tp.exe places them (chooser 0x535dd0 class 2 from the cell's link byte, table 0x7630b0; models of 0x522900 at yaw 360 minus the rotation, 0x5229e0; no ground tile, 0x5365d0), baked into static batches with each vertex draped onto the landscape as tp.exe does it (0x469a80, QueueModelDrape), the cell a hole of the ground (0x46df50) and, as an object's base is, the skirt read at load (the opaque slot, the normalised coordinates, 0x46aa10) and, with the procedural terrain textures on, the skirt word and the composite floor of the art over the cell's procedural ground (0x463060, 0x57ace0); without the animation of their texture frames (the fire0 torches) and the load flag 0x100000 of the two quedead entries"),
		("PATH-017", "PATH-PAUSE-WINDOW: the pause key P (tp.exe 0x40bf70 → 0x409350(1)) toggles a window (the global handle 0x7cb2cc, 0x4a27f0 → 0x47ed80): a full-screen layer (0x65dd7b(1, 1, 1, 0, 0, 0x7ff, 0x5ff), the 'lolight' backdrop) under the panel 5094 (174,500)-(1874,900) of table 0x752e00; whether that layer stops the click route to the park click handler 0x4879d0 (which reads neither the pause flag 0x786b84 nor this handle, only the pause-menu object 0x7c2534 through 0x48c8d0) is not read, so OpenTPW's speed pause keeps the tool live as the click handler does (evidence needed: the window manager's hit test of the 0x65dd7b layer classes)"),
	};
}
