using System.Text.Json.Nodes;
using OpenTPW.Hud;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>
/// [EXT:online-coop-commands] The live acceptance's management scenario (C4b-2; docs/COOP.md, "The live acceptance"): after the baseline's lines, whose guest's line is the scenario's
/// purchase, the host gives the attractions right through the rights dialog; the guest types a shop's price and toggles a ride's door through their windows, each at its record on both
/// parks; then the guest types a second price and, the frame after its click, while its request still waits unsent, forces a desync of its own stream, so the session resyncs and the
/// price goes once after the swap.
/// </summary>
internal sealed partial class CoopLiveTest
{
	/// <summary>The reason the guest's forced desync logs (<see cref="CoopGuestStream.ForceDesync"/>).</summary>
	internal const string ForcedDesyncReason = "forced by the live test";

	/// <summary>
	/// [EXT:online-coop-money] A seat's first path line as one park applied it (the management scenario's purchase): the balance just before the command (after the tick or the command
	/// before it) and just after it, what it charged and how many cells it laid.
	/// </summary>
	private sealed class LinePurchase : IDisposable
	{
		private readonly Level level;
		private readonly int seat;
		private long last;

		public LinePurchase( Level level, int seat )
		{
			this.level = level;
			this.seat = seat;
			last = Balance;
			level.TickEnded += OnTickEnded;
			level.Commands.Applied += OnApplied;
		}

		/// <summary>The purchase once applied, else null.</summary>
		public JsonObject? Evidence { get; private set; }

		private long Balance => level.Park?.Economy.Balance ?? 0;

		private void OnTickEnded( long tick ) => last = Balance;

		private void OnApplied( ParkCommandLogEntry entry, ParkCommandResult result )
		{
			if ( Evidence == null && entry.Command.Player == seat && entry.Command is LayLineCommand or QueueSegmentCommand && result.Accepted && result.As<SegmentResult>() is { } segment )
				Evidence = new JsonObject
				{
					["before"] = last,
					["after"] = Balance,
					["charged"] = segment.Charged,
					["cells"] = segment.Built.Count,
				};
			last = Balance;
		}

		public void Dispose()
		{
			level.TickEnded -= OnTickEnded;
			level.Commands.Applied -= OnApplied;
		}
	}

	/// <summary>The economy record of <paramref name="item"/> in <paramref name="level"/>, or null.</summary>
	private static ParkObjectState? RecordOf( Level level, OriginalObject item ) =>
		level.GetEconomyInstance( item ) is int instance && level.Park?.Economy is { } economy && economy.TryGetObject( instance, out var state ) ? state : null;

	/// <summary>The placed object of attraction <paramref name="attraction"/> in the park open now, or null.</summary>
	private OriginalObject? Attraction( int attraction ) => ThePark.Objects.Objects.FirstOrDefault( item => !item.IsDeleted && item.Runtime.AttractionId == attraction );

	/// <summary>
	/// The object window of <paramref name="item"/> opened as a player opens it: the view over it and a click on one of its cells; a click that opened something else (a visitor's
	/// window) is closed with Escape and tried again.
	/// </summary>
	private IEnumerable<CoopLiveWait> OpenWindow( OriginalObject item )
	{
		var level = ThePark;
		var canvas = flow.Context.Canvas;
		for ( var attempt = 0; attempt < LineTries; attempt++ )
		{
			yield return Wait( "the park's base screen alone", () => TheHud.Stack.Screens.Count == 1 && !TheHud.InfoArmOpen && InputGiven, 30 );
			var cell = RideCell( item );
			Require( cell != null, "the object has a cell a click reaches" );
			Look( (cell!.Value.X + 0.5f, cell.Value.Y + 0.5f) );
			yield return Pause( "the view over the object", 0.5 );
			Require( level.TryProjectCell( cell.Value.X, cell.Value.Y, new System.Numerics.Vector2( canvas.Width, canvas.Height ), out var point ), "the object is in the view" );
			foreach ( var step in ClickThrough( point ) )
				yield return step;
			yield return Wait( "the click's window", () => InputGiven && (TheHud.InfoArmOpen || TheHud.Stack.Screens.Count > 1), 10 );
			yield return Pause( "the window's opening", 0.3 );
			if ( TheHud.InfoArmOpen && ReferenceEquals( TheHud.SelectedObject, item ) )
				yield break;
			Log.Info( $"Live co-op acceptance: the click at {cell} opened {TheHud.SelectedObject?.Entry.DisplayName ?? TheHud.Stack.Top?.Name}; trying again." );
			foreach ( var step in CloseWindows() )
				yield return step;
		}
		Require( false, $"a click opened the window of {item.Entry.DisplayName} within {LineTries} tries" );
	}

