using System.Numerics;

namespace OpenTPW;

/// <summary>
/// Where an object stands on a park grid: the anchor cell (save record X/Y), rotation in degrees, the engine
/// position of grid corner (0, 0) and the engine height of the object's base. One cell is 10 MD2 units,
/// <see cref="CellSize"/> engine units.
/// </summary>
public readonly record struct ObjectPlacement( int X, int Y, int Rotation, System.Numerics.Vector2 GridOrigin, float BaseHeight, bool LegacyBaseHeight = false )
{
	// LegacyBaseHeight: BaseHeight was restored from a save made before the vertex-midpoint rule (the mean ground height of the footprint's cells), so the next save writes no rule and still restores it.
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
	/// <see cref="ModelToEngine"/> composed with <see cref="DetMatrix.Multiply"/> (plain multiplies and adds), for simulation code that must compute the same bits on
	/// every runtime (docs/DETERMINISM.md, "Browser and native").
	/// </summary>
	public Matrix4x4 DeterministicModelToEngine =>
		DetMatrix.Multiply( DetMatrix.Multiply( DetMatrix.Multiply( CellRotation( Rotation ), Matrix4x4.CreateTranslation( X * 10, Y * 10, 0 ) ),
			Matrix4x4.CreateScale( ModelScale ) ), Matrix4x4.CreateTranslation( GridOrigin.X, GridOrigin.Y, BaseHeight ) );

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
public class OriginalObject : Entity, IPointLightSource, IObjectPickShape
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
	/// <summary>The skirt and edge conform of this object's floor, or null (a fixed item, a park without terrain).</summary>
	private ObjectFloor? floor;
	/// <summary>Per render part: its material and the MD2 texture slot each entry of the material's texture array shows (the floor composites replace those entries).</summary>
	private readonly List<(Material Material, int[] Slots)> slotBindings = new();
	/// <summary>The textures of this object's floor composites (<see cref="PlaceFloorBase"/>), released with the object.</summary>
	private readonly List<Texture> compositeTextures = new();
	/// <summary>The composites this object made: the cell, its word and the slot whose texture each one replaced (the pixels are on the GPU, not kept).</summary>
	internal IReadOnlyList<FloorComposite> FloorComposites { get; private set; } = Array.Empty<FloorComposite>();
	private readonly List<(int X, int Z)> floorWordCells = new();
	/// <summary>The landscape cells this object's placement wrote a word into (its skirt cells, then its refreshed footprint cells), which its removal restores.</summary>
	internal IReadOnlyList<(int X, int Z)> FloorWordCells => floorWordCells;
	/// <summary>The type's hoarding drawn at this object, or null when its type has none (docs/OBJECTS.md, "Hoarding").</summary>
	private ObjectHoardingRenderer? hoarding;
	private readonly List<Model> models = new();
	/// <summary>Parts of vertex- or UV-animated meshes: their model, own vertex array and the pose version it shows.</summary>
	private readonly List<(Model Model, int MeshIndex, Vertex[] Vertices, int Version)> vertexParts = new();
	/// <summary>Texture slots a clip of this object animates with texture-frame tracks, with their render bindings.</summary>
	private readonly List<FrameTextures> frameTextures = new();
	private bool texturesAnimating = true;

	/// <summary>Whether the detail option ANIMATINGTEXTURES lets the texture-frame tracks show: Low turns it off, Medium, High and the extended preset keep it on.</summary>
	// [BIN:TPW-EXE:0x0044D9CD render flag 0x40] the option record 4 (ANIMATINGTEXTURES, value 0 or 1; the records are 0x3C bytes from TEXTUREQUALITY at 0x744300, their values the words from 0x785874) clears (0) or sets (1) bit 0x40 of the render flags 0x8BD550
	private static bool TextureAnimationEnabled => IGraphicsSettings.Instance is not { } settings || settings.GetPresetValues( settings.Current.Preset ) is not { } detail || detail.AnimatingTextures != 0;

	/// <summary>The frame a texture slot shows: its track's frame while texture animation is on, else frame 0.</summary>
	// [BIN:TPW-EXE:0x0057A420 slot texture bind] each model texture slot binds frames[flags 0x40 of 0x8BD550 ? slot word +2 : 0] (0x00575F90), the slot's current frame that the clip update writes (0x00470C90)
	internal static int TextureFrameToShow( int frame, bool animating ) => animating ? frame : 0;
	private int textureFrameVersion = -1;
	private bool deleted;
	private int hiddenVersion = -1;
	private int poseVersion = -1;

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
			using ( LoadProfile.Measure( "object-runtime" ) )
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
			Runtime.SetNodeEngine( GetNodeTransform );
			// the train loops of a coaster lie at the camera car while the player rides the train
			if ( Runtime.Coaster?.TrainSounds is { } trainSounds )
				trainSounds.IsRidden = train => Level?.RideView is { } view && ReferenceEquals( view.Ride, this ) && view.Train == train.Index;
			using ( LoadProfile.Measure( "object-models" ) )
				BuildModels();
			// the type's hoarding (docs/OBJECTS.md, "Hoarding"), on the level's terrain at this footprint; its state lives in the runtime
			var level = Level ?? Level.Current;
			using ( LoadProfile.Measure( "object-hoarding" ) )
				hoarding = ObjectHoardingRenderer.Create( entry, Runtime.Model, placement, level?.OriginalPark?.Heightfield, level?.OriginalPark?.LevelName, Runtime.Hoarding, level?.HoardingCache );
			UpdateTransforms();
			// the instance joins the scene with a key table of its pose at creation (0x463851)
			Runtime.RefreshKeyPose();
			// [EXT:object-lights] an object whose model has light dummies shows them through its level's point lights
			if ( Runtime.Lights != null )
				Level?.PointLights?.Add( this );
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
		var model = Runtime.Model;
		return new ParticleFootprint( FootprintMiddle(), maxX - minX + 1, maxY - minY + 1, model.BoundsMax.Y - model.BoundsMin.Y );
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
	/// <summary>
	/// The object's thing id: the slot of the park's thing pool its constructor took (tp.exe 0x004DB090 → 0x0050AFE0 → 0x00516270), 0 for an object of a park without a pool. Distinct
	/// from <see cref="RideVisitorBridge.AttractionId"/>, the script world's own counter.
	/// </summary>
	public int ThingId { get; internal set; }
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

	/// <summary>The engine height of the model's bounding-box minimum (the cursor pick's window, <see cref="IObjectPickShape.PickBottom"/>).</summary>
	public float PickBottom => Runtime.Model.BoundsMin.Y * ObjectPlacement.ModelScale;

	/// <summary>The first face of the object's model at its current pose that the line meets, in the original's walk order (<see cref="ObjectPickModel"/>).</summary>
	public bool TryHitPickLine( System.Numerics.Vector3 origin, System.Numerics.Vector3 direction, out System.Numerics.Vector3 hit )
	{
		hit = default;
		if ( deleted )
			return false;
		var animator = Runtime.Animator;
		// the floor's vertices are this instance's conformed copy (the original's pick reads the instance's own vertices, 0x450F20); a vertex track's samples win over it, as in what is drawn (DrawnMesh)
		Func<int, IReadOnlyList<System.Numerics.Vector3>?> positions = floor == null ? animator.GetVertexPositionsInSimulation : mesh => animator.GetVertexPositionsInSimulation( mesh ) ?? floor.ConformedPositions( mesh );
		return ObjectPickModel.TryHitLine( Runtime.Model, NodeTransforms, animator.IsNodeHiddenInSimulation, positions, Placement.ModelToEngine, origin, direction, out hit );
	}

	/// <summary>
	/// The launch pad of a tour ride in the tour module's units (300 per model unit): the world position of the model's pad node (a dummy of class 0x800 and
	/// number 99; the ride origin when it has none) with each coordinate truncated to whole model units first, as the original's creation command does, and the placement rotation.
	/// </summary>
	// [BIN:TPW-EXE:0x005542FE TOUR 1] the node lookup 0x0044B220 (class 0x800, number 0x63), its world position 0x004386D0, each coordinate converted to an integer and multiplied by 300
	// [BIN:TPW-EXE:0x00554385 TOUR 1 pad rotation] the instance angle read by TOUR 1 is the thing's word +0x10, written in degrees by the constructor from the build angle (0x004DB0CD, handed over by the placement 0x00529E10) and, for a saved ride, read back from the save's mAngle (the attraction reader 0x004DB7D0 loads mAngle straight into +0x10), so the placement rotation is the instance angle for built and imported rides alike
	// [BIN:TPW-EXE:0x00554342 TOUR 1 pad node] the pad node is read live (0x0044B220 then 0x004386D0), but all four tour scripts run TOUR 1 before they trigger any clip (WAITANIM 0 0 on an idle channel, then TOUR 1: TourAssetTests.TheLaunchPadIsReadBeforeAnyClipPlays), so the live node instance is still the model's rest pose, which is what this reads
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
	/// <remarks>
	/// It reads <see cref="ObjectAnimator.LocalTransforms"/>, which hold the model's stored node matrices only until a clip moves a node; after that they are
	/// System.Numerics products. The frame is taken when the station is placed (<c>Level.OnCoasterStationPlaced</c>), before any clip has run, so it composes
	/// the exact stored matrices; a caller after the first animation tick would bring the platform's products into the pinned simulation.
	/// </remarks>
	internal CoasterStationLayout? ComputeCoasterStationLayout() => CoasterStationLayout.FromModel( Runtime.Model, Runtime.Animator.LocalTransforms, Placement );

	/// <summary>One fixed simulation tick.</summary>
	public void Simulate( double deltaSeconds ) => Simulate( deltaSeconds, null );

	/// <summary>One fixed simulation tick of the park's substep scheduler (<see cref="ParkObjects.Simulate(double, SimulationSubstep, OriginalObject?)"/>).</summary>
	internal void Simulate( double deltaSeconds, SimulationSubstep? substep )
	{
		if ( deleted )
			return;
		Runtime.Simulate( deltaSeconds, substep );
		UpdateTransforms();
	}

	/// <summary>
	/// Puts the instance's skirt on the landscape and its base under it while the procedural terrain textures are on (tp.exe <c>0x463060</c> from 0x46334C: the skirt cells take their words, then the
	/// composites of the footprint box are made, <c>0x57ACE0</c>). A type without a base (<see cref="ObjectSkirt.HasBase"/>) does neither. Called once, when the object is in the park: the cells of
	/// a neighbour inside the box and the cells this object's own demolition victims left are as they are then.
	/// </summary>
	/// <param name="words">The level's landscape words, which this writes the skirt words into.</param>
	/// <param name="ground">The level's procedural terrain, whose texturer builds the bases.</param>
	// [BIN:TPW-EXE:0x0046334C skirt base] after the placement 0x467030 and the conform 0x469940 the block runs when the creation flag has bit 8, [0x7A1A8C] has 0x4000 (the texturing is on) and the base record's flag 0x40 is set; it writes the skirt words and calls 0x57ACE0
	internal void PlaceFloorBase( OriginalCellWords words, ProceduralTerrain ground, HoardingTerrain terrain )
	{
		if ( deleted || floor is not { Skirt: { HasBase: true } skirt } )
			return;
		using var profile = LoadProfile.Measure( "object-floor-base" );
		var (originX, originZ) = ObjectFloorBase.OriginCell( Placement, terrain );
		var model = Runtime.Model;
		var cache = (Level ?? Level.Current)?.HoardingCache;
		foreach ( var cell in ObjectFloorBase.SkirtWords( skirt, originX, originZ, Placement.Rotation, words.Word ) )
		{
			words.Write( cell.X, cell.Z, cell.Word );
			floorWordCells.Add( (cell.X, cell.Z) );
		}
		var box = ObjectFloorBase.Box( skirt.Width, skirt.Height, Placement.Rotation, originX, originZ );
		var composites = ObjectFloorBase.Compose( words, box, model.Textures.Count, ground.Texturer,
			slot => slot < model.Textures.Count && model.Textures[slot].FrameNames.Count > 0
				? (cache != null ? cache.FloorArt( Entry, model.Textures[slot].FrameNames[0] ) : DecodeSlotArt( Entry, model.Textures[slot].FrameNames[0] )) : null );
		LoadProfile.Count( "floor composites", composites.Count );
		LoadProfile.Count( "floor composite bytes", composites.Sum( composite => (long)composite.Rgba.Length ) );
		if ( composites.Count == 0 )
			return;
		// the last composite of a slot is the one the slot shows (a slot named by two cells of the box); the textures of the earlier ones are not kept
		var size = ground.Size;
		var shown = new Dictionary<int, Texture>();
		foreach ( var composite in composites )
		{
			if ( shown.TryGetValue( composite.Slot, out var earlier ) )
			{
				compositeTextures.Remove( earlier );
				earlier.ReleaseOwned();
			}
			var texture = Texture.CreatePage( size, size );
			// the composite is made in the original's device order (row r at v = r / N); the renderer keeps its textures in the order the files decode, which is the reverse (its shader reads 1 - v)
			texture.UpdateRegion( 0, 0, size, size, ObjectFloorBase.FlipRows( composite.Rgba, size, size ) );
			compositeTextures.Add( texture );
			shown[composite.Slot] = texture;
		}
		foreach ( var (material, slots) in slotBindings )
			for ( var index = 0; index < slots.Length; index++ )
				if ( shown.TryGetValue( slots[index], out var texture ) )
					material.SetTextureArrayElement( TextureArrayName, index, texture );
		// the pixels are uploaded: keep what identifies each composite, not its bytes
		FloorComposites = composites.Select( composite => composite with { Rgba = Array.Empty<byte>() } ).ToArray();
	}

	/// <summary>
	/// The cell refresh of this object's footprint (tp.exe <c>0x5365D0</c> through <c>0x46DF50</c> mode 8, once the cells are typed): every footprint cell takes the hole word, which clears bit 0x80 of the
	/// skirt cells, so a later object's composite scan does not compose them. A live placement refreshes at once, after the constructor; a park load creates every instance first and refreshes
	/// every cell afterwards (<c>0x415140</c> → <c>0x4D83A0</c>).
	/// </summary>
	internal void RefreshFootprint( OriginalCellWords words )
	{
		if ( deleted )
			return;
		foreach ( var (x, y, _) in Cells )
		{
			words.MarkHole( x, y );
			if ( !floorWordCells.Contains( (x, y) ) )
				floorWordCells.Add( (x, y) );
		}
	}

	/// <summary>
	/// The art of a model texture slot's first frame as the floor composite reads it (RGBA, in the order the file decodes), or null when no searched directory has it. The decoder follows the
	/// resolved file's extension, as <see cref="TryLoadTexture(ObjectCatalogEntry, string)"/>'s does: Theme Park Inc's own data resolves its slots to <c>.fsh</c> and <c>.tga</c> files
	/// (the science Log Mill's <c>ns_cnr04</c> is <c>sharetex/ns_cnr04.fsh</c>), which the WCT reader cannot read.
	/// </summary>
	internal static ObjectFloorBase.SlotArt? DecodeSlotArt( ObjectCatalogEntry entry, string name )
	{
		var resolved = ObjectAssets.ResolveTexture( entry, name );
		if ( resolved == null )
			return null;
		var (fileSystem, path) = resolved.Value;
		// a game-data .wct goes through DecodeWct for the texture pack; everything else decodes by its extension
		var (data, width, height) = fileSystem == FileSystem && path.HasExtension( ".wct" ) ? Texture.DecodeWct( path ) : Texture.DecodeImage( fileSystem, path );
		return new ObjectFloorBase.SlotArt( data, width, height );
	}

	private void BuildModels()
	{
		bool[] vertexAnimated;
		using ( LoadProfile.Measure( "object-parts" ) )
			vertexAnimated = ObjectRenderParts.FindVertexAnimatedMeshes( Entry, Runtime.Model );
		// The model's multi-frame texture slots that one of the object's clips addresses with a texture-frame
		// track (RIDES-035): each frame decodes once, and the part bindings record which material slots show them.
		Dictionary<int, FrameTextures> frameSlots;
		using ( LoadProfile.Measure( "object-textures" ) )
			frameSlots = FindFrameAnimatedSlots( Entry, Runtime.Model );
		foreach ( var frames in frameSlots.Values )
			Runtime.Animator.SetTextureFrameAvailability( frames.Slot, frames.Frames.Select( texture => texture != null ).ToArray() );
		IReadOnlyList<ObjectRenderPart> built;
		using ( LoadProfile.Measure( "object-parts" ) )
		{
			// the floor of the placed object: the skirt the type loader read and the instance's edge conform, over the level's terrain (docs/OBJECTS.md, "Floors")
			floor = ObjectFloor.Create( Entry, Runtime.Model, Placement, ( Level ?? Level.Current )?.OriginalPark?.Heightfield, ( Level ?? Level.Current )?.HoardingCache );
			built = ObjectRenderParts.Build( Entry, Runtime.Model, Entry.Animations, floor );
		}
		foreach ( var part in built )
		{
			var slots = new Texture[16];
			Array.Fill( slots, Texture.Missing );
			// Runtime sign faces replace the shipped placeholders, including fixed gate faces.
			var hasSignSlot = part.Textures.Any( ObjectSigns.IsSignSlot );
			// the object's own level: its gate and that gate's sign (made before the level's objects, SetupParkName then SetupObjects)
			var level = Level ?? Level.Current;
			SignSlotPlan plan;
			using ( LoadProfile.Measure( "object-sign" ) )
				plan = hasSignSlot
					? SignSlotPlan.For( Entry.IsFixedItem, Entry.InfoId, Entry.ArchivePath, level?.GateInfoId, level?.GateSign?.Images, () => ObjectSigns.Get( Entry ) )
					: SignSlotPlan.None;
			var sign = plan.Images;
			var fallbackBaseSign = plan.UsesFallback ? SignSlotPlan.FallbackBoard : null;
			if ( sign != null )
				Sign = sign;
			using ( LoadProfile.Measure( "object-textures" ) )
				for ( var index = 0; index < part.Textures.Length; index++ )
				{
					var name = part.Textures[index];
					slots[index] = sign?.TextureFor( name )
						?? (fallbackBaseSign != null && ObjectSigns.IsSignSlot( name ) ? fallbackBaseSign : LoadTexture( Entry, name ));
				}
			Material<ObjectUniformBuffer> material;
			using ( LoadProfile.Measure( "object-materials" ) )
			{
				material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
				material.Set( TextureArrayName, slots );
			}
			// A texture-frame track swaps the frames of the model slots the part shows, unless a runtime sign
			// face owns that slot's texture.
			if ( part.TextureSlots != null )
				for ( var index = 0; index < part.Textures.Length && index < part.TextureSlots.Length; index++ )
				{
					if ( sign?.TextureFor( part.Textures[index] ) != null || (fallbackBaseSign != null && ObjectSigns.IsSignSlot( part.Textures[index] )) )
						continue;
					if ( frameSlots.TryGetValue( part.TextureSlots[index], out var frames ) )
					{
						frames.Bindings.Add( (material, index) );
						// the model classifies the slot by its most translucent frame (Material.AddSlotAlternatives)
						material.AddSlotAlternatives( index, frames.Frames );
					}
				}
			if ( part.TextureSlots != null )
				slotBindings.Add( (material, part.TextureSlots) );
			var dynamic = part.MeshIndex >= 0 && vertexAnimated[part.MeshIndex];
			Model model;
			using ( LoadProfile.Measure( "object-materials" ) )
				model = new Model( part.Vertices, part.Indices, material, dynamic );
			models.Add( model );
			// Build gives every object its own vertex arrays, so the part's array is this instance's to rewrite;
			// version -1 makes the first update show the pose of a clip the script may already have started.
			if ( dynamic )
				vertexParts.Add( (model, part.MeshIndex, part.Vertices, -1) );
			// [EXT:winter-snow-surfaces] objects (roofs, trees, rides) take snow on their up-facing surfaces; a part on an animated node (it can move or turn) keeps its snow noise in model space, so the patches do not crawl over it
			parts.Add( (new ModelEntity { Model = model, Name = $"{Entry.ArchiveName}:{part.Name}", SnowSurface = SnowCover.GroundSurface, SnowNoiseInModelSpace = part.NodeIndex >= 0, Prepared = PresentDrawn }, part.NodeIndex) );
		}
		frameTextures.AddRange( frameSlots.Values.Where( frames => frames.Bindings.Count > 0 ) );
	}

	/// <summary>
	/// The model's texture slots with more than one frame name that an enabled texture-frame track of the object's
	/// animation members addresses, each with every frame decoded once (null when no searched directory has it).
	/// </summary>
	// [EXT:missing-frame-keeps-visible] A frame whose art no searched texture directory has (including Theme Park Inc tvsim slot 39 frame 3, lt1.tga) keeps the effective selected frame the slot currently shows (the renderer registers decoded-art availability with the animator, so save history retains the visible frame); a missing art file has no original fallback to match and showing nothing or failing would break the object, so keeping the visible frame is OpenTPW's design
	private static Dictionary<int, FrameTextures> FindFrameAnimatedSlots( ObjectCatalogEntry entry, ModelFile model )
	{
		var slots = new Dictionary<int, FrameTextures>();
		foreach ( var (slot, frames) in FrameAnimatedSlots( entry, model ) )
			slots[slot] = new FrameTextures
			{
				Slot = slot,
				Frames = frames.Select( name => TryLoadTexture( entry, name ) ).ToArray(),
				Bindings = new List<(Material, int)>()
			};
		return slots;
	}

	/// <summary>
	/// The model's multi-frame texture slots that an enabled texture-frame track of the type's animation members addresses, each once in the order the tracks first name it, with its
	/// frame names: the slots <see cref="FindFrameAnimatedSlots"/> decodes every frame of (a member that cannot be read is passed over), and the frames a type's first use reads.
	/// </summary>
	internal static IEnumerable<(int Slot, IReadOnlyList<string> Frames)> FrameAnimatedSlots( ObjectCatalogEntry entry, ModelFile model )
	{
		var seen = new HashSet<int>();
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
				if ( track.Slot >= model.Textures.Count || model.Textures[track.Slot].FrameNames.Count < 2 || !seen.Add( track.Slot ) )
					continue;
				yield return (track.Slot, model.Textures[track.Slot].FrameNames);
			}
		}
	}

	private static readonly Dictionary<string, Texture> bonusTextures = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>The bonus-content textures made so far, kept for the session on purpose (for the tests that count what a park leaves behind).</summary>
	internal static Texture[] SessionTextures() => bonusTextures.Values.ToArray();

	/// <summary>Game-data textures go through the shared texture cache; bonus-content textures are decoded from their own root.</summary>
	private static Texture LoadTexture( ObjectCatalogEntry entry, string name ) => TryLoadTexture( entry, name ) ?? Texture.Missing;

	/// <summary>The texture of a material or frame name, or null when no searched directory has it (also the buy window's preview of an object outside the game data).</summary>
	internal static Texture? TryLoadTexture( ObjectCatalogEntry entry, string name ) => TryLoadTexture( entry, name, aheadOfUse: false );

	/// <summary><see cref="TryLoadTexture(ObjectCatalogEntry, string)"/>, ahead of the object's own load when <paramref name="aheadOfUse"/> (<see cref="ObjectAssets.ResolveTexture(ObjectCatalogEntry, string, bool)"/>).</summary>
	internal static Texture? TryLoadTexture( ObjectCatalogEntry entry, string name, bool aheadOfUse )
	{
		var resolved = ObjectAssets.ResolveTexture( entry, name, aheadOfUse );
		if ( resolved == null )
			return null;
		var (fileSystem, path) = resolved.Value;
		if ( fileSystem == FileSystem )
			return new Texture( path, TextureFlags.Repeat );
		var key = fileSystem.GetAbsolutePath( path );
		if ( !bonusTextures.TryGetValue( key, out var texture ) )
		{
			texture = Texture.FromFileSystem( fileSystem, path, TextureFlags.Repeat );
			bonusTextures[key] = texture;
		}
		return texture;
	}

	private void UpdateTransforms()
	{
		UpdateVertices();
		UpdateTextures();
		var animator = Runtime.Animator;
		// the parts' transforms follow the animator's pose and the object's placement, which never changes: a tick on which the animator did not update leaves them as they are
		if ( animator.PoseVersion == poseVersion )
			return;
		poseVersion = animator.PoseVersion;
		var placement = Placement.ModelToEngine;
		var nodes = animator.NodeTransforms;
		var hidden = animator.HiddenVersion != hiddenVersion;
		hiddenVersion = animator.HiddenVersion;
		foreach ( var (entity, node) in parts )
		{
			if ( hidden && node >= 0 )
				entity.Hidden = animator.IsNodeHidden( node ) || PresentationHidden( node );
			var transform = node < 0 ? placement : ObjectPlacement.SwapAxes( PresentationPose( node, nodes[node] ) ) * placement;
			entity.TransformOverride = transform;
			entity.Position = new Vector3( transform.M41, transform.M42, transform.M43 );
		}
	}

	// [EXT:evening-torches-flicker] presentation-only local transforms of model nodes (the evening torches' flames, EveningFlames), null for a node drawn hidden: applied on the drawn parts only, never to the animator, the runtime,
	// the lights or a save
	private Dictionary<int, Matrix4x4?>? presentationNodes;

	private Matrix4x4 PresentationPose( int node, Matrix4x4 pose ) => presentationNodes != null && presentationNodes.TryGetValue( node, out var local ) && local is { } matrix ? matrix * pose : pose;

	private bool PresentationHidden( int node ) => presentationNodes != null && presentationNodes.TryGetValue( node, out var local ) && local == null;

	/// <summary>
	/// [EXT:evening-torches-flicker] Draws the parts of model node <paramref name="node"/> with <paramref name="local"/> applied in the node's own frame before its pose (a flame scaled about its base), or hides them when it is null
	/// (a flame by day). Only the node's own parts are changed, not its children's (a flame node has none). Presentation only: the animator's node transforms and hidden nodes, which the lamps, the snow and the simulation read, are unchanged.
	/// </summary>
	/// <summary>[EXT:evening-torches-flicker] Takes away the presentation transform of <paramref name="node"/> (<see cref="SetPresentationNodeTransform"/>): its parts are drawn at the animator's pose again, hidden only when the animator hides them.</summary>
	internal void ClearPresentationNodeTransform( int node )
	{
		if ( deleted || presentationNodes == null || !presentationNodes.Remove( node ) )
			return;
		var animator = Runtime.Animator;
		var nodes = animator.NodeTransforms;
		if ( node < 0 || node >= nodes.Count )
			return;
		var placement = Placement.ModelToEngine;
		foreach ( var (entity, index) in parts )
		{
			if ( index != node )
				continue;
			entity.Hidden = animator.IsNodeHidden( node );
			var transform = ObjectPlacement.SwapAxes( nodes[node] ) * placement;
			entity.TransformOverride = transform;
			entity.Position = new Vector3( transform.M41, transform.M42, transform.M43 );
		}
	}

	internal void SetPresentationNodeTransform( int node, Matrix4x4? local )
	{
		if ( deleted || node < 0 )
			return;
		var animator = Runtime.Animator;
		var nodes = animator.NodeTransforms;
		if ( node >= nodes.Count )
			return;
		( presentationNodes ??= new Dictionary<int, Matrix4x4?>() )[node] = local;
		var placement = Placement.ModelToEngine;
		foreach ( var (entity, index) in parts )
		{
			if ( index != node )
				continue;
			entity.Hidden = local == null || animator.IsNodeHidden( node );
			if ( local is not { } matrix )
				continue;
			var transform = ObjectPlacement.SwapAxes( matrix * nodes[node] ) * placement;
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
		var animating = TextureAnimationEnabled;
		if ( version == textureFrameVersion && animating == texturesAnimating )
			return;
		textureFrameVersion = version;
		texturesAnimating = animating;
		foreach ( var frames in frameTextures )
		{
			var frame = TextureFrameToShow( animator.GetTextureFrame( frames.Slot ), animating );
			if ( frame == frames.Shown )
				continue;
			frames.Shown = frame;
			if ( frame >= frames.Frames.Length || frames.Frames[frame] is not { } texture )
				continue;
			foreach ( var (material, slot) in frames.Bindings )
				material.SetTextureArrayElement( TextureArrayName, slot, texture );
		}
	}

	private Dictionary<int, ModelFile.Mesh>? drawnMeshes;

	/// <summary>The mesh as this instance draws it when its pose carries no samples of its own: the floor's conformed positions and normalised coordinates (<see cref="ObjectFloor"/>), else the model's.</summary>
	private ModelFile.Mesh DrawnMesh( int meshIndex )
	{
		var mesh = Runtime.Model.Meshes[meshIndex];
		if ( floor == null )
			return mesh;
		drawnMeshes ??= new Dictionary<int, ModelFile.Mesh>();
		if ( !drawnMeshes.TryGetValue( meshIndex, out var drawn ) )
			drawnMeshes[meshIndex] = drawn = floor.Apply( meshIndex, mesh );
		return drawn;
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
			var drawn = DrawnMesh( mesh );
			ObjectRenderParts.WritePositions( drawn, animator.GetVertexPositions( mesh ), vertices );
			ObjectRenderParts.WriteTexCoords( drawn, animator.GetTexCoords( mesh ), vertices );
			model.UpdateVertices( vertices );
			vertexParts[index] = (model, mesh, vertices, animator.GetVertexVersion( mesh ));
		}
	}

	/// <summary>
	/// [EXT:object-lights] The lights that are on, at their dummy nodes' world transforms at the current pose (so a light on a moving part moves with it), in engine units: the position from the node's
	/// engine matrix, the radius from model units at <see cref="ObjectPlacement.ModelScale"/>, the colour the opcodes last set.
	/// </summary>
	private Func<int, bool>? hiddenTest;

	void IPointLightSource.AppendPointLights( List<PointLight> into )
	{
		if ( deleted || Runtime.Lights is not { } bank )
			return;
		hiddenTest ??= Runtime.Animator.IsNodeHidden;
		bank.AppendTo( into, NodeTransforms, Placement.ModelToEngine, hiddenTest );
	}

	/// <summary>Instance flag 0x200000: the render callback ran for this instance in the previous frame's drawing, so this frame's list pass leaves it alone.</summary>
	private bool calledBack;
	/// <summary>Instance flag 0x800000: this frame's list pass updated the instance without posing it, so its next callback restores the stored pose first.</summary>
	private bool returnPending;
	private long callbackFrame = -1;

	/// <summary>
	/// A render frame, the list pass of the original (tp.exe <c>0x0044E410</c>, once per frame): an instance the callback did not pose in the previous frame's drawing is updated without posing
	/// and marked, one it did pose only loses its mark; the hoarding rises or sinks by the frame's real time and shows its state (<c>0x004548F0</c>).
	/// </summary>
	// [BIN:TPW-EXE:0x44E49A list pass] per instance of the table [0x7A4614..]: flag 0x200000 set → clear it (0x44E4E0); else, when it has an animation record (+0x14) and flags 0x1000/0x2000 are clear, 0x473C70(instance, 0, 8) updates its channels without sampling (0x4735D0 samples only for a posing call or instance flag 0x400000), hoardings 0x4548F0 for flag 0x20, then flag 0x800000 is set (0x44E4DC)
	// [BIN:TPW-EXE:0x44E410 ride view list pass] besides the two full passes (argument 3, 0x54E2B3 and 0x54E722) the frame step 0x54FA96 calls it with argument 2: with the ride view's option bit 0x4 of [0x790AB0] the ridden instance [0x790988] is updated and posed (0x473C70(instance, 1, 0), 0x44B510, 0x4548F0), which also rebuilds its key table
	// [RIDES-032 sim split] this simulation always updates and poses every instance in its tick (the saved node states, the picks and the hash must not depend on the camera); the list pass and the callback of the original decide only what is presented: when the key table is rebuilt (RefreshKeyPose) and when the stored pose is restored (ObjectAnimator.ResetToStored)
	protected override void OnUpdate()
	{
		if ( deleted )
			return;
		Runtime.Hoarding.Advance( Time.Delta );
		hoarding?.Update();
		if ( Level?.RideView is { } view && ReferenceEquals( view.Ride, this ) )
			Runtime.RefreshKeyPose();
		if ( calledBack )
			calledBack = false;
		else
			returnPending = true;
	}

	/// <summary>
	/// The render callback (tp.exe <c>0x00463030</c>): the mesh prepare found this instance inside the view for this frame. The first call of a frame rebuilds the key table and flags the instance,
	/// and, when the previous frame's list pass left it unposed, first restores the stored pose before it is posed again (<c>0x0044E380</c>).
	/// </summary>
	// [BIN:TPW-EXE:0x463030 render callback] 0x44E380(instance from the mesh object +0xB4, 1) then flag 0x200000 (0x463047..0x463052)
	// [BIN:TPW-EXE:0x44E380 pose] with the posing argument set and flag 0x800000 set: 0x4726D0 (ObjectAnimator.ResetToStored) unless flag 0x80000 is set (no creator on the placed ride's path sets it: 0x463060 builds the flags from 0x211F/0x231F and the argument bits 0x1, 0x2, 0x80), then flag 0x800000 cleared; then 0x473C70(instance, 1, 0) and its key table refresh (0x473E01) and the head tick 0x44B510
	internal void PresentDrawn()
	{
		if ( deleted )
			return;
		var frame = global::Global.Render?.FrameNumber ?? 0;
		if ( callbackFrame == frame && calledBack )
			return;
		callbackFrame = frame;
		if ( returnPending )
		{
			returnPending = false;
			Runtime.Animator.ResetToStored();
			UpdateTransforms();
		}
		calledBack = true;
		Runtime.RefreshKeyPose();
	}

	protected override void OnDelete()
	{
		deleted = true;
		hoarding?.Delete();
		hoarding = null;
		Level?.PointLights?.Remove( this );
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
		drawnMeshes = null;
		frameTextures.Clear();
		// the composites live with the instance (tp.exe 0x57A100 restores the slot textures and 0x57B070 frees them)
		foreach ( var texture in compositeTextures )
			texture.ReleaseOwned();
		compositeTextures.Clear();
		slotBindings.Clear();
		FloorComposites = Array.Empty<FloorComposite>();
	}
}
