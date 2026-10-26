using OpenTPW.UI.Original;

namespace OpenTPW.Hud;

/// <summary>
/// [EXT:online-coop-commands] A guest's tools in a shared park (docs/COOP.md, "Building by every player"; C3a-2): the line, placement and remove tools send their commands to the host
/// as requests (<see cref="ParkCommands.Submit"/>) and wait. The requests resolve where their records apply or the host answers, inside the stream's application or the link's pump;
/// the HUD takes them at the start of its update, outside any command, tick and stream application, and completes the tool that still waits for each. Any other request is an orphan
/// (its tool changed, or a resync replaced the park it was clicked in): its outcome is only a message. The outcomes are said to their issuer only, each as its own event.
/// </summary>
public sealed partial class ParkHud
{
	/// <summary>What a path or queue click knew before its line was committed, for the presentation its outcome gets (now, or once a guest's request comes back).</summary>
	private sealed record LineClick( (int X, int Y) Cell, (int X, int Y)? QueueJoin, bool HadStart, CellToolMode Mode, string? InterfaceBefore );

	/// <summary>The click of the guest's line that waits for the host (<see cref="CellBuildTool.Waiting"/>), or null.</summary>
	private LineClick? waitingLineClick;

	/// <summary>The guest's pick-up that waits for the host and the buy item its Move tool arms with once it is back (C3b-2), or null.</summary>
	private (CoopGuestCommands.Request Request, BuildItem Item)? waitingMove;

	/// <summary>The level's waiting clicks the HUD has said already (<see cref="Level.WaitingClicks"/>), and when it last said so.</summary>
	private int lastWaitingClicks;
	private float lastWaitingNotice = float.NegativeInfinity;

	/// <summary>Completes the tools whose requests resolved since the last frame, oldest first, and says the orphans' outcomes.</summary>
	private void CompleteGuestRequests()
	{
		if ( level.CoopSession?.GuestCommands is not { } requests )
			return;
		foreach ( var request in requests.TakeCompleted() )
		{
			// [EXT:online-coop-commands] a batch of a guest's pool stroke: its stroke reports once all of it is back (C3b-3)
			if ( level.PoolTool?.CompleteGuestRequest( request ) == true )
				continue;
			if ( level.CellTool.Waiting is { } line && ReferenceEquals( line.Commit.Request, request ) )
				CompleteLine( request );
			else if ( level.WaitingPlacement is { } placement && ReferenceEquals( placement.Request, request ) )
				CompletePlacement( placement );
			else if ( ReferenceEquals( level.WaitingToolStart, request ) && request.Command is PickUpObjectCommand )
				CompleteMove( request );
			else if ( ReferenceEquals( level.WaitingToolStart, request ) )
				CompleteQueueEdit( request );
			else if ( level.IsWaitingFor( request ) )
			{
				// a removal, a Backspace or a sale: its button or key takes the next press again, and the outcome is said
				level.EndWaiting( request );
				SayOutcome( request, null );
			}
			else
				SayOutcome( request, request.Command is PlaceObjectCommand place ? level.ResolveCommandEntry( place.Entry ) : null );
		}
	}

	/// <summary>
	/// The guest's pick-up comes back (C3b-2): accepted, the Move tool arms with the type it carries and the buy item of its click, as the host's Move does, unless the view has a tool by
	/// now (then <see cref="Level.EndStrayCarry"/> ends the carry and the object stays sold, as a cancelled Move leaves it); refused, nothing is said, as for the host.
	/// </summary>
	private void CompleteMove( CoopGuestCommands.Request request )
	{
		level.EndWaiting( request );
		var item = waitingMove is { } waiting && ReferenceEquals( waiting.Request, request ) ? waiting.Item : null;
		waitingMove = null;
		if ( request is { Accepted: true, Result: { } result } && result.As<(ObjectCatalogEntry Entry, int Rotation)>() is { Entry: { } entry } picked )
		{
			if ( item != null && level.CompleteMove( entry, picked.Rotation ) )
				pendingItem = item;
			return;
		}
		SayOutcome( request, null );
	}

	/// <summary>The buy item the build tool arms with for <paramref name="entry"/> (the Move button's, <see cref="MoveObject"/>), or null when the catalogue offers none.</summary>
	private BuildItem? BuyItemOf( ObjectCatalogEntry entry ) => Catalog.GetItems( (BuildCategory)entry.WhichUIType ).FirstOrDefault( candidate => candidate.InfoId == entry.InfoId );

