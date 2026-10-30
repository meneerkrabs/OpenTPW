using System.Numerics;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>The riders of the coasters as head sources of <see cref="RideHeadRenderer"/>.</summary>
internal sealed class CoasterHeadSources : IHeadSourceProvider
{
	private readonly CoasterRenderer renderer;

	public CoasterHeadSources( CoasterRenderer renderer ) => this.renderer = renderer;

	public void Collect( List<HeadSource> sources, int? onlyAttraction ) => renderer.CollectHeads( sources, onlyAttraction );
}

/// <summary>
/// Draws the park's coasters (presentation only): each coaster's track as its <c>coaster.sam</c> cross section extruded along the curve with the track textures,
/// the pylon models stretched to the node heights, and the train cars posed on the samples. A track still being built is drawn as the curve through its placed
/// nodes and the build cursor. Meshes are rebuilt only when the track (or the cursor) changes; the cars move by matrix and a scrolling texture (a flume's water,
/// a chain lift) moves by rewriting its own few vertices.
/// </summary>
// [BIN:TPW-EXE:0x43eb70 track rebuild] the original rebuilds the track mesh as a dynamic model when the track changes (0x43eb70 from 0x435a30/0x447d30), animates the scrolling textures by shifting the V of the faces of scrolling textures every frame (0x435a30) and poses each car from its sample coordinate (0x43ce20)
// [BIN:TPW-EXE:0x435adf scroll shift] the per-frame tick shifts V by δ = (now − ctl+0x138)·P·0.001 for every polygon of a texture the loader flagged (definition flag 4, set when any fScrollRate is nonzero; texture flag 0x20), P the power factor at controller +0x2c; the stored fScrollRate (texture +0xc) is not read in that loop
// [APPROX:COAST-026] another reader of the stored scroll rate (texture +0xc) outside the controller tick is not excluded — evidence needed: a scan of the generic texture library for readers of texture +0xc, and whether a build preview's shared texture object (wrap bits, 0x575E10) outlives the build menu; the object renderer's models share the same texture cache and are not given the accumulated wrap bits
public sealed class CoasterRenderer : Entity, IFirstFramePreparation
{
	/// <summary>
	/// The marker texture of the pair of knots starting at <paramref name="node"/> and ending in <paramref name="next"/> (the same node for a pair between two tags of one pylon), or null.
	/// </summary>
	internal static int? LinkMarker( IReadOnlyList<CoasterNode> chain, IReadOnlyList<IReadOnlyList<int>> crossings, bool cursorValid, int node, int next )
	{
		// [BIN:TPW-EXE:0x43f4e0 marker links] crossings (+0xb0) give the red marker; a link into the build cursor's spare node (0x2000) the blue one, or red when the validator refused its cell (0x4000)
		if ( node < crossings.Count && crossings[node].Count > 0 )
			return CoasterTrackMesh.RefusedMarker;
		// [BIN:TPW-EXE:0x43f4e0 closing marker] a node's own tag pairs (both nodes the same) take the marker while the node carries 0x800000: blue, or red when it also carries 0x4000 (the closing link is refused)
		if ( next == node && node >= 0 && node < chain.Count && (chain[node].Flags & CoasterNodeFlags.ClosingTarget) != 0 )
			return (chain[node].Flags & CoasterNodeFlags.Invalid) != 0 ? CoasterTrackMesh.RefusedMarker : CoasterTrackMesh.CursorMarker;
		if ( next == node || next < 0 || next >= chain.Count )
			return null;
		if ( (chain[next].Flags & CoasterNodeFlags.Cursor) != 0 )
			return cursorValid ? CoasterTrackMesh.CursorMarker : CoasterTrackMesh.RefusedMarker;
		// [APPROX:COAST-026] a link between two nodes a kept edit left invalid (0x4000) is drawn with the red marker; the original's marker needs 0x800000 or 0x2000 on the next node besides 0x4000 (the drag's moved node), and which of them a released move leaves is not traced — evidence needed: the flags of a moved node after the release 0x445a70
		if ( (chain[node].Flags & CoasterNodeFlags.Invalid) != 0 && (chain[next].Flags & CoasterNodeFlags.Invalid) != 0 )
			return CoasterTrackMesh.RefusedMarker;
		return null;
	}

	/// <summary>The knot ranges (first knot, last knot, closed) the mesh builder draws as separate chains.</summary>
	internal static List<(int From, int To, bool Closed)> ChainSpans( IReadOnlyList<CoasterNode> chain, CoasterKnotPass pass, bool closed )
	{
		var spans = new List<(int, int, bool)>();
		var start = 0;
		for ( var index = 0; index < chain.Count; index++ )
		{
			var last = index == chain.Count - 1;
			var cutEnd = (chain[index].Flags & CoasterNodeFlags.CutEnd) != 0;
			if ( !last && (closed || !cutEnd) )
				continue;
			// [BIN:TPW-EXE:0x43eb70 chain end] the mesh builder's loop runs only while a next node exists: the link ends at the last node's first knot, its own tag pairs are never drawn
			var end = last || (cutEnd && !closed) ? pass.FirstKnot[index] : pass.LastKnot( index );
			if ( index - start >= 1 )
				spans.Add( (pass.FirstKnot[start], end, closed && start == 0 && last) );
			start = index + 1;
		}
		return spans;
	}

