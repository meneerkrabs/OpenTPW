using System.Numerics;

namespace OpenTPW;

/// <summary>
/// Draws the flying cars of a level's tour rides (docs/TOURS.md): every live car of every controller is the ride's supplemental mesh 0 (the bird, balloon, craft or kite),
/// placed at the car's smoothed position with its heading, pitch and roll (<see cref="TourPose"/>), playing the clip the controller triggered on channel 0 from the mesh's own
/// clip members (<c>Bird?.md2</c>: C 0, L 3, S 4, M 5, E 6, B 9, R 10), and its riders hang from the mesh's head dummies like the go-karts' (<see cref="CollectHeads"/>).
/// </summary>
// [BIN:TPW-EXE:0x0055AA80 car animation] a changed (animation, channel, loops) triggers the car instance's clip through 0x004732A0(instance, animation, 0, loops, 1.0, 0); the instance is the ride's supplemental mesh 0 created by 0x00463060 (0x0055AA07), whose clips are the mesh's own letter members (the loader 0x461F10)
// [BIN:TPW-EXE:0x0055CE60 car pose] the pose angles go through the engine's rotation builder 0x0046F6F0, which reads its sines from a 4096-entry table (OriginalSineTable, TourPose.Rotation)
// [BIN:TPW-EXE:0x0055CE60 no car shadow] no shadow is drawn under the cars: the original's car draw 0x0055CE60 places only the instance and its emitter (it calls the node lookup, the matrix builder and the emitter move 0x0051FE30), the MESHSHADOW option's flag (bit 0x200 of 0x0079D86C, set at 0x0044DA8A) has no reader (none of the 26 instruction references to that word tests bit 0x200), the car instance is created with the generic flags 0x101 (0x00463060, a dynamic vertex buffer), its vertex writer 0x00580530 (texture coordinates, environment mapping and the vertex copy; callees 0x00561BC0, 0x00562ED0, 0x005630B0, 0x00565E70, 0x00575F90, 0x005812F0..0x00581410) holds no shadow code and the batcher that consumes the vertices (0x00581DB0) only sorts the instance's parts into bins by texture key and blend flags without making geometry; the one shadow model the game names, w_small_shadow.md2, is skipped by its model loader (0x00489060), and the sprite shadow (alphkid.tga, 0x0055093A) belongs to the person sprites
public sealed class TourRenderer : IDisposable
{
	private const string TextureArrayName = "Color";
	private const int MaximumSeats = 12;

	/// <summary>
	/// A car mesh: its parts (static ones baked, the nodes its clips move kept apart and shared by every car), the vertex-animated parts every car copies, its clip members,
	/// the head nodes and the smoke node (class 0x100, number 1).
	/// </summary>
	private sealed record CarGeometry( ModelFile Model, IReadOnlyList<ObjectAnimationFile> Animations, List<(Model Model, int Node)> SharedParts, List<(ObjectRenderPart Part, Material Material)> VertexParts,
		List<Material> Materials, Matrix4x4[] Rest, int[] HeadNodes, uint[] HeadFlags, int SmokeNode, bool Animates )
	{
		public readonly Dictionary<int, (ModelAnimation? Clip, string Name)> Clips = new();
	}

	private sealed class CarVisual
	{
		public List<(ModelEntity Entity, int Node)> Parts = new();
		public List<(Model Model, int Mesh, Vertex[] Vertices, int Version)> VertexModels = new();
		public ObjectAnimator? Animator;
		public long Serial = -1;
		public int Generation = -1;
		public TourController? Controller;
		public ObjectCatalogEntry? Entry;
		public TourCar? Car;
		public Matrix4x4 Transform = Matrix4x4.Identity;
		public bool Seen;
	}

	private readonly Func<TourController, ObjectCatalogEntry?> entryOf;
	private readonly Func<Vector2> gridOrigin;
	private readonly Dictionary<string, CarGeometry?> meshes = new();
	private readonly Dictionary<(TourController Controller, int Slot), CarVisual> visuals = new();
	private readonly List<(TourController Controller, int Slot)> gone = new();
	private bool disposed;
	private bool visible = true;

