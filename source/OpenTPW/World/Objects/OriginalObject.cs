using System.Numerics;

namespace OpenTPW;

/// <summary>
/// Where an object stands on a park grid: the anchor cell (save record X/Y), rotation in degrees, the engine
/// position of grid corner (0, 0) and the engine height of the object's base. One cell is 10 MD2 units,
/// <see cref="CellSize"/> engine units.
/// </summary>
public readonly record struct ObjectPlacement( int X, int Y, int Rotation, System.Numerics.Vector2 GridOrigin, float BaseHeight )
{
	// [EXT:md2-engine-scale] Engine units: 1 MD2 unit = 0.2 (presentation scale shared with the terrain; the original has no such game rule) — OpenTPW's presentation convention (formerly RIDES-026)
	public const float ModelScale = 0.2f;
	public const float CellSize = 10 * ModelScale;

	/// <summary>
	/// Model-space (MD2 units, axes already swapped so XY is the ground) to engine matrix: the rigid rotation of
	/// <see cref="ObjectFootprint.ToGrid(int,int,int,System.Numerics.Vector2)"/>, the anchor offset, the 0.2 scale
	/// and the grid origin. Fixed items (Info.DontApplyOffset) use anchor (0, 0) without rotation: their
	/// models are authored in park coordinates (e.g. the Jungle gates span MD2 X 450..510, cells 45..51).
	/// </summary>
	public Matrix4x4 ModelToEngine
	{
		get
		{
			var rotation = CellRotation( Rotation );
			return rotation
				* Matrix4x4.CreateTranslation( X * 10, Y * 10, 0 )
				* Matrix4x4.CreateScale( ModelScale )
				* Matrix4x4.CreateTranslation( GridOrigin.X, GridOrigin.Y, BaseHeight );
		}
	}

	/// <summary>
	/// The rigid turn of a model by <paramref name="rotation"/> degrees (0, 90, 180, 270) in model units with the ground in XY, about the
	/// first cell (0..10, 0..10) so that cell maps onto itself (<see cref="ObjectFootprint.ToGrid(int,int,int,System.Numerics.Vector2)"/>).
	/// </summary>
	public static Matrix4x4 CellRotation( int rotation ) => rotation switch
	{
		0 => Matrix4x4.Identity,
		90 => new Matrix4x4( 0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 0, 10, 0, 1 ),
		180 => new Matrix4x4( -1, 0, 0, 0, 0, -1, 0, 0, 0, 0, 1, 0, 10, 10, 0, 1 ),
		270 => new Matrix4x4( 0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1, 0, 10, 0, 0, 1 ),
		_ => throw new ArgumentOutOfRangeException( nameof( rotation ), "Rotations are multiples of 90 degrees." )
	};

	/// <summary>Swaps MD2 Y (up) and Z on both sides of a node matrix, matching <see cref="ObjectAssets.ConvertAxes"/>.</summary>
	public static Matrix4x4 SwapAxes( Matrix4x4 node )
	{
		var swap = new Matrix4x4( 1, 0, 0, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 1 );
		return swap * node * swap;
	}
}

/// <summary>
/// A placed original ride, shop, sideshow, feature or fixed item: its original model hierarchy rendered
/// with original textures, animated by its original RSE script (<see cref="OriginalObjectRuntime"/>).
/// </summary>
public class OriginalObject : Entity
{
	/// <summary>The material's texture-array binding the object renderer uses (BuildModels).</summary>
	private const string TextureArrayName = "Color";

	/// <summary>
	/// One MD2 texture slot that a clip of this object animates with a texture-frame track: every frame's
	/// texture decoded once (null when no searched directory has the frame's art), where the slot's current
	/// frame is bound (each entry of the material's texture array that shows the slot) and the frame it shows.
	/// </summary>
	private sealed class FrameTextures
	{
		public required int Slot { get; init; }
		public required Texture?[] Frames { get; init; }
		public required List<(Material Material, int Slot)> Bindings { get; init; }
		public int Shown { get; set; }
	}