	/// <summary>
	/// The scroll phase after a frame (<c>0x435adf..0x435b20</c>): the accumulator gains P·Δt and wraps above 1. A breakdown (power factor 0) stops the scroll.
	/// </summary>
	public static float AdvanceScroll( float phase, float powerFactor, float deltaSeconds )
	{
		var next = phase + powerFactor * deltaSeconds;
		return next - MathF.Floor( next );
	}

	/// <summary>The wheel clip's current frame, read from the live train slot rather than a renderer-owned start time.</summary>
	internal float WheelTick( CoasterRide ride, int trainIndex, int duration )
	{
		var train = ride.Trains[trainIndex];
		return CoasterCarRig.WheelTick( (ride.TimeSeconds - train.WheelAnimationStartSeconds) * 1000.0, train.Speed, duration );
	}

	private readonly Func<IReadOnlyList<CoasterRide>> coasters;
	private readonly Func<SVector2> origin;
	private readonly float scale;
	private readonly Func<CoasterRide, ObjectCatalogEntry?> entryOf;
	private readonly Func<CoasterRide, CoasterPreview?> previewOf;
	private readonly Func<CoasterRide, int> attractionOf;
	private readonly Func<uint>? sceneMilliseconds;
	private readonly List<RideView> views = new();
	private readonly Dictionary<ObjectCatalogEntry, CoasterAssets> assets = new();
	private bool deleted;

	/// <param name="coasters">The coasters to draw.</param>
	/// <param name="origin">Engine position of grid corner (0, 0).</param>
	/// <param name="scale">Engine units per original world unit (0.2).</param>
	/// <param name="entryOf">The catalogue entry (archive) of a coaster's station, or null when it has none.</param>
	/// <param name="previewOf">The build cursor of the coaster the tool edits, or null.</param>
	/// <param name="sceneMilliseconds">The scene's rate-scaled clock in whole milliseconds (<see cref="SceneClock"/>); the scrolling textures follow it, so they run at the game speed and stand still while paused. Null reads the real frame time.</param>
	/// <param name="attractionOf">The attraction id of a coaster's station (0 when it has none), which the riders' heads are matched against.</param>
	public CoasterRenderer( Func<IReadOnlyList<CoasterRide>> coasters, Func<SVector2> origin, float scale, Func<CoasterRide, ObjectCatalogEntry?> entryOf, Func<CoasterRide, CoasterPreview?> previewOf, Func<CoasterRide, int> attractionOf, Func<uint>? sceneMilliseconds = null )
	{
		this.sceneMilliseconds = sceneMilliseconds;
		this.attractionOf = attractionOf;
		this.coasters = coasters;
		this.origin = origin;
		this.scale = scale;
		this.entryOf = entryOf;
		this.previewOf = previewOf;
		Name = "Coasters";
	}

	/// <summary>
	/// The camera seat of a train's camera car in engine axes (the car model's seat node placed on the car), null when the coaster has no drawn
	/// car yet or its car model has no seat.
	/// </summary>
	internal Matrix4x4? TryGetSeat( CoasterRide ride, int train )
	{
		foreach ( var view in views )
		{
			if ( ReferenceEquals( view.Ride, ride ) )
				return view.CameraCarSeat( train );
		}
		return null;
	}

	/// <summary>Triangles the coaster models submit (track, pylons and cars; the view may cull some of them).</summary>
	public int DrawnTriangles { get; private set; }

	/// <summary>Track and pylon models alive across the park's coasters (the cars share the type's models).</summary>
	public int ModelCount => views.Sum( view => view.ModelCount );

	protected override void OnUpdate()
	{
		if ( deleted )
			return;
		var rides = coasters();
		for ( var index = views.Count - 1; index >= 0; index-- )
		{
			var present = false;
			for ( var ride = 0; ride < rides.Count && !present; ride++ )
				present = ReferenceEquals( rides[ride], views[index].Ride );
			if ( present )
				continue;
			var gone = views[index];
			views.RemoveAt( index );
			gone.Dispose();
			ReleaseAssets( gone.Assets );
		}
		var triangles = 0;
		for ( var index = 0; index < rides.Count; index++ )
		{
			var view = ViewOf( rides[index] );
			if ( view == null )
				continue;
			view.Update();
			triangles += view.Triangles;
		}
		DrawnTriangles = triangles;
	}