	/// <param name="entryOf">The catalogue entry of the ride a controller belongs to (null: its cars are not drawn).</param>
	/// <param name="gridOrigin">Engine position of grid corner (0, 0).</param>
	public TourRenderer( Func<TourController, ObjectCatalogEntry?> entryOf, Func<Vector2> gridOrigin )
	{
		this.entryOf = entryOf;
		this.gridOrigin = gridOrigin;
	}

	/// <summary>Whether the cars are drawn (the native smoke test hides them to read back the pixels they cover).</summary>
	public bool Visible
	{
		get => visible;
		set
		{
			visible = value;
			foreach ( var visual in visuals.Values )
			{
				foreach ( var (entity, node) in visual.Parts )
					entity.Hidden = !value || node >= 0 && visual.Animator?.IsNodeHidden( node ) == true;
			}
		}
	}

	/// <summary>The engine position of the first car carrying a rider (else the first car), or null when none is drawn.</summary>
	public Vector3? FlyingCarPosition()
	{
		var drawn = visuals.Values.Where( item => item.Parts.Count > 0 ).ToList();
		var visual = drawn.FirstOrDefault( item => item.Car is { State: TourCar.StateTour, RiderCount: > 0 } ) ?? drawn.FirstOrDefault();
		return visual == null ? null : visual.Transform.Translation;
	}

	/// <summary>Car instances drawn after the last <see cref="Update"/>.</summary>
	public int CarCount => visuals.Values.Count( visual => visual.Parts.Count > 0 );

	/// <summary>The clip a drawn car plays on channel 0 (its animation id, −1 ended, <see cref="ObjectAnimator.NoAnimation"/> none), or null when the car is not drawn (tests).</summary>
	internal int? DrawnAnimation( TourController controller, int slot ) =>
		visuals.TryGetValue( (controller, slot), out var visual ) && visual.Animator is { } animator ? animator.GetAnimation( 0 ) : null;

	/// <summary>
	/// Moves one instance to every live car of <paramref name="controllers"/>, creating and removing instances as cars come and go, and plays each car's clip for
	/// <paramref name="seconds"/> of simulation time (the world step that just ran).
	/// </summary>
	public void Update( IEnumerable<TourController> controllers, double seconds = 0 )
	{
		foreach ( var visual in visuals.Values )
			visual.Seen = false;
		var origin = gridOrigin();
		foreach ( var controller in controllers )
		{
			for ( var slot = 0; slot < controller.Cars.Count; slot++ )
			{
				var car = controller.Cars[slot];
				if ( !car.IsLive )
					continue;
				if ( !visuals.TryGetValue( (controller, slot), out var visual ) )
					visuals[(controller, slot)] = visual = new CarVisual { Controller = controller };
				visual.Seen = true;
				Draw( visual, controller, slot, car, origin, seconds );
			}
		}
		gone.Clear();
		foreach ( var (key, visual) in visuals )
		{
			if ( !visual.Seen )
				gone.Add( key );
		}
		foreach ( var key in gone )
		{
			Delete( visuals[key] );
			visuals.Remove( key );
		}
	}

