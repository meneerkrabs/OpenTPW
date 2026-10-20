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
/// directly. Every sub-mode follows the original's handler (see the comments at each method); the mouse-to-value rates are the original's, in pixels of the original's 1024-wide screen.
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
	/// <summary>Bank and incline range: ±3.1416 (<c>0x40490e38</c>).</summary>
	public const float TiltLimit = 3.1416f;

	private readonly CoasterBuilder builder;
	private readonly Func<int, int, bool> isFreeCell;
	private readonly int station;
	private readonly Func<ParkCommand, ParkCommandResult> issue;
	private DragState? drag;
	private int? cutStart;
	private int cutVersion;
	private (int Version, CoasterToolButton Button, bool Loop)? areaKey;
	private IReadOnlyList<(int X, int Y)> area = [];

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
	private CoasterBuilder.NodeEdit? BeginEdit( int node, bool includeNext = false ) =>
		issue( new BeginCoasterNodeEditCommand( station, node, includeNext ) ).Value as CoasterBuilder.NodeEdit;

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
	// [BIN:TPW-EXE:0x004980D1 loop button] the panel enables the loop button (control 0x20) only when the mask has bit 2 and the ride's level byte (thing +0x50, the upgrade level 0x4DF8F0 raises) is at least 1 (0x4980D6), and the refresh 0x4987F0 hides it at level 0 (0x49880F); the descriptor's flag 0x80 is set for every coaster (the loader ORs 0xA0), so the level is the only gate; OpenTPW adds that the coaster has a loop pylon control
	public CoasterToolButton EnabledMask
	{
		get
		{
			var mask = (uint)0x1fc;
			if ( !PairPending )
				mask |= 1;
			if ( LoopUnlocked )
				mask |= 2;
			if ( IsFinished )
				mask &= ~3u;
			else
				mask &= ~(uint)CoasterToolButton.Demolish;
			return (CoasterToolButton)mask;
		}
	}

	/// <summary>The loop button exists: the coaster has a loop pylon and its ride is upgraded to level 1 or more (a station without an economy counts as unlocked).</summary>
	public bool LoopUnlocked => builder.Ride.Definition.HasLoopPylon && (builder.UpgradeLevel is not int level || level >= 1);

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
		Select( OpeningButton );
	}

	/// <summary>A click on a button (<c>0x446fa0</c> with param 1): leaves the old sub-mode and starts the new one. A disabled button does nothing.</summary>
	public bool Select( CoasterToolButton button )
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
	// [APPROX:COAST-028] the original picks the pylon model under the pointer (0x443ee0 maps the picked model handle through the table 0x7a4610 to the node, skipping cell descriptors with flag 1); OpenTPW picks by cell and, in a stack, by the pointer's nearness to each piece's projected position (the HUD) — evidence needed: the engine's model pick under the pointer
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
	}

	/// <summary>
	/// The press of the left button on a pylon (<c>0x446300</c>): the move, height, rotate and tilt buttons pick it up and start their drag from the pointer position <paramref name="mouse"/>.
	/// Returns whether a drag started. The stack and demolish buttons act on the release.
	/// </summary>
	public bool Press( int node, NVector2 mouse, int cellX, int cellY )
	{
		if ( drag != null || node < 0 || node >= Track.Nodes.Count || !builder.CanEdit( node ) )
			return false;
		var picked = Track.Nodes[node];
		var capabilities = builder.Track.Definition.Pylon( picked.LoopPylon ).Capabilities;
		switch ( Selected )
		{
			case CoasterToolButton.Move:
				// [BIN:TPW-EXE:0x446d50 move press] the pylon is picked up (a station node cannot be): its cell and cost are noted, the range around it marked, and the node follows the pointer's cell until the release (0x442f40)
				if ( BeginEdit( node, includeNext: true ) is not { } move )
					return false;
				drag = new DragState { Kind = Selected, Edit = move, Origin = mouse, Moves = true, Cell = (picked.X, picked.Y) };
				return true;
			case CoasterToolButton.Height:
			{
				// [BIN:TPW-EXE:0x441a00 height drag] needs the loft clip (pylon flag 0x20); the range is [0, (fMinHeight − h0)/(h1 − h0)] up to what the stack's top leaves of fMaxHeight; the money settles on the release
				if ( (capabilities & CoasterPylonCapabilities.Loft) == 0 || BeginEdit( node ) is not { } edit )
					return false;
				var (low, high) = Track.LoftRange( builder.Site, node );
				drag = new DragState { Kind = Selected, Edit = edit, Origin = mouse, StartValue = picked.T, Low = low, High = high };
				DragCost = 0;
				return true;
			}
			case CoasterToolButton.Rotate:
			{
				// [BIN:TPW-EXE:0x441e80 rotate drag] needs the rotate clip (pylon flag 0x40); the yaw (+0xE0) stays within the automatic yaw ± fMaxRotnAngleDeviance; the node takes flag 1
				if ( (capabilities & CoasterPylonCapabilities.Rotate) == 0 || BeginEdit( node ) is not { } edit )
					return false;
				var automatic = Track.AutomaticYaw( builder.Site, node );
				var range = Track.Definition.Pylon( picked.LoopPylon ).MaxRotationDeviance;
				var start = (picked.Flags & CoasterNodeFlags.ManualYaw) != 0 ? picked.Yaw : automatic;
				drag = new DragState { Kind = Selected, Edit = edit, Origin = mouse, StartValue = start, Low = automatic - range, High = automatic + range };
				return true;
			}
			case CoasterToolButton.Tilt:
				return BeginTilt( node, picked, capabilities, mouse );
			default:
				return false;
		}
	}

	private bool BeginTilt( int node, CoasterNode picked, CoasterPylonCapabilities capabilities, NVector2 mouse )
	{
		if ( (picked.Flags & CoasterNodeFlags.Loop) != 0 )
		{
			// [BIN:TPW-EXE:0x441cd0 loop tilt] a loop node needs pylon flag 0x200; the loop rotation (+0xF8) of the first pylon of a pair first loses 1 when above 0.5 and stays in [−0.35, 0.35], any other in [0.15, 0.85]
			if ( (capabilities & CoasterPylonCapabilities.LoopTilt) == 0 || BeginEdit( node ) is not { } loop )
				return false;
			var first = (picked.Flags & CoasterNodeFlags.LoopFirst) != 0;
			var value = picked.LoopRotate;
			if ( first && value > 0.5f )
			{
				value -= 1f;
				Edit( CoasterNodeField.LoopRotate, value );
			}
			drag = new DragState { Kind = Selected, Edit = loop, Origin = mouse, StartValue = value, Low = first ? -0.35f : 0.15f, High = first ? 0.35f : 0.85f, Loop = true };
			return true;
		}
		// [BIN:TPW-EXE:0x442010 tilt drag] the bank (+0xD8, flag 4) needs pylon flag 0x100 and the incline (+0xDC, flag 2) flag 0x80, both in [−π, π]; the drag is turned to the camera's view of the pylon
		var bank = (capabilities & CoasterPylonCapabilities.Bank) != 0;
		var incline = (capabilities & CoasterPylonCapabilities.Incline) != 0;
		if ( !bank && !incline || BeginEdit( node ) is not { } edit )
			return false;
		drag = new DragState
		{
			Kind = Selected, Edit = edit, Origin = mouse, StartValue = picked.Bank, StartSecond = picked.Incline,
			Low = bank ? -TiltLimit : picked.Bank, High = bank ? TiltLimit : picked.Bank,
			LowSecond = incline ? -TiltLimit : picked.Incline, HighSecond = incline ? TiltLimit : picked.Incline
		};
		return true;
	}

	/// <summary>
	/// The pointer moves while a drag runs (<c>0x443340</c>): the value follows the distance from the drag's origin; a clamped value moves the origin so the pointer must come back to change it again.
	/// <paramref name="cameraYaw"/> turns a tilt drag to the view (the original's camera yaw 0x790a38, in the node-yaw convention of the knot directions), <paramref name="cell"/> is the hovered cell of a move.
	/// </summary>
	public void Drag( NVector2 mouse, float cameraYaw, (int X, int Y)? cell )
	{
		if ( drag is not { } state )
			return;
		var edit = state.Edit;
		// the node the drag holds is gone (a pylon was deleted meanwhile): the drag ends where it stands
		if ( edit.Index >= Track.Nodes.Count || !builder.CanEdit( edit.Index ) )
		{
			drag = null;
			DragCost = null;
			return;
		}
		switch ( state.Kind )
		{
			case CoasterToolButton.Height:
			{
				var (value, clamped) = Clamp( state.StartValue + (state.Origin.Y - mouse.Y) * HeightRate, state.Low, state.High );
				if ( clamped )
					state.Origin.Y = (value - state.StartValue) / HeightRate + mouse.Y;
				if ( value != Track.Nodes[edit.Index].T )
					Edit( CoasterNodeField.Loft, value );
				var cost = CoasterTrack.RoundUp( Track.NodeCost( edit.Index, chargeUnit: false ) ) - edit.Refund;
				DragCost = cost;
				break;
			}
			case CoasterToolButton.Rotate:
			{
				var (value, clamped) = Clamp( state.StartValue + (state.Origin.X - mouse.X) * AngleRate, state.Low, state.High );
				if ( clamped )
					state.Origin.X = (value - state.StartValue) / AngleRate + mouse.X;
				if ( (Track.Nodes[edit.Index].Flags & CoasterNodeFlags.ManualYaw) == 0 || value != Track.Nodes[edit.Index].Yaw )
					Edit( CoasterNodeField.Yaw, value );
				break;
			}
			case CoasterToolButton.Tilt when state.Loop:
			{
				var (value, clamped) = Clamp( state.StartValue + (state.Origin.X - mouse.X) * LoopRotateRate, state.Low, state.High );
				if ( clamped )
					state.Origin.X = (value - state.StartValue) / LoopRotateRate + mouse.X;
				if ( value != Track.Nodes[edit.Index].LoopRotate )
					Edit( CoasterNodeField.LoopRotate, value );
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
					if ( value != node.Bank || (node.Flags & CoasterNodeFlags.NoAutoBank) == 0 )
						Edit( CoasterNodeField.Bank, value );
				}
				if ( state.HighSecond > state.LowSecond )
				{
					var value = Clamp( state.StartSecond + incline, state.LowSecond, state.HighSecond ).Value;
					if ( value != Track.Nodes[edit.Index].Incline || (Track.Nodes[edit.Index].Flags & CoasterNodeFlags.ManualIncline) == 0 )
						Edit( CoasterNodeField.Incline, value );
				}
				break;
			}
			case CoasterToolButton.Move when cell is { } hovered && hovered != state.Cell:
				// [BIN:TPW-EXE:0x443340 move drag] the node follows the hovered cell (0x442f40), the validator marks it invalid (flag 0x4000) when the cell fails, and the cost shown is the new cost against the noted one
				state.Cell = hovered;
				state.MoveResult = Edit( CoasterNodeField.Move, 0, hovered.X, hovered.Y ).Value is CoasterPlaceResult moved ? moved : CoasterPlaceResult.NotEditable;
				DragCost = CoasterTrack.RoundUp( Track.NodeCost( edit.Index, chargeUnit: false ) ) - edit.Refund;
				break;
		}
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
	/// <paramref name="node"/> is the pylon under the pointer, <paramref name="cell"/> the hovered cell. Returns what happened.
	/// </summary>
	public CoasterPanelResult Release( int? node, (int X, int Y)? cell )
	{
		if ( drag is { } state )
			return EndDrag( state, cell );
		switch ( Selected )
		{
			case CoasterToolButton.StackUp:
			case CoasterToolButton.StackDown:
				// [BIN:TPW-EXE:0x4423f0 stack up] on the release over a pylon that has a neighbour that way in its cell's stack the two exchange places (0x4424e0); the track is rebuilt
				return node is int swapped && issue( new SwapCoasterStackCommand( station, swapped, Selected == CoasterToolButton.StackUp ) ).Accepted ? CoasterPanelResult.Swapped : CoasterPanelResult.Nothing;
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
		// a pending range ends with any edit of the track in between (a deleted pylon would leave its end pointing at another node)
		if ( cutStart is not int first || cutVersion != Track.Version )
		{
			cutStart = index;
			cutVersion = Track.Version;
			return CoasterPanelResult.CutStarted;
		}
		cutStart = null;
		var result = issue( new CutCoasterCommand( station, first, index ) ).Value is CoasterTrack.CutResult cut ? cut : CoasterTrack.CutResult.Refused;
		if ( result == CoasterTrack.CutResult.Refused )
			return CoasterPanelResult.Nothing;
		// the cut reselects the build button (0x446740 ends in 0x444360(1), 0x4445b0(1)) and recomputes the enabled buttons
		Selected = CoasterToolButton.None;
		Select( CoasterToolButton.Build );
		return result == CoasterTrack.CutResult.Reopened ? CoasterPanelResult.Reopened : CoasterPanelResult.Cut;
	}

	private CoasterPanelResult EndDrag( DragState state, (int X, int Y)? cell )
	{
		drag = null;
		DragCost = null;
		var edit = state.Edit;
		// [EXT:online-coop-commands] the release's writes are commands: a move's last step, then the end that settles the money (or only fits the track again)
		switch ( state.Kind )
		{
			case CoasterToolButton.Height:
				issue( new EndCoasterNodeEditCommand( station, CoasterEditEnd.Settle ) );
				return CoasterPanelResult.Edited;
			case CoasterToolButton.Move:
				// [BIN:TPW-EXE:0x445a70 move release] a release over no cell puts the node back; over a cell the node is dropped there (0x442f40) and the refund and charge of the node and, on a finished track, the one after it settle (0x447830, 0x4d0190, 0x4d01f0)
				if ( cell is { } target )
				{
					if ( target != state.Cell )
						Edit( CoasterNodeField.Move, 0, target.X, target.Y );
				}
				else if ( edit.Index < Track.Nodes.Count && (Track.Nodes[edit.Index].X, Track.Nodes[edit.Index].Y) != (edit.Start.X, edit.Start.Y) )
					Edit( CoasterNodeField.Move, 0, edit.Start.X, edit.Start.Y );
				issue( new EndCoasterNodeEditCommand( station, CoasterEditEnd.SettleIfMoved ) );
				return CoasterPanelResult.Edited;
			default:
				issue( new EndCoasterNodeEditCommand( station, CoasterEditEnd.Finish ) );
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
	/// through 0x4455c0 whenever the mask changes).
	/// </summary>
	public void Refresh()
	{
		if ( Selected == CoasterToolButton.None || IsEnabled( Selected ) )
			return;
		drag = null;
		Select( OpeningButton );
	}

	/// <summary>The tool ends: a drag in progress is released where it stands.</summary>
	public void Close()
	{
		if ( drag is { } state )
			EndDrag( state, null );
		cutStart = null;
		Cursor = null;
		builder.LoopMode = false;
	}
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
	Reopened
}