	private readonly List<(ModelEntity Entity, int NodeIndex)> parts = new();
	private readonly List<Model> models = new();
	/// <summary>Parts of vertex- or UV-animated meshes: their model, own vertex array and the pose version it shows.</summary>
	private readonly List<(Model Model, int MeshIndex, Vertex[] Vertices, int Version)> vertexParts = new();
	/// <summary>Texture slots a clip of this object animates with texture-frame tracks, with their render bindings.</summary>
	private readonly List<FrameTextures> frameTextures = new();
	private int textureFrameVersion = -1;
	private bool deleted;
	private int hiddenVersion = -1;

	public OriginalObject( ObjectCatalogEntry entry, ObjectPlacement placement, RideScriptWorld? world = null, int? seed = null, bool open = true )
		: this( entry, placement, world, seed, open, attractionId: null, seedScript: true )
	{
	}

	/// <summary>
	/// Constructor body. <paramref name="attractionId"/> null: the runtime allocates the world's next attraction id
	/// (the default placement path). A restored id (layout restore, <see cref="ParkObjects.AddRestored"/>) is this
	/// object's identity verbatim: the runtime is built by <see cref="OriginalObjectRuntime.CreateForRestore"/>,
	/// which moves the world's frontier past it without allocating — dead ids are skipped in constant time, never
	/// replayed. Everything else (validation, render init, self-cleanup on failure) is the constructor's own.
	/// </summary>
	internal OriginalObject( ObjectCatalogEntry entry, ObjectPlacement placement, RideScriptWorld? world, int? seed, bool open, int? attractionId, bool seedScript = true )
	{
		try
		{
			if ( !ObjectFootprint.IsValidRotation( placement.Rotation ) || !float.IsFinite( placement.BaseHeight ) )
				throw new ArgumentOutOfRangeException( nameof( placement ) );
			Placement = placement;
			CreationSeed = seed;
			Runtime = attractionId is { } restored
				? OriginalObjectRuntime.CreateForRestore( entry, world ?? new RideScriptWorld(), seedScript ? seed : null, open, restored )
				: new OriginalObjectRuntime( entry, world, seedScript ? seed : null, open );
			Name = entry.DisplayName;
			Position = System.Numerics.Vector3.Transform( System.Numerics.Vector3.Zero, placement.ModelToEngine );
			Vector3 ToSound(System.Numerics.Vector3 engine)
			{
				// Match UpdateListener: imported terrain is centred for rendering, but sound uses its original coordinates.
				if (Level?.OriginalPark?.Heightfield is { } field)
					engine += new System.Numerics.Vector3(OriginalParkPlacement.GetOrigin(field), 0);
				return GameAudio.SoundPosition(engine);
			}
			Runtime.SetEventOrigin(() => ToSound(System.Numerics.Vector3.Transform(System.Numerics.Vector3.Zero, Placement.ModelToEngine)));
			Runtime.SetEventAnchors(() => ToSound(FootprintMiddle()), ToSound);
			Runtime.SetParticleFootprint(ParticleFootprintOrNull);
			Runtime.BindParticlePlacement(placement);
			// the kart effects (sounds, particles) happen where the vehicle is: section units at 307.2 per model unit, on the grid, with the height the pose probes found
			Runtime.SetBumpPlacement(
				( x, y, height ) => new System.Numerics.Vector3( x / TrackRenderer.UnitsPerModelUnit * ObjectPlacement.ModelScale + Placement.GridOrigin.X, y / TrackRenderer.UnitsPerModelUnit * ObjectPlacement.ModelScale + Placement.GridOrigin.Y, Placement.BaseHeight + height * ObjectPlacement.ModelScale ),
				engine =>
				{
					if ( Level?.OriginalPark?.Heightfield is { } field )
						engine += new System.Numerics.Vector3( OriginalParkPlacement.GetOrigin( field ), 0 );
					return GameAudio.SoundPosition( engine );
				} );
			// the vehicles' voices and emitters are placed in the park grid's frame (the karts' and boats' section units, the tour cars' module units)
			Runtime.SetVehicleOrigin(
				() =>
				{
					var grid = new System.Numerics.Vector3( Placement.GridOrigin, 0 );
					if ( Level?.OriginalPark?.Heightfield is { } field )
						grid += new System.Numerics.Vector3( OriginalParkPlacement.GetOrigin( field ), 0 );
					return GameAudio.SoundPosition( grid );
				},
				() => Placement.GridOrigin );
			Runtime.SetTourStation( ComputeTourStation );
			BuildModels();
			UpdateTransforms();
		}
		catch
		{
			Delete();
			throw;
		}
	}

