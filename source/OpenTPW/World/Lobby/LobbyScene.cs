using OpenTPW.FrontEnd;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// The original 3D front-end lobby: the lobby terrain (<c>lobby/terrain/Base.MD2</c>: sea meshes and
/// heightfield) with the four theme islands and their gates placed at the ISLANDCAMERAPOSITION of
/// their index, turned by the ISLAND angle (docs/UI.md). Node transforms are composed up the
/// hierarchy (parent-relative, docs/MD2-MODELS.md); MD2 (x, y-up, z) maps to engine (x, z, y-up).
/// The island models play their own clips (<c>M1</c>/<c>M2</c>) and the flying meshes follow <see cref="LobbyFlyingMeshes"/>; rain and lightning come from LobbyEffects and the weather renderer.
/// </summary>
// [BIN:TPW-EXE:0x5e0490 enter sequence] LobbyCameraMode runs the enter (state 1: turn to the island angle + π at 0.05 × step per frame, open the gate with M1 (0x5e06e4); state 2: radius × (1 − 0.07 × step), height × (1 − 0.6 × step), black fade by the closed share of SPINRADIUS − 8, park start below 8, 0x5e1e50); the gate plays M1 here
public sealed class LobbyScene : OriginalModelScene
{
	internal const string TerrainDirectory = "/lobby/terrain";

	protected override string TextureDirectory => $"{TerrainDirectory}/textures";

	/// <summary>
	/// The lobby with the four theme islands, or with <paramref name="slots"/>: the theme island to stand at each
	/// ISLANDCAMERAPOSITION slot (the city lobby shows a city's top-10 parks by rank this way; null leaves a slot empty).
	/// </summary>
	// [BIN:TPW-EXE:0x005E3210 lobby island] islands are built from a theme's ISLAND entry at the caller's slot counter, at that slot's ISLANDCAMERAPOSITION and the ISLAND height and angle
	/// <param name="generator">The lobby generator (<see cref="LobbyEffects.Draw"/>) the flying meshes draw from; without it none fly.</param>
	/// <param name="flyerPercent">The share of each group's flyers that is spawned (the LOBBYOBJECTS option, 0x7858dc); 100 keeps the authored counts.</param>
	public LobbyScene( LobbyDefinition definition, IReadOnlyList<LobbyIslandInfo?>? slots = null, Func<uint>? generator = null, int flyerPercent = 100 )
	{
		Definition = definition;
		FlyingMeshes = generator == null ? null : new LobbyFlyingMeshes( generator, flyerPercent );
		var placed = slots == null
			? definition.Islands.Select( island => (Island: (LobbyIslandInfo?)island, Slot: island.Index) ).ToList()
			: slots.Select( ( island, slot ) => (Island: island, Slot: slot) ).ToList();
		try
		{
			var terrain = new ModelFile( $"{TerrainDirectory}/Base.MD2" );
			AddTerrain( terrain );
			if ( terrain.Heightfield != null )
				AddHeightfield( terrain );
			foreach ( var (candidate, slot) in placed )
			{
				if ( candidate is not { } parsed )
					continue;
				// [BIN:TPW-EXE:0x5dfb4a island constructor] the constructor draws three rand() values (each & 0xFF, shifted left 16) into the sky target +0x64, +0x68 and +0x6c before the file's SKYCOLOUR line is read; a file without one keeps them
				var (randomRed, randomGreen, randomBlue) = ((byte)(Rand() & 0xFF), (byte)(Rand() & 0xFF), (byte)(Rand() & 0xFF));
				var island = parsed.HasSkyColour ? parsed : parsed with { SkyColour = (randomRed, randomGreen, randomBlue), HasSkyColour = true };
				var (x, z) = definition.PositionOf( slot );
				var placement = NMatrix4x4.CreateRotationY( island.Angle * MathF.PI / 180 ) * NMatrix4x4.CreateTranslation( x, 0, z );
				// [BIN:TPW-EXE:0x5dfa60 island models] the island and its gate are animated lobby models (0x5d8870; the island with two animation channels, the gate with one), both at (x, 0, z) turned by the ISLAND angle
				var isle = AddIslandModel( island.IslandModel, placement );
				var gate = AddIslandModel( island.GateModel, placement );
				IslandModels.Add( new IslandModel( island, isle.Model, isle.Clips, gate.Model, gate.Clips ) );
				IslandCount++;
				Slots.Add( (island, slot) );
				// [BIN:TPW-EXE:0x5e36b3 flying-mesh centre] a FLYINGMESH group's centre is the island's camera target (x, height, z) (+0x3c) with 50 added to the height
				foreach ( var flight in island.Flights )
				{
					if ( FlyingMeshes?.Add( flight, new NVector3( x, island.Height, z ) ) is not { } group )
						continue;
					// Every flyer's model is built with the scene and shown once its instance spawns, so the scene's entities stay the same.
					var models = new List<AnimatedModel>();
					for ( var index = 0; index < group.Count; index++ )
						if ( CreateFlyer( group.Info.Mesh ) is { } flyer )
						{
							flyer.Shown = false;
							models.Add( flyer );
						}
					flyerModels[group] = models;
				}
			}
			QueueSigns();
		}
		catch
		{
			Delete();
			throw;
		}
	}