	/// <summary>
	/// [EXT:park-entry-preparation] Each coaster's view, its type's assets and its track's and cars' models, one coaster a step, as the first update makes them (the ride stands
	/// still until the park's first frame); that update then finds them made, and takes the scroll's clock as a view made in it does. A view whose preparation fails is dropped, so
	/// the first update makes it again and meets the same failure as before.
	/// </summary>
	IEnumerable<bool> IFirstFramePreparation.PrepareFirstFrame()
	{
		if ( deleted )
			yield break;
		foreach ( var ride in coasters().ToList() )
		{
			if ( ViewOf( ride ) is { } view )
			{
				try
				{
					view.Prepare();
				}
				catch
				{
					views.Remove( view );
					view.Dispose();
					ReleaseAssets( view.Assets );
					throw;
				}
			}
			yield return true;
		}
	}

	private RideView? ViewOf( CoasterRide ride )
	{
		for ( var index = 0; index < views.Count; index++ )
		{
			if ( ReferenceEquals( views[index].Ride, ride ) )
				return views[index];
		}
		if ( entryOf( ride ) is not { } entry )
			return null;
		if ( !assets.TryGetValue( entry, out var shared ) )
			assets[entry] = shared = new CoasterAssets( entry, ride.Definition );
		shared.Users++;
		var view = new RideView( this, ride, shared );
		views.Add( view );
		return view;
	}

	/// <summary>Drops a view's use of its type's assets; the models and textures are released with the last user.</summary>
	private void ReleaseAssets( CoasterAssets shared )
	{
		if ( --shared.Users > 0 )
			return;
		shared.Dispose();
		foreach ( var (entry, candidate) in assets )
		{
			if ( ReferenceEquals( candidate, shared ) )
			{
				assets.Remove( entry );
				break;
			}
		}
	}

	protected override void OnDelete()
	{
		deleted = true;
		foreach ( var view in views )
			view.Dispose();
		views.Clear();
		foreach ( var shared in assets.Values )
			shared.Dispose();
		assets.Clear();
	}

	/// <summary>The engine matrix of original-unit geometry in the engine axis order: the 0.2 scale and the grid origin.</summary>
	private Matrix4x4 ToEngine()
	{
		var corner = origin();
		return Matrix4x4.CreateScale( scale ) * Matrix4x4.CreateTranslation( corner.X, corner.Y, 0 );
	}

	/// <summary>The engine axis order of an original-unit point (X, Z, Y).</summary>
	internal static SVector3 Swap( SVector3 world ) => new( world.X, world.Z, world.Y );

	/// <summary>The engine vertex of an extruded track point, including its texture's lighting without changing its addressing or alpha flags.</summary>
	// [BIN:TPW-EXE:0x42f390 track textures] asTextureData.bIsSelfIlluminating sets texture flag 4; the three appended marker textures have flags 7 and are self-illuminated too.
	internal static Vertex TrackVertex( CoasterMeshVertex source, CoasterCrossSections sections )
	{
		var flags = 0u;
		var markerStart = Math.Max( 1, sections.Textures.Count ); // Match the mesh's synthetic fallback texture when no texture metadata is present.
		if ( source.Texture >= 0 && source.Texture < sections.Textures.Count )
		{
			var texture = sections.Textures[source.Texture];
			if ( texture.SelfIlluminated )
			{
				flags = TextureSlotAddressing.SelfLitFlag;
				// [EXT:evening] Reuse the existing light-source policy: other self-lit textures take the night tint, just like self-lit model materials.
				if ( TextureSlotAddressing.IsLightSource( texture.FileName ) )
					flags |= TextureSlotAddressing.LightSourceFlag;
			}
		}
		else if ( source.Texture >= markerStart && source.Texture - markerStart < CoasterTrackMesh.MarkerTextureNames.Length )
			flags = TextureSlotAddressing.SelfLitFlag; // m_erase, red and blue are markers, not light sources.
		return new Vertex( Swap( source.Position ), Swap( source.Normal ), new SVector2( source.U, source.V ) ) { TexIndex = source.Texture, MatFlags = flags };
	}

	private sealed class CarInstance
	{
		public required CoasterCarRig Rig { get; init; }
		public required ModelEntity[] Parts { get; init; }
		public required float YOffset { get; init; }
		public required int Train { get; init; }
		public required int Car { get; init; }
		/// <summary>The index of the car's type: its metrics reach the bend tables.</summary>
		public required int TypeIndex { get; init; }
		/// <summary>The car-to-world matrix of the last update (original axes).</summary>
		public Matrix4x4 World { get; set; } = Matrix4x4.Identity;
		/// <summary>The models of the parts whose texture coordinates follow the car's own wheel clip (null for the shared ones), released with the car.</summary>
		public Model?[]? OwnModels { get; set; }
	}

