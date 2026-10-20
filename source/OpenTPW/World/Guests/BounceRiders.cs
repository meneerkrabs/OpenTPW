using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// One bouncing rider as the original's script pass draws it: the guest's own body sprite at the node of its bounce slot, in the run animation
/// requested while the ride is up and stand requested while broken. A prepared pose displays the persistent player's last emitted frame.
/// </summary>
/// <param name="Visitor">The rider's guest id.</param>
/// <param name="AttractionId">The attraction the rider must be riding.</param>
/// <param name="Feet">The sprite's point in engine space (Z up).</param>
/// <param name="Sector">The sprite's heading byte: the 45 degree sector the body faces before the camera's yaw is added (<see cref="GuestSpriteAtlas.SelectOriginalDirection(int, float, int)"/>).</param>
/// <param name="Broken">The ride is broken down (script variable VAR_BROKEN set): request stand instead of run; old output survives until the next scheduler emission.</param>
/// <param name="Step">The animation step of the body in its slot.</param>
/// <param name="Slot">The sprite slot of the body's template, or -1 for the bounce's own (the run slot, the stand slot while broken); a walking body names its template's slot (<see cref="WalkerBodies.SlotOf"/>).</param>
internal readonly record struct BounceRiderPose( int Visitor, int AttractionId, SVector3 Feet, int Sector, bool Broken, int Step, int Slot = -1, bool Visible = true, bool UsesSpriteState = false );

/// <summary>A producer of bouncing riders (the flat rides' bounce tables).</summary>
internal interface IBounceRiderSource
{
	/// <summary>Adds the pose of every rider currently bouncing, keyed by guest id.</summary>
	void Collect( Dictionary<int, BounceRiderPose> poses );

	/// <summary>The pose of one rider, if the source poses it now.</summary>
	bool TryGetPose( int guest, out BounceRiderPose pose )
	{
		var poses = new Dictionary<int, BounceRiderPose>();
		Collect( poses );
		return poses.TryGetValue( guest, out pose );
	}
}

/// <summary>
/// The rule the original's per-frame script pass (tp.exe <c>0x00557AB0</c>) draws a rider of a BOUNCE table by (BOUNCESETNODE, BOUNCESETBASE, BOUNCE; docs/RSE-VM.md, "Bounce opcodes"). GPU-free.
/// </summary>
/// <remarks>
/// For every occupied slot the pass looks the slot's node up in the ride's model (class 0x800, the number BOUNCE recorded), reads the node's position and the ride's position, and calls the visitor's sprite
/// routine <c>0x004F9E60</c> with (node x, height, node z, heading byte, template). While VAR_BROKEN is 0 the template is 2 (the run animation, slot 2) and the height is the larger of the node's height and
/// <c>(visitor mod 7 + 12) x sin(phase) + ride height + base height</c>, the phase running through half a turn (0 to 3.125 radians) once a second of the ride's speed-scaled time; the heading byte is the
/// rider's bounce time in half seconds. While the ride is broken the template is 3 (standing), the height the node's and the heading byte the visitor mod 7.
/// </remarks>
public static class BounceRiders
{
	/// <summary>The entries of the sine table (the two creation calls <c>0x00449EB0</c> and <c>0x0044A1D0</c> pass 0x1000 to the builder <c>0x004708D0</c>).</summary>
	public const int SineTableSize = 4096;

	/// <summary>The sine table of the original's builder: entry i is the sine of i / 4096 turns, rounded to a float.</summary>
	// [BIN:TPW-EXE:0x004708D0 sine table] mask n - 1 (0x7B0960), index scale n x 1 / (2 pi) as a float (0x7B095C, constants 0x006FEB9C and 0x006FEBA0), entry i = fsin(i / n x 2 pi) stored as a float from 0x7B0968
	private static readonly float[] Sine = Enumerable.Range( 0, SineTableSize ).Select( index => (float)DetMath.Sin( (float)index / SineTableSize * 6.2831854820251465 ) ).ToArray();

	/// <summary>Entry <paramref name="index"/> of the sine table (test seam).</summary>
	internal static float SineAt( int index ) => Sine[index];

	internal static readonly float IndexScale = SineTableSize * 0.15915493667125702f;

