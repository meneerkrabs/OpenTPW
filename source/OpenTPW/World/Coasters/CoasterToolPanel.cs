using NVector2 = System.Numerics.Vector2;

namespace OpenTPW;

/// <summary>The nine buttons of the coaster tool's slide-out panel (<c>0x4982a0</c> → <c>0x446fa0</c>), by the bit each one sets in the original's selection mask (<c>0x79c638</c>).</summary>
[Flags]
public enum CoasterToolButton : uint
{
	None = 0,
	/// <summary>Control 0x1f: build standard pylons.</summary>
	Build = 1,
	/// <summary>Control 0x20: build a pair of loop pylons.</summary>
	Loop = 2,
	/// <summary>Control 0x1b: pick a pylon up and drop it on another cell.</summary>
	Move = 4,
	/// <summary>Control 0x19: drag a pylon's bank and incline (a loop pylon's loop rotation).</summary>
	Tilt = 8,
	/// <summary>Control 0x1a: drag a pylon's height.</summary>
	Height = 0x10,
	/// <summary>Control 0x1c: drag a pylon's rotation.</summary>
	Rotate = 0x20,
	/// <summary>Control 0x21: cut a range of the circuit out.</summary>
	Demolish = 0x40,
	/// <summary>Control 0x1e: move a piece of a stacked pylon up.</summary>
	StackUp = 0x80,
	/// <summary>Control 0x1d: move a piece of a stacked pylon down.</summary>
	StackDown = 0x100
}

/// <summary>What the build cursor shows: the cell under the pointer, what the validator says, the cost a pylon there would add and whether it is the closing target.</summary>
public readonly record struct CoasterCursor( int X, int Y, CoasterPlaceResult Result, long Cost, bool Closing );

/// <summary>
/// The coaster tool's panel (COAST-028), headless: which of the nine buttons is selected and enabled, and what a press, a drag and a release do in each sub-mode. The HUD feeds it the pointer; tests drive it
/// directly. Every sub-mode follows the original's handler (see the comments at each method); the mouse-to-value rates are the original's, per device pixel of the render resolution (0x42a760: the pointer is a whole pixel of the resolution the game renders at).
/// </summary>
public sealed class CoasterToolPanel
{
	/// <summary>Height drag rate: loft parameter per pixel (<c>_DAT_0079c6a8</c>, 0.005).</summary>
	// [BIN:TPW-EXE:0x441a00 height drag] 0x3ba3d70a = 0.005 stored to 0x79c6a8, the vertical drag rate; the start value is node +0xE8 and the value is the start plus (start mouse Y − mouse Y) × rate (0x4433a7..0x4434d5)
	public const float HeightRate = 0.005f;
	/// <summary>Rotate and tilt drag rate: radians per pixel (<c>0x3ca3d70a</c> = 0.02).</summary>
	// [BIN:TPW-EXE:0x441e80 rotate drag] 0x3ca3d70a = 0.02 stored to the horizontal rate 0x79c6ec; 0x442010 stores it to both rates
	public const float AngleRate = 0.02f;
	/// <summary>Loop-rotation drag rate: loop rotation per pixel (<c>0x3c23d70a</c> = 0.01).</summary>
	// [BIN:TPW-EXE:0x441cd0 loop tilt drag] 0x3c23d70a = 0.01 to the horizontal rate; range [0.15, 0.85], or [−0.35, 0.35] on the first pylon of a pair whose value above 0.5 first loses 1
	public const float LoopRotateRate = 0.01f;
	/// <summary>Bank and incline range: ±3.1416 (<c>0x40490e38</c>, <see cref="CoasterBuilder.TiltLimit"/>).</summary>
	public const float TiltLimit = CoasterBuilder.TiltLimit;

	private readonly CoasterBuilder builder;
	private readonly Func<int, int, bool> isFreeCell;
	private readonly int station;
	private readonly Func<ParkCommand, ParkCommandResult> issue;
	private DragState? drag;
	private int? cutStart;
	private int cutVersion;
	private (int Version, CoasterToolButton Button, bool Loop)? areaKey;
	private IReadOnlyList<(int X, int Y)> area = [];
	/// <summary>[EXT:online-coop-commands] The first and the last number of the drags this guest's panel took from its queue (<see cref="CoasterRequestQueue.NewDrag"/>, C3c-2); 0 for none.</summary>
	private int firstDrag, lastDrag;
	/// <summary>[EXT:online-coop-commands] A guest's press whose <see cref="BeginCoasterNodeEditCommand"/> waits for the host: the drag starts from its outcome (C3c-2).</summary>
	private PendingPress? pendingPress;
	/// <summary>[EXT:online-coop-commands] A guest's click, Backspace, swap or cut that waits for the host (C3c-2).</summary>
	private CoasterGuestAction? waitingAction;
	/// <summary>Set by <see cref="Close"/>: a closed panel's outcomes change no tool.</summary>
	private bool closed;
	/// <summary>
	/// [EXT:online-coop-undo] The records of the pylons this panel's clicks placed in a shared park, oldest first (C5-3): its Backspace takes back the newest, and pops it once taken. A panel keeps
	/// them for its own run of the tool, as single player's Backspace takes back only what is on the open chain.
	/// </summary>
	private readonly List<long> ownPylons = new();

	/// <summary>
	/// [EXT:online-coop-commands] A guest's queue to the host (the level's, C3c-2): the panel's commands go through it as requests, and their outcomes come back to the panel; null for the host and in
	/// single player, whose commands apply at once.
	/// </summary>
	internal CoasterRequestQueue? GuestRequests { get; init; }
	/// <summary>[EXT:online-coop-commands] Whether the panel's commands go to the host now: a guest's, while its session lasts.</summary>
	internal Func<bool>? SendsRequests { get; init; }
	/// <summary>[EXT:online-coop-commands] The edit this panel's player holds in the level (<see cref="CoasterEdits"/>), read when a guest's press comes back: its range is the one the press's record took.</summary>
	internal Func<CoasterEdits.Held?>? HeldEdit { get; init; }
	/// <summary>[EXT:online-coop-commands] A guest's completing click came back to this open panel: the level finishes the tool, or closes it when the claim went meanwhile (C3c-2).</summary>
	internal Action? Completed { get; init; }
	/// <summary>
	/// [EXT:online-coop-undo] Whether the park is a shared one (C5-3): Backspace is then a take-back of the player's own last pylon, the host's and every guest's alike, which the host's journal
	/// decides; single player's takes the last pylon off.
	/// </summary>
	internal Func<bool>? InSharedPark { get; init; }

	/// <summary>[EXT:online-coop-undo] Whether the panel's Backspace takes back its player's own last pylon (C5-3, <see cref="InSharedPark"/>).</summary>
	internal bool TakesBack => InSharedPark?.Invoke() == true;

	/// <summary>[EXT:online-coop-undo] The records of the panel's own pylons its Backspace can still take back, oldest first (tests, C5-3).</summary>
	internal IReadOnlyList<long> OwnPylonRecords => ownPylons;

	/// <summary>[EXT:online-coop-undo] The host's click placed a pylon in a shared park: the record its Backspace takes back (C5-3; a guest's comes back with its click's outcome).</summary>
	internal void PlacedOwnPylon( long record ) => ownPylons.Add( record );