	/// <summary>
	/// The middle of the object's footprint in engine coordinates at the object's base height: the original's anchor for its screams and for an EVENT sound with a negative key.
	/// A fixed item (authored in park coordinates) has no footprint and uses its model origin.
	/// </summary>
	// [BIN:TPW-EXE:0x466b70 footprint middle] the middle of the footprint rectangle in cell*10 units (cell corners, so a one-cell object's middle is half a cell in); the height is the root node's +0x44 (its matrix's translation y, the object's height that 0x556a80 also reads), the object's base height here
	internal System.Numerics.Vector3 FootprintMiddle()
	{
		if ( Entry.IsFixedItem )
			return System.Numerics.Vector3.Transform( System.Numerics.Vector3.Zero, Placement.ModelToEngine );
		var (minX, minY, maxX, maxY) = ObjectFootprint.GetBounds( Entry.Shape, Placement.X, Placement.Y, Placement.Rotation );
		return new System.Numerics.Vector3(
			Placement.GridOrigin.X + (minX + maxX + 1) * 0.5f * ObjectPlacement.CellSize,
			Placement.GridOrigin.Y + (minY + maxY + 1) * 0.5f * ObjectPlacement.CellSize,
			Placement.BaseHeight );
	}

	/// <summary>The footprint for the type's particle effects: its middle and its size in cells; a fixed item has no footprint box and starts none (0x466B70 fails without one).</summary>
	private ParticleFootprint? ParticleFootprintOrNull()
	{
		if ( Entry.IsFixedItem )
			return null;
		var (minX, minY, maxX, maxY) = ObjectFootprint.GetBounds( Entry.Shape, Placement.X, Placement.Y, Placement.Rotation );
		return new ParticleFootprint( FootprintMiddle(), maxX - minX + 1, maxY - minY + 1 );
	}

	public ObjectCatalogEntry Entry => Runtime.Entry;
	public OriginalObjectRuntime Runtime { get; } = null!;
	public ObjectPlacement Placement { get; }
	/// <summary>
	/// The seed this object's script was created with — explicit for every <see cref="ParkObjects"/> placement, so a
	/// layout snapshot can recreate the same script stream (null: created without one, not snapshottable).
	/// </summary>
	public int? CreationSeed { get; }
	public RideVM? Script => Runtime.Script;
	/// <summary>Visitor side of the script (guests slice).</summary>
	public RideVisitorBridge Visitors => Runtime.Visitors;
	public bool IsOpen => Runtime.IsOpen;
	public bool IsAnimating => Runtime.Animator.IsPlaying;
	public IReadOnlyList<Matrix4x4> NodeTransforms => Runtime.Animator.NodeTransforms;
	public int PartCount => parts.Count;
	public bool IsDeleted => deleted;

	/// <summary>Occupied grid cells with their shape kinds (entrance, exit, ...).</summary>
	public IEnumerable<(int X, int Y, ObjectShapeCell Cell)> Cells => Entry.IsFixedItem
		? Enumerable.Empty<(int, int, ObjectShapeCell)>()
		: ObjectFootprint.GetCells( Entry.Shape, Placement.X, Placement.Y, Placement.Rotation );

	/// <summary>Entrance/exit cells and the outside cells guests queue at or leave to (guests slice).</summary>
	public IEnumerable<ObjectAccessPoint> AccessPoints => Entry.IsFixedItem
		? Enumerable.Empty<ObjectAccessPoint>()
		: ObjectFootprint.GetAccessPoints( Entry.Shape, Placement.X, Placement.Y, Placement.Rotation );

