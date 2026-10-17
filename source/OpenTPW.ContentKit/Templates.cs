namespace OpenTPW.ContentKit;

/// <summary>
/// The kit's built-in object settings and ride script. Both are modelled on the original jungle sideshow <c>puzzle.wad</c> (<c>Puzzle.sam</c>,
/// <c>Puzzle.RSE</c>): the same keys, with values chosen for the content kit's sample pool. The values are the kit's own choices, not the original's;
/// the category defaults (<c>SideShow.sam</c>) of the game data are layered under them by the object catalog.
/// </summary>
public static class Templates
{
	/// <summary>The settings template name accepted by <c>object --sam</c>.</summary>
	public const string SamTemplateName = "template";

	/// <summary>
	/// A sideshow-like attraction of 3 x 3 cells whose entrance is the middle cell of the last text row (model Z = 0, as in the original sideshows).
	/// <c>{Name}</c> is the display name; <c>Info.Id</c> is a placeholder, <c>objects.json</c> replaces it with the mod's own id.
	/// </summary>
	public const string SideshowSam = """
		#
		#	OpenTPW content kit: sideshow-like attraction (modelled on the keys of the original Puzzle.sam)
		#
		Info.Id			1
		Info.Name		"{Name}"
		Info.Shape
		---
		***
		***
		*2*
		---

		Info.Hoarding
		---
		F^7
		[.]
		L.J
		---

		Info.RideTypeStringIndex 22

		UsageInfo.InitPricePerUse		15
		UsageInfo.InitCostOfGoods		0
		UsageInfo.InitChanceOfLoosing	0

		UsageInfo.EntryCellStandPosX	0.5
		UsageInfo.EntryCellStandPosY	0.3

		UsageInfo.ExitCellAppearPosX	0.5
		UsageInfo.ExitCellAppearPosY	0.3

		UsageInfo.ExcitementLevel		35

		UsageInfo.MinCapacity			1
		UsageInfo.MaxCapacity			6

		UsageInfo.RideHandlesSprite		1		Riders are drawn as heads on the model's head records

		Upgrades[0].CostOfUpgrade			1500
		Upgrades[0].CostOfResearch			0
		Upgrades[0].InitCapacity			6
		Research.Group		0
		""";

	/// <summary>
	/// A placeholder ride script for the pool: guests enter (<c>ADDHEAD</c> hangs a head on the next free head record), wait a few seconds and leave.
	/// It is the shape of the original rides that use head records (the fill loop with <c>SETTIMER</c>/<c>GETTIMER</c>, the <c>HUSH</c>/<c>HOP</c> pair and the
	/// <c>VAR_LETMEON</c>/<c>VAR_LETMEOFF</c> protocol of docs/GUESTS.md), without animations, sounds or effects. Real swimming comes later, in an extension.
	/// </summary>
	public const string PoolScript = """
		; Placeholder pool ride script (OpenTPW content kit).
		; Guests enter, stay in the water for a while and leave; nothing is animated.
		; The stack size is also the size of the HUSH/HOP heap that holds the riders' ids: it must be at least the capacity.
		.stack 20
		.timeslice 50
		.limbo 0
		.bounce 0
		.walk 0

		.var VAR_LETMEON
		.var VAR_LETMEOFF
		.var VAR_CAPACITY
		.var VAR_DURATION
		.var VAR_BREAKSTAT
		.var VAR_ONRIDE
		.var VAR_RIDECLOSED
		.var VAR_BROKEN
		.var VAR_WORN
		.var VAR_RUNNING
		.var VAR_PAD
		.var VAR_PARAM
		.var VAR_SPACELEFT

			NAME "Pool"
		closed:
			ENDSLICE
			TEST VAR_RIDECLOSED
			BRANCH_NZ @closed
		fill:
			SETTIMER 10000
			COPY VAR_SPACELEFT VAR_CAPACITY
		take:
			TEST VAR_SPACELEFT
			BRANCH_Z @swim
			TEST VAR_RIDECLOSED
			BRANCH_NZ @unload
			GETTIMER 10000
			BRANCH_Z @swim
			CRIT_LOCK
			TEST VAR_LETMEON
			BRANCH_Z @nobody
			SETTIMER 10000
			HUSH VAR_LETMEON
			ADDHEAD VAR_LETMEON
			COPY VAR_LETMEON 0
			ADD VAR_ONRIDE 1
			ADD VAR_SPACELEFT 0xFFFF
			BRANCH_Z @full
		nobody:
			CRIT_UNLOCK
			BRANCH @take
		full:
			CRIT_UNLOCK
		swim:
			TEST VAR_ONRIDE
			BRANCH_Z @unload
			COPY VAR_RUNNING 1
			WAIT 8000
		unload:
			COPY VAR_RUNNING 0
		leave:
			TEST VAR_ONRIDE
			BRANCH_Z @done
			CRIT_LOCK
			HOP VAR_LETMEOFF
			DELHEAD VAR_LETMEOFF
			CRIT_UNLOCK
		taken:
			TEST VAR_LETMEOFF
			BRANCH_NZ @taken
			ADD VAR_ONRIDE 0xFFFF
			WAIT 300
			BRANCH_NZ @leave
		done:
			TEST VAR_RIDECLOSED
			BRANCH_NZ @closed
			BRANCH @fill
		""";

	/// <summary>The sideshow template with its display name filled in.</summary>
	public static string RenderSam( string displayName )
	{
		if ( displayName.Contains( '"' ) || displayName.Any( char.IsControl ) )
			throw new ArgumentException( "The display name cannot hold quotes or control characters (the .sam format has no escapes).", nameof( displayName ) );
		return SideshowSam.Replace( "{Name}", displayName, StringComparison.Ordinal ).Replace( "\r\n", "\n" ) + "\n";
	}
}