	/// <summary>
	/// Appends the riders of every coaster's cars as heads: rider <c>j</c> of a car hangs from the car model's head socket <c>j</c> (<see cref="CoasterCarRig.Seats"/>),
	/// in the MD2 axes and engine scale the head presentation works in.
	/// </summary>
	// [BIN:TPW-EXE:0x430130 car heads] each car type counts the head sockets of its model ("Car type %s can receive %d heads"); the riders of a car take them in order
	// [BIN:TPW-EXE:0x43b70d seat socket] a guest seated in a car (0x43b600) is hung on the car model's socket of its index in the car's seated list (0x44b410 with the type's socket array entry at the list count), and a rider leaving at the arrival is taken off its socket (0x44b4c0, 0x43b480): the lap's riders and the guests seated at the station use the same sockets, one list at a time
	internal void CollectHeads( List<HeadSource> sources, int? onlyAttraction )
	{
		var corner = origin();
		var shift = Matrix4x4.CreateScale( scale ) * Matrix4x4.CreateTranslation( corner.X, 0, corner.Y );
		foreach ( var view in views )
			view.CollectHeads( sources, onlyAttraction, attractionOf( view.Ride ), shift );
	}

	private sealed class RideView : IDisposable
	{
		private readonly CoasterRenderer owner;
		private readonly CoasterAssets assets;
		private readonly List<ModelEntity> entities = new();
		private readonly List<Model> models = new();
		private readonly List<CarInstance> cars = new();
		private (int Version, bool Operational, bool Edited, int Samples, CoasterPreview? Preview) built = (-1, false, false, -1, null);
		private bool dirty = true;
		private int carTrains = -1;
		private bool carsOperational;
		private Model? scrollModel;
		private Vertex[] scrollVertices = Array.Empty<Vertex>();
		private float[] scrollBase = Array.Empty<float>();
		private float scrollPhase;
		private uint lastSceneMilliseconds;
		private float[] scrollU = Array.Empty<float>();
		private int trackTriangles;
		private int pylonTriangles;
		private int carTriangles;
		private bool disposed;

		public RideView( CoasterRenderer owner, CoasterRide ride, CoasterAssets assets )
		{
			this.owner = owner;
			Ride = ride;
			this.assets = assets;
			lastSceneMilliseconds = owner.sceneMilliseconds?.Invoke() ?? 0;
			ride.TrackChanged += OnTrackChanged;
		}

		public CoasterRide Ride { get; }
		public CoasterAssets Assets => assets;
		public int Triangles => trackTriangles + pylonTriangles + carTriangles;
		public int ModelCount => models.Count;

		private void OnTrackChanged( CoasterRide ride ) => dirty = true;

		public void Update()
		{
			if ( disposed )
				return;
			// a view prepared ahead takes its clock in the park's first frame, as one made there does in its constructor, scrolling mesh or not
			if ( rebaseScroll )
			{
				lastSceneMilliseconds = owner.sceneMilliseconds?.Invoke() ?? 0;
				rebaseScroll = false;
			}
			EnsureTrack();
			UpdateScroll();
			UpdateCars();
		}

		/// <summary>
		/// [EXT:park-entry-preparation] The track's and the cars' models ahead of the park's first frame, without the frame's update: that update takes the scroll's clock again,
		/// so the scroll starts there as it does for a view made in that frame.
		/// </summary>
		public void Prepare()
		{
			if ( disposed )
				return;
			EnsureTrack();
			EnsureCars();
			rebaseScroll = true;
		}

		private void EnsureTrack()
		{
			var preview = Ride.IsOperational ? null : owner.previewOf( Ride );
			var key = (Ride.Track.Version, Ride.IsOperational, Ride.BeingEdited, Ride.Samples.Count, preview);
			if ( dirty || key != built )
			{
				Rebuild( preview );
				built = key;
				dirty = false;
			}
		}

		private bool rebaseScroll;

		// ---- Track and pylons ----------------------------------------------------------------------

		// [BIN:TPW-EXE:0x43eb70 parity] the triangle parity flag 0x7923e4 is reset once per rebuild and carries across the chains a cut circuit is drawn in
		private bool meshParity;