	/// <summary>Escape until the park's base screen stands alone with no object window, as a player closes them.</summary>
	private IEnumerable<CoopLiveWait> CloseWindows()
	{
		while ( TheHud.Stack.Screens.Count > 1 || TheHud.InfoArmOpen )
		{
			foreach ( var step in KeyThrough( UiKeys.Back, "Escape" ) )
				yield return step;
			yield return Wait( "Escape closes the window", () => InputGiven, 10 );
			yield return Pause( "the window's close", 0.3 );
		}
	}

	/// <summary>The shown shop's typed price editor, opened from its price box, with <paramref name="price"/> typed in.</summary>
	private IEnumerable<CoopLiveWait> TypeShopPrice( int price )
	{
		yield return Wait( "the shop's price box", () => Usable<UiButton>( "hud", "shopPrice" ), 10 );
		Press( "hud", "shopPrice" );
		yield return Wait( "the price editor", () => Top( "shopPrice" ), 10 );
		TypeInto( "shopPrice", "price", price.ToString( System.Globalization.CultureInfo.InvariantCulture ) );
	}

	/// <summary>A number of the other parties' JSON, or null.</summary>
	private static long? LongOf( JsonNode? node ) => node is JsonValue value && value.TryGetValue<long>( out var number ) ? number : null;

	/// <summary>
	/// The guest's request of a command its management queue sent that <paramref name="matches"/>, followed to its record: sent (the queue's last submitted request), then answered,
	/// applied and accepted; <paramref name="answered"/> gets it.
	/// </summary>
	private IEnumerable<CoopLiveWait> AtItsRecord( string what, Func<ParkCommand, bool> matches, Action<CoopGuestCommands.Request> answered )
	{
		CoopGuestCommands.Request? sent = null;
		yield return Wait( $"{what} sent", () => (sent ??= TheSession.GuestCommands?.LastSubmitted is { } request && matches( request.Command ) ? request : null) != null, 30 );
		yield return Wait( $"{what} at its record", () => !sent!.IsOpen, 120 );
		Require( sent!.State == CoopGuestRequestState.Applied && sent.Accepted, $"{what} applied ({sent.State}, {sent.Result?.Reason ?? sent.Refusal?.ToString()})" );
		answered( sent );
	}