	private AnimatedModel? terrainModel;

	/// <summary>The animator of the lobby terrain's sea clip (<c>Basem1.MD2</c>), or null when the archive has none; for tests.</summary>
	internal ObjectAnimator? TerrainAnimator => terrainModel?.Animator;

	/// <summary>The number of meshes of the lobby terrain model (the range of mesh indexes <see cref="TerrainAnimator"/> answers for), for tests.</summary>
	internal int TerrainMeshCount => terrainModel?.File.Meshes.Count ?? 0;

	/// <summary>
	/// The lobby terrain: <c>Base.MD2</c> drawn as an animated model that plays its <c>M</c> clip (AnimID 5, sub 0: <c>Basem1.MD2</c>, else <c>Basem.MD2</c>), which scrolls the
	/// texture coordinates of the sea meshes (<see cref="TerrainClip"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x44e510 terrain clip] the terrain instance (the model 0x4504c0 loads with base.md2 through the object loader 0x461f10, flags 8; the loader binds the animation members <base><letter>[number] of the letter table 0x6fe6bc to it, so Basem1 is group 5) is updated once per lobby frame by 0x44e510 (0x473c70(instance, 1, 4), called from 0x54e72a, the lobby state of 0x54e360, after the clock refresh 0x473440; a park runs the same call through the render callback 0x54e2b0 instead, see TerrainUvAnimator); the instance has flag 4 clear, so the idle restart at the head of 0x473c70 (0x473490, then 0x4732a0 with AnimID 5, sub 0, flags 4, speed 1.0) starts the clip at the first update and again whenever its frame is past the total (the overshoot carried, 0x472f60), on the scaled clock 0x7b496c (channel flag 0x40 clear), and 0x4735d0 samples the clip's UV records (0x471860 → 0x4745c0) into the mesh record's coordinate table (+0x68) every frame
	private void AddTerrain( ModelFile terrain )
	{
		var clip = TerrainClip( out var name );
		var animatedMeshes = clip?.Tracks.Any( track => track.VertexAnimation != null || track.UvAnimation != null ) == true;
		terrainModel = AddAnimatedModel( terrain, animatedMeshes );
		if ( clip != null )
			terrainModel.Animator.Trigger( 0, clip, name, animationId: 5, variant: 0, loop: true, speed: 1f );
	}

