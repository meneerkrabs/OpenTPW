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
// [APPROX:COAST-026] see CoasterTrackMesh: the extruded mesh draws both faces and takes cross-section changes per segment; scroll textures advance by their fScrollRate per second (the original's rate source, controller +0x2c, is not resolved) and self-illuminated textures are lit like the rest
public sealed class CoasterRenderer : Entity
{
	private readonly Func<IReadOnlyList<CoasterRide>> coasters;
	private readonly Func<SVector2> origin;
	private readonly float scale;
	private readonly Func<CoasterRide, ObjectCatalogEntry?> entryOf;
	private readonly Func<CoasterRide, CoasterPreview?> previewOf;
	private readonly Func<CoasterRide, int> attractionOf;
	private readonly List<RideView> views = new();
	private readonly Dictionary<ObjectCatalogEntry, CoasterAssets> assets = new();
	private bool deleted;

	/// <param name="coasters">The coasters to draw.</param>
	/// <param name="origin">Engine position of grid corner (0, 0).</param>
	/// <param name="scale">Engine units per original world unit (0.2).</param>
	/// <param name="entryOf">The catalogue entry (archive) of a coaster's station, or null when it has none.</param>
	/// <param name="previewOf">The build cursor of the coaster the tool edits, or null.</param>
	/// <param name="attractionOf">The attraction id of a coaster's station (0 when it has none), which the riders' heads are matched against.</param>
	public CoasterRenderer( Func<IReadOnlyList<CoasterRide>> coasters, Func<SVector2> origin, float scale, Func<CoasterRide, ObjectCatalogEntry?> entryOf, Func<CoasterRide, CoasterPreview?> previewOf, Func<CoasterRide, int> attractionOf )
	{
		this.attractionOf = attractionOf;
		this.coasters = coasters;
		this.origin = origin;
		this.scale = scale;
		this.entryOf = entryOf;
		this.previewOf = previewOf;
		Name = "Coasters";
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
			views[index].Dispose();
			views.RemoveAt( index );
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
		var view = new RideView( this, ride, shared );
		views.Add( view );
		return view;
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

	private sealed class CarInstance
	{
		public required CoasterCarRig Rig { get; init; }
		public required ModelEntity[] Parts { get; init; }
		public required float Offset { get; init; }
		public required float YOffset { get; init; }
		public required int Train { get; init; }
		public required int Car { get; init; }
		/// <summary>The car-to-world matrix of the last update (original axes).</summary>
		public Matrix4x4 World { get; set; } = Matrix4x4.Identity;
	}

	/// <summary>
	/// Appends the riders of every coaster's cars as heads: rider <c>j</c> of a car hangs from the car model's head socket <c>j</c> (<see cref="CoasterCarRig.Seats"/>),
	/// in the MD2 axes and engine scale the head presentation works in.
	/// </summary>
	// [BIN:TPW-EXE:0x430130 car heads] each car type counts the head sockets of its model ("Car type %s can receive %d heads"); the riders of a car take them in order
	// [APPROX:COAST-034] rider j takes socket j modulo the sockets of its car (the original's seat assignment is not traced)
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
		private (int Version, bool Operational, int Samples, CoasterPreview? Preview) built = (-1, false, -1, null);
		private bool dirty = true;
		private int carTrains = -1;
		private bool carsOperational;
		private Model? scrollModel;
		private Vertex[] scrollVertices = Array.Empty<Vertex>();
		private float[] scrollBase = Array.Empty<float>();
		private float[] scrollRate = Array.Empty<float>();
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
			ride.TrackChanged += OnTrackChanged;
		}

		public CoasterRide Ride { get; }
		public int Triangles => trackTriangles + pylonTriangles + carTriangles;
		public int ModelCount => models.Count;

		private void OnTrackChanged( CoasterRide ride ) => dirty = true;

		public void Update()
		{
			if ( disposed )
				return;
			var preview = Ride.IsOperational ? null : owner.previewOf( Ride );
			var key = (Ride.Track.Version, Ride.IsOperational, Ride.Samples.Count, preview);
			if ( dirty || key != built )
			{
				Rebuild( preview );
				built = key;
				dirty = false;
			}
			UpdateScroll();
			UpdateCars();
		}

		// ---- Track and pylons ----------------------------------------------------------------------