	/// <summary>
	/// The guest's management (C4b-2): the attractions right reaches it; a shop's typed price and a ride's door, each at its record; then a second price forced across a resync.
	/// </summary>
	private IEnumerable<CoopLiveWait> GuestManagement()
	{
		var level = ThePark;
		var seat = level.Commands.Player;
		yield return Wait( "the host gives the attractions right", () => OtherSays( "attractions" ) && level.RightsOf( seat ).HasFlag( CoopRights.Attractions ), 180 );
		var queue = TheSession.Management ?? throw new InvalidOperationException( "Live co-op acceptance: the guest's session has no management queue." );
		var evidence = new JsonObject();

		// a shop of the park: its window by a click, a typed price, the window closed; the price at its record
		var shop = level.Objects.Objects.FirstOrDefault( item => !item.IsDeleted && item.Entry.WhichUIType == (int)BuildCategory.Shops && RecordOf( level, item ) is { Kind: ParkObjectKind.Shop }
			&& RideCell( item ) != null );
		Require( shop != null, "the park has a shop" );
		var attraction = shop!.Runtime.AttractionId;
		var price = (RecordOf( level, shop )!.Price + 7) % ParkHud.MaximumPrice;
		foreach ( var step in OpenWindow( shop ) )
			yield return step;
		foreach ( var step in TypeShopPrice( price ) )
			yield return step;
		Press( "shopPrice", "apply" );
		yield return Wait( "the editor closes, its price queued", () => Top( "hud" ), 10 );
		long? record = null;
		foreach ( var step in AtItsRecord( "the guest's price", command => command is SetPriceCommand set && set.Attraction == attraction && set.Price == price,
			request => record = request.Record ) )
			yield return step;
		Require( RecordOf( level, shop )!.Price == price, "the guest's typed price in its park" );
		foreach ( var step in CloseWindows() )
			yield return step;
		evidence["price"] = new JsonObject { ["shop"] = attraction, ["price"] = price, ["record"] = record };
		Publish( "priceSet", evidence["price"] );

		// a ride's door through its window, closed and opened again, each at its record (one open-state action at a time)
		var ride = level.Objects.Objects.FirstOrDefault( item => !item.IsDeleted && item.Entry.WhichUIType == (int)BuildCategory.Rides && item.Runtime.IsAttraction && item.IsOpen
			&& RecordOf( level, item ) != null && RideCell( item ) != null );
		Require( ride != null, "the park has an open ride" );
		foreach ( var step in OpenWindow( ride! ) )
			yield return step;
		var doors = new JsonArray();
		foreach ( var open in new[] { false, true } )
		{
			yield return Wait( "the ride's door", () => Usable<UiButton>( "hud", "openRide" ) && !queue.Pending.Any(), 10 );
			Press( "hud", "openRide" );
			long? doorRecord = null;
			foreach ( var step in AtItsRecord( open ? "the opened door" : "the closed door",
				command => command is SetAttractionOpenCommand door && door.Attraction == ride!.Runtime.AttractionId && door.Open == open, request => doorRecord = request.Record ) )
				yield return step;
			Require( ride!.IsOpen == open, $"the door {(open ? "opened" : "closed")} in the guest's park" );
			doors.Add( new JsonObject { ["ride"] = ride.Runtime.AttractionId, ["open"] = open, ["record"] = doorRecord } );
		}
		foreach ( var step in CloseWindows() )
			yield return step;
		evidence["doors"] = doors;
		Publish( "doorSet", doors );

		// the forced resync: a second price typed and applied by Enter (the frame's HUD queues it after the frame's pump); at the next step, before the next frame's pump, it waits
		// unsent, and the guest's own stream is forced to desync
		yield return Wait( "the host ready for the resync", () => OtherSays( "readyForResync" ), 120 );
		var second = (RecordOf( level, shop )!.Price + 11) % ParkHud.MaximumPrice;
		foreach ( var step in OpenWindow( shop ) )
			yield return step;
		foreach ( var step in TypeShopPrice( second ) )
			yield return step;
		foreach ( var step in KeyThrough( UiKeys.Accept, "Enter" ) )
			yield return step;
		bool Queued( ManagementEntry entry ) => entry.Command is SetPriceCommand set && set.Attraction == attraction && set.Price == second;
		yield return Wait( "the second price queued", () => queue.Pending.Any( Queued ), 30 );
		var waiting = queue.Pending.Single( Queued );
		Require( waiting.State == ManagementEntryState.Unsent, "the second price waits unsent at the cut" );
		var stream = TheSession.GuestStream!;
		var (park, generation, failedJoins) = (level, stream.Generation, TheSession.FailedJoins);
		stream.ForceDesync( ForcedDesyncReason );
		var report = stream.Desync ?? throw new InvalidOperationException( "Live co-op acceptance: the forced desync made no report." );
		var forced = new JsonObject
		{
			["frame"] = frame,
			["generation"] = generation,
			["failedJoinsBefore"] = failedJoins,
			["entry"] = waiting.State.ToString(),
			["desync"] = new JsonObject { ["id"] = report.Id, ["kind"] = report.Kind.ToString(), ["tick"] = report.Tick, ["reason"] = ForcedDesyncReason },
		};
		Publish( "forcedDesync", forced );
		yield return Wait( "the host's park swapped in", () => !ReferenceEquals( flow.Level, park ) && flow.Level?.CoopSession is { Phase: CoopGuestPhase.Following, GuestStream: { } now }
			&& now.Generation == generation + 1, 600 );
		yield return Wait( "the second price after the swap", () => !queue.Pending.Any() && Attraction( attraction ) is { } now && RecordOf( ThePark, now )?.Price == second, 120 );
		yield return Wait( "checkpoints compared after the swap", () => TheSession.GuestStream is { Stopped: false, Checks: >= 2 }, 60 );
		// the forced desync takes the stream's own path: within the session's early window after the join it counts as the join's failure, as a real one would; nothing more may
		// (the session's desync ids go on across its streams, so the new stream's count is the forced one's id)
		Require( TheSession.GuestStream is { Desync: null } now && now.Desyncs == report.Id && TheSession.FailedJoins <= failedJoins + 1,
			"no further desync or failed join after the swap" );
		forced["failedJoinsAfter"] = TheSession.FailedJoins;
		forced["after"] = StreamEvidence();
		forced["price"] = second;
		forced["shop"] = attraction;
		evidence["forced"] = forced;
		Keep( "management", evidence );
		Publish( "resynced", forced );
		yield return Wait( "the host saw the guest's management", () => OtherSays( "sawManagement" ), 180 );
	}