	/// <summary>The clip the lobby terrain plays (AnimID 5, sub 0 of <c>Base</c>), or null when the lobby archive has none.</summary>
	private ModelAnimation? TerrainClip( out string name )
	{
		name = "";
		try
		{
			var members = FileSystem.GetFiles( TerrainDirectory ).Where( file => file.EndsWith( ".md2", StringComparison.OrdinalIgnoreCase ) ).ToArray();
			if ( ObjectAnimations.Resolve( ObjectAnimations.Find( "Base", members ), 5, 0 ) is not { } file )
				return null;
			name = file.Name;
			return new ModelFile( file.Path ).Clip;
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or NotSupportedException or InvalidOperationException )
		{
			Log?.Warning( $"Lobby terrain clip: {exception.Message}" );
			return null;
		}
	}

	/// <summary>A sign model of the scene, the island it stands on, and the lines and <c>.sgn</c> it is drawn with.</summary>
	private sealed record SignJob( string Model, LobbyIslandInfo Island, string SignPath, ParkNameLines Lines );

	private readonly List<SignJob> pendingSigns = new();
	private bool signFrameSeen;

	/// <summary>Sign models whose sign is still to be composed (<see cref="StepSigns"/>); they show the plain board until then.</summary>
	public int PendingSignCount => pendingSigns.Count;

	/// <summary>The models of the scene with sign slots.</summary>
	public IReadOnlyList<string> SignModels => SignSlotModels;

	/// <summary>Where the sign slots of <paramref name="model"/> take their texture from now (a runtime sign or the plain board, never the shipped art).</summary>
	internal IReadOnlyList<SignSlotSource> SignSlotSources( string model ) => SignSlotSourcesOf( model );
	private readonly Dictionary<string, ObjectSignImages> signs = new( StringComparer.OrdinalIgnoreCase );
	private readonly Dictionary<string, (Texture Left, Texture Right)> signTextures = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// Queues the sign of every island and gate model with sign slots: its theme's gate lines on its own <c>.sgn</c> (<see cref="LobbySigns"/>). A sign the session has
	/// composed before is bound at once; the others are composed one per frame from the lobby's second frame on (<see cref="StepSigns"/>), so the lobby appears without
	/// waiting for them (a sign costs about 0.2 s in the browser).
	/// </summary>
	private void QueueSigns()
	{
		string[] files;
		try
		{
			files = FileSystem.GetFiles( TerrainDirectory ).ToArray();
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidOperationException )
		{
			Log?.Warning( $"Lobby signs: {exception.Message}" );
			return;
		}
		var queued = new HashSet<string>( StringComparer.OrdinalIgnoreCase );
		// the gate rows' texts, read once for the lobby's islands
		var objectNames = LobbySigns.LoadObjectNames();
		foreach ( var (island, _) in Slots )
		{
			foreach ( var model in new[] { island.IslandModel, island.GateModel } )
			{
				if ( !HasSignSlots( model ) || !queued.Add( model ) )
					continue;
				var member = files.FirstOrDefault( file => file.EndsWith( ".sgn", StringComparison.OrdinalIgnoreCase )
					&& string.Equals( Path.GetFileNameWithoutExtension( file ), model, StringComparison.OrdinalIgnoreCase ) );
				if ( member == null )
				{
					Log?.Warning( $"Lobby sign: {TerrainDirectory} has no {model}.sgn; the board stays plain." );
					continue;
				}
				var job = new SignJob( model, island, $"{TerrainDirectory}/{Path.GetFileName( member )}", LobbySigns.Lines( island, objectNames ) );
				if ( LobbySigns.TryGetCached( job.SignPath, job.Lines, out var cached ) )
					Apply( job.Model, cached );
				else
					pendingSigns.Add( job );
			}
		}
	}

	/// <summary>
	/// One lobby frame of sign work: from the second frame on, while the camera is not entering a park (<paramref name="paused"/>; the zoom keeps the board then),
	/// composes one queued sign, the one on <paramref name="first"/> (the selected island) before the others.
	/// </summary>
	public void StepSigns( LobbyIslandInfo? first, bool paused )
	{
		// the first frame draws the lobby at once; composing starts with the next
		if ( !signFrameSeen )
		{
			signFrameSeen = true;
			return;
		}
		if ( !paused )
			ComposeNextSign( first );
	}