	/// <summary>[EXT:online-coop-commands] Whether the panel's commands go to the host as requests (a guest's).</summary>
	private bool Guest => GuestRequests != null && SendsRequests?.Invoke() == true;

	/// <summary>[EXT:online-coop-commands] Whether the panel's commands go to the host as requests (a guest's, C3c-2): its click and Backspace go through the level's send methods.</summary>
	internal bool SendsToHost => Guest;

	/// <summary>[EXT:online-coop-commands] Whether a guest's press waits for the host (C3c-2): its release is kept for the drag its outcome starts.</summary>
	public bool PressWaits => pendingPress != null;

	/// <summary>[EXT:online-coop-commands] Whether <paramref name="drag"/> is a drag of this guest's panel (C3c-2).</summary>
	internal bool Numbered( int drag ) => firstDrag > 0 && drag >= firstDrag && drag <= lastDrag;

	/// <summary>
	/// [EXT:online-coop-commands] Whether a guest's request of the panel waits for the host (C3c-2): a click, a Backspace, a swap or a cut, or a drag's press. The panel takes no button, press, click
	/// or Backspace meanwhile, and the HUD's press made meanwhile is no click once it is back.
	/// </summary>
	public bool IsWaiting => waitingAction != null || pendingPress != null;

	/// <param name="builder">The builder of the station's track.</param>
	/// <param name="isFreeCell">Whether a cell holds nothing else (no path, queue, ride or scenery): the placement area marks only such cells. Null: every cell is free.</param>
	/// <param name="station">The attraction id of the station, which the panel's commands name.</param>
	/// <param name="issue">[EXT:online-coop-commands] Hands each of the panel's writes to the park's command funnel (<see cref="CoasterPanelCommand"/>): a drag's press, each of its steps and its release,
	/// a stack swap and a cut. The selection, the cursor and the drag's pointer stay the panel's.</param>
	public CoasterToolPanel( CoasterBuilder builder, Func<int, int, bool>? isFreeCell, int station, Func<ParkCommand, ParkCommandResult> issue )
	{
		this.builder = builder ?? throw new ArgumentNullException( nameof( builder ) );
		this.isFreeCell = isFreeCell ?? ((_, _) => true);
		this.station = station;
		this.issue = issue ?? throw new ArgumentNullException( nameof( issue ) );
		Open();
	}

	/// <summary>The press of a drag on <paramref name="node"/>: the edit the command began, or null when it refused.</summary>
	private CoasterBuilder.NodeEdit? BeginEdit( int node, CoasterDrag kind ) =>
		issue( new BeginCoasterNodeEditCommand( station, node, kind ) ).Value as CoasterBuilder.NodeEdit;

	/// <summary>One step of the drag (no money moves): the command's value (a move's verdict).</summary>
	private ParkCommandResult Edit( CoasterNodeField field, float value, int x = 0, int y = 0 ) => issue( new EditCoasterNodeCommand( station, field, value, x, y ) );

	public CoasterBuilder Builder => builder;
	public CoasterTrack Track => builder.Track;
	/// <summary>The selected button (selection mask 0x79c638).</summary>
	public CoasterToolButton Selected { get; private set; }
	/// <summary>The cursor of the build buttons, null when no cell is hovered or another button is selected.</summary>
	public CoasterCursor? Cursor { get; private set; }
	/// <summary>Whether a drag (height, rotate, tilt, move) is under way.</summary>
	public bool IsDragging => drag != null;
	/// <summary>The node of the drag, the end of a pending cut, or null.</summary>
	public int? DraggedNode => drag?.Edit.Index;
	/// <summary>The first end of a cut the demolish button waits to complete (<c>0x79c6dc</c>), or null.</summary>
	public int? CutStart => cutStart;
	/// <summary>
	/// The links the tool marks now, or null: the demolish range between the first end and the hovered pylon while a cut waits for its second end (<see cref="HoverCut"/>), or the links around the
	/// pylon of a move drag (blue).
	/// </summary>
	public CoasterRangeMark? RangeMark
	{
		get
		{
			if ( drag is { Kind: CoasterToolButton.Move } moving )
				return MoveRange( moving.Edit.Index );
			return Selected == CoasterToolButton.Demolish && cutStart != null && cutVersion == Track.Version ? cutPreview : null;
		}
	}

	private CoasterRangeMark? cutPreview;
	private int? cutHovered;
	private bool cutHoverKnown;

	/// <summary>The moved pylon's links: the nodes from the one before it up to it, or to the one after it.</summary>
	// [BIN:TPW-EXE:0x446d50 move marks] the press of the move button marks [prev, next) with the variant 3 (blue) where prev is the node's previous node (itself when none) and next its next node (itself when none); 0x445a70 clears them
	private CoasterRangeMark MoveRange( int node )
	{
		var last = Track.Nodes.Count - 1;
		return new CoasterRangeMark( node > 0 ? node - 1 : node, node < last ? node + 1 : node, CoasterTrackMesh.CursorMarker );
	}

	/// <summary>
	/// The pointer hovers the pylon <paramref name="node"/> (null: none) while the demolish button waits for its second end (<c>0x443340</c>): the range between the first end and the hovered pylon
	/// is marked for the erase marker, recomputed when the hovered pylon changes.
	/// </summary>
	// [BIN:TPW-EXE:0x443340 demolish preview] with the cut pending (0x80) and the hit changed, the hovered pylon (0x443ee0) gives the range: none clears it; the first end itself takes its neighbours (the previous node when it has none of its own: itself; a loop pair widens by its partner); another pylon takes the range between it and the first end, ordinal 0 standing for the last node, a loop pair's first widening to the node before it and its second to the node after; 0x444040 clears the old marks and 0x444090 sets 0x1000000 on [low, high)
	public void HoverCut( int? node )
	{
		if ( Selected != CoasterToolButton.Demolish || cutStart is not int first || cutVersion != Track.Version )
		{
			cutPreview = null;
			cutHoverKnown = false;
			return;
		}
		if ( cutHoverKnown && cutHovered == node )
			return;
		cutHoverKnown = true;
		cutHovered = node;
		cutPreview = CutRange( first, node );
	}

	private CoasterRangeMark? CutRange( int first, int? hovered )
	{
		var nodes = Track.Nodes;
		var last = nodes.Count - 1;
		if ( hovered is not int hover || hover < 0 || hover > last || first < 0 || first > last )
			return null;
		bool IsLoop( int index ) => (nodes[index].Flags & CoasterNodeFlags.Loop) != 0;
		bool IsFirstOfPair( int index ) => IsLoop( index ) && (nodes[index].Flags & CoasterNodeFlags.LoopFirst) != 0;
		int low, high;
		if ( hover == first )
		{
			if ( nodes[first].IsStation )
				return null;
			int? previous = first > 0 ? first - 1 : null, next = first < last ? first + 1 : null;
			if ( !IsLoop( first ) )
				(low, high) = (previous ?? first, next ?? first);
			else if ( !IsFirstOfPair( first ) )
			{
				low = previous is int before && before > 0 ? before - 1 : previous ?? first;
				high = next ?? first;
			}
			else
			{
				low = previous ?? first;
				high = next is int after ? (after < last ? after + 1 : after) : first;
			}
		}
		else
		{
			var near = first;
			var far = hover;
			if ( far == 0 )
				far = last;
			else if ( near == 0 )
				near = last;
			(low, high) = far < near ? (far, near) : (near, far);
			if ( IsFirstOfPair( low ) && low > 0 )
				low--;
			if ( IsLoop( high ) && !IsFirstOfPair( high ) && high < last )
				high++;
		}
		return new CoasterRangeMark( low, high, CoasterTrackMesh.EraseMarker );
	}