	/// <summary>The table entry the bounce reads for a rider that bounced <paramref name="elapsedMilliseconds"/> ago on a script with the speed bias <paramref name="speedBias"/> (script +0xC0).</summary>
	// [BIN:TPW-EXE:0x00557C03 bounce phase] the elapsed time is scaled by 0.8 + 0.005 x the speed word (constants 0x00701008, 0x00701010), truncated, taken modulo 1000 and turned into radians by 0.003125 (0x00701018); the table index is that angle x the index scale, rounded to nearest and masked
	internal static int TableIndex( int elapsedMilliseconds, short speedBias )
	{
		var scaled = (int)((0.8 - speedBias * -0.004999999888241291) * elapsedMilliseconds);
		var angle = (float)((scaled % 1000) * (double)0.003125f);
		var product = (float)(IndexScale * (double)angle);
		return (int)Math.Round( product, MidpointRounding.ToEven ) & (SineTableSize - 1);
	}

	/// <summary>
	/// The height of a bouncing rider in the original's units: the larger of <paramref name="nodeHeight"/> and the bounce above <paramref name="rideHeight"/> plus <paramref name="baseHeight"/>.
	/// </summary>
	// [BIN:TPW-EXE:0x00557C75 bounce height] ((visitor mod 7) - -12.0) (0x0070101C) x table[index] + the ride's height (the matrix word +0x34 of its thing, 0x00557D60) + the base word (script +0x6E); when that is below the node's height (matrix word +0x34 of the node, 0x00435710) the node's height is drawn
	internal static float Height( int visitor, int elapsedMilliseconds, short speedBias, float nodeHeight, float rideHeight, short baseHeight )
	{
		var bounce = (visitor % 7 + 12.0) * Sine[TableIndex( elapsedMilliseconds, speedBias )] + rideHeight + baseHeight;
		return bounce < nodeHeight ? nodeHeight : (float)bounce;
	}

	/// <summary>The sprite's heading byte of a bouncing rider: the bounce time in half seconds (the sprite pass keeps the low three bits).</summary>
	// [BIN:TPW-EXE:0x00557CF1 bounce heading] the elapsed time x 0x10624DD3 >> 37, which is elapsed / 500, goes to the sprite routine as the heading (+0xC0 of the sprite, read with & 7 by 0x00542010)
	internal static int BouncingSector( int elapsedMilliseconds ) => elapsedMilliseconds / 500 & 7;

	/// <summary>The sprite's heading byte of a rider on a broken-down ride: the visitor modulo 7.</summary>
	// [BIN:TPW-EXE:0x00557BE1 broken ride] with script variable 7 (VAR_BROKEN, through the variable buffer +0x1C) not 0 the sprite routine gets template 3, the node's position and the visitor modulo 7 (0x00557BE9..0x00557BFE)
	internal static int BrokenSector( int visitor ) => visitor % 7;


	/// <summary>
	/// The pose of one rider, or null when the rider is not drawn. <paramref name="nodePosition"/> and <paramref name="rideHeight"/> are in engine space (Z up, <see cref="ObjectPlacement.ModelScale"/> engine units
	/// to the original's unit); the height rule runs in the original's units.
	/// </summary>
	internal static BounceRiderPose Pose( int visitor, int attractionId, SVector3 nodePosition, float rideHeightEngine, int elapsedMilliseconds, short speedBias, short baseHeight, bool broken )
	{
		if ( broken )
			return new BounceRiderPose( visitor, attractionId, nodePosition, BrokenSector( visitor ), true, 0 );
		var scale = ObjectPlacement.ModelScale;
		var nodeHeight = nodePosition.Z / scale;
		var height = Height( visitor, elapsedMilliseconds, speedBias, nodeHeight, rideHeightEngine / scale, baseHeight );
		// a rider on its node keeps the node's own engine height (no round trip through the original's units)
		return new BounceRiderPose( visitor, attractionId, new SVector3( nodePosition.X, nodePosition.Y, height <= nodeHeight ? nodePosition.Z : height * scale ), BouncingSector( elapsedMilliseconds ), false, 0 );
	}
}