	/// <summary>Composes the next queued sign (the one on <paramref name="first"/> before the others) and binds it; false when none is left.</summary>
	public bool ComposeNextSign( LobbyIslandInfo? first = null )
	{
		if ( pendingSigns.Count == 0 )
			return false;
		var index = first == null ? -1 : pendingSigns.FindIndex( job => job.Island.Index == first.Index && string.Equals( job.Island.Level, first.Level, StringComparison.OrdinalIgnoreCase ) );
		var job = pendingSigns[Math.Max( index, 0 )];
		pendingSigns.Remove( job );
		var images = LobbySigns.Compose( job.SignPath, job.Lines );
		// a read that may pass is queued again until LobbySigns gives it up (it is then cached as absent)
		if ( images == null && !LobbySigns.TryGetCached( job.SignPath, job.Lines, out _ ) )
			pendingSigns.Add( job );
		Apply( job.Model, images );
		return true;
	}

	private void Apply( string model, ObjectSignImages? images )
	{
		if ( images == null )
			return;
		signs[model] = images;
		RebindSigns( model );
	}

	protected override ObjectSignImages? SignOf( string model ) => signs.TryGetValue( model, out var images ) ? images : null;

	protected override Texture? SignTexture( string model, int side )
	{
		if ( !signs.TryGetValue( model, out var images ) || side is not (0 or 1) )
			return null;
		// the scene's own textures from the session's composed halves: another lobby scene (the city lobby) may draw the same sign, and each releases its own
		if ( !signTextures.TryGetValue( model, out var textures ) )
			signTextures[model] = textures = (new Texture( images.Left, SignSurfaceComposer.HalfWidth, SignSurfaceComposer.Height ),
				new Texture( images.Right, SignSurfaceComposer.HalfWidth, SignSurfaceComposer.Height ));
		return side == 0 ? textures.Left : textures.Right;
	}

	protected override void OnDelete()
	{
		base.OnDelete();
		pendingSigns.Clear();
		foreach ( var (left, right) in signTextures.Values )
		{
			left.ReleaseOwned();
			right.ReleaseOwned();
		}
		signTextures.Clear();
		signs.Clear();
	}

	public LobbyDefinition Definition { get; }
	/// <summary>A placed island's animated island and gate models and their <c>&lt;model&gt;M&lt;n&gt;</c> clips (index n − 1; null when absent).</summary>
	protected sealed record IslandModel( LobbyIslandInfo Island, AnimatedModel? Isle, ModelAnimation?[] IsleClips, AnimatedModel? Gate, ModelAnimation?[] GateClips );

	/// <summary>The placed islands' models, in placement order.</summary>
	protected List<IslandModel> IslandModels { get; } = new();

	/// <summary>The clip each island model plays now (<c>M1</c> or <c>M2</c>, 0 before the first), for tests and the smoke test.</summary>
	public IReadOnlyList<int> IslandClips => IslandModels.Select( model => model.Isle?.Animator.GetAnimationId( 0 ) == 5 && model.Isle.Animator.GetClip( 0 ) is { } clip
		? clip.EndsWith( "2", StringComparison.Ordinal ) ? 2 : 1 : 0 ).ToList();

	/// <summary>
	/// The stand-in for the C runtime's <c>rand()</c> the island clips are picked with: the MSVC formula on a stream of
	/// its own seeded from the clock (the original's stream is shared with the rest of the game, so its values cannot be
	/// reproduced). Presentation only.
	/// </summary>
	// [BIN:TPW-EXE:0x5e11ea island clip] the island clips are picked with rand() & 1 (CRT rand 0x67b5c0), and each island's constructor spends three rand() calls on its default sky target (0x5dfb4a-0x5dfb7b), which this stream mirrors
	// [APPROX:UI-018] the CRT's process-wide rand() stream is replaced by a local clock-seeded stand-in (the original's is shared with the rest of the game and cannot be reproduced); no world or guest stream is used
	private uint crtRand = (uint)Environment.TickCount;
	private int Rand()
	{
		crtRand = unchecked(crtRand * 214013u + 2531011u);
		return (int)((crtRand >> 16) & 0x7FFF);
	}