	private void Draw( CarVisual visual, TourController controller, int slot, TourCar car, Vector2 origin, double seconds )
	{
		if ( visual.Parts.Count == 0 && visual.Entry == null )
		{
			visual.Entry = entryOf( controller );
			if ( visual.Entry is { } entry && Mesh( entry ) is { } geometry )
				Build( visual, geometry );
		}
		visual.Car = car;
		if ( visual.Parts.Count == 0 || visual.Entry is not { } drawn || Mesh( drawn ) is not { } mesh )
			return;
		// a new car in the slot starts a fresh instance (the original creates one per car, 0x0055AA07)
		if ( visual.Generation != car.Generation )
		{
			visual.Generation = car.Generation;
			visual.Animator = mesh.Animates ? new ObjectAnimator( mesh.Model ) : null;
			visual.Serial = -1;
		}
		if ( visual.Animator is { } animator )
		{
			if ( visual.Serial != car.AnimationSerial )
			{
				visual.Serial = car.AnimationSerial;
				var (clip, name) = Clip( drawn, mesh, car.Animation );
				animator.Trigger( 0, clip, name, car.Animation, 0, car.AnimationLoops );
			}
			if ( seconds > 0 )
				animator.Advance( seconds );
			UpdateVertices( visual, mesh, animator );
		}
		// the original redraws every frame with the time since the last tick; here the pose is smoothed once per simulation step with the tick clock's remainder, which is the same on every machine
		if ( car.DrawnGeneration != car.Generation )
		{
			car.Drawn = TourPose.Snap( car );
			car.DrawnGeneration = car.Generation;
		}
		else
			car.Drawn = TourPose.Smooth( car, TourPose.Fraction( controller ), car.Drawn );
		var pose = car.Drawn;
		// the pose is in the original's frame (Y up): the rotation is swapped into the engine's (Z up) like the coaster cars'
		var transform = ObjectPlacement.SwapAxes( TourPose.Rotation( pose ) )
			* Matrix4x4.CreateTranslation( pose.X, pose.Z, pose.Y )
			* Matrix4x4.CreateScale( ObjectPlacement.ModelScale )
			* Matrix4x4.CreateTranslation( origin.X, origin.Y, 0 );
		visual.Transform = transform;
		foreach ( var (entity, node) in visual.Parts )
		{
			var placed = node < 0 ? transform : ObjectPlacement.SwapAxes( NodeTransform( visual, mesh, node ) ) * transform;
			entity.TransformOverride = placed;
			entity.Position = new Vector3( placed.M41, placed.M42, placed.M43 );
			if ( node >= 0 )
				entity.Hidden = !visible || visual.Animator?.IsNodeHidden( node ) == true;
		}
		// [BIN:TPW-EXE:0x0055CE60 car draw] a car holding an emitter moves it to its node class 0x100 number 1 every draw
		if ( car.Emitter != 0 && mesh.SmokeNode >= 0 )
			controller.DrawEmitter( slot, System.Numerics.Vector3.Transform( System.Numerics.Vector3.Zero, ObjectPlacement.SwapAxes( NodeTransform( visual, mesh, mesh.SmokeNode ) ) * transform ) );
	}

	/// <summary>A node's model-space matrix in the car's current pose: the animator's, or the rest pose without one.</summary>
	private static Matrix4x4 NodeTransform( CarVisual visual, CarGeometry mesh, int node ) =>
		visual.Animator is { } animator ? animator.NodeTransforms[node] : mesh.Rest[node];

