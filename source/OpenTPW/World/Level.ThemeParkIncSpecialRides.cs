using OpenTPW.Hud;

namespace OpenTPW;

public partial class Level
{
	/// <summary>Whether a Theme Park Inc challenge of <paramref name="type"/> is in its timed phase.</summary>
	private static bool ThemeParkIncChallengeRunning( ParkEconomy economy, int type ) =>
		economy.TpiChallenges?.Slots.Any( slot => slot.Type == type && slot.Status == ThemeParkIncChallengeStatus.Running ) == true;

	/// <summary>
	/// Theme Park Inc's open guard (the one the repair tail <c>0x5E2C35</c> and the ride window's open toggle <c>0x5E1A50</c> ask): the reopening guard's states, request, queue link and coaster
	/// test, no running ride-closing challenge for a fat shop, the park open and no running spell that closes the attraction.
	/// </summary>
	// [BIN:TPI-EXE:0x5E1A50 may open] state 1 or 4, the request +0x78, state 2, the queue link 0x5E07B0, 0x5C8B70 (a running challenge of type 42 while UsageInfo.SpecialIngredient is 1), the park closed
	// (0x64FA30, world +0x1EAB44), the spell's ClosedDuring word 1 (0x5E1AC2..0x5E1B20) and, for track type 3, the coaster test 0x485BB0 answer false; the repair tail's guard 0x5E2C35 is the same
	private bool ThemeParkIncMayOpen( OriginalObjectRuntime runtime, ParkObjectState? record, EconomyObjectInfo? info, ParkEconomy economy ) =>
		ParkGate.MayOpen( Guests, runtime, record ) && !(info?.SpecialIngredient == 1 && ThemeParkIncChallengeRunning( economy, 42 )) && economy.IsParkOpen
		&& !runtime.WeatherCloses( economy.TpiWeatherState );

	/// <summary>Whether a running challenge holds a ride open (<c>0x5C8BE0</c>: a running challenge of type 46 while the type's <c>Info.WhichUIType</c> is 0).</summary>
	// [BIN:TPI-EXE:0x5E1BC0 may close] 0x5C8BE0 on the challenge system: a slot of type 46 in state 3 and the object's type +0x70 (Info.WhichUIType) 0 answer true, and the close toggle is disabled
	private static bool ThemeParkIncHeldOpen( EconomyObjectInfo? info, ParkEconomy economy ) => info?.WhichUIType == 0 && ThemeParkIncChallengeRunning( economy, 46 );

	/// <summary>What the ride window's open toggle shows for a selected Theme Park Inc ride: whether it is offered, whether it is open and whether a press would act.</summary>
	internal readonly record struct ThemeParkIncDoor( bool Shown, bool Open, bool Enabled );

	/// <summary>
	/// The ride window's open toggle (control <c>0x2EE4</c> of the ride window <c>0xA6BBC0</c>) for <paramref name="item"/>: offered for a ride (<c>Info.WhichUIType</c> 0) that no special-ride record
	/// holds (such a ride opens the activation window instead); enabled while the queue link holds and, closed, the open guard allows it, or, open, no running challenge holds the ride open.
	/// </summary>
	// [BIN:TPI-EXE:0x5036AB object click] a ride (+0x600 == 0) that is no photo booth and no pending special ride opens the ride window 0x580120 (0xA6BBC0, template 0x9C3FB8)
	// [BIN:TPI-EXE:0x57D370 toggle state] the toggle 0x2EE4 is disabled when the queue link 0x5E07B0 fails, when closed (0x5E3C20: +0x7C == 0) and 0x5E1A50 fails, when open and 0x5E1BC0 fails; else enabled
	// in state 1 (closed, image 0xB) or 0 (open, image 0xC)
	// [EXT:tpi-capability-gates] the toggle's other conditions are not ported: the window's own word +8 (8, 0x1E and 0x1F disable it) and the exit-cell link test 0x5E0940 (0x57D3CA, and 0x5E1FF0's for a
	// queue ride), whose cell words +0x46/+0x48 OpenTPW does not keep
	internal ThemeParkIncDoor ThemeParkIncDoorOf( OriginalObject item )
	{
		if ( !IsThemeParkIncSlice || Park?.Economy is not { } economy || item.IsDeleted || !item.Runtime.IsAttraction || item.Entry.WhichUIType != (int)BuildCategory.Rides
			|| GetEconomyInstance( item ) is not { } instance || !economy.TryGetObject( instance, out var record ) || economy.IsThemeParkIncSpecialRidePending( instance ) )
			return default;
		var info = economy.Catalog.TryGet( record.InfoId, out var known ) ? known : null;
		var open = item.IsOpen;
		var linked = Guests == null || item.Runtime.Visitors is not RideVisitorBridge bridge || ParkGate.QueueConnected( Guests.Grid, bridge );
		var enabled = linked && (open ? !ThemeParkIncHeldOpen( info, economy ) : ThemeParkIncMayOpen( item.Runtime, record, info, economy ));
		return new ThemeParkIncDoor( true, open, enabled );
	}

	/// <summary>
	/// A press of the ride window's open toggle (<c>0x57FF8E</c>): a closed ride is opened (<c>0x5E1DC0</c>), an open one closed (<c>0x5E1CD0</c>). Nothing happens while the toggle is disabled.
	/// </summary>
	// [BIN:TPI-EXE:0x57FF8E toggle press] control 0x2EE4: 0x5E3C20 (closed) passes 0 to 0x50FB10, which opens through 0x5E1DC0 (the open word +0x7C = 1, VAR_RIDECLOSED = 0, SetState(0)), else 1, which closes
	// through 0x5E1CD0 (+0x7C = 0, +0x80 = 0, VAR_RIDECLOSED = 1, the close model call); then the window's refresh 0x57E280
	internal bool ToggleThemeParkIncDoor( OriginalObject item )
	{
		var door = ThemeParkIncDoorOf( item );
		if ( !door.Shown || !door.Enabled )
			return false;
		if ( door.Open )
			item.Runtime.Close();
		else
			item.Runtime.Open();
		SyncOpenState( item.Runtime );
		return true;
	}

	/// <summary>
	/// A park built from a Theme Park Inc save takes each imported attraction's saved open word (<c>mCanLoad</c>, <c>+0x7C</c>): an idle one saved with it clear stays closed, as the Theme Park World
	/// import does; the prebuilt Science park saved its four special rides closed.
	/// </summary>
	// [BIN:TPI-EXE:0x5DC430 attraction serializer] the loader writes the open word without a closing routine (CloseLoaded keeps the saved hoarding); [BIN:TPI-EXE:0x5DB5FB attraction constructor] a newly built
	// attraction starts open
	private void ImportThemeParkIncOpenWords( ThemeParkIncSaveWorld world, IReadOnlyDictionary<int, int> instances )
	{
		if ( Park?.Economy is not { } economy )
			return;
		foreach ( var attraction in world.Attractions )
		{
			if ( attraction.CanLoad != 0 || !instances.TryGetValue( attraction.Id, out var instance ) || AttractionRuntimeOf( instance ) is not { } runtime
				|| !economy.TryGetObject( instance, out var record ) || !RideOpening.ClosedByImportedOpenWord( attraction.CanLoad, ParkEconomy.ObjectState( record ) ) || !runtime.IsOpen )
				continue;
			runtime.CloseLoaded();
			SyncOpenState( runtime );
		}
	}
}