	/// <summary>The cost change a drag shows in the HUD's cost text (<c>0x52fba0</c>), or null.</summary>
	public long? DragCost { get; private set; }

	/// <summary>Whether the selected original tool mode exposes the HUD cost: build/move tools, or an active height drag in the edit mode.</summary>
	// [BIN:TPW-EXE:0x498337 coaster cost modes] Build/Loop call 0x497D20 and Move calls 0x497E10 (tool mode 3); other buttons call 0x497BC0 (mode 8). Cash refresh 0x4A0CEE gates mode 8 on 0x447990, bit 0 of 0x79C658, set/cleared by height handler 0x441A00; rotate/tilt use other bits.
	public bool ShowsActionCost => IsBuilding || Selected == CoasterToolButton.Move || drag?.Kind == CoasterToolButton.Height;

	/// <summary>The track is complete and not cut open (controller flags 1 set, 2 clear): the build buttons are off and the demolish button is on.</summary>
	private bool IsFinished => Track.IsComplete && !Track.IsCut;

	/// <summary>
	/// The enabled buttons (<c>0x445910</c>): the seven edit buttons always; build unless a loop pair waits for its second pylon; loop when the coaster has a loop pylon; a finished circuit
	/// turns the two build buttons off and any other track the demolish button.
	/// </summary>
	// [BIN:TPW-EXE:0x445910 enabled buttons] the mask starts at 0x1fc; bit 1 (build) is added unless the last node is the first of a loop pair (0x444c00 / node flag 0x80), bit 2 (loop) with definition flag 0x80 and 0x200 with 0x100; then a complete track whose circuit is not cut open (controller flags &1 set, &2 clear) clears bits 1 and 2, any other track clears bit 0x40
	// [BIN:TPW-EXE:0x004980D1 loop button] the panel enables the loop button (control 0x20) only when the mask has bit 2 and the ride's level byte (thing +0x50, the upgrade level 0x4DF8F0 raises) is at least 1 (0x4980D6), and the refresh 0x4987F0 hides it at level 0 (0x49880F); the descriptor's flag 0x80 is set for every coaster (the loader ORs 0xA0), so the level is the only gate (a coaster without a loop pylon shows the button too, and its selection only picks the build mode, CoasterBuilder.LoopUnlocked)
	public CoasterToolButton EnabledMask
	{
		get
		{
			var mask = (uint)0x1fc;
			if ( !PairPending )
				mask |= 1;
			if ( LoopButtonShown )
				mask |= 2;
			if ( IsFinished )
				mask &= ~3u;
			else
				mask &= ~(uint)CoasterToolButton.Demolish;
			return (CoasterToolButton)mask;
		}
	}

	/// <summary>The loop button is shown and enabled: the ride is upgraded to level 1 or more (a station without an economy counts as unlocked), as the original's panel does whether or not the coaster has a loop pylon.</summary>
	public bool LoopButtonShown => builder.LoopButtonShown;

	/// <summary>The loop mode can start: <see cref="LoopButtonShown"/> and the coaster has a loop pylon control (the definition's flag 2, <see cref="CoasterBuilder.LoopUnlocked"/>).</summary>
	public bool LoopUnlocked => builder.LoopUnlocked;

	/// <summary>Whether the chain endpoint is the first pylon of a loop pair: leaving loop mode does not remove the pending second pylon.</summary>
	public bool PairPending => builder.LoopPairPending;

	/// <summary>The opening selection, keeping an edit button available when a pending pair's loop control is locked or absent.</summary>
	private CoasterToolButton OpeningButton => IsFinished ? CoasterToolButton.Height
		: IsEnabled( CoasterToolButton.Build ) ? CoasterToolButton.Build
		: IsEnabled( CoasterToolButton.Loop ) ? CoasterToolButton.Loop : CoasterToolButton.Height;

	public bool IsEnabled( CoasterToolButton button ) => (EnabledMask & button) != 0;

	/// <summary>
	/// The tool opens: build for an unfinished circuit (the loop button while a pair waits), the height button for a finished one (<c>0x4455c0</c>).
	/// </summary>
	// [BIN:TPW-EXE:0x4455c0 initial selection] a complete track that is not cut open selects button 0x10 (height), any other track button 1 (build), or button 2 (loop) while a loop pair is pending
	public void Open()
	{
		drag = null;
		cutStart = null;
		DragCost = null;
		Selected = CoasterToolButton.None;
		builder.LoopMode = false;
		SelectNow( OpeningButton );
	}

	/// <summary>
	/// A click on a button (<c>0x446fa0</c> with param 1): leaves the old sub-mode and starts the new one. A disabled button does nothing, nor [EXT:online-coop-commands] any while a guest's request
	/// waits (C3c-2): a click's outcome then finds the tool state its click left.
	/// </summary>
	public bool Select( CoasterToolButton button ) => !IsWaiting && SelectNow( button );

	private bool SelectNow( CoasterToolButton button )
	{
		if ( button == CoasterToolButton.None || !IsEnabled( button ) )
			return false;
		EndDragWithoutCommit();
		cutStart = null;
		Selected = button;
		Cursor = null;
		switch ( button )
		{
			case CoasterToolButton.Build:
				// leaving loop mode with the pair untouched cancels it (0x444c80); a placed first pylon keeps its place
				builder.LoopMode = false;
				break;
			case CoasterToolButton.Loop:
				// 0x446fa0 -> 0x447135 -> 0x442d10 resumes the endpoint before 0x4445b0(2) starts or preserves loop mode.
				builder.StartOrResumeLoopPair();
				break;
			default:
				builder.LoopMode = false;
				break;
		}
		return true;
	}

	/// <summary>Whether the selected button builds pylons (a click on a cell places one, the cursor follows the pointer).</summary>
	public bool IsBuilding => Selected is CoasterToolButton.Build or CoasterToolButton.Loop;

	// ---- The build cursor and the placement area ----------------------------------------------------------