	/// <summary>The bats and butterflies of the placed islands (null without a generator).</summary>
	public LobbyFlyingMeshes? FlyingMeshes { get; }

	private readonly Dictionary<LobbyFlyingMeshes.Group, List<AnimatedModel>> flyerModels = new();
	private readonly Dictionary<string, (ModelFile? Model, ModelAnimation? Clip)> flyerAssets = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// One lobby frame of <paramref name="seconds"/>: <see cref="StepFrame"/>, then <see cref="Animate"/> for the same time.
	/// </summary>
	public void Step( float seconds )
	{
		StepFrame( seconds );
		Animate( seconds );
	}

	/// <summary>
	/// The frame-driven part of a lobby frame of <paramref name="seconds"/>: the flying meshes take a lobby step (the frame's milliseconds, at most
	/// 500, x 0.01) and every island model whose clip has run out picks its next one. The game flow calls it once per reference frame
	/// (<see cref="ReferenceFrameClock"/>), as the original runs both once per rendered frame.
	/// </summary>
	// [BIN:TPW-EXE:0x5d5c50 lobby frame] the lobby steps its subsystems once per rendered frame in registration order: the flying-mesh system (vf2 0x5da3a0) and the islands component (vf2 0x5e0470, vtable 0x702cb0, slot 2 at 0x702cb8, which also picks the island clips, 0x5e11d0), both in the same frame
	// [EXT:frame-driven-at-60hz] the original steps these once per rendered frame; OpenTPW steps them at the 60 Hz reference, so the flyers and the island clips step as often as the effects and the camera (docs/COMPATIBILITY.md "Frame-driven timing")
	public void StepFrame( float seconds )
	{
		if ( !float.IsFinite( seconds ) || seconds < 0 )
			return;
		var step = Math.Clamp( seconds * 1000f, 0f, LobbyCameraMode.MaximumStepMilliseconds ) * LobbyCameraMode.StepPerMillisecond;
		FlyingMeshes?.Step( step );
		PickIslandClips();
	}

	/// <summary>
	/// The time-driven part of a lobby frame: every flyer's model follows its instance and plays its wing cycle, and the island and gate models
	/// advance their clips by <paramref name="seconds"/> of real time (the original samples a clip by the clock when it draws).
	/// </summary>
	public void Animate( float seconds )
	{
		if ( !float.IsFinite( seconds ) || seconds < 0 )
			return;
		terrainModel?.Advance( seconds );
		foreach ( var group in FlyingMeshes?.Groups ?? Array.Empty<LobbyFlyingMeshes.Group>() )
		{
			if ( !flyerModels.TryGetValue( group, out var models ) )
				continue;
			for ( var index = 0; index < group.Instances.Count && index < models.Count; index++ )
			{
				var flyer = models[index];
				if ( !flyer.Shown )
					flyer.Shown = true;
				flyer.Placement = group.Instances[index].Matrix;
				flyer.Advance( seconds );
			}
		}
		foreach ( var model in IslandModels )
		{
			model.Isle?.Advance( seconds );
			model.Gate?.Advance( seconds );
		}
	}

	/// <summary>
	/// Plays the gate clip of <paramref name="island"/>: <c>M1</c> opens it, <c>M2</c> closes it (not looping).
	/// </summary>
	// [BIN:TPW-EXE:0x5e06e4 gate open] when the camera has turned to the island's angle + π the enter sequence calls 0x5d83f0 (AnimID 5, sub 0, not looping) on the island's gate object: the gate's M1 clip; an early leave plays M2 (0x5e1350, 0x5e1890, 0x5e2360, 0x5e2660)
	public void PlayGate( LobbyIslandInfo island, bool open )
	{
		foreach ( var model in IslandModels )
		{
			if ( !ReferenceEquals( model.Island, island ) && model.Island.Index != island.Index || model.Gate is not { } gate )
				continue;
			var sub = open ? 0 : 1;
			gate.Animator.Trigger( 0, model.GateClips[sub], $"{island.GateModel}M{sub + 1}", animationId: 5, variant: sub, loop: false, speed: 1f );
			return;
		}
	}