/// <summary>The bounce tables of every flat ride of the park (<see cref="RideVisitorBridge.CollectBouncing"/>), posed by <see cref="BounceRiders"/>.</summary>
internal sealed class FlatRideBounceRiders : IBounceRiderSource
{
	private const int WalkNodeClass = 0x800;
	private readonly ParkObjects objects;
	private readonly Dictionary<int, BounceRiderPose> prepared = new();
	private readonly Dictionary<int, BounceRiderPose> placed = new();
	private readonly List<(int Guest, int Node, double Start)> scratch = new();
	private readonly List<RideVisitorBridge.WalkerView> walkers = new();

	public FlatRideBounceRiders( ParkObjects objects ) => this.objects = objects;

	/// <summary>Renderer-only read. Preparing once in the host phase prevents kid/costume double requests and hidden-renderer clock loss.</summary>
	public void Collect( Dictionary<int, BounceRiderPose> poses )
	{
		foreach ( var (guest, pose) in prepared )
			poses[guest] = pose;
	}

	public bool TryGetPose( int guest, out BounceRiderPose pose ) => prepared.TryGetValue( guest, out pose );

	/// <summary>Post-substep pose phase. Positioning and template requests are separate from scheduler ticks and guest begin-turn application.</summary>
	internal void Prepare( GuestSimulation guests )
	{
		prepared.Clear();
		foreach ( var ride in objects.Objects )
		{
			if ( ride.IsDeleted )
				continue;
			scratch.Clear();
			Collect( prepared, ride.Runtime, node => ride.GetNodeTransform( node ).Translation, ride.Placement.ModelToEngine.Translation.Z, scratch );
			foreach ( var (guest, _, _) in scratch )
				if ( prepared.TryGetValue( guest, out var pose ) && pose.AttractionId == ride.Runtime.Visitors.AttractionId )
					prepared[guest] = ApplyBodyOutput( guests, pose );
			// WALK follows BOUNCE and consumes the same visitor-owned last emitted frame.
			CollectWalkers( prepared, ride.Runtime, ride.GetNodeTransform, walkers,
				( guest, template ) => guests.RequestRideBody( guest, ride.Runtime.Visitors.AttractionId, (VisitorSpriteTemplate)template ),
				guest => guests.HideRideBody( guest, ride.Runtime.Visitors.AttractionId ) );
		}
		HoldKeptBodies( prepared, placed, guests );
	}

	/// <summary>
	/// A kept body stays where the walk update or the bounce pass last placed it once its entry is gone (WALKGET took it out of the walk table, UNBOUNCE out of the bounce table) while the guest still uses
	/// the object: it is drawn there, with the frames its player runs, until the guest is let off or the body is deleted. <paramref name="placed"/> carries the poses of the previous pass.
	/// </summary>
	// [BIN:TPW-EXE:0x00542010 sprite pass] a sprite is drawn every frame at its stored position (+0x88..+0x90) while it has a frame; the walk update and the bounce pass store it (0x004F9E60 through 0x00475480) only for the entries they pass, and the guest update stores it again only after the state-15 entry has cleared +0x28 (0x004FA087, 0x00501F79), so between the two the body stays where it was last placed
	internal static void HoldKeptBodies( Dictionary<int, BounceRiderPose> prepared, Dictionary<int, BounceRiderPose> placed, GuestSimulation guests )
	{
		foreach ( var (id, pose) in placed )
			if ( !prepared.ContainsKey( id ) && guests.Find( id ) is { } guest && RideHeadRenderer.CanDraw( guest, pose.AttractionId ) && guests.ShowsKeptBody( guest ) )
				prepared[id] = guest.BodySprite.Player!.Output is { } frame ? pose with { Slot = frame.Slot, Step = frame.Frame, Visible = true, UsesSpriteState = true }
					: pose with { Visible = false, UsesSpriteState = true };
		placed.Clear();
		foreach ( var (id, pose) in prepared )
			placed[id] = pose;
	}

	internal static BounceRiderPose ApplyBodyOutput( GuestSimulation guests, BounceRiderPose pose )
	{
		var output = guests.RequestRideBody( pose.Visitor, pose.AttractionId, pose.Broken ? VisitorSpriteTemplate.Stand : VisitorSpriteTemplate.Run );
		return output is { } frame ? pose with { Slot = frame.Slot, Step = frame.Frame, Visible = true, UsesSpriteState = true }
			: pose with { Visible = false, UsesSpriteState = true };
	}