	/// <summary>
	/// The pointer hovers cell (x, y) (<c>0x435d50</c>): in the build modes the cursor shows what a pylon there would do, a closing target when the cell holds the chain start. Other modes show nothing.
	/// </summary>
	// [BIN:TPW-EXE:0x435d50 build cursor] a hovered cell within 300 units of the last node fills the spare cursor node (flag 0x8000, plus 0x4000 when the validator 0x434740 refuses the cell); the chain start marks the closing flag 8 instead; the cost preview is 0x447830
	// [BIN:TPW-EXE:0x435e2a build cursor reach] (10x − X)² + (10y − Z)² of the hovered cell's corner against the reference node's position (+0x4c/+0x54) is compared with 90000 (0x6fdeec); a farther cell returns at once (0x435e37 → 0x4360b8), so the cursor node keeps its last fill, closing target included
	public void Hover( int x, int y )
	{
		if ( !IsBuilding )
		{
			Cursor = null;
			return;
		}
		if ( Track.Nodes.Count > 0 && !WithinReach( Track.NodePosition( builder.Site, Track.Nodes[Track.LastIndex] ), x, y ) )
			return;
		if ( Track.IsChainStart( x, y ) && (!Track.IsComplete || Track.IsCut) )
		{
			// [EXT:coaster-completion-click] the closing target shows what its click answers: a pylon marked invalid elsewhere refuses the closing as well
			var closing = Track.IsClosed && !Track.IsCut ? builder.CheckComplete() : builder.CheckClose();
			Cursor = new CoasterCursor( x, y, closing, 0, true );
			return;
		}
		var check = builder.Check( x, y );
		Cursor = new CoasterCursor( x, y, check, CoasterTrack.Truncate( builder.PreviewCost( x, y ) ), false );
	}

	/// <summary>The pointer left the park.</summary>
	public void HoverNothing() => Cursor = null;

	/// <summary>The cursor's reach (<c>0x435d50</c>): cell (x, y)'s corner lies within 300 units of the reference node's position in X and Z.</summary>
	internal static bool WithinReach( System.Numerics.Vector3 reference, int x, int y )
	{
		double dx = (float)(x * 10.0) - reference.X, dz = (float)(y * 10.0) - reference.Z;
		return dx * dx + dz * dz <= 90000.0;
	}

	/// <summary>
	/// The cells the build buttons mark yellow: the validator-passing, empty cells in the box around the last node (<c>0x434f10</c>, drawn by <c>0x5234d0</c>). Empty while another button is selected.
	/// </summary>
	// [BIN:TPW-EXE:0x434f10 placement area] the box around the reference node (the last node, the node before the cut) has the half size trunc(sqrt(pylon +0xa0)) + 1 = the maximum place distance + 1 cells, clamped to the map; its upper edges are exclusive (0x5240c0..0x524470)
	// [BIN:TPW-EXE:0x5243a6 area cell test] a cell is marked (face 0x11f) when the validator 0x434740 passes it, its cell flag 0x40 is clear, the object on it (or the parent of a type 0xc or 0x11 object) is not a ride (0x19) and has none of the types 0xb, 0xd, 0x10, 0x12, 0x19, the cell's own type is none of 9, 0xa, 3, 4, 7, 0x1e and it is not a type 1 cell with flag 0x20
	// [BIN:TPW-EXE:0x5243a6 area cell test] the level supplies the original's cell test (Level.IsCoasterAreaCell): cell flag 0x40, the second layer's track tiles and fence posts, the cell types 3, 4, 7, 9, 10 and 0x1e, a path with flag 0x20
	public IReadOnlyList<(int X, int Y)> PlacementArea
	{
		get
		{
			if ( !IsBuilding || Track.Nodes.Count < CoasterTrack.StationNodeCount || Track.IsClosed || (Track.IsComplete && !Track.IsCut) )
				return [];
			var key = (Track.Version, Selected, builder.LoopMode);
			if ( areaKey == key )
				return area;
			areaKey = key;
			var site = builder.Site;
			var reference = Track.Nodes[Track.LastIndex];
			var pylon = Track.ControlAfter( reference, builder.LoopMode );
			var half = (int)MathF.Truncate( MathF.Sqrt( pylon.MaxPlaceDistanceSquared ) ) + 1;
			int minX = Math.Max( reference.X - half, 0 ), minY = Math.Max( reference.Y - half, 0 );
			int maxX = Math.Min( reference.X + half, site.Width - 1 ), maxY = Math.Min( reference.Y + half, site.Height - 1 );
			var cells = new List<(int X, int Y)>();
			for ( var y = minY; y < maxY; y++ )
			{
				for ( var x = minX; x < maxX; x++ )
				{
					if ( builder.Check( x, y ) == CoasterPlaceResult.Ok && isFreeCell( x, y ) )
						cells.Add( (x, y) );
				}
			}
			return area = cells;
		}
	}

	// ---- Picking ------------------------------------------------------------------------------------------

	/// <summary>The pylon nodes standing on cell (x, y), lowest stack piece first (<c>0x443ee0</c> finds the pylon model under the pointer; the HUD chooses among a stack's pieces).</summary>
	// [APPROX:COAST-028] the HUD picks the pylon whose drawn faces the cursor ray meets first (Level.PickCoasterPylon, as 0x443ee0 maps the picked model handle through the table 0x7a4610 to the node); a stack's pieces are not tested as a group (0x447790, 0x4477d0), and a coaster without drawn pylon models (no game data) is picked by its cell and, in a stack, by the pointer's nearness to each piece's projected position — evidence needed: the group test's use for stacked pylons
	public IReadOnlyList<int> NodesAt( int x, int y ) => Enumerable.Range( 0, Track.Nodes.Count )
		.Where( index => !Track.Nodes[index].IsPylonless && Track.Nodes[index].X == x && Track.Nodes[index].Y == y )
		.OrderBy( index => Track.Nodes[index].StackIndex ).ToList();

	// ---- Press, drag, release -----------------------------------------------------------------------------

	private sealed class DragState
	{
		public required CoasterToolButton Kind;
		public required CoasterBuilder.NodeEdit Edit;
		/// <summary>The mouse position the drag counts from (the original's 0x79c630/634; moves with the pointer when a value is clamped).</summary>
		public NVector2 Origin;
		public float StartValue;
		public float StartSecond;
		public float Low, High;
		public float LowSecond, HighSecond;
		public bool Moves;
		public bool Loop;
		public (int X, int Y) Cell;
		public CoasterPlaceResult MoveResult;
		/// <summary>[EXT:online-coop-commands] A guest's drag's number in its queue (0: the host's, whose steps apply at once).</summary>
		public int Number;
		/// <summary>[EXT:online-coop-commands] The last value a guest's drag queued for each field: a step goes only when the value changed.</summary>
		public Dictionary<CoasterNodeField, float> Last = new();
	}

	/// <summary>[EXT:online-coop-commands] A guest's press whose begin waits for the host, and what the pointer did meanwhile (C3c-2).</summary>
	private sealed class PendingPress
	{
		public required int Number;
		public required CoasterToolButton Kind;
		public required CoasterDrag Drag;
		public required int Node;
		public required NVector2 Origin;
		public (NVector2 Mouse, float Yaw, (int X, int Y)? Cell)? Moved;
		public bool Released;
		public (int X, int Y)? ReleaseCell;
	}

