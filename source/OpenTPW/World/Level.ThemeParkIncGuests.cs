namespace OpenTPW;

// What Theme Park Inc's native guests read of the park (docs/reverse/TPI-GUESTS.md, phase P2): the object list's litter bins and shows, an attraction thing by its id, the entertainers' list and a
// bin's customers, and the object removal message 0xA (0x60D120) for the guests whose target goes.
public sealed partial class Level
{
	/// <summary>Gives the native guests (switch on) this level's objects and entertainers and the removal message; nothing for the Theme Park World rules.</summary>
	private void AttachThemeParkIncGuestWorld()
	{
		if ( !RunsThemeParkIncNativeGuests || Guests is not { RunsThemeParkIncNativeGuests: true } guests )
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
	/// The attractions the ride choice reads (phase P3a), from the object list's walk (newest first): the choosable things (the flag <c>+0x42</c> bit 4, <c>Info.IsChoosable</c>) whose ride bridge
	/// the guests host, each with its type's score words (the keys of the type record words the score <c>0x6101E0</c> reads; a key the type's .sam leaves out reads 0, as the record the loader
	/// zeroes) and whether the save's special-ride table names its thing.
	/// </summary>
	// [DATA:<attraction>.sam:Info.GenderSpecific] type +0x78; UsageInfo.AppearanceEffect +0x21C, UsageInfo.IsInfoDesk +0x244 (the key table 0x9A4520, four bytes a key from GoldenTicketCost +0x174);
	// WeatherModifier.Snow/Rain/Storm/HeatWavePercentage +0xF0..+0xFC and GenerationModifier.Kid/Parent/GrandparentScorePercentage +0x110..+0x118
	// [BIN:TPI-EXE:0x6242F0 special ride] the table 0xAA0420 (stride 0xF4, count 0xAA071C) of the park's special rides, which only the park loader 0x6244B0 fills: the prebuilt park's records
	internal static ThemeParkIncAttractionView[] ThemeParkIncAttractionViews(
		IEnumerable<(int ThingId, (int X, int Y) Cell, ObjectSettings Settings, RideVisitorBridge Visitors)> listed, GuestSimulation guests, ThemeParkIncSaveWorld? save )
	{
		var special = save?.SpecialRides is { Count: > 0 } rides ? rides.Select( ride => (int)ride.Id ).ToHashSet() : null;
		return listed.Where( item => item.ThingId is > 0 and <= ushort.MaxValue && item.Settings.GetInt( "Info.IsChoosable" ) != 0 && guests.Hosts( item.Visitors ) )
			.Select( item => new ThemeParkIncAttractionView( (ushort)item.ThingId, item.Cell, item.Visitors, ThemeParkIncScoreTermsOf( item.Settings ), special?.Contains( item.ThingId ) == true ) )
			.ToArray();
	}

	/// <summary>The score words of an attraction's type (<see cref="ThemeParkIncScoreTerms"/>); a key the .sam leaves out reads 0.</summary>
	internal static ThemeParkIncScoreTerms ThemeParkIncScoreTermsOf( ObjectSettings settings )
	{
		int Key( string key ) => settings.Has( key ) ? settings.GetInt( key ) : 0;
		return new ThemeParkIncScoreTerms( Key( "Info.GenderSpecific" ), Key( "UsageInfo.AppearanceEffect" ), Key( "UsageInfo.IsInfoDesk" ) != 0,
			Key( "WeatherModifier.SnowPercentage" ), Key( "WeatherModifier.RainPercentage" ), Key( "WeatherModifier.StormPercentage" ), Key( "WeatherModifier.HeatWavePercentage" ),
			Key( "GenerationModifier.KidScorePercentage" ), Key( "GenerationModifier.ParentScorePercentage" ), Key( "GenerationModifier.GrandparentScorePercentage" ) );
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
		private ThemeParkIncAttractionView[] attractions = [];
		private Dictionary<int, int> thingTypes = [];

		/// <summary>Forgets the views: an object was placed or removed.</summary>
		internal void Invalidate() => fresh = false;

		private void Refresh()
		{
			if ( fresh )
				return;
			fresh = true;
			bins = level.ThemeParkIncObjectsWith( "UsageInfo.HoldsLitter" ).Select( level.ThemeParkIncViewOf ).ToArray();
			shows = level.ThemeParkIncObjectsWith( "UsageInfo.IsFireworks" ).Select( level.ThemeParkIncViewOf ).ToArray();
			attractions = level.Guests is { } guests
				? ThemeParkIncAttractionViews( level.ThemeParkIncObjectsWith( "Info.IsChoosable" ).Select( item => (item.ThingId, item.ThingCell, item.Entry.Settings, item.Visitors) ), guests,
					level.OriginalPark?.Save?.ThemeParkIncWorld )
				: [];
			// [BIN:TPI-EXE:0x61035F thing slot] the thing table 0xAA18B0 holds every live thing; OriginalObjectRuntime's ride bridges carry the type as Entry.InfoId (RideVisitorBridge.TypeId)
			thingTypes = [];
			foreach ( var item in level.Objects.Objects )
				if ( !item.IsDeleted && item.ThingId is > 0 and <= ushort.MaxValue )
					thingTypes.TryAdd( item.ThingId, item.Entry.InfoId );
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

		public IReadOnlyList<ThemeParkIncAttractionView> Attractions()
		{
			Refresh();
			return attractions;
		}

		public int? ThingTypeId( ushort thingId )
		{
			Refresh();
			return thingTypes.TryGetValue( thingId, out var type ) ? type : null;
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