	/// <summary>The clip member of an animation id on the car mesh (variant 0), loaded once per mesh; a missing member leaves the channel as it is.</summary>
	private static (ModelAnimation? Clip, string Name) Clip( ObjectCatalogEntry entry, CarGeometry mesh, int animation )
	{
		if ( mesh.Clips.TryGetValue( animation, out var cached ) )
			return cached;
		(ModelAnimation? Clip, string Name) result = (null, $"tour car {animation}");
		if ( ObjectAnimations.Resolve( mesh.Animations, animation, 0 ) is { } file )
		{
			try
			{
				result = (ObjectAssets.LoadModel( entry.FileSystem, file.Path ).Clip, Path.GetFileName( file.Path ));
			}
			catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException or IOException )
			{
				Log.Warning( $"The tour car clip {file.Path} could not be read: {exception.Message}" );
			}
		}
		mesh.Clips[animation] = result;
		return result;
	}

	private static void UpdateVertices( CarVisual visual, CarGeometry mesh, ObjectAnimator animator )
	{
		for ( var index = 0; index < visual.VertexModels.Count; index++ )
		{
			var (model, meshIndex, vertices, version) = visual.VertexModels[index];
			if ( animator.GetVertexVersion( meshIndex ) == version )
				continue;
			ObjectRenderParts.WritePositions( mesh.Model.Meshes[meshIndex], animator.GetVertexPositions( meshIndex ), vertices );
			ObjectRenderParts.WriteTexCoords( mesh.Model.Meshes[meshIndex], animator.GetTexCoords( meshIndex ), vertices );
			model.UpdateVertices( vertices );
			visual.VertexModels[index] = (model, meshIndex, vertices, animator.GetVertexVersion( meshIndex ));
		}
	}

	private void Build( CarVisual visual, CarGeometry geometry )
	{
		foreach ( var (model, node) in geometry.SharedParts )
			visual.Parts.Add( (new ModelEntity { Model = model, Name = "tour car", Hidden = !visible }, node) );
		foreach ( var (part, material) in geometry.VertexParts )
		{
			// every car moves its own copy of a vertex-animated mesh
			var vertices = (Vertex[])part.Vertices.Clone();
			var model = new Model( vertices, part.Indices, material, true );
			visual.VertexModels.Add( (model, part.MeshIndex, vertices, -1) );
			visual.Parts.Add( (new ModelEntity { Model = model, Name = "tour car", Hidden = !visible }, part.NodeIndex) );
		}
	}

	/// <summary>The riders of the cars as head sources: each rider hangs from a head dummy picked by the car's shuffle (<see cref="TourPose.HeadNode"/>), in the car's current pose.</summary>
	internal void CollectHeads( List<HeadSource> sources, int? onlyAttraction )
	{
		foreach ( var visual in visuals.Values )
		{
			if ( visual.Car is not { } car || visual.Controller is not { AttractionId: >= 0 } controller || onlyAttraction is { } only && only != controller.AttractionId
				|| visual.Entry is not { } entry || Mesh( entry ) is not { } geometry )
				continue;
			var riders = Math.Min( Math.Abs( car.RiderCount ), TourCar.MaximumRiders );
			for ( var seat = 0; seat < riders; seat++ )
			{
				// the rider sits on a shuffled head node: (car random + rider + 1) mod the head count (TourPose.HeadNode)
				var head = TourPose.HeadNode( car.Random, seat, geometry.HeadNodes.Length );
				if ( head < 0 )
					break;
				var node = ObjectPlacement.SwapAxes( NodeTransform( visual, geometry, geometry.HeadNodes[head] ) ) * visual.Transform;
				sources.Add( new HeadSource( car.Riders[seat], controller.AttractionId, ObjectPlacement.SwapAxes( node ), geometry.HeadFlags[head] ) );
			}
		}
	}

	private static readonly Dictionary<string, int> headNodeCounts = new();

	/// <summary>The head dummies a tour ride's car mesh holds, numbered consecutively from 1 (0 when the ride has no readable car mesh); the simulation's head-hang draw reads it.</summary>
	internal static int CarHeadNodes( ObjectCatalogEntry entry )
	{
		lock ( headNodeCounts )
		{
			if ( headNodeCounts.TryGetValue( entry.ArchivePath, out var cached ) )
				return cached;
		}
		var count = 0;
		try
		{
			var name = entry.Settings["SupplementalMeshes[0].FileName"];
			var files = entry.FileSystem.GetFiles( entry.ArchivePath ).ToList();
			var path = name == null ? null : files.FirstOrDefault( file => string.Equals( Path.GetFileName( file ), name.Trim( '"', ' ' ), StringComparison.OrdinalIgnoreCase ) );
			if ( path != null && ObjectAssets.LoadModel( entry.FileSystem, path ) is { Kind: ModelFileKind.Geometry } model )
			{
				for ( var id = 1; id <= MaximumSeats; id++ )
				{
					var node = RideHeadSpace.NodeForHead( model, id );
					if ( node < 0 || node >= model.Nodes.Count )
						break;
					count++;
				}
			}
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException or IOException )
		{
			count = 0;
		}
		lock ( headNodeCounts )
			headNodeCounts[entry.ArchivePath] = count;
		return count;
	}

	private CarGeometry? Mesh( ObjectCatalogEntry entry )
	{
		if ( meshes.TryGetValue( entry.ArchivePath, out var cached ) )
			return cached;
		CarGeometry? geometry = null;
		var name = entry.Settings["SupplementalMeshes[0].FileName"];
		try
		{
			var files = entry.FileSystem.GetFiles( entry.ArchivePath ).ToList();
			var path = name == null ? null : files.FirstOrDefault( file => string.Equals( Path.GetFileName( file ), name.Trim( '"', ' ' ), StringComparison.OrdinalIgnoreCase ) );
			if ( path != null )
			{
				var model = ObjectAssets.LoadModel( entry.FileSystem, path );
				if ( model.Kind == ModelFileKind.Geometry )
				{
					// the car mesh's clip members are its own name plus a letter (Bird.md2: BirdC, BirdL, BirdS, ...), like an object's
					var animations = ObjectAnimations.Find( Path.GetFileNameWithoutExtension( path ), files.Where( file => !string.Equals( file, path, StringComparison.OrdinalIgnoreCase ) ) );
					geometry = Build( model, entry, animations );
				}
			}
		}
		catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException or IOException )
		{
			Log.Warning( $"The tour ride's car mesh {name} could not be read: {exception.Message}" );
		}
		meshes[entry.ArchivePath] = geometry;
		return geometry;
	}

	private static CarGeometry Build( ModelFile model, ObjectCatalogEntry entry, IReadOnlyList<ObjectAnimationFile> animations )
	{
		var animates = true;
		try
		{
			_ = new ObjectAnimator( model );
		}
		catch ( ArgumentException )
		{
			// two meshes on one node: the animator cannot bind it, the mesh is drawn in its rest pose
			animates = false;
		}
		var vertexAnimated = animates ? ObjectRenderParts.FindVertexAnimatedMeshes( entry, model, animations ) : new bool[model.Meshes.Count];
		var shared = new List<(Model Model, int Node)>();
		var vertexParts = new List<(ObjectRenderPart Part, Material Material)>();
		var materials = new List<Material>();
		foreach ( var part in ObjectRenderParts.Build( entry, model, animates ? animations : Array.Empty<ObjectAnimationFile>() ) )
		{
			var slots = new Texture[16];
			Array.Fill( slots, Texture.Missing );
			for ( var index = 0; index < part.Textures.Length; index++ )
				slots[index] = LoadTexture( entry, part.Textures[index] );
			var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
			material.Set( TextureArrayName, slots );
			materials.Add( material );
			if ( part.MeshIndex >= 0 && vertexAnimated[part.MeshIndex] )
				vertexParts.Add( (part, material) );
			else
				shared.Add( (new Model( part.Vertices, part.Indices, material, false ), part.NodeIndex) );
		}
		var rest = ModelAnimationPlayer.ComputeRestTransforms( model );
		var heads = new List<int>();
		var flags = new List<uint>();
		for ( var id = 1; id <= MaximumSeats; id++ )
		{
			var node = RideHeadSpace.NodeForHead( model, id );
			if ( node < 0 || node >= rest.Length )
				break;
			heads.Add( node );
			flags.Add( model.Nodes[node].Type );
		}
		var smoke = TrackRenderer.SparksNode( model, 1 );
		return new CarGeometry( model, animations, shared, vertexParts, materials, rest, heads.ToArray(), flags.ToArray(), smoke >= 0 && smoke < rest.Length ? smoke : -1, animates );
	}

	private static Texture LoadTexture( ObjectCatalogEntry entry, string name )
	{
		var resolved = ObjectAssets.ResolveTexture( entry, name );
		if ( resolved == null )
			return Texture.Missing;
		var (fileSystem, path) = resolved.Value;
		if ( fileSystem == FileSystem )
			return new Texture( path, TextureFlags.Repeat );
		return Texture.FromFileSystem( fileSystem, path, TextureFlags.Repeat );
	}

	private static void Delete( CarVisual visual )
	{
		foreach ( var (entity, _) in visual.Parts )
			entity.Delete();
		foreach ( var (model, _, _, _) in visual.VertexModels )
			model.ReleaseBuffers();
		visual.Parts = new List<(ModelEntity, int)>();
		visual.VertexModels = new List<(Model, int, Vertex[], int)>();
		visual.Animator = null;
		visual.Controller = null;
		visual.Entry = null;
		visual.Car = null;
	}

	public void Dispose()
	{
		if ( disposed )
			return;
		disposed = true;
		foreach ( var visual in visuals.Values )
			Delete( visual );
		visuals.Clear();
		foreach ( var geometry in meshes.Values.Where( item => item != null ) )
		{
			foreach ( var (model, _) in geometry!.SharedParts )
				model.ReleaseBuffers();
			foreach ( var material in geometry.Materials )
				material.ReleaseOwnedResources();
		}
		meshes.Clear();
	}
}

/// <summary>The riders of the tour cars as head sources (the ride-head renderer asks for them each frame).</summary>
internal sealed class TourHeadSources : IHeadSourceProvider
{
	private readonly Func<TourRenderer?> renderer;

	public TourHeadSources( Func<TourRenderer?> renderer ) => this.renderer = renderer;

	public void Collect( List<HeadSource> sources, int? onlyAttraction ) => renderer()?.CollectHeads( sources, onlyAttraction );
}