	/// <summary>
	/// The bodies of one ride's walk table: the walk update (tp.exe 0x00557D80) draws every entry it passes, in the pose <see cref="WalkerBodies"/> describes.
	/// <paramref name="nodeWorld"/> is the engine-space matrix of a model node.
	/// </summary>
	// [BIN:TPW-EXE:0x00557D80 walk update drawing] each entry draws its body through 0x005580A0 between the nodes of its state, by the share of the state's time spent, in template 1 (walking) while it walks on or off or waits at the fourth node; on board the head actions draw none and the actions 3, 5 and 6 draw templates 3, 8 and 5 at the second node
	// [BIN:TPW-EXE:0x005580A0 body position] each node's position is read (0x00556B90: the class mask and number's first dummy, its matrix words +0x30..+0x38), and with bit 0 of the entry's last word (+0x1A, set by every shipped script) the height is taken from the nodes, else from the terrain (0x004511A0)
	// [APPROX:TOUR-011] a body whose WALKON leaves bit 0 of its last word clear stands on the terrain in the original (0x004511A0, whose result is not modelled here: the bodies are drawn from the nodes' own heights), while the host pose phase consumes the shared body history on its existing approximate clock
	internal static void CollectWalkers( Dictionary<int, BounceRiderPose> poses, OriginalObjectRuntime runtime, Func<int, System.Numerics.Matrix4x4> nodeWorld, List<RideVisitorBridge.WalkerView> scratch, Func<int, int, VisitorSpriteFrame?>? requestBody = null, Action<int>? hideBody = null )
	{
		if ( runtime.Script is not { } script )
			return;
		scratch.Clear();
		runtime.Visitors.CollectWalkers( scratch );
		if ( scratch.Count == 0 )
			return;
		var now = script.TimeMilliseconds;
		var scale = ObjectPlacement.ModelScale;
		SVector3? Position( int mask, int number ) => runtime.FindDummyNode( mask, number ) is { } node ? nodeWorld( node ).Translation : null;
		// [BIN:TPW-EXE:0x00556B90 node orientation] the node's third matrix row (+0x20..+0x28, the engine's second row after the axes swap), whose x and z give a body on board its heading
		int? Orientation( int number ) => runtime.FindDummyNode( WalkerBodies.WalkClass, number ) is { } node ? WalkerBodies.HeadingOf( nodeWorld( node ).M21, nodeWorld( node ).M22 ) : null;
		foreach ( var walker in scratch )
		{
			var head = walker.Mode == 4;
			var heading = walker.Heading ?? runtime.WalkHeadingOf( WalkerBodies.WalkClass, walker.U1, head ? WalkerBodies.HeadClass : WalkerBodies.WalkClass, walker.U2 ) ?? 0;
			SVector3? feet;
			var template = 1;
			switch ( walker.State )
			{
				case 1:
					feet = Between( Position( WalkerBodies.WalkClass, walker.U1 ), Position( head ? WalkerBodies.HeadClass : WalkerBodies.WalkClass, walker.U2 ), WalkerBodies.BodyShare( WalkerBodies.Share( now, walker.Began, walker.Ends ) ) );
					break;
				case 3:
					feet = Between( Position( head ? WalkerBodies.HeadClass : WalkerBodies.WalkClass, walker.U3 ), Position( WalkerBodies.WalkClass, walker.U4 ), WalkerBodies.BodyShare( WalkerBodies.Share( now, walker.Began, walker.Ends ) ) );
					break;
				case 4:
					feet = Between( Position( WalkerBodies.WalkClass, walker.U4 ), Position( WalkerBodies.WalkClass, walker.U4 ), 0 );
					break;
				case 2:
				{
					var (start, duration, words) = script.WalkFloat;
					var floating = duration != 0 && !(unchecked( start + (uint)duration ) < script.ClockMilliseconds32);
					var hides = WalkerBodies.HidesOnBoard( walker.Mode );
					// [BIN:TPW-EXE:0x00557F4E body on board heading] an action other than 1, 2 and 4 takes its heading from the second node's orientation every frame
					if ( !hides && Orientation( walker.U2 ) is { } oriented )
						heading = oriented;
					if ( floating )
					{
						feet = walker.Mode switch
						{
							4 => Between( Position( WalkerBodies.WalkClass, walker.U1 ), Position( WalkerBodies.HeadClass, walker.U2 ), 1 ),
							1 or 2 => Between( Position( WalkerBodies.WalkClass, walker.U1 ), Position( WalkerBodies.WalkClass, walker.U2 ), 1 ),
							_ => Between( Position( WalkerBodies.WalkClass, walker.U2 ), Position( WalkerBodies.WalkClass, walker.U2 ), 0 )
						};
						if ( feet is { } floated )
						{
							var (dx, dz, up) = WalkerBodies.FloatOffset( walker.Guest, script.ClockMilliseconds32, start, duration, words, (short)script.SpeedBias );
							feet = floated + new SVector3( dx * scale, dz * scale, up * scale );
						}
						heading = WalkerBodies.FloatHeading( walker.Guest, script.ClockMilliseconds32, heading );
						template = 2;
					}
					else
					{
						if ( hides )
						{
							hideBody?.Invoke( walker.Guest );
							poses.Remove( walker.Guest ); // WALK follows BOUNCE: explicit deletion also invalidates any earlier body pose.
							continue;
						}
						if ( WalkerBodies.Template( walker.Mode ) is not (> 0 and var chosen) )
							continue;
						template = chosen;
						feet = Between( Position( WalkerBodies.WalkClass, walker.U2 ), Position( WalkerBodies.WalkClass, walker.U2 ), 0 );
					}
					break;
				}
				default:
					continue;
			}
			if ( feet is not { } point )
				continue;
			var output = requestBody?.Invoke( walker.Guest, template );
			runtime.Visitors.SetWalkHeading( walker.Guest, heading );
			var pose = new BounceRiderPose( walker.Guest, runtime.Visitors.AttractionId, point, WalkerBodies.SectorOf( heading ), false, WalkerBodies.StepOf( now, walker.Began ), WalkerBodies.SlotOf( template ) );
			poses[walker.Guest] = requestBody == null ? pose : output is { } frame
				? pose with { Slot = frame.Slot, Step = frame.Frame, Visible = true, UsesSpriteState = true }
				: pose with { Visible = false, UsesSpriteState = true };
		}
	}

