using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>
/// [EXT:online-coop-commands] A guest's attraction windows in a shared park (C4b-2, docs/COOP.md "A guest's management windows"). The object window keeps no draft: its controls draw the
/// drawn values (each group's latest request not resolved yet, else the park's), and each change goes at once as a click, which the session's rights, a claim or the queue's room may refuse
/// before anything changes; its close and retarget send nothing. The door, the call button and the upgrade rows are open-state actions, one per object at a time, and the window draws the
/// object's record as the waiting one will leave it. A host's and a single player's windows apply their values on close, a host's in a shared park only what it changed (<see cref="loaded"/>).
/// </summary>
public sealed partial class ParkHud
{
	/// <summary>The management target of a placed attraction: its entries go one at a time, in their click's order.</summary>
	private static ManagementTarget AttractionTarget( OriginalObject item ) => ManagementTarget.Attraction( item.Runtime.AttractionId );

	// ---- the window's values ----

	/// <summary>Loads the window's values of <paramref name="item"/> for a guest: the park's, then each group's latest request not resolved yet (the drawn values).</summary>
	private void LoadDrawnSettings( OriginalObject item )
	{
		LoadObjectSettings( item );
		if ( GuestManagement is not { } queue )
			return;
		var target = AttractionTarget( item );
		if ( queue.PendingCommand( target, ManagementKnob.RideSettings ) is SetRideSettingsCommand ride )
			pending.Ride = new RideSettingWords( ride.Speed, ride.Capacity, ride.Duration );
		if ( queue.PendingCommand( target, ManagementKnob.ShopSettings ) is SetShopSettingsCommand shop )
		{
			pending.QualityStep = SliderPosition( (shop.QualityOfGoods & 0xFF) / 50, 0, 2 );
			pending.Ingredient = SliderPosition( shop.IngredientAmount & 0xFF, 0, 100 );
		}
		if ( queue.PendingCommand( target, ManagementKnob.Price ) is SetPriceCommand price )
			pending.Price = price.Price;
		if ( queue.PendingCommand( target, ManagementKnob.Game ) is SetGamePrizeCommand game )
		{
			pending.Price = game.Price;
			pending.Prize = game.CostOfGoods;
			pending.WinChance = SliderPosition( (100 - game.ChanceOfLosing) & 0xFF, 0, 100 );
		}
	}

	/// <summary>The group a price change of <paramref name="item"/> sends: a winnable sideshow's price goes with its game (one writer per field), any other price alone.</summary>
	private static ManagementKnob PriceKnob( OriginalObject item ) =>
		item.Entry.WhichUIType == (int)BuildCategory.Sideshows && !IsNonWinnable( item ) ? ManagementKnob.Game : ManagementKnob.Price;

	/// <summary>The groups a window apply of a shop or a sideshow writes, in the host's apply's order (a shop's settings before its price).</summary>
	private static IEnumerable<ManagementKnob> WindowGroups( OriginalObject item, ParkObjectState state )
	{
		if ( state.Kind == ParkObjectKind.Shop )
		{
			yield return ManagementKnob.ShopSettings;
			yield return ManagementKnob.Price;
		}
		else
			yield return PriceKnob( item );
	}

	/// <summary>The command of <paramref name="knob"/>'s group of <paramref name="item"/> with the window's values, or null when the object has no such group.</summary>
	private ParkCommand? WindowGroupCommand( OriginalObject item, ManagementKnob knob ) => knob.Kind switch
	{
		ManagementKnobKind.RideSettings when item.Entry.WhichUIType == (int)BuildCategory.Rides && item.Runtime.Script != null =>
			new SetRideSettingsCommand( item.Runtime.AttractionId, pending.Ride.Speed, pending.Ride.Capacity, pending.Ride.Duration ),
		ManagementKnobKind.ShopSettings => new SetShopSettingsCommand( item.Runtime.AttractionId, (byte)(pending.QualityStep * 50), pending.Ingredient ),
		ManagementKnobKind.Price => new SetPriceCommand( item.Runtime.AttractionId, pending.Price ),
		ManagementKnobKind.Game => new SetGamePrizeCommand( item.Runtime.AttractionId, pending.Price, pending.Prize, 100 - (pending.WinChance & 0xFF) ),
		_ => null
	};

	/// <summary>
	/// A control of the shown object's window changed its group (<paramref name="knob"/>) on a guest: the group goes at once with the window's values, its other fields as drawn; refused (the
	/// rights, a claim, no room), the window takes the drawn values again, so the control returns. Nothing on a host or a single player, whose window applies on close.
	/// </summary>
	private void SendWindowGroup( ManagementKnob knob )
	{
		if ( GuestManagement is not { } queue || selectedObject is not { IsDeleted: false } item || !ReferenceEquals( item, settingsOwner ) || WindowGroupCommand( item, knob ) is not { } command )
			return;
		if ( !QueueManagement( queue, new[] { new ManagementItem( AttractionTarget( item ), knob, command ) } ).Queued )
			LoadDrawnSettings( item );
	}