	/// <summary>
	/// The press of the left button on a pylon (<c>0x446300</c>): the move, height, rotate and tilt buttons pick it up and start their drag from the pointer position <paramref name="mouse"/>.
	/// Returns whether a drag started. The stack and demolish buttons act on the release. [EXT:online-coop-commands] A guest's press goes to the host first (C3c-2): the drag starts from its
	/// outcome, from this press's pointer, and what the pointer did meanwhile follows.
	/// </summary>
	public bool Press( int node, NVector2 mouse, int cellX, int cellY )
	{
		if ( drag != null || IsWaiting || node < 0 || node >= Track.Nodes.Count || !builder.CanEdit( node ) )
			return false;
		var picked = Track.Nodes[node];
		var loop = (picked.Flags & CoasterNodeFlags.Loop) != 0;
		CoasterDrag? kind = Selected switch
		{
			CoasterToolButton.Move => CoasterDrag.Move,
			CoasterToolButton.Height => CoasterDrag.Height,
			CoasterToolButton.Rotate => CoasterDrag.Rotate,
			CoasterToolButton.Tilt => loop ? CoasterDrag.LoopTilt : CoasterDrag.Tilt,
			_ => null
		};
		// the pylon's controls and the drag's range are the builder's (CoasterBuilder.DragRange), which the press's command checks the same way
		if ( kind is not { } dragKind || builder.DragRange( node, dragKind ) is not { } range )
			return false;
		if ( Guest )
		{
			var number = lastDrag = GuestRequests!.NewDrag();
			if ( firstDrag == 0 )
				firstDrag = number;
			pendingPress = new PendingPress { Number = number, Kind = Selected, Drag = dragKind, Node = node, Origin = mouse };
			GuestRequests!.Enqueue( new BeginCoasterNodeEditCommand( station, node, dragKind ), number, outcome => PressAnswered( number, outcome ) );
			return true;
		}
		if ( BeginEdit( node, dragKind ) is not { } edit )
			return false;
		StartDrag( Selected, dragKind, edit, range, mouse, 0 );
		return true;
	}

	/// <summary>
	/// [EXT:online-coop-commands] A guest's press came back (C3c-2): accepted, the drag starts from the press's pointer, against the pylon as its record left it and the range it took, and what the
	/// pointer did meanwhile follows (its last move, its release); refused, the press did nothing, as a press on a pylon that takes no drag.
	/// </summary>
	private void PressAnswered( int number, CoasterOutcome outcome )
	{
		if ( pendingPress is not { } press || press.Number != number )
			return;
		pendingPress = null;
		if ( outcome.Result is { RightsRefused: true } || outcome.NotTaken || outcome.Unknown )
			Report( CoasterGuestAction.Press, outcome, open: !closed );
		if ( closed || outcome.Result is not { Accepted: true, Value: CoasterBuilder.NodeEdit edit } || edit.Index >= Track.Nodes.Count )
			return;
		var range = HeldEdit?.Invoke()?.Range ?? builder.DragRange( edit.Index, press.Drag );
		if ( range is not { } held )
			return;
		StartDrag( press.Kind, press.Drag, edit, held, press.Origin, number );
		if ( press.Moved is { } moved )
			Drag( moved.Mouse, moved.Yaw, moved.Cell );
		if ( press.Released && drag is { } state )
			EndDrag( state, press.ReleaseCell );
	}

	/// <summary>The drag's state from its edit: the pylon's values as they stand now, the range, the pointer it counts from; <paramref name="number"/> is a guest's drag's number (0: the host's).</summary>
	private void StartDrag( CoasterToolButton button, CoasterDrag kind, CoasterBuilder.NodeEdit edit, CoasterDragRange range, NVector2 mouse, int number )
	{
		var node = edit.Index;
		var picked = Track.Nodes[node];
		switch ( kind )
		{
			case CoasterDrag.Move:
				// [BIN:TPW-EXE:0x446d50 move press] the pylon is picked up (a station node cannot be): its cell and cost are noted, the range around it marked, and the node follows the pointer's cell until the release (0x442f40)
				drag = new DragState { Kind = button, Edit = edit, Origin = mouse, Moves = true, Cell = (picked.X, picked.Y), Number = number };
				return;
			case CoasterDrag.Height:
				// [BIN:TPW-EXE:0x441a00 height drag] needs the loft clip (pylon flag 0x20); the range is [0, (fMinHeight − h0)/(h1 − h0)] up to what the stack's top leaves of fMaxHeight; the money settles on the release
				drag = new DragState { Kind = button, Edit = edit, Origin = mouse, StartValue = picked.T, Low = range.Low, High = range.High, Number = number };
				DragCost = 0;
				return;
			case CoasterDrag.Rotate:
			{
				// [BIN:TPW-EXE:0x441e80 rotate drag] needs the rotate clip (pylon flag 0x40); the yaw (+0xE0) stays within the automatic yaw ± fMaxRotnAngleDeviance; the node takes flag 1
				var start = (picked.Flags & CoasterNodeFlags.ManualYaw) != 0 ? picked.Yaw : Track.AutomaticYaw( builder.Site, node );
				drag = new DragState { Kind = button, Edit = edit, Origin = mouse, StartValue = start, Low = range.Low, High = range.High, Number = number };
				return;
			}
			case CoasterDrag.LoopTilt:
			{
				// [BIN:TPW-EXE:0x441cd0 loop tilt] a loop node needs pylon flag 0x200; the loop rotation (+0xF8) of the first pylon of a pair first loses 1 when above 0.5 (the press's command writes it) and stays in [−0.35, 0.35], any other in [0.15, 0.85]
				var value = picked.LoopRotate;
				if ( (picked.Flags & CoasterNodeFlags.LoopFirst) != 0 && value > 0.5f )
					value -= 1f;
				drag = new DragState { Kind = button, Edit = edit, Origin = mouse, StartValue = value, Low = range.Low, High = range.High, Loop = true, Number = number };
				return;
			}
			default:
				// [BIN:TPW-EXE:0x442010 tilt drag] the bank (+0xD8, flag 4) needs pylon flag 0x100 and the incline (+0xDC, flag 2) flag 0x80, both in [−π, π]; the drag is turned to the camera's view of the pylon
				drag = new DragState
				{
					Kind = button, Edit = edit, Origin = mouse, StartValue = picked.Bank, StartSecond = picked.Incline,
					Low = range.Low, High = range.High, LowSecond = range.LowSecond, HighSecond = range.HighSecond, Number = number
				};
				return;
		}
	}