	/// <summary>
	/// A resync replaced the guest's park while its Move tool was armed (C3b-2): the new park's tool arms again from the carry its snapshot holds, at the angle it had, with the buy item of its
	/// type (<see cref="Level.TakeMoveToRestore"/>); without one the carry is a stray one, which the frame ends.
	/// </summary>
	private void RestoreMove()
	{
		if ( level.TakeMoveToRestore() is not { } move || BuyItemOf( move.Entry ) is not { } item || !level.CompleteMove( move.Entry, move.Rotation ) )
			return;
		pendingItem = item;
	}

	/// <summary>
	/// The guest's queue edit comes back (C3b): accepted, the queue tool starts from what its record read, with the help the host's click posts, unless the view has a tool by now or a
	/// later record changed the queue (then the park shows what happened); otherwise its outcome is said.
	/// </summary>
	private void CompleteQueueEdit( CoopGuestCommands.Request request )
	{
		level.EndWaiting( request );
		if ( request is { Accepted: true, Result: { } result } )
		{
			if ( result.As<Level.QueueEditStart>() is { } edit && level.CompleteQueueEdit( edit ) )
			{
				PostMessage( strings.Help( 445 ) );
				level.CellTool.Hover( (cursorX, cursorY) );
			}
			return;
		}
		SayOutcome( request, null );
	}

	/// <summary>
	/// The guest's line comes back: applied, the click's continuation runs with the segment its record laid here, with the sounds and the messages of a line laid at once; refused without a
	/// record, the click is as if never made; otherwise (an error, a lost answer, a snapshot, the session's end) what it laid is not known here, and the tool ends.
	/// </summary>
	private void CompleteLine( CoopGuestCommands.Request request )
	{
		var click = waitingLineClick;
		waitingLineClick = null;
		var tool = level.CellTool;
		if ( request is { State: CoopGuestRequestState.Applied, Result: { Error: null } outcome } )
		{
			var result = level.CompleteCellTool( outcome );
			if ( outcome.RightsRefused )
				SayGuestNote( RightsRefusalText( request.Command, outcome.Reason ) );
			if ( click != null )
				FinishLineClick( click, result, released: true, sessionRefused: outcome.RightsRefused );
			return;
		}
		if ( request.State == CoopGuestRequestState.Refused )
		{
			tool.DropWaiting();
			SayGuestNote( OpenTpwText.CoopHostCouldNotTake );
			return;
		}
		tool.Cancel();
		SayOutcome( request, null );
	}

	/// <summary>
	/// The guest's placement comes back: accepted, the placement finishes as the host's does (the Built message, the ride's queue tool, the tool kept for exactly Ctrl at the click),
	/// also when a later record in the same frames removed the object again (then no tool follows it); refused, its reason is said and the tool stays armed with its item.
	/// </summary>
	private void CompletePlacement( Level.PlacementRequest placement )
	{
		level.EndWaiting( placement.Request );
		if ( placement.Request is { Accepted: true, Result: { } result } && result.As<OriginalObject>() is { } item )
		{
			FinishPlacement( item, placement.KeepTool );
			return;
		}
		SayOutcome( placement.Request, placement.Entry );
	}

	/// <summary>
	/// The outcome of a request whose tool takes nothing from it (an orphan, a removal, a line or placement that cannot continue, land, the area clear, a Backspace, a sale, a queue edit):
	/// an accepted placement says Built, anything else accepted nothing (the park shows it); a refusal by the rights says the host has not let this player build (or that only the host
	/// does that); a refused placement or line its reason and a refused purchase that the land cannot be bought, as the host's refusals are said, and other refusals of the park nothing,
	/// as for the host; a refusal without a record that the host could not take it; an error or a lost answer that its outcome is not known. A snapshot and the session's end say nothing.
	/// </summary>
	private void SayOutcome( CoopGuestCommands.Request request, ObjectCatalogEntry? entry )
	{
		switch ( request.State )
		{
			case CoopGuestRequestState.Applied when request.Result is { Error: null } result:
				if ( result.Accepted )
				{
					if ( request.Command is PlaceObjectCommand && entry != null )
						PostMessage( string.Format( strings.Extra( OpenTpwText.Built ), EntryName( entry ) ) );
				}
				else if ( result.RightsRefused )
					SayGuestNote( RightsRefusalText( request.Command, result.Reason ) );
				// [EXT:online-coop-claims] a claim another player holds refuses the guest's claim in its words (C3c-1)
				else if ( request.Command is ClaimRideCommand && result.Reason == Level.ClaimedReason )
					SayGuestNote( OpenTpwText.CoopClaimed );
				else if ( request.Command is PlaceObjectCommand or LayLineCommand or QueueSegmentCommand )
					SayCannotBuild( result.Reason ?? "", entry );
				// [EXT:land-feedback] a refused purchase says so, as the host's refused click does
				else if ( request.Command is BuyLandCommand )
					SayGuestNote( OpenTpwText.CannotBuyLand );
				// [EXT:pools-bridges] [EXT:pools-tool] a refused Backspace over a pool bridge or water cell says why, as the host's key does (C3b-3)
				else if ( request.Command is BacktrackCommand && result.Value is PoolBridgeResult bridge )
					SayGuestNote( BridgeRefusalText( bridge ) );
				else if ( request.Command is BacktrackCommand && result.Value is PoolBuildResult water )
					SayGuestNote( PoolRefusalText( water ) );
				return;
			case CoopGuestRequestState.Applied:
			case CoopGuestRequestState.Unknown:
				SayGuestNote( OpenTpwText.CoopBuildUnknown );
				return;
			case CoopGuestRequestState.Refused:
				SayGuestNote( OpenTpwText.CoopHostCouldNotTake );
				return;
		}
	}