	/// <summary>The body routine's point between two nodes, or null when one is missing (the original logs and leaves its stack words as they were).</summary>
	private static SVector3? Between( SVector3? from, SVector3? to, int share ) => from is { } a && to is { } b ? WalkerBodies.Between( a, b, share ) : null;

	/// <summary>The riders of one ride's bounce table; <paramref name="nodeWorld"/> is the engine position of a model node, <paramref name="rideHeight"/> the engine height of the ride's origin.</summary>
	internal static void Collect( Dictionary<int, BounceRiderPose> poses, OriginalObjectRuntime runtime, Func<int, SVector3> nodeWorld, float rideHeight, List<(int Guest, int Node, double Start)> scratch )
	{
		if ( runtime.Script is not { } script )
			return;
		scratch.Clear();
		runtime.Visitors.CollectBouncing( scratch );
		if ( scratch.Count == 0 )
			return;
		// [BIN:TPW-EXE:0x00557B26 bounce lookup] the node of a slot is the first dummy of class 0x800 with its number (0x0044B220); a slot whose node the model lacks is skipped
		var broken = script.Variables[(int)RideVariables.VAR_BROKEN] != 0;
		foreach ( var (guest, number, start) in scratch )
		{
			if ( runtime.FindDummyNode( WalkNodeClass, number ) is not { } node || node >= runtime.Animator.NodeTransforms.Count )
				continue;
			var elapsed = unchecked( (int)(script.TimeMilliseconds - start) );
			poses[guest] = BounceRiders.Pose( guest, runtime.Visitors.AttractionId, nodeWorld( node ), rideHeight, elapsed, (short)script.SpeedBias, runtime.Visitors.BounceBaseHeight, broken );
		}
	}
}