	/// <summary>
	/// The pointer moves while a drag runs (<c>0x443340</c>): the value follows the distance from the drag's origin; a clamped value moves the origin so the pointer must come back to change it again.
	/// <paramref name="cameraYaw"/> turns a tilt drag to the view (the original's camera yaw 0x790a38, in the node-yaw convention of the knot directions), <paramref name="cell"/> is the hovered cell of a move.
	/// [EXT:online-coop-commands] While a guest's press waits, the pointer's last move is kept for the drag its outcome starts; a guest's drag queues each value for its field, the latest only (C3c-2).
	/// </summary>
	public void Drag( NVector2 mouse, float cameraYaw, (int X, int Y)? cell )
	{
		if ( pendingPress is { } press )
		{
			// the pointer after the press's release is no part of its drag
			if ( !press.Released )
				press.Moved = (mouse, cameraYaw, cell);
			return;
		}
		if ( drag is not { } state )
			return;
		var edit = state.Edit;
		// the node the drag holds is gone (a pylon was deleted meanwhile): the drag ends where it stands, and so does its edit
		if ( edit.Index >= Track.Nodes.Count || !builder.CanEdit( edit.Index ) )
		{
			drag = null;
			DragCost = null;
			EndEdit( state );
			return;
		}
		switch ( state.Kind )
		{
			case CoasterToolButton.Height:
			{
				var (value, clamped) = Clamp( state.StartValue + (state.Origin.Y - mouse.Y) * HeightRate, state.Low, state.High );
				if ( clamped )
					state.Origin.Y = (value - state.StartValue) / HeightRate + mouse.Y;
				if ( Differs( state, CoasterNodeField.Loft, value, Track.Nodes[edit.Index].T ) )
					Step( state, CoasterNodeField.Loft, value );
				var cost = CoasterTrack.RoundUp( Track.NodeCost( edit.Index, chargeUnit: false ) ) - edit.Refund;
				DragCost = cost;
				break;
			}
			case CoasterToolButton.Rotate:
			{
				var (value, clamped) = Clamp( state.StartValue + (state.Origin.X - mouse.X) * AngleRate, state.Low, state.High );
				if ( clamped )
					state.Origin.X = (value - state.StartValue) / AngleRate + mouse.X;
				if ( state.Number > 0 ? Differs( state, CoasterNodeField.Yaw, value, float.NaN )
					: (Track.Nodes[edit.Index].Flags & CoasterNodeFlags.ManualYaw) == 0 || value != Track.Nodes[edit.Index].Yaw )
					Step( state, CoasterNodeField.Yaw, value );
				break;
			}
			case CoasterToolButton.Tilt when state.Loop:
			{
				var (value, clamped) = Clamp( state.StartValue + (state.Origin.X - mouse.X) * LoopRotateRate, state.Low, state.High );
				if ( clamped )
					state.Origin.X = (value - state.StartValue) / LoopRotateRate + mouse.X;
				if ( Differs( state, CoasterNodeField.LoopRotate, value, Track.Nodes[edit.Index].LoopRotate ) )
					Step( state, CoasterNodeField.LoopRotate, value );
				break;
			}
			case CoasterToolButton.Tilt:
			{
				// the drag vector is turned by the camera's yaw against the pylon's (0x4433a7: local_8 = cos·dy + sin·dx, local_c = cos·dx − sin·dy)
				var dx = (state.Origin.X - mouse.X) * AngleRate;
				var dy = (state.Origin.Y - mouse.Y) * AngleRate;
				var angle = cameraYaw - EffectiveYaw( edit.Index );
				float sin = MathF.Sin( angle ), cos = MathF.Cos( angle );
				var incline = cos * dy + sin * dx;
				var bank = cos * dx - sin * dy;
				var node = Track.Nodes[edit.Index];
				if ( state.High > state.Low )
				{
					var value = Clamp( state.StartValue + bank, state.Low, state.High ).Value;
					if ( state.Number > 0 ? Differs( state, CoasterNodeField.Bank, value, float.NaN ) : value != node.Bank || (node.Flags & CoasterNodeFlags.NoAutoBank) == 0 )
						Step( state, CoasterNodeField.Bank, value );
				}
				if ( state.HighSecond > state.LowSecond )
				{
					var value = Clamp( state.StartSecond + incline, state.LowSecond, state.HighSecond ).Value;
					if ( state.Number > 0 ? Differs( state, CoasterNodeField.Incline, value, float.NaN )
						: value != Track.Nodes[edit.Index].Incline || (Track.Nodes[edit.Index].Flags & CoasterNodeFlags.ManualIncline) == 0 )
						Step( state, CoasterNodeField.Incline, value );
				}
				break;
			}
			case CoasterToolButton.Move when cell is { } hovered && hovered != state.Cell:
				// [BIN:TPW-EXE:0x443340 move drag] the node follows the hovered cell (0x442f40), the validator marks it invalid (flag 0x4000) when the cell fails, and the cost shown is the new cost against the noted one
				state.Cell = hovered;
				state.MoveResult = Step( state, CoasterNodeField.Move, 0, hovered.X, hovered.Y )?.Value is CoasterPlaceResult moved ? moved : CoasterPlaceResult.NotEditable;
				DragCost = CoasterTrack.RoundUp( Track.NodeCost( edit.Index, chargeUnit: false ) ) - edit.Refund;
				break;
		}
	}

	/// <summary>
	/// Whether a drag's new <paramref name="value"/> for <paramref name="field"/> is a change: against the pylon's <paramref name="applied"/> value for the host's drag, and [EXT:online-coop-commands]
	/// against the last value a guest's drag queued, or, before its first, the applied one (NaN: always a change).
	/// </summary>
	private static bool Differs( DragState state, CoasterNodeField field, float value, float applied ) =>
		state.Number > 0 && state.Last.TryGetValue( field, out var last ) ? value != last : float.IsNaN( applied ) || value != applied;

	/// <summary>
	/// One step of <paramref name="state"/>'s drag: the host's applies at once (its result), [EXT:online-coop-commands] a guest's is queued for its field (C3c-2: the latest value goes, at most every
	/// <see cref="CoasterRequestQueue.StepSeconds"/>), with no result here.
	/// </summary>
	private ParkCommandResult? Step( DragState state, CoasterNodeField field, float value, int x = 0, int y = 0 )
	{
		if ( state.Number == 0 )
			return Edit( field, value, x, y );
		GuestRequests!.Step( state.Number, new EditCoasterNodeCommand( station, field, value, x, y ) );
		state.Last[field] = value;
		return null;
	}

	/// <summary>The end of <paramref name="state"/>'s edit: the host's applies at once, [EXT:online-coop-commands] a guest's goes behind its drag's steps.</summary>
	private void EndEdit( DragState state )
	{
		if ( state.Number == 0 )
			issue( new EndCoasterNodeEditCommand( station ) );
		else
			GuestRequests!.Enqueue( new EndCoasterNodeEditCommand( station ), state.Number, DragEndAnswered );
	}

	/// <summary>
	/// [EXT:online-coop-commands] A guest's drag end came back (C3c-2): refused by the rights while the panel is open (the grant or the claim went), the drag's edit stays held at the host, so the
	/// tool ends as for a refused click (the claim's release then ends the edit); a closed panel's says nothing, its close said why or was the player's own.
	/// </summary>
	private void DragEndAnswered( CoasterOutcome outcome )
	{
		if ( !closed && outcome.Result is { RightsRefused: true } )
			Report( CoasterGuestAction.DragEnd, outcome, open: true );
	}

	/// <summary>The yaw a node is turned to: the hand-set one, or the heading of its knot direction.</summary>
	private float EffectiveYaw( int index )
	{
		var node = Track.Nodes[index];
		return (node.Flags & CoasterNodeFlags.ManualYaw) != 0 ? node.Yaw : Track.AutomaticYaw( builder.Site, index );
	}

	private static (float Value, bool Clamped) Clamp( float value, float low, float high )
	{
		if ( value < low )
			return (low, true);
		if ( value > high )
			return (high, true);
		return (value, false);
	}