	/// <summary>[EXT:online-coop-commands] A guest's pool strokes (C3b-3): what waits goes to the host, and each stroke that finished reports as the host's release does.</summary>
	private void UpdateGuestPoolStrokes()
	{
		if ( level.PoolTool is not { } tool )
			return;
		tool.SendGuestBatches( Time.Now );
		foreach ( var report in tool.TakeGuestReports() )
			ReportGuestPoolStroke( report );
	}

	/// <summary>
	/// A guest's finished pool stroke, said as <see cref="ReportPoolStroke"/> says the host's at its release: the lay sound when it wrote, the slide's done message, one refusal with its
	/// marker; and its notes: a refusal by the rights, a request the host could not take, an outcome not known.
	/// </summary>
	private void ReportGuestPoolStroke( PoolGuestReport report )
	{
		if ( report.Wrote )
			GameAudio.PlayPathToolSound( GameAudio.PathLayEvent );
		if ( report.SlideDone )
			PostMessage( strings.Extra( OpenTpwText.PoolSlideDone ) );
		if ( report.RightsRefused is { } refused )
			SayGuestNote( RightsRefusalText( refused, report.RightsReason ) );
		else if ( report.NotTaken )
			SayGuestNote( OpenTpwText.CoopHostCouldNotTake );
		else if ( report.Unknown )
			SayGuestNote( OpenTpwText.CoopBuildUnknown );
		if ( report.Refusal is not { } refusal )
			return;
		// the stroke's press was released before its report: the refusal cue plays now, as a guest's line released before its outcome plays it (FinishLineClick)
		GameAudio.PlayPathRefused( refusal.Cell.X, refusal.Cell.Y );
		PostMessage( strings.Extra( report.BridgeRefusal is { } bridge ? BridgeRefusalText( bridge ) : PoolRefusalText( refusal.Reason, report.SlideRefusal ) ) );
	}

	/// <summary>
	/// [EXT:online-coop-permissions] What a refusal of <paramref name="command"/> by the session's rights says, from its <paramref name="reason"/>: [EXT:online-coop-claims] another player's claim
	/// (C3c-1), building the host has not let this player do yet, or what only the host does.
	/// </summary>
	private OpenTpwText RightsRefusalText( ParkCommand command, string? reason ) =>
		RefusalText( level.Commands.Rights is CoopBuildRights rights ? rights.RefusalOf( command, reason ) : CoopRefusal.HostOnly );

	/// <summary>The note of a refusal by the session's rights.</summary>
	private static OpenTpwText RefusalText( CoopRefusal refusal ) => refusal switch
	{
		CoopRefusal.Claimed => OpenTpwText.CoopClaimed,
		CoopRefusal.NotGranted => OpenTpwText.CoopNotGranted,
		_ => OpenTpwText.CoopHostOnly
	};

	/// <summary>A note about this player's own request, with the error sound.</summary>
	private void SayGuestNote( OpenTpwText text )
	{
		PostMessage( strings.Extra( text ) );
		GameAudio.PlayUi( GameAudio.ErrorEvent );
	}

	/// <summary>A tool took no click while its request waited for the host: the player is asked to wait, once for a run of clicks.</summary>
	private void SayWaitingClicks()
	{
		if ( level.WaitingClicks == lastWaitingClicks )
			return;
		lastWaitingClicks = level.WaitingClicks;
		if ( Time.Now - lastWaitingNotice < SessionRefusalNoticeSeconds )
			return;
		lastWaitingNotice = Time.Now;
		PostMessage( strings.Extra( OpenTpwText.CoopWaitingForHost ) );
	}
}