		private void Rebuild( CoasterPreview? preview )
		{
			ClearStatic();
			meshParity = false;
			var track = Ride.Track;
			var definition = Ride.Definition;
			IReadOnlyList<CoasterNode>? chain;
			bool closed;
			if ( Ride.IsOperational )
				(chain, closed) = (track.Nodes, true);
			else if ( track.BuildPreviewChain( preview is { } cursor ? (cursor.X, cursor.Y) : null, preview?.Height ?? 0, preview?.Loop ?? false, preview?.FirstOfPair ?? false, preview?.Valid ?? true ) is { } open )
				(chain, closed) = (open.Chain, open.Closed);
			else
				return;
			var pass = track.BuildKnotPass( Ride.Site, chain, writeBanks: false );
			var knots = pass.Knots;
			// A circuit the demolish button cut open is two chains: the nodes before the cut and the nodes after it (the cut ends are marked, 0x446740), each drawn on its own.
			// [BIN:TPW-EXE:0x43ef02 cut end] the mesh builder stops drawing at a node with 0x10000 (neither its own knot pairs nor its link) and starts again at one with 0x20000 (0x43ef0d)
			// [BIN:TPW-EXE:0x43eb70 rebuild order] the original samples the chain before the mesh is extruded and the run table reads that sample list (0x4402d0)
			// [APPROX:COAST-026] a chain the demolish button cut open, and one of fewer than three knots, has no sample list here, so its links take their section per segment by the mean pitch where the original builds the run table over every chain — evidence needed: the sampler across the cut ends (COAST-004)
			var matrix = owner.ToEngine();
			var sampling = chain.Count >= 3 && knots.Length >= 3 && !chain.Any( node => (node.Flags & (CoasterNodeFlags.CutEnd | CoasterNodeFlags.CutStart)) != 0 ) ? Ride.Curve.Sample( knots, definition.Curviness ) : null;
			// [BIN:TPW-EXE:0x43eb70 rebuild per frame] the original's rebuild runs the pass 0x439ab0 on every frame of an edit, so the crossing markers follow a drag live; the controller's own lists are refitted on the release
			var crossings = Ride.IsOperational && !Ride.BeingEdited ? Ride.CollisionLists : CoasterRide.ChainCrossings( Ride.Curve, chain, pass, definition, sampling );
			var cursorValid = preview?.Valid ?? true;
			int? Marker( int node, int next ) => LinkMarker( chain, crossings, cursorValid, node, next );
			foreach ( var (first, final, isClosed) in CoasterRenderer.ChainSpans( chain, pass, closed ) )
				BuildChain( knots[first..(final + 1)], first, definition.Curviness, isClosed, Marker, matrix, first == 0 && final == knots.Length - 1 ? sampling : null );
			BuildPylons( chain, pass, matrix );
		}

		/// <summary>Draws one chain of knots: the track through them, the links <paramref name="marker"/> names in a marker texture.</summary>
		private void BuildChain( CoasterKnot[] knots, int firstKnot, float curviness, bool closed, Func<int, int, int?> marker, Matrix4x4 matrix, CoasterSampling? sampling )
		{
			// [BIN:TPW-EXE:0x43e6b0 suspended roll] a suspended coaster's slices take the swing's sample rolls at the knots' coordinates (the controller's samples; none before the track is finished)
			var suspended = Ride.Definition.IsSuspended ? new CoasterSuspendedRolls( Ride.Samples, Ride.KnotCoordinates, firstKnot ) : null;
			var frames = sampling == null ? null : new CoasterSliceFrames( knots, curviness, closed, suspended );
			var runs = sampling == null ? null : HermiteCoasterCurve.ExtrusionRuns( assets.Sections, knots, curviness, sampling, suspended, frames );
			var keys = runs is { Count: > 0 } && sampling != null ? new CoasterSliceKeys( runs, sampling.KnotCoordinates, firstKnot ) : null;
			BuildTrack( HermiteCoasterCurve.Slices( knots, curviness, closed, suspended: suspended, keys: keys, frames: frames ), Ride.IsOperational ? Ride.WinchSections : Array.Empty<int>(), marker, matrix, keys == null ? null : runs );
		}