	/// <summary>
	/// The release of the left button (<c>0x446300</c> with param 0, <c>0x445a70</c>, <c>0x441a00</c>): ends a drag (the height and move edits settle their money), swaps a stack piece, or completes a cut.
	/// <paramref name="node"/> is the pylon under the pointer, <paramref name="cell"/> the hovered cell. Returns what happened. [EXT:online-coop-commands] A guest's release of a press that waits is kept for
	/// the drag its outcome starts, and a guest's swap and cut go to the host and wait (C3c-2): their outcome is reported then.
	/// </summary>
	public CoasterPanelResult Release( int? node, (int X, int Y)? cell )
	{
		if ( pendingPress is { } press )
		{
			// the press's own release: a later one is another gesture's
			if ( !press.Released )
			{
				press.Released = true;
				press.ReleaseCell = cell;
			}
			return CoasterPanelResult.Nothing;
		}
		if ( drag is { } state )
			return EndDrag( state, cell );
		switch ( Selected )
		{
			case CoasterToolButton.StackUp:
			case CoasterToolButton.StackDown:
				// [BIN:TPW-EXE:0x4423f0 stack up] on the release over a pylon that has a neighbour that way in its cell's stack the two exchange places (0x4424e0); the track is rebuilt
				if ( node is not int swapped )
					return CoasterPanelResult.Nothing;
				if ( Guest )
				{
					// [EXT:online-coop-commands] one request of the panel waits at a time: a press held from before the wait swaps nothing now (C3c-2)
					if ( IsWaiting )
						return CoasterPanelResult.Waiting;
					SendAction( CoasterGuestAction.Swap, new SwapCoasterStackCommand( station, swapped, Selected == CoasterToolButton.StackUp ), _ => { } );
					return CoasterPanelResult.Nothing;
				}
				return issue( new SwapCoasterStackCommand( station, swapped, Selected == CoasterToolButton.StackUp ) ).Accepted ? CoasterPanelResult.Swapped : CoasterPanelResult.Nothing;
			case CoasterToolButton.Demolish:
				return ReleaseDemolish( node );
		}
		return CoasterPanelResult.Nothing;
	}

	private CoasterPanelResult ReleaseDemolish( int? node )
	{
		// [BIN:TPW-EXE:0x446740 cut range] the first release over a pylon starts the range (0x79c62c, 0x79c6dc), the second over a pylon completes it; a release over nothing does nothing
		if ( node is not int index || index < 0 || index >= Track.Nodes.Count || Track.Nodes[index].IsPylonless )
			return CoasterPanelResult.Nothing;
		// [EXT:online-coop-commands] one request of a guest's panel waits at a time: a release while it waits, also of a press held from before it, does nothing, and a range started stays (C3c-2)
		if ( Guest && IsWaiting )
			return CoasterPanelResult.Waiting;
		// a pending range ends with any edit of the track in between (a deleted pylon would leave its end pointing at another node)
		if ( cutStart is not int first || cutVersion != Track.Version )
		{
			cutStart = index;
			cutVersion = Track.Version;
			cutPreview = null;
			cutHoverKnown = false;
			return CoasterPanelResult.CutStarted;
		}
		cutStart = null;
		// [EXT:online-coop-commands] a guest's cut goes to the host and its outcome finishes it (C3c-2)
		if ( Guest )
		{
			SendAction( CoasterGuestAction.Cut, new CutCoasterCommand( station, first, index ), outcome =>
			{
				if ( !closed && outcome.Result?.Value is CoasterTrack.CutResult cut && cut != CoasterTrack.CutResult.Refused )
					Reselect();
			} );
			return CoasterPanelResult.Nothing;
		}
		var result = issue( new CutCoasterCommand( station, first, index ) ).Value is CoasterTrack.CutResult done ? done : CoasterTrack.CutResult.Refused;
		if ( result == CoasterTrack.CutResult.Refused )
			return CoasterPanelResult.Nothing;
		Reselect();
		return result == CoasterTrack.CutResult.Reopened ? CoasterPanelResult.Reopened : CoasterPanelResult.Cut;
	}

	/// <summary>The cut reselects the build button (0x446740 ends in 0x444360(1), 0x4445b0(1)) and recomputes the enabled buttons.</summary>
	private void Reselect()
	{
		Selected = CoasterToolButton.None;
		SelectNow( CoasterToolButton.Build );
	}

	private CoasterPanelResult EndDrag( DragState state, (int X, int Y)? cell )
	{
		drag = null;
		DragCost = null;
		var edit = state.Edit;
		// [EXT:online-coop-commands] the release's writes are commands: a move's last step, then the end, which settles the money of a height or move drag (or only fits the track again); a guest's go
		// behind its drag's steps (C3c-2)
		switch ( state.Kind )
		{
			case CoasterToolButton.Height:
				EndEdit( state );
				return CoasterPanelResult.Edited;
			case CoasterToolButton.Move:
				// [BIN:TPW-EXE:0x445a70 move release] a release over no cell puts the node back; over a cell the node is dropped there (0x442f40) and the refund and charge of the node and, on a finished track, the one after it settle (0x447830, 0x4d0190, 0x4d01f0)
				if ( cell is { } target )
				{
					if ( target != state.Cell )
						Step( state, CoasterNodeField.Move, 0, target.X, target.Y );
				}
				else if ( state.Number > 0 ? state.Cell != (edit.Start.X, edit.Start.Y)
					: edit.Index < Track.Nodes.Count && (Track.Nodes[edit.Index].X, Track.Nodes[edit.Index].Y) != (edit.Start.X, edit.Start.Y) )
					Step( state, CoasterNodeField.Move, 0, edit.Start.X, edit.Start.Y );
				EndEdit( state );
				return CoasterPanelResult.Edited;
			default:
				EndEdit( state );
				return CoasterPanelResult.Edited;
		}
	}

	/// <summary>A button was clicked while a drag ran: the drag ends as a release over no cell would end it (a move goes back, a height edit settles its money).</summary>
	private void EndDragWithoutCommit()
	{
		if ( drag is { } state )
			EndDrag( state, null );
	}

	/// <summary>
	/// Keeps the selection on an enabled button: after the circuit closes the build buttons are off and the height button is the one the tool opens on a finished track (the original reselects
	/// through 0x4455c0 whenever the mask changes). [EXT:online-coop-commands] Not while a guest's request waits: its outcome comes first.
	/// </summary>
	public void Refresh()
	{
		if ( IsWaiting || Selected == CoasterToolButton.None || IsEnabled( Selected ) )
			return;
		// [EXT:online-coop-commands] a drag the new selection ends is released where it stands (Select), so its edit ends too
		SelectNow( OpeningButton );
	}

	/// <summary>
	/// The tool ends: a drag in progress is released where it stands, as a release over no cell (a move goes back, a height edit settles its money). [EXT:online-coop-commands] A guest's drag
	/// too (C3c-2): its values not sent yet still go, before its end, so it ends at its latest values as the host's does, a move put back first. A guest's press that did not go is dropped, one
	/// that went is ended once it began. A click, Backspace, swap or cut that waits comes back as a message only.
	/// </summary>
	public void Close()
	{
		closed = true;
		if ( pendingPress is { } press )
		{
			pendingPress = null;
			if ( GuestRequests!.PressUnsent( press.Number ) )
				GuestRequests.DropUnsent( press.Number );
			else
				GuestRequests.Enqueue( new EndCoasterNodeEditCommand( station ), press.Number );
		}
		if ( drag is { } state )
			EndDrag( state, null );
		cutStart = null;
		Cursor = null;
		builder.LoopMode = false;
	}