		private void Rebuild( CoasterPreview? preview )
		{
			ClearStatic();
			var track = Ride.Track;
			var definition = Ride.Definition;
			IReadOnlyList<CoasterNode>? chain;
			bool closed;
			if ( Ride.IsOperational )
				(chain, closed) = (track.Nodes, true);
			else if ( track.BuildPreviewChain( preview is { } cursor ? (cursor.X, cursor.Y) : null, preview?.Height ?? 0, preview?.Loop ?? false ) is { } open )
				(chain, closed) = (open.Chain, open.Closed);
			else
				return;
			var knots = track.BuildKnots( Ride.Site, chain, closed, writeBanks: false );
			var slices = HermiteCoasterCurve.Slices( knots, definition.Curviness, closed );
			var invalid = new HashSet<int>();
			for ( var pair = 0; pair < chain.Count; pair++ )
			{
				var next = closed ? (pair + 1) % chain.Count : pair + 1;
				if ( next < chain.Count && (chain[pair].Flags & CoasterNodeFlags.Invalid) != 0 && (chain[next].Flags & CoasterNodeFlags.Invalid) != 0 )
					invalid.Add( pair );
			}
			// The link to the build cursor is marked by the cursor's own check; the placed nodes carry their own flags.
			if ( !closed && preview is { Valid: false } && chain.Count == track.Nodes.Count + 1 )
				invalid.Add( chain.Count - 2 );
			var matrix = owner.ToEngine();
			BuildTrack( slices, closed, Ride.IsOperational ? Ride.WinchSections : Array.Empty<int>(), invalid, matrix );
			BuildPylons( chain, knots, matrix );
		}

		private void BuildTrack( CoasterSlice[] slices, bool closed, IReadOnlyCollection<int> winch, IReadOnlyCollection<int> invalid, Matrix4x4 matrix )
		{
			var sections = assets.Sections;
			var mesh = CoasterTrackMesh.Build( sections, slices, closed, winch, invalid );
			trackTriangles = mesh.TriangleCount;
			if ( mesh.TriangleCount == 0 )
				return;
			var fixedVertices = new List<Vertex>();
			var fixedIndices = new List<uint>();
			var movingVertices = new List<Vertex>();
			var movingIndices = new List<uint>();
			var moveBase = new List<float>();
			var moveRate = new List<float>();
			var moveU = new List<float>();
			// The mesh emits one quad (four vertices, six indices) per edge and segment, all of one texture.
			for ( var quad = 0; quad < mesh.Vertices.Count / 4; quad++ )
			{
				var texture = mesh.Vertices[quad * 4].Texture;
				var rate = texture >= 0 && texture < sections.Textures.Count ? sections.Textures[texture].ScrollRate : 0f;
				var vertices = rate != 0 ? movingVertices : fixedVertices;
				var indices = rate != 0 ? movingIndices : fixedIndices;
				var first = (uint)vertices.Count;
				for ( var corner = 0; corner < 4; corner++ )
				{
					var source = mesh.Vertices[quad * 4 + corner];
					vertices.Add( new Vertex( Swap( source.Position ), Swap( source.Normal ), new SVector2( source.U, source.V ) ) { TexIndex = source.Texture == CoasterTrackMesh.InvalidTexture ? CoasterAssets.InvalidSlot : source.Texture } );
					if ( rate != 0 )
					{
						moveBase.Add( source.V );
						moveRate.Add( rate );
						moveU.Add( source.U );
					}
				}
				indices.Add( first );
				indices.Add( first + 1 );
				indices.Add( first + 2 );
				indices.Add( first );
				indices.Add( first + 2 );
				indices.Add( first + 3 );
			}
			if ( fixedVertices.Count > 0 )
				AddModel( fixedVertices.ToArray(), fixedIndices.ToArray(), assets.GetTrackSlots(), matrix, doubleSided: true, dynamic: false, out _ );
			if ( movingVertices.Count > 0 )
			{
				scrollVertices = movingVertices.ToArray();
				scrollBase = moveBase.ToArray();
				scrollRate = moveRate.ToArray();
				scrollU = moveU.ToArray();
				AddModel( scrollVertices, movingIndices.ToArray(), assets.GetTrackSlots(), matrix, doubleSided: true, dynamic: true, out scrollModel );
			}
		}