	/// <summary>
	/// A guest's apply-all (the shop and sideshow windows' button): the window's values for every placed object of the shown one's type, one entry per group per object, queued whole or not at
	/// all; refused, its note and nothing changes.
	/// </summary>
	private void ApplyGuestSettingsToAll( CoopManagementRequests queue, OriginalObject owner, ParkEconomy economy )
	{
		var items = new List<ManagementItem>();
		foreach ( var other in level.Objects.Objects )
		{
			if ( other.IsDeleted || other.Entry.InfoId != owner.Entry.InfoId || level.GetEconomyInstance( other ) is not int instance
				|| !economy.TryGetObject( instance, out var target ) || target.InfoId != owner.Entry.InfoId )
				continue;
			foreach ( var knob in WindowGroups( other, target ) )
				if ( WindowGroupCommand( other, knob ) is { } command )
					items.Add( new ManagementItem( AttractionTarget( other ), knob, command ) );
		}
		if ( items.Count > 0 )
			QueueManagement( queue, items );
	}

	// ---- the open state ----

	/// <summary>
	/// The economy record of <paramref name="item"/> as a guest's waiting open-state action will leave it (C4b-2): a copy of the park's record, its open word the runtime's (a door command
	/// changes it at once, the record's mirrors it at the next tick), with the action applied only when its record's checks would pass, so a refusal or a no-op (the host's call or cancel came
	/// first) changes nothing. Null on a host, a single player, or with no open-state action waiting.
	/// </summary>
	private ParkObjectState? ProjectedRecord( OriginalObject item )
	{
		if ( GuestManagement is not { } queue || item.IsDeleted || queue.OpenStateCommand( AttractionTarget( item ) ) is not { } command || LiveState( item ) is not { } record
			|| level.Park?.Economy is not { } economy )
			return null;
		var copy = record.Projection();
		copy.IsOpen = item.IsOpen;
		var condemned = ParkEconomy.ObjectState( copy ) == RideObjectStates.Condemned;
		switch ( command )
		{
			case SetAttractionOpenCommand door when !condemned && !(item.Entry.WhichUIType == (int)BuildCategory.Rides && (item.Runtime.TrackIncomplete || item.Runtime.TrackInvalid)):
				// an opening asks the reopening guard first; its refusal leaves the door as it is
				if ( !door.Open || (level.Gate?.MayOpen( item.Runtime, copy ) ?? true) )
					copy.IsOpen = door.Open;
				break;
			case RequestMaintenanceCommand call when !condemned && call.On != copy.MaintenanceRequested
				&& !(call.On && CallTarget( item, out _, out _, out var toilet ) && CallRefusal( economy, toilet ) != null):
				copy.MaintenanceRequested = call.On;
				if ( call.On )
					copy.IsOpen = false;
				else if ( level.Gate?.MayOpen( item.Runtime, copy ) ?? true )
					copy.IsOpen = true;
				break;
			case BuyUpgradeCommand buy when (buy.ExpectedLevel is not { } expected || copy.Level + 1 == expected && copy.PendingLevel <= copy.Level)
				&& economy.UpgradePurchaseRefusal( copy ) == ParkEconomy.PurchaseResult.Ok:
				copy.PendingLevel = copy.Level + 1;
				copy.UpgradeCostDue = true;
				copy.IsOpen = false;
				break;
			case CancelUpgradeCommand cancel when (cancel.ExpectedPendingLevel is not { } pendingLevel
					|| copy.PendingLevel == pendingLevel && (cancel.ExpectedPurchase is not { } purchase || copy.UpgradePurchases == purchase))
				&& ParkEconomy.ObjectState( copy ) == RideObjectStates.Upgrading:
				copy.PendingLevel = 0;
				copy.UpgradeCostDue = false;
				copy.IsOpen = true;
				break;
		}
		return copy;
	}

	/// <summary>The economy record the window reads for <paramref name="item"/>: the projected one while a guest's open-state action waits (<see cref="ProjectedRecord"/>), else the park's.</summary>
	private ParkObjectState? DrawnRecord( OriginalObject item ) => ProjectedRecord( item ) ?? LiveState( item );

	/// <summary>Whether <paramref name="item"/>'s open word is drawn open: the projected record's while a guest's open-state action waits, else the runtime's.</summary>
	private bool DrawnIsOpen( OriginalObject item ) => ProjectedRecord( item ) is { } projected ? projected.IsOpen : item.IsOpen;