	// ---- [EXT:online-coop-commands] A guest's click, Backspace, swap and cut (C3c-2) -----------------------

	/// <summary>
	/// [EXT:online-coop-commands] A guest's build click (C3c-2): <paramref name="command"/> goes to the host and the panel waits. Its outcome takes the tool state the placement left
	/// (<see cref="CoasterClickDone.Next"/>), reports the click's result, and a completing click finishes the tool (<see cref="Completed"/>). False when a request waits already or a drag runs.
	/// </summary>
	internal bool SendClick( CoasterClickCommand command, bool closing )
	{
		ArgumentNullException.ThrowIfNull( command );
		if ( IsWaiting || drag != null || !Guest )
			return false;
		SendAction( CoasterGuestAction.Click, command, outcome =>
		{
			if ( !closed && outcome.Result?.Value is CoasterClickDone done )
			{
				builder.SetToolState( done.Next );
				// [EXT:online-coop-undo] a pylon it placed is the player's own, named by its record, which the panel's Backspace takes back (C5-3)
				if ( done.Placed && outcome.Result.Accepted && outcome.Record is { } record )
					ownPylons.Add( record );
				if ( done.Completed )
					Completed?.Invoke();
			}
		}, closing );
		return true;
	}

	/// <summary>
	/// [EXT:online-coop-commands] A guest's Backspace (C3c-2): [EXT:online-coop-undo] a take-back of its newest own pylon goes to the host and the panel waits (C5-3); it pops that pylon once
	/// taken, and a refusal keeps it. It waits while a request of the panel waits or a drag runs, and sends nothing when the panel placed no pylon of its own.
	/// </summary>
	internal CoasterBackspace SendBackspace()
	{
		if ( IsWaiting || drag != null || !Guest )
			return CoasterBackspace.Waits;
		if ( ownPylons.Count == 0 )
			return CoasterBackspace.NotOwn;
		var record = ownPylons[^1];
		SendAction( CoasterGuestAction.Backspace, TakeBackOf( record ), outcome =>
		{
			if ( outcome.Result is { Accepted: true } )
				ownPylons.Remove( record );
		} );
		return CoasterBackspace.Sent;
	}

	/// <summary>
	/// [EXT:online-coop-undo] The host's Backspace in a shared park (C5-3): a take-back of its newest own pylon, issued at once, which its journal decides as a guest's; the pylon is popped once
	/// taken and kept on a refusal. Its result, or null when the panel placed no pylon of its own (nothing goes).
	/// </summary>
	internal ParkCommandResult? TakeBackOwnPylon()
	{
		if ( ownPylons.Count == 0 )
			return null;
		var record = ownPylons[^1];
		var result = issue( TakeBackOf( record ) );
		if ( result.Accepted )
			ownPylons.Remove( record );
		return result;
	}

	/// <summary>[EXT:online-coop-undo] The take-back of the pylon of <paramref name="record"/>: a request names it by its record alone, and the host fills its ride (C5-3).</summary>
	private static TakeBackCommand TakeBackOf( long record ) => new( false, record, -1, -1, -1 );

	/// <summary>
	/// Queues a guest's waited action: the panel waits for it, and its outcome runs <paramref name="answered"/>, then is reported, with whether the panel was still open when it came back (a closed
	/// panel's outcome is only said).
	/// </summary>
	private void SendAction( CoasterGuestAction action, ParkCommand command, Action<CoasterOutcome> answered, bool closing = false )
	{
		waitingAction = action;
		GuestRequests!.Enqueue( command, 0, outcome =>
		{
			var open = !closed;
			if ( waitingAction == action )
				waitingAction = null;
			answered( outcome );
			Report( action, outcome, open, closing );
		} );
	}

	/// <summary>Reports a guest's outcome for the HUD (<see cref="CoasterRequestQueue.TakeReports"/>).</summary>
	private void Report( CoasterGuestAction action, CoasterOutcome outcome, bool open, bool closing = false ) =>
		GuestRequests!.Reports.Add( new CoasterGuestReport( action, outcome, open, closing ) );
}

/// <summary>[EXT:online-coop-undo] What a guest's Backspace in the coaster tool did (C5-3, <see cref="CoasterToolPanel.SendBackspace"/>).</summary>
internal enum CoasterBackspace
{
	/// <summary>No coaster tool is open: nothing happened.</summary>
	None,
	/// <summary>The take-back of the player's own last pylon went to the host.</summary>
	Sent,
	/// <summary>A request of the panel waits, or a drag runs: nothing went, and the player is asked to wait.</summary>
	Waits,
	/// <summary>The panel placed no pylon of its own: nothing went.</summary>
	NotOwn
}

/// <summary>[EXT:online-coop-commands] A guest's coaster tool action that waits for the host (C3c-2).</summary>
internal enum CoasterGuestAction
{
	/// <summary>A build click (<see cref="CoasterClickCommand"/>).</summary>
	Click,
	/// <summary>Backspace: [EXT:online-coop-undo] the take-back of the player's own last pylon (<see cref="TakeBackCommand"/>, C5-3).</summary>
	Backspace,
	/// <summary>A stack swap (<see cref="SwapCoasterStackCommand"/>).</summary>
	Swap,
	/// <summary>The cut's second release (<see cref="CutCoasterCommand"/>).</summary>
	Cut,
	/// <summary>A drag's press (<see cref="BeginCoasterNodeEditCommand"/>).</summary>
	Press,
	/// <summary>A drag's end (<see cref="EndCoasterNodeEditCommand"/>), reported only when the rights refused it while its panel was open.</summary>
	DragEnd,
	/// <summary>
	/// A drag the queue gave up (<see cref="CoasterRequestQueue.Complete"/>): its end, or its last value of a field, was lost (a park that takes no commands now, a seat lost, an outcome not known
	/// again and again). Its panel ends, and the claim's release ends the edit the host may hold.
	/// </summary>
	DragLost
}

/// <summary>
/// [EXT:online-coop-commands] What a guest's coaster tool action came back with, for the HUD to say (C3c-2): the action, its outcome, whether its panel was still the open one when it came back (a
/// closed panel's outcome ends no tool), and for a click whether it was on the chain start.
/// </summary>
internal sealed record CoasterGuestReport( CoasterGuestAction Action, CoasterOutcome Outcome, bool Open, bool Closing )
{
	/// <summary>The drag of a <see cref="CoasterGuestAction.DragLost"/> report (<see cref="CoasterRequestQueue.NewDrag"/>).</summary>
	public int Drag { get; init; }
}

/// <summary>What a release in the coaster panel did.</summary>
public enum CoasterPanelResult
{
	Nothing,
	/// <summary>A drag ended (height, rotate, tilt, move).</summary>
	Edited,
	/// <summary>Two stack pieces exchanged places.</summary>
	Swapped,
	/// <summary>The first end of a cut was chosen.</summary>
	CutStarted,
	/// <summary>The cut opened the circuit in the middle.</summary>
	Cut,
	/// <summary>The cut reached the end: the track is unfinished again.</summary>
	Reopened,
	/// <summary>[EXT:online-coop-commands] A guest's swap or cut while another request of its panel waits for the host: nothing went, and the player is asked to wait (C3c-2).</summary>
	Waiting
}