		private void BuildTrack( CoasterSlice[] slices, IReadOnlyCollection<int> winch, Func<int, int, int?> marker, Matrix4x4 matrix, IReadOnlyList<CoasterExtrusionRun>? runs )
		{
			var sections = assets.Sections;
			// The slices of a finished track end on the station end node, which stands on S0's track dummy: the mesh needs no wrap segment.
			var mesh = CoasterTrackMesh.Build( sections, slices, closed: false, winch, marker, meshParity, runs );
			meshParity = mesh.EndParity;
			trackTriangles += mesh.TriangleCount;
			if ( mesh.TriangleCount == 0 )
				return;
			// The mesh's vertices are shared along the strips, each of one texture: a scrolling texture's vertices (and triangles) move to their own dynamic model.
			bool Scrolls( int texture ) => texture >= 0 && texture < sections.Textures.Count && sections.Textures[texture].ScrollRate != 0;
			var fixedVertices = new List<Vertex>();
			var fixedIndices = new List<uint>();
			var movingVertices = new List<Vertex>();
			var movingIndices = new List<uint>();
			var moveBase = new List<float>();
			var moveU = new List<float>();
			var remap = new uint[mesh.Vertices.Count];
			for ( var index = 0; index < mesh.Vertices.Count; index++ )
			{
				var source = mesh.Vertices[index];
				var moving = Scrolls( source.Texture );
				var vertices = moving ? movingVertices : fixedVertices;
				remap[index] = (uint)vertices.Count;
				vertices.Add( TrackVertex( source, sections ) );
				if ( moving )
				{
					moveBase.Add( source.V );
					moveU.Add( source.U );
				}
			}
			for ( var triangle = 0; triangle < mesh.Indices.Count; triangle += 3 )
			{
				var moving = Scrolls( mesh.Vertices[(int)mesh.Indices[triangle]].Texture );
				var indices = moving ? movingIndices : fixedIndices;
				for ( var corner = 0; corner < 3; corner++ )
					indices.Add( remap[mesh.Indices[triangle + corner]] );
			}
			// [BIN:TPW-EXE:0x43f4e0 slice strips] the strips are single-sided: the culling keeps the faces the emitter's winding turns toward the camera, as for the models
			if ( fixedVertices.Count > 0 )
				AddModel( fixedVertices.ToArray(), fixedIndices.ToArray(), assets.GetTrackSlots(), matrix, doubleSided: false, dynamic: false, out _ );
			if ( movingVertices.Count > 0 )
			{
				scrollVertices = movingVertices.ToArray();
				scrollBase = moveBase.ToArray();
				scrollU = moveU.ToArray();
				AddModel( scrollVertices, movingIndices.ToArray(), assets.GetTrackSlots(), matrix, doubleSided: false, dynamic: true, out scrollModel );
			}
		}

		/// <summary>The pylons of a chain, each posed with its node's clip values (<c>0x430ed0</c>): the height parameter, the yaw and pitch of its node tangent or the hand-set ones, its bank and its loop rotate; the pose its knots were taken from.</summary>
		// [BIN:TPW-EXE:0x431057 pylon pose values] the knot pass turns the node tangent T+ into the automatic yaw (+0xd4) and pitch with 0x43e590 (yaw = atan2(T.x, T.z), pitch = atan2(−T.y, T.z′) with T turned by −yaw through the sine table) and copies them to +0xe0 and +0xdc unless node flags 1 and 2 hold hand-set values; +0xd8 is 0 unless flag 4 holds a hand-set bank, then the auto-bank (0x43c160) fills it
		private void BuildPylons( IReadOnlyList<CoasterNode> chain, CoasterKnotPass pass, Matrix4x4 matrix )
		{
			var accumulators = new Dictionary<(CoasterPylonRig Rig, int Part), (List<Vertex> Vertices, List<uint> Indices, string[] Textures)>();
			var definition = Ride.Definition;
			for ( var index = 0; index < chain.Count; index++ )
			{
				var node = chain[index];
				// the build cursor's node has no pylon of its own (the video shows only the ribbon)
				if ( node.IsPylonless || (node.Flags & CoasterNodeFlags.Cursor) != 0 || assets.GetPylon( node.LoopPylon ) is not { } rig )
					continue;
				var lift = CoasterTrack.PylonHeight( definition.Pylon( node.LoopPylon ), node.T );
				var baseHeight = pass.Positions[index].Y - lift;
				var pose = pass.Poses[index] ?? new CoasterPylonPose( node.T, (node.Flags & CoasterNodeFlags.ManualYaw) != 0 ? node.Yaw : pass.AutomaticYaw[index],
					(node.Flags & CoasterNodeFlags.ManualIncline) != 0 ? node.Incline : pass.AutomaticPitch[index], pass.Banks[index], node.LoopRotate, pass.TurnedBack[index] );
				var above = chain.FirstOrDefault( other => !other.IsPylonless && (other.Flags & CoasterNodeFlags.Cursor) == 0 && other.X == node.X && other.Y == node.Y && other.StackIndex == node.StackIndex + 1 );
				var parts = assets.PosePylon( node.LoopPylon, rig, pose, new CoasterStackPlace( node.StackIndex == 0, above == null, above != null ) );
				var shift = new SVector3( node.X * CoasterTrack.CellUnits, node.Y * CoasterTrack.CellUnits, baseHeight );
				for ( var part = 0; part < parts.Count; part++ )
				{
					if ( !accumulators.TryGetValue( (rig, part), out var accumulator ) )
						accumulators[(rig, part)] = accumulator = (new List<Vertex>(), new List<uint>(), parts[part].Textures);
					var first = (uint)accumulator.Vertices.Count;
					foreach ( var vertex in parts[part].Vertices )
					{
						var moved = vertex;
						moved.Position = vertex.Position.GetSystemVector3() + shift;
						accumulator.Vertices.Add( moved );
					}
					foreach ( var vertexIndex in parts[part].Indices )
						accumulator.Indices.Add( first + vertexIndex );
				}
			}
			foreach ( var (_, accumulator) in accumulators )
			{
				pylonTriangles += accumulator.Indices.Count / 3;
				AddModel( accumulator.Vertices.ToArray(), accumulator.Indices.ToArray(), assets.SlotsFor( accumulator.Textures ), matrix, doubleSided: false, dynamic: false, out _ );
			}
		}