	/// <summary>The rendered sign this object's model shows on its <c>sign1</c>/<c>sign2</c> faces, or null when it has none.</summary>
	public ObjectSignImages? Sign { get; private set; }

	public void Open() => Runtime.Open();
	public void Close() => Runtime.Close();

	/// <summary>Engine-space matrix of a model node (e.g. a seat dummy) for other systems.</summary>
	public Matrix4x4 GetNodeTransform( int node ) => ObjectPlacement.SwapAxes( NodeTransforms[node] ) * Placement.ModelToEngine;

	/// <summary>
	/// The launch pad of a tour ride in the tour module's units (300 per model unit): the world position of the model's pad node (a dummy of class 0x800 and
	/// number 99; the ride origin when it has none) with each coordinate truncated to whole model units first, as the original's creation command does, and the placement rotation.
	/// </summary>
	// [BIN:TPW-EXE:0x005542FE TOUR 1] the node lookup 0x0044B220 (class 0x800, number 0x63), its world position 0x004386D0, each coordinate converted to an integer and multiplied by 300
	// [APPROX:TOUR-003] the pad is read from the model's rest pose (the original reads the live node instance); the placement's rotation is the original's instance angle for a built ride (0x00529E10 hands the build angle to the constructor 0x004DB090, stored in degrees at +0x10 by 0x004DB0CD and read by TOUR 1 at 0x00554385); evidence needed: the live pad node at TOUR 1 time and the rotation field the save loader restores for an imported tour ride
	internal TourStation ComputeTourStation()
	{
		var model = Runtime.Model;
		var point = Placement.ModelToEngine.Translation;
		for ( var index = 0; index < model.DummyAttributes.Count; index++ )
		{
			var attribute = model.DummyAttributes[index];
			if ( (attribute.Type & 0x800) == 0 || attribute.Value != 99 )
				continue;
			var node = model.Counts.Unknown46 + index;
			if ( node < model.Nodes.Count && node < NodeTransforms.Count )
				point = GetNodeTransform( node ).Translation;
			break;
		}
		var x = (point.X - Placement.GridOrigin.X) / ObjectPlacement.ModelScale;
		var y = (point.Y - Placement.GridOrigin.Y) / ObjectPlacement.ModelScale;
		var up = point.Z / ObjectPlacement.ModelScale;
		return new TourStation( (int)MathF.Truncate( x ) * 300, (int)MathF.Truncate( up ) * 300, (int)MathF.Truncate( y ) * 300, Placement.Rotation );
	}

	/// <summary>The station frame of a coaster station (<see cref="CoasterStationLayout.FromModel"/>) at the model's rest pose, or null when its model lacks a dummy.</summary>
	internal CoasterStationLayout? ComputeCoasterStationLayout() => CoasterStationLayout.FromModel( Runtime.Model, NodeTransforms, Placement );

	/// <summary>One fixed simulation tick.</summary>
	public void Simulate( double deltaSeconds )
	{
		if ( deleted )
			return;
		Runtime.Simulate( deltaSeconds );
		UpdateTransforms();
	}