	/// <summary>The clip each gate model plays now (1 <c>M1</c>, 2 <c>M2</c>, 0 none), for tests.</summary>
	public IReadOnlyList<int> GateClips => IslandModels.Select( model => model.Gate?.Animator.GetAnimationId( 0 ) == 5 && model.Gate.Animator.GetClip( 0 ) is { } clip
		? clip.EndsWith( "2", StringComparison.Ordinal ) ? 2 : 1 : 0 ).ToList();

	/// <summary>
	/// Every island model whose clip has (almost) run out starts <c>M1</c> or <c>M2</c> at random, once.
	/// </summary>
	// [BIN:TPW-EXE:0x5e11d0 island clips] at the end of the islands update every island model whose channel 0 has under 1 ms left (0x5d8440 -> 0x473f50) plays AnimID 5 sub rand() & 1, not looping (0x5d83f0(sub, 0)): M1 or M2
	private void PickIslandClips()
	{
		foreach ( var model in IslandModels )
		{
			if ( model.Isle is { } isle && isle.Animator.RemainingMilliseconds( 0 ) < 1 )
			{
				var sub = Rand() & 1;
				var clip = model.IsleClips[sub];
				isle.Animator.Trigger( 0, clip, $"{model.Island.IslandModel}M{sub + 1}", animationId: 5, variant: sub, loop: false, speed: 1f );
			}
		}
	}

	/// <summary>
	/// Builds an island or gate model with its <c>&lt;name&gt;M1</c>..<c>M3</c> clips, or nothing when the lobby lacks the
	/// model; its vertices are rewritable when a clip moves them.
	/// </summary>
	private (AnimatedModel? Model, ModelAnimation?[] Clips) AddIslandModel( string name, NMatrix4x4 placement )
	{
		var clips = new ModelAnimation?[3];
		if ( FindModel( name ) is not { } path )
			return (null, clips);
		for ( var index = 0; index < clips.Length; index++ )
		{
			try
			{
				if ( FindModel( $"{name}M{index + 1}" ) is { } clipPath )
					clips[index] = new ModelFile( clipPath ).Clip;
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or NotSupportedException )
			{
				Log?.Warning( $"Lobby clip {name}M{index + 1}: {exception.Message}" );
			}
		}
		var animatedMeshes = clips.Any( clip => clip?.Tracks.Any( track => track.VertexAnimation != null || track.UvAnimation != null ) == true );
		var model = AddAnimatedModel( new ModelFile( path ), animatedMeshes, name );
		model.Placement = placement;
		model.UpdateParts();
		return (model, clips);
	}

	/// <summary>A model of <paramref name="mesh"/> playing its first clip in a loop, or null when the lobby lacks the mesh.</summary>
	// [BIN:TPW-EXE:0x5d986b flying-mesh clip] each instance loads the mesh with one animation channel (0x5d8870 → 0x4629d0 with 1) and plays AnimID 5, sub 0 (member <mesh>m1), looping at speed 1 (0x5d83f0(0, 1) → 0x4732a0(instance, 5, 0, 1, 1.0, channel 0))
	private AnimatedModel? CreateFlyer( string mesh )
	{
		if ( !flyerAssets.TryGetValue( mesh, out var assets ) )
		{
			ModelFile? model = null;
			ModelAnimation? clip = null;
			try
			{
				if ( FindModel( mesh ) is { } path )
					model = new ModelFile( path );
				if ( FindModel( $"{mesh}m1" ) is { } clipPath )
					clip = new ModelFile( clipPath ).Clip;
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or NotSupportedException )
			{
				Log?.Warning( $"Lobby flying mesh {mesh}: {exception.Message}" );
			}
			flyerAssets[mesh] = assets = (model, clip);
		}
		if ( assets.Model == null )
			return null;
		var flyer = AddAnimatedModel( assets.Model, animatedMeshes: assets.Clip != null );
		if ( assets.Clip != null )
			flyer.Animator.Trigger( 0, assets.Clip, $"{mesh}m1", animationId: 5, variant: 0, loop: true, speed: 1f );
		return flyer;
	}
	public int IslandCount { get; private set; }
	/// <summary>The islands drawn and the slot each stands at.</summary>
	public List<(LobbyIslandInfo Island, int Slot)> Slots { get; } = new();