		private void AddModel( Vertex[] vertices, uint[] indices, Texture[] slots, Matrix4x4 matrix, bool doubleSided, bool dynamic, out Model model )
		{
			var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader", doubleSided ? MaterialFlags.DoubleSided : MaterialFlags.None );
			material.Set( "Color", slots );
			model = new Model( vertices, indices, material, dynamic );
			models.Add( model );
			entities.Add( new ModelEntity { Model = model, Name = "coaster", SnowSurface = SnowCover.GroundSurface, TransformOverride = matrix, Position = new SVector3( matrix.M41, matrix.M42, matrix.M43 ) } );
		}

		private void ClearStatic()
		{
			foreach ( var entity in entities )
				entity.Delete();
			entities.Clear();
			foreach ( var model in models )
			{
				model.ReleaseBuffers();
				model.Material.ReleaseOwnedResources();
			}
			models.Clear();
			scrollModel = null;
			scrollVertices = Array.Empty<Vertex>();
			trackTriangles = pylonTriangles = 0;
		}

		/// <summary>Moves the scrolling textures (a flume's water and ratchet, a chain lift): V shifts by the power factor × elapsed seconds, the same for every scrolling texture.</summary>
		private void UpdateScroll()
		{
			if ( scrollModel == null )
				return;
			// [BIN:TPW-EXE:0x435adf scroll clock] the gap is on the scaled clock (0x4709e0 with argument 0 reads 0x7b496c, the unscaled 0x7b4974 only for a non-zero one): whole milliseconds since the controller's last tick (+0x138, stored again at the end of each tick), times the power factor, times 0.001
			var gapSeconds = Time.Delta;
			if ( owner.sceneMilliseconds is { } clock )
			{
				var now = clock();
				gapSeconds = unchecked( now - lastSceneMilliseconds ) * 0.001f;
				lastSceneMilliseconds = now;
			}
			scrollPhase = AdvanceScroll( scrollPhase, Ride.PowerFactor, gapSeconds );
			for ( var index = 0; index < scrollVertices.Length; index++ )
				scrollVertices[index].TexCoords = new SVector2( scrollU[index], scrollBase[index] - scrollPhase );
			scrollModel.UpdateVertices( scrollVertices );
		}

		// ---- Cars ----------------------------------------------------------------------------------

		private void ClearCars()
		{
			foreach ( var car in cars )
			{
				foreach ( var part in car.Parts )
					part.Delete();
				foreach ( var own in car.OwnModels ?? Array.Empty<Model?>() )
				{
					own?.ReleaseBuffers();
					own?.Material.ReleaseOwnedResources();
				}
			}
			cars.Clear();
			carTriangles = 0;
		}

		private void RebuildCars()
		{
			ClearCars();
			carsOperational = Ride.IsOperational;
			carTrains = Ride.Trains.Count;
			if ( !Ride.IsOperational )
				return;
			var types = assets.CarTypes;
			for ( var train = 0; train < Ride.Trains.Count; train++ )
			{
				var count = Ride.Trains[train].Cars.Count;
				for ( var car = 0; car < count; car++ )
				{
					var typeIndex = types.TypeIndexFor( car, count );
					if ( assets.GetCar( typeIndex ) is not { } rig )
						continue;
					var carModels = assets.GetCarModels( rig );
					var parts = new ModelEntity[carModels.Length];
					Model?[]? own = null;
					try
					{
						for ( var part = 0; part < parts.Length; part++ )
						{
							// a part whose texture coordinates follow the car's wheel clip has a model of its own: every car turns its wheels at its own speed
							if ( rig.IsDynamic( part ) )
								(own ??= new Model?[parts.Length])[part] = assets.CreateOwnPartModel( rig, part );
							parts[part] = new ModelEntity { Model = own?[part] ?? carModels[part], Name = "coaster car" };
						}
					}
					catch
					{
						// a car that failed half-way is not one of the view's cars, so nothing else would release what it made
						foreach ( var made in parts )
							made?.Delete();
						foreach ( var model in own ?? Array.Empty<Model?>() )
						{
							model?.ReleaseBuffers();
							model?.Material.ReleaseOwnedResources();
						}
						throw;
					}
					for ( var part = 0; part < parts.Length; part++ )
						carTriangles += rig.Parts[part].Indices.Length / 3;
					cars.Add( new CarInstance { Rig = rig, Parts = parts, OwnModels = own, YOffset = typeIndex < types.Types.Count ? types.Types[typeIndex].YOffset : 0, Train = train, Car = car, TypeIndex = typeIndex } );
				}
			}
		}

