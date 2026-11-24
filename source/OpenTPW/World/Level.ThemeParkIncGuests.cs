namespace OpenTPW;

// What Theme Park Inc's native guests read of the park (docs/reverse/TPI-GUESTS.md, phase P2): the object list's litter bins and shows, an attraction thing by its id, the entertainers' list and a
// bin's customers, and the object removal message 0xA (0x60D120) for the guests whose target goes.
public sealed partial class Level
{
	/// <summary>Gives the native guests (switch on) this level's objects and entertainers and the removal message; nothing for the Theme Park World rules.</summary>
	private void AttachThemeParkIncGuestWorld()
	{
		if ( Guests is not { ThemeParkIncNativeGuests: true } guests || !GameEditions.IsThemeParkInc )
			return;
		var world = new ThemeParkIncGuestWorld( this );
		guests.ThemeParkIncWorld = world;
		// a placement or a removal between two sweeps (the player's, at any frame) is seen by the next guest that looks
		Objects.ObjectPlaced += _ => world.Invalidate();
		Objects.ObjectRemoved += _ => world.Invalidate();
		// [BIN:TPI-EXE:0x637C40 thing destroy] the destroy broadcasts message 0xA at the click (OnThemeParkIncObjectRemoving); every guest hears it through its handler 0x60D120
		Objects.ObjectRemoving += item =>
		{
			world.Invalidate();
			if ( item.ThingId is > 0 and <= ushort.MaxValue )
				Guests?.ThemeParkIncObjectRemoved( item.ThingId );
		};
	}

	/// <summary>An attraction thing as the guests read it: its thing cell, its entrance word <c>+0x46</c>, its angle, its type's stand position and its script's variables.</summary>
	// [DATA:<facility>.sam:UsageInfo.EntryCellStandPosX/Y] the type record's +0x184 and +0x188; every shipped Theme Park Inc litter bin declares both (science 0.5/0.0, water 0.9/0.0, arabian 0.99/0.5)
	private ThemeParkIncThingView ThemeParkIncViewOf( OriginalObject item )
	{
		var settings = item.Entry.Settings;
		var thing = new ThemeParkIncStaffWorld.DropObject( settings.GetInt( "Info.WhichUIType" ), item.ThingCell, item.Placement.Rotation, item.Entry.Shape,
			settings.GetInt( "Info.MapOffsetX" ), settings.GetInt( "Info.MapOffsetY" ) );
		return new ThemeParkIncThingView( (ushort)item.ThingId, item.ThingCell, ThemeParkIncStaffWorld.EntranceWord( thing ), ((item.Placement.Rotation % 360) + 360) % 360,
			settings.GetFloat( "UsageInfo.EntryCellStandPosX", 0.5f ), settings.GetFloat( "UsageInfo.EntryCellStandPosY", 0.5f ), item.Runtime.Script?.Variables );
	}

	/// <summary>
	/// The level's side of <see cref="IThemeParkIncGuestWorld"/>. The object views are built when a guest first looks after a placement or a removal (the sweep reads them for every idle guest); a
	/// script variable is read live through its array, and the entertainers are read live from the staff.
	/// </summary>
	private sealed class ThemeParkIncGuestWorld( Level level ) : IThemeParkIncGuestWorld
	{
		private bool fresh;
		private ThemeParkIncThingView[] bins = [];
		private ThemeParkIncThingView[] shows = [];

		/// <summary>Forgets the views: an object was placed or removed.</summary>
		internal void Invalidate() => fresh = false;

		private void Refresh()
		{
			if ( fresh )
				return;
			fresh = true;
			bins = level.ThemeParkIncObjectsWith( "UsageInfo.HoldsLitter" ).Select( level.ThemeParkIncViewOf ).ToArray();
			shows = level.ThemeParkIncObjectsWith( "UsageInfo.IsFireworks" ).Select( level.ThemeParkIncViewOf ).ToArray();
		}

		public IReadOnlyList<ThemeParkIncThingView> Bins()
		{
			Refresh();
			return bins;
		}

		public IReadOnlyList<ThemeParkIncThingView> Shows()
		{
			Refresh();
			return shows;
		}

		public IReadOnlyList<ThemeParkIncEntertainerView> Entertainers() => level.TpiStaffWorld?.Entertainers( level.Park?.Economy ) ?? [];

		public ThemeParkIncThingView? Thing( ushort thingId )
		{
			Refresh();
			foreach ( var bin in bins )
				if ( bin.ThingId == thingId )
					return bin;
			return null;
		}

		// [BIN:TPI-EXE:0x5E5DA0 customer] the thing's history object (+0x130) raises its daily customers (+0x110) and its monthly customers (+0x1278): the economy's customer ring and month count
		public void RecordCustomer( ushort thingId )
		{
			if ( level.ThemeParkIncThing( thingId ) is { } item && level.economyInstances.TryGetValue( item, out var instance ) )
				level.Park?.Economy.RecordCustomer( instance );
		}
	}
}