	private void BuildModels()
	{
		var vertexAnimated = ObjectRenderParts.FindVertexAnimatedMeshes( Entry, Runtime.Model );
		// The model's multi-frame texture slots that one of the object's clips addresses with a texture-frame
		// track (RIDES-035): each frame decodes once, and the part bindings record which material slots show them.
		var frameSlots = FindFrameAnimatedSlots( Entry, Runtime.Model );
		foreach ( var frames in frameSlots.Values )
			Runtime.Animator.SetTextureFrameAvailability( frames.Slot, frames.Frames.Select( texture => texture != null ).ToArray() );
		foreach ( var part in ObjectRenderParts.Build( Entry, Runtime.Model ) )
		{
			var slots = new Texture[16];
			Array.Fill( slots, Texture.Missing );
			// Runtime sign faces replace the shipped placeholders, including fixed gate faces.
			var hasSignSlot = part.Textures.Any( ObjectSigns.IsSignSlot );
			var gateSign = hasSignSlot && Entry.IsFixedItem && Entry.ArchiveName.Equals( "gates", StringComparison.OrdinalIgnoreCase )
				? Level.Current?.GateSign?.Images : null;
			var sign = hasSignSlot ? gateSign ?? (!Entry.IsFixedItem ? ObjectSigns.Get( Entry ) : null) : null;
			var fallbackBaseSign = gateSign == null && Entry.IsFixedItem && Entry.ArchiveName.Equals( "gates", StringComparison.OrdinalIgnoreCase )
				&& hasSignSlot
				? new Texture( [38, 28, 18, 255], 1, 1 ) : null;
			if ( sign != null )
				Sign = sign;
			for ( var index = 0; index < part.Textures.Length; index++ )
			{
				var name = part.Textures[index];
				slots[index] = sign?.TextureFor( name )
					?? (fallbackBaseSign != null && ObjectSigns.IsSignSlot( name ) ? fallbackBaseSign : LoadTexture( Entry, name ));
			}
			var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
			material.Set( TextureArrayName, slots );
			// A texture-frame track swaps the frames of the model slots the part shows, unless a runtime sign
			// face owns that slot's texture.
			if ( part.TextureSlots != null )
				for ( var index = 0; index < part.Textures.Length && index < part.TextureSlots.Length; index++ )
				{
					if ( sign?.TextureFor( part.Textures[index] ) != null || (fallbackBaseSign != null && ObjectSigns.IsSignSlot( part.Textures[index] )) )
						continue;
					if ( frameSlots.TryGetValue( part.TextureSlots[index], out var frames ) )
						frames.Bindings.Add( (material, index) );
				}
			var dynamic = part.MeshIndex >= 0 && vertexAnimated[part.MeshIndex];
			var model = new Model( part.Vertices, part.Indices, material, dynamic );
			models.Add( model );
			// Build gives every object its own vertex arrays, so the part's array is this instance's to rewrite;
			// version -1 makes the first update show the pose of a clip the script may already have started.
			if ( dynamic )
				vertexParts.Add( (model, part.MeshIndex, part.Vertices, -1) );
			parts.Add( (new ModelEntity { Model = model, Name = $"{Entry.ArchiveName}:{part.Name}" }, part.NodeIndex) );
		}
		frameTextures.AddRange( frameSlots.Values.Where( frames => frames.Bindings.Count > 0 ) );
	}

	/// <summary>
	/// The model's texture slots with more than one frame name that an enabled texture-frame track of the object's
	/// animation members addresses, each with every frame decoded once (null when no searched directory has it).
	/// </summary>
	// [APPROX:MD2-902] A frame whose art no searched texture directory has (including Theme Park Inc tvsim slot 39 frame 3, lt1.tga) keeps the effective selected frame the slot currently shows (renderer registers decoded-art availability with the animator, so save history retains the visible frame) — evidence needed: the original's fallback for a missing frame file
	private static Dictionary<int, FrameTextures> FindFrameAnimatedSlots( ObjectCatalogEntry entry, ModelFile model )
	{
		var slots = new Dictionary<int, FrameTextures>();
		foreach ( var animation in entry.Animations )
		{
			ModelAnimation? clip;
			try
			{
				clip = ObjectAssets.LoadModel( entry.FileSystem, animation.Path ).Clip;
			}
			catch ( Exception exception ) when ( exception is InvalidDataException or NotSupportedException or IOException )
			{
				continue;
			}
			if ( clip?.TextureFramesEnabled != true )
				continue;
			foreach ( var track in clip.TextureFrameTracks )
			{
				if ( track.Slot >= model.Textures.Count || model.Textures[track.Slot].FrameNames.Count < 2 || slots.ContainsKey( track.Slot ) )
					continue;
				slots[track.Slot] = new FrameTextures
				{
					Slot = track.Slot,
					Frames = model.Textures[track.Slot].FrameNames.Select( name => TryLoadTexture( entry, name ) ).ToArray(),
					Bindings = new List<(Material, int)>()
				};
			}
		}
		return slots;
	}