	/// <summary>Whether an open-state action of <paramref name="item"/> waits on a guest: its door and call buttons are greyed and take no click, its level and cancel rows are inert.</summary>
	private bool OpenStateWaits( OriginalObject item ) => GuestManagement is { } queue && queue.OpenStateWaits( AttractionTarget( item ) );

	/// <summary>A guest's door click: the opposite of the drawn open word, as an open-state action; no click while one of the object waits. The original's door posts nothing to wait for.</summary>
	private void RequestAttractionDoor( CoopManagementRequests queue, OriginalObject item )
	{
		if ( queue.OpenStateWaits( AttractionTarget( item ) ) )
			return;
		QueueManagement( queue, new[] { new ManagementItem( AttractionTarget( item ), ManagementKnob.Door, new SetAttractionOpenCommand( item.Runtime.AttractionId, !DrawnIsOpen( item ) ) ) } );
	}

	/// <summary>
	/// A guest's call button: the opposite of the drawn call, as an open-state action, after the rights (their note) and then the call's own refusal of a new call (574, 575: the error sound,
	/// as the host's disabled button gives none); no click while an open-state action of the object waits.
	/// </summary>
	private void RequestGuestCall( CoopManagementRequests queue, OriginalObject owner, ParkEconomy economy, ParkObjectState record, bool toilet )
	{
		var target = AttractionTarget( owner );
		if ( queue.OpenStateWaits( target ) )
			return;
		var command = new RequestMaintenanceCommand( owner.Runtime.AttractionId, !record.MaintenanceRequested );
		var item = new ManagementItem( target, ManagementKnob.Maintenance, command );
		if ( level.Commands.RightsRefusalOf( command ) is { } refusal )
		{
			queue.NoteRefused( item, refusal );
			GameAudio.PlayUi( GameAudio.ErrorEvent );
			return;
		}
		if ( command.On && CallRefusal( economy, toilet ) != null )
		{
			GameAudio.PlayUi( GameAudio.ErrorEvent );
			return;
		}
		QueueManagement( queue, new[] { item } );
	}

	// ---- the upgrade list ----

	/// <summary>The upgrade list's level or cancel row waiting for the host on a guest (C4b-2): its waiter, the list showing a waiting line and its rows inert until the outcome.</summary>
	private ManagementWaiter? upgradeWaiter;

	/// <summary>
	/// A guest's level or cancel row: the rights first (their note, and the list closes), then the buy or the cancellation with what the list showed (the next level; the pending level and
	/// its purchase) as an open-state action waiting with the list; no click while an open-state action of the ride waits.
	/// </summary>
	private void ActivateGuestUpgradeRow( CoopManagementRequests queue, OriginalObject owner, RideUpgradeRow row )
	{
		var target = AttractionTarget( owner );
		if ( queue.OpenStateWaits( target ) || LiveState( owner ) is not { } state )
			return;
		ParkCommand command = row.Kind == RideUpgradeRowKind.Level ? new BuyUpgradeCommand( owner.Runtime.AttractionId, state.Level + 1 )
			: new CancelUpgradeCommand( owner.Runtime.AttractionId, state.PendingLevel, state.UpgradePurchases );
		var waiter = queue.NewWaiter();
		if ( !QueueManagement( queue, new[] { new ManagementItem( target, null, command ) }, waiter ).Queued )
		{
			queue.Detach( waiter );
			CloseRideUpgrade();
			return;
		}
		upgradeWaiter = waiter;
	}

	/// <summary>The waiting list's outcome: the list closes first (the host's closes after every row), then the outcome's note, if any (an accepted one says nothing).</summary>
	private void CompleteGuestUpgrade()
	{
		if ( upgradeWaiter is not { } waiter )
			return;
		foreach ( var outcome in waiter.Queue.TakeOutcomes( waiter ) )
		{
			upgradeWaiter = null;
			CloseRideUpgrade();
			NoteManagement( outcome );
		}
	}

	/// <summary>
	/// A guest's add-on row without the building grant: the grant's note through the session's notes, before the row's price check or the tool's arming (C4b-2). False with the grant, or on a
	/// host or a single player.
	/// </summary>
	private bool RefusesAddOnWithoutGrant( OriginalObject ride, ObjectCatalogEntry entry )
	{
		if ( GuestManagement is not { } queue || level.HasBuildGrant( level.Commands.Player ) )
			return false;
		var placement = new PlaceObjectCommand( ParkCommandCodec.EntryKey( entry ), ride.Placement.X, ride.Placement.Y, 0, AddOnRide: ride.Runtime.AttractionId );
		queue.NoteRefused( new ManagementItem( AttractionTarget( ride ), null, placement ), ParkCommandResult.Rejected( CoopBuildRights.NotGrantedReason ) with { RightsRefused = true } );
		GameAudio.PlayUi( GameAudio.ErrorEvent );
		return true;
	}
}