	/// <summary>Engine-space centre of an island's camera orbit (its position at the ISLAND height).</summary>
	public Vector3 Target( LobbyIslandInfo island ) => Target( island, island.Index );

	/// <summary>Engine-space centre of the camera orbit around <paramref name="island"/> standing at <paramref name="slot"/>.</summary>
	public Vector3 Target( LobbyIslandInfo island, int slot )
	{
		var (x, z) = Definition.PositionOf( slot );
		return new Vector3( x, z, island.Height );
	}

	private static string? FindModel( string name )
	{
		try
		{
			return FileSystem.GetFiles( TerrainDirectory ).FirstOrDefault( file => file.EndsWith( ".md2", StringComparison.OrdinalIgnoreCase )
				&& string.Equals( Path.GetFileNameWithoutExtension( file ), name, StringComparison.OrdinalIgnoreCase ) );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidOperationException )
		{
			Log?.Warning( $"Lobby model {name}: {exception.Message}" );
			return null;
		}
	}

	/// <summary>The lobby heightfield (111×110 cells) with each cell's ground texture slot, like the park terrain.</summary>
	private void AddHeightfield( ModelFile model )
	{
		var field = model.Heightfield!;
		var node = model.Nodes.FirstOrDefault( candidate => string.Equals( candidate.Name, "heightfield", StringComparison.OrdinalIgnoreCase ) );
		var world = node == null ? NMatrix4x4.Identity : OriginalTerrain.GetWorldMatrix( model, node.Index );
		var names = new List<string>();
		var vertices = new List<Vertex>();
		var indices = new List<uint>();
		for ( var z = 0; z < field.CellCountZ; z++ )
		{
			for ( var x = 0; x < field.CellCountX; x++ )
			{
				var slot = field.GetCellTextureSlot( x, z );
				if ( slot < 0 || slot >= model.Textures.Count )
					continue;
				var name = Path.GetFileNameWithoutExtension( model.Textures[slot].FrameNames[0] );
				var textureIndex = names.FindIndex( existing => string.Equals( existing, name, StringComparison.OrdinalIgnoreCase ) );
				if ( textureIndex < 0 )
				{
					if ( names.Count >= MaximumTextureSlots )
						continue;
					names.Add( name );
					textureIndex = names.Count - 1;
				}
				var start = (uint)vertices.Count;
				var corners = new[] { (x, z, 0f, 0f), (x + 1, z, 1f, 0f), (x, z + 1, 0f, 1f), (x + 1, z + 1, 1f, 1f) };
				var positions = corners.Select( corner => ToEngine( NVector3.Transform(
					new NVector3( corner.Item1 * field.CellSizeX, field.GetCornerHeight( corner.Item1, corner.Item2 ), corner.Item2 * field.CellSizeZ ), world ) ) ).ToArray();
				var normal = NVector3.Normalize( NVector3.Cross( positions[1] - positions[0], positions[2] - positions[0] ) );
				if ( normal.Z < 0 )
					normal = -normal;
				for ( var corner = 0; corner < 4; corner++ )
					vertices.Add( new Vertex { Position = positions[corner], Normal = normal, TexCoords = new Vector2( corners[corner].Item3, corners[corner].Item4 ), TexIndex = textureIndex } );
				foreach ( var offset in new uint[] { 0, 2, 3, 3, 1, 0 } )
					indices.Add( start + offset );
			}
		}
		Flush( names, vertices, indices );
	}

}