	private static readonly Dictionary<string, Texture> bonusTextures = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Game-data textures go through the shared texture cache; bonus-content textures are decoded from their own root.</summary>
	private static Texture LoadTexture( ObjectCatalogEntry entry, string name ) => TryLoadTexture( entry, name ) ?? Texture.Missing;

	/// <summary>The texture of a material or frame name, or null when no searched directory has it.</summary>
	private static Texture? TryLoadTexture( ObjectCatalogEntry entry, string name )
	{
		var resolved = ObjectAssets.ResolveTexture( entry, name );
		if ( resolved == null )
			return null;
		var (fileSystem, path) = resolved.Value;
		if ( fileSystem == FileSystem )
			return new Texture( path, TextureFlags.Repeat );
		var key = fileSystem.GetAbsolutePath( path );
		if ( !bonusTextures.TryGetValue( key, out var texture ) )
		{
			using var stream = fileSystem.OpenRead( path );
			var data = new TextureFile( stream ).Data;
			texture = new Texture( data.Data, data.Width, data.Height, TextureFlags.Repeat );
			bonusTextures[key] = texture;
		}
		return texture;
	}

	private void UpdateTransforms()
	{
		UpdateVertices();
		UpdateTextures();
		var placement = Placement.ModelToEngine;
		var animator = Runtime.Animator;
		var nodes = animator.NodeTransforms;
		var hidden = animator.HiddenVersion != hiddenVersion;
		hiddenVersion = animator.HiddenVersion;
		foreach ( var (entity, node) in parts )
		{
			if ( hidden && node >= 0 )
				entity.Hidden = animator.IsNodeHidden( node );
			var transform = node < 0 ? placement : ObjectPlacement.SwapAxes( nodes[node] ) * placement;
			entity.TransformOverride = transform;
			entity.Position = new Vector3( transform.M41, transform.M42, transform.M43 );
		}
	}

	/// <summary>
	/// Swaps the bound texture of every material slot whose frame-animated model slot changed: the animator's
	/// winning texture-frame track picks the frame, the material rebinds the element at its next draw and
	/// nothing is reallocated while the frame stays the same. A frame without decoded art keeps the texture
	/// the slot currently shows.
	/// </summary>
	private void UpdateTextures()
	{
		if ( frameTextures.Count == 0 )
			return;
		var animator = Runtime.Animator;
		var version = animator.TextureFrameVersion;
		if ( version == textureFrameVersion )
			return;
		textureFrameVersion = version;
		foreach ( var frames in frameTextures )
		{
			var frame = animator.GetTextureFrame( frames.Slot );
			if ( frame == frames.Shown )
				continue;
			frames.Shown = frame;
			if ( frame >= frames.Frames.Length || frames.Frames[frame] is not { } texture )
				continue;
			foreach ( var (material, slot) in frames.Bindings )
				material.SetTextureArrayElement( TextureArrayName, slot, texture );
		}
	}

	/// <summary>Rewrites the parts whose mesh pose changed; the GPU copy happens when the part is next drawn.</summary>
	private void UpdateVertices()
	{
		var animator = Runtime.Animator;
		for ( var index = 0; index < vertexParts.Count; index++ )
		{
			var (model, mesh, vertices, version) = vertexParts[index];
			if ( animator.GetVertexVersion( mesh ) == version )
				continue;
			ObjectRenderParts.WritePositions( Runtime.Model.Meshes[mesh], animator.GetVertexPositions( mesh ), vertices );
			ObjectRenderParts.WriteTexCoords( Runtime.Model.Meshes[mesh], animator.GetTexCoords( mesh ), vertices );
			model.UpdateVertices( vertices );
			vertexParts[index] = (model, mesh, vertices, animator.GetVertexVersion( mesh ));
		}
	}

	protected override void OnDelete()
	{
		deleted = true;
		Runtime?.Stop();
		foreach ( var (entity, _) in parts )
			entity.Delete();
		parts.Clear();
		foreach ( var model in models )
		{
			model.ReleaseBuffers();
			model.Material.ReleaseOwnedResources();
		}
		models.Clear();
		vertexParts.Clear();
		frameTextures.Clear();
	}
}