		private void BuildPylons( IReadOnlyList<CoasterNode> chain, IReadOnlyList<CoasterKnot> knots, Matrix4x4 matrix )
		{
			var accumulators = new Dictionary<(CoasterPylonRig Rig, int Part), (List<Vertex> Vertices, List<uint> Indices, string[] Textures)>();
			var definition = Ride.Definition;
			for ( var index = 0; index < chain.Count; index++ )
			{
				var node = chain[index];
				if ( node.IsPylonless || assets.GetPylon( node.LoopPylon ) is not { } rig )
					continue;
				var lift = CoasterTrack.PylonHeight( definition.Pylon( node.LoopPylon ), node.T );
				var knot = knots[index];
				var baseHeight = knot.Position.Y - definition.TrackHeightAdjust - lift;
				var yaw = MathF.Atan2( knot.Direction.X, knot.Direction.Z );
				var stackedAbove = chain.Any( other => !other.IsPylonless && other.X == node.X && other.Y == node.Y && other.StackIndex == node.StackIndex + 1 );
				var parts = rig.Pose( lift, yaw, stackedAbove );
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
			entities.Add( new ModelEntity { Model = model, Name = "coaster", TransformOverride = matrix, Position = new SVector3( matrix.M41, matrix.M42, matrix.M43 ) } );
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

		/// <summary>Moves the scrolling textures (a flume's water and ratchet, a chain lift): V shifts by rate × time, wrapped.</summary>
		private void UpdateScroll()
		{
			if ( scrollModel == null )
				return;
			var time = Time.Now;
			for ( var index = 0; index < scrollVertices.Length; index++ )
			{
				var shift = scrollRate[index] * time;
				shift -= MathF.Floor( shift );
				scrollVertices[index].TexCoords = new SVector2( scrollU[index], scrollBase[index] - shift );
			}
			scrollModel.UpdateVertices( scrollVertices );
		}

		// ---- Cars ----------------------------------------------------------------------------------

		private void ClearCars()
		{
			foreach ( var car in cars )
			{
				foreach ( var part in car.Parts )
					part.Delete();
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
				var rigs = new CoasterCarRig?[count];
				var extents = new (float Front, float Rear)[count];
				for ( var car = 0; car < count; car++ )
				{
					var typeIndex = types.TypeIndexFor( car, count );
					rigs[car] = assets.GetCar( typeIndex );
					var type = typeIndex >= 0 && typeIndex < types.Types.Count ? types.Types[typeIndex] : new CoasterCarType( "", 0, 0, 0, false );
					extents[car] = ((rigs[car]?.FrontExtent ?? 0) + type.FrontAdd, (rigs[car]?.RearExtent ?? 0) + type.RearAdd);
				}
				var offsets = CoasterCarRig.CarOffsets( extents );
				for ( var car = 0; car < count; car++ )
				{
					if ( rigs[car] is not { } rig )
						continue;
					var typeIndex = types.TypeIndexFor( car, count );
					var carModels = assets.GetCarModels( rig );
					var parts = new ModelEntity[carModels.Length];
					for ( var part = 0; part < parts.Length; part++ )
					{
						parts[part] = new ModelEntity { Model = carModels[part], Name = "coaster car" };
						carTriangles += rig.Parts[part].Indices.Length / 3;
					}
					cars.Add( new CarInstance { Rig = rig, Parts = parts, Offset = offsets[car], YOffset = typeIndex < types.Types.Count ? types.Types[typeIndex].YOffset : 0, Train = train, Car = car } );
				}
			}
		}

		private void UpdateCars()
		{
			if ( carTrains != Ride.Trains.Count || carsOperational != Ride.IsOperational )
				RebuildCars();
			if ( cars.Count == 0 || Ride.Spacing <= 0 )
				return;
			var engine = owner.ToEngine();
			for ( var index = 0; index < cars.Count; index++ )
			{
				var car = cars[index];
				if ( car.Train >= Ride.Trains.Count )
					continue;
				var coordinate = Ride.Trains[car.Train].Position - car.Offset / Ride.Spacing;
				var pose = CoasterCarRig.PoseAt( Ride.Samples, coordinate );
				car.World = CoasterCarRig.Matrix( pose, car.YOffset );
				var transform = ObjectPlacement.SwapAxes( car.World ) * engine;
				var position = new SVector3( transform.M41, transform.M42, transform.M43 );
				foreach ( var part in car.Parts )
				{
					part.TransformOverride = transform;
					part.Position = position;
				}
			}
		}

		public void CollectHeads( List<HeadSource> sources, int? onlyAttraction, int attraction, Matrix4x4 shift )
		{
			if ( disposed || !Ride.IsOperational || attraction == 0 || onlyAttraction is int only && only != attraction )
				return;
			foreach ( var car in cars )
			{
				if ( car.Train >= Ride.Trains.Count || car.Rig.Seats.Count == 0 || car.Car >= Ride.Trains[car.Train].Cars.Count )
					continue;
				var riders = Ride.Trains[car.Train].Cars[car.Car];
				for ( var rider = 0; rider < riders.Count; rider++ )
				{
					var (node, local) = car.Rig.Seats[rider % car.Rig.Seats.Count];
					sources.Add( new HeadSource( riders[rider], attraction, local * car.World * shift, car.Rig.Model.Nodes[node].Type ) );
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