		private void EnsureCars()
		{
			if ( carTrains != Ride.Trains.Count || carsOperational != Ride.IsOperational )
				RebuildCars();
		}

		private void UpdateCars()
		{
			EnsureCars();
			if ( cars.Count == 0 || Ride.Spacing <= 0 )
				return;
			var engine = owner.ToEngine();
			for ( var index = 0; index < cars.Count; index++ )
			{
				var car = cars[index];
				if ( car.Train >= Ride.Trains.Count )
					continue;
				var coordinate = Ride.CarCoordinate( Ride.Trains[car.Train], car.Car );
				var pose = Ride.CarPose( coordinate );
				car.World = CoasterCarRig.Matrix( pose, car.YOffset );
				var transform = ObjectPlacement.SwapAxes( car.World ) * engine;
				var position = new SVector3( transform.M41, transform.M42, transform.M43 );
				// [BIN:TPW-EXE:0x43ce20 wheel clip] a car of a non-bend type plays its model's clip at the type's factor × the train's speed: the parts of animated nodes follow it
				Matrix4x4[]? wheels = null;
				if ( car.Rig.WheelClip is { } clip )
				{
					var tick = owner.WheelTick( Ride, car.Train, clip.Duration );
					wheels = car.Rig.WheelPartMatrices( tick );
					for ( var partIndex = 0; partIndex < car.Parts.Length; partIndex++ )
					{
						if ( car.OwnModels?[partIndex] is { } own && car.Rig.WheelVertices( partIndex, tick ) is { } vertices )
							own.UpdateVertices( vertices );
					}
				}
				if ( car.Rig.Bend )
				{
					// [BIN:TPW-EXE:0x43ce20 bend car] a car of a bend type bends its mesh along the samples around it every tick (0x438800) instead of playing a clip: the tables take the car's coordinate, its direction (the third row of its matrix) and its type's extents
					var metrics = Ride.Definition.CarTypeMetrics( car.TypeIndex );
					var forward = new SVector3( car.World.M31, car.World.M32, car.World.M33 );
					var tables = CoasterCarBend.Tables( Ride.Samples, Ride.Spacing, Ride.Definition.BendElevationLow, Ride.Definition.BendElevationHigh, metrics.RearExtent, metrics.FrontExtent, forward, coordinate );
					for ( var partIndex = 0; partIndex < car.Parts.Length; partIndex++ )
					{
						if ( car.OwnModels?[partIndex] is { } own && car.Rig.BendVertices( partIndex, tables, Ride.Spacing ) is { } vertices )
							own.UpdateVertices( vertices );
					}
				}
				for ( var partIndex = 0; partIndex < car.Parts.Length; partIndex++ )
				{
					var part = car.Parts[partIndex];
					part.TransformOverride = wheels == null ? transform : ObjectPlacement.SwapAxes( wheels[partIndex] * car.World ) * engine;
					part.Position = position;
				}
			}
		}

		/// <summary>The seat of a train's camera car (<see cref="CoasterDefinition.RideCameraCar"/>): its model's seat node on the car's last pose, in engine axes.</summary>
		public Matrix4x4? CameraCarSeat( int train )
		{
			foreach ( var car in cars )
			{
				if ( car.Train == train && car.Car == Ride.Definition.RideCameraCar && car.Rig.CameraSeat is { } local )
				{
					return ObjectPlacement.SwapAxes( local * car.World ) * owner.ToEngine();
				}
			}
			return null;
		}

		public void CollectHeads( List<HeadSource> sources, int? onlyAttraction, int attraction, Matrix4x4 shift )
		{
			if ( disposed || !Ride.IsOperational || attraction == 0 || onlyAttraction is int only && only != attraction )
				return;
			foreach ( var car in cars )
			{
				if ( car.Train >= Ride.Trains.Count || car.Rig.Seats.Count == 0 || car.Car >= Ride.Trains[car.Train].Cars.Count )
					continue;
				foreach ( var riders in new[] { Ride.Trains[car.Train].Cars[car.Car], Ride.Trains[car.Train].BoardingCars[car.Car] } )
				{
					// A rider past the car's sockets (a car seated by a capacity above its own sockets, which COAST 6 does not allow) has no socket to hang from.
					for ( var rider = 0; rider < riders.Count && rider < car.Rig.Seats.Count; rider++ )
					{
						var (node, local) = car.Rig.Seats[rider];
						sources.Add( new HeadSource( riders[rider], attraction, local * car.World * shift, car.Rig.Model.Nodes[node].Type ) );
					}
				}
			}
		}

		public void Dispose()
		{
			if ( disposed )
				return;
			disposed = true;
			Ride.TrackChanged -= OnTrackChanged;
			ClearCars();
			ClearStatic();
		}
	}
}