	/// <summary>
	/// The host's side of the management scenario (C4b-2): the attractions right through the rights dialog; the guest's price and door at their records in this park; then the forced
	/// resync: exactly one desync report, the forced one, one new capture, and the second price applied once.
	/// </summary>
	private IEnumerable<CoopLiveWait> HostManagement( string guestName, int seat )
	{
		var level = ThePark;
		var hostStream = TheSession.HostStream!;
		var applied = new List<(ParkCommand Command, bool Accepted, long? Record)>();
		void OnApplied( ParkCommandLogEntry entry, ParkCommandResult result )
		{
			if ( entry.Command.Player == seat && entry.Command is SetPriceCommand or SetAttractionOpenCommand )
				applied.Add( (entry.Command, result.Accepted, hostStream.LastRecord) );
		}
		var desyncs = new JsonArray();
		void OnDesynced( uint guest, CoopDesyncReport report ) =>
			desyncs.Add( new JsonObject { ["guest"] = guest, ["id"] = report.Id, ["kind"] = report.Kind.ToString(), ["tick"] = report.Tick } );
		level.Commands.Applied += OnApplied;
		hostStream.Desynced += OnDesynced;
		var evidence = new JsonObject();
		try
		{
			// [EXT:online-coop-permissions] a rights change through the dialog: the attractions right
			foreach ( var step in GiveTheGuestARight( guestName, seat, CoopRights.Attractions, "attractionsGrant" ) )
				yield return step;
			Publish( "attractions", true );
			// the guest's typed price at its record here
			yield return Wait( "the guest's price", () => Other( "priceSet" ) is JsonObject, 300 );
			var price = (JsonObject)Other( "priceSet" )!;
			var (shop, value) = (price["shop"]!.GetValue<int>(), price["price"]!.GetValue<int>());
			yield return Wait( "the guest's price in the host's park", () => applied.Any( item => item.Command is SetPriceCommand set && set.Attraction == shop && set.Price == value ), 60 );
			var priced = applied.Single( item => item.Command is SetPriceCommand set && set.Attraction == shop && set.Price == value );
			Require( priced.Accepted && Attraction( shop ) is { } item && RecordOf( level, item )?.Price == value, "the guest's price applied in the host's park" );
			evidence["price"] = new JsonObject { ["shop"] = shop, ["price"] = value, ["record"] = priced.Record };
			// the guest's door, closed and opened again, each at its record here
			yield return Wait( "the guest's door", () => Other( "doorSet" ) is JsonArray, 300 );
			var doors = new JsonArray();
			foreach ( var door in ((JsonArray)Other( "doorSet" )!).OfType<JsonObject>() )
			{
				var (ride, open) = (door["ride"]!.GetValue<int>(), door["open"]!.GetValue<bool>());
				var match = applied.FirstOrDefault( item => item.Command is SetAttractionOpenCommand set && set.Attraction == ride && set.Open == open && item.Record == LongOf( door["record"] ) );
				Require( match.Command != null && match.Accepted, $"the guest's door {(open ? "opened" : "closed")} at the same record in the host's park" );
				doors.Add( new JsonObject { ["ride"] = ride, ["open"] = open, ["record"] = match.Record } );
			}
			evidence["doors"] = doors;
			// the forced resync: the host sees one desync report, the forced one, takes one capture for it, and the second price applies once
			var captures = hostStream.CapturesTaken;
			Publish( "readyForResync", true );
			yield return Wait( "the guest's resync", () => Other( "resynced" ) is JsonObject, 600 );
			var forced = (JsonObject)Other( "resynced" )!;
			var (second, forcedId) = (forced["price"]!.GetValue<int>(), forced["desync"]!["id"]!.GetValue<uint>());
			var seconds = level.Commands.Log.Count( entry => entry.Command.Player == seat && entry.Command is SetPriceCommand set && set.Attraction == shop && set.Price == second );
			Require( desyncs.Count == 1 && desyncs[0]!["id"]!.GetValue<uint>() == forcedId && desyncs[0]!["kind"]!.GetValue<string>() == nameof( CoopDesyncKind.Protocol ),
				"the host saw exactly one desync report, the forced one" );
			Require( hostStream.CapturesTaken == captures + 1, "one new capture for the guest" );
			Require( seconds == 1, $"the second price applied once in the host's park ({seconds})" );
			evidence["forced"] = new JsonObject
			{
				["desyncs"] = desyncs.DeepClone(),
				["capturesBefore"] = captures,
				["capturesAfter"] = hostStream.CapturesTaken,
				["secondPriceRecords"] = seconds,
			};
			Keep( "management", evidence );
			Publish( "sawManagement", true );
		}
		finally
		{
			level.Commands.Applied -= OnApplied;
			hostStream.Desynced -= OnDesynced;
		}
	}
}
