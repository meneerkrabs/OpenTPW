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
			if ( level.CellTool.Waiting is { } line && ReferenceEquals( line.Commit.Request, request ) )
				CompleteLine( request );
			else if ( level.WaitingPlacement is { } placement && ReferenceEquals( placement.Request, request ) )
				CompletePlacement( placement );
			else if ( ReferenceEquals( level.WaitingRemoval, request ) )
			{
				level.EndWaiting( request );
				SayOutcome( request, null );
			}
			else
				SayOutcome( request, request.Command is PlaceObjectCommand place ? level.ResolveCommandEntry( place.Entry ) : null );
		}
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
			var result = tool.CompleteWaiting( outcome );
			if ( outcome.RightsRefused )
				SayGuestNote( OpenTpwText.CoopNotGranted );
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
	/// The outcome of a request whose tool takes nothing from it (an orphan, a removal, a line or placement that cannot continue): an accepted placement says Built, an accepted removal or
	/// line nothing (the park shows it); a refusal by the rights says the host has not let this player build (or that only the host does that); a refused placement or line its reason,
	/// as the host's refusals are said, and other refusals of the park nothing, as for the host; a refusal without a record that the host could not take it; an error or a lost answer
	/// that its outcome is not known. A snapshot and the session's end say nothing.
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
					SayGuestNote( level.Commands.Rights is CoopBuildRights rights && rights.Builds( request.Command ) ? OpenTpwText.CoopNotGranted : OpenTpwText.CoopHostOnly );
				else if ( request.Command is PlaceObjectCommand or LayLineCommand or QueueSegmentCommand )
					SayCannotBuild( result.Reason ?? "", entry );
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
