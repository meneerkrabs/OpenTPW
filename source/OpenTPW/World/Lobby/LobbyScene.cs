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
// [APPROX:UI-018] the enter sequence is not built, so the gates stay closed: tp.exe's enter (0x5e1e30, state 1) turns the camera to the island angle + π at 0.05 × step per frame, opens the gate with M1 (0x5e06e4) and zooms in under a black fade (state 2) until the radius is below 8 (0x5e1e50 starts the park); leaving early closes it with M2 (0x5e1350, 0x5e1890, 0x5e2360, 0x5e2660)
public sealed class LobbyScene : OriginalModelScene
{
	private const string TerrainDirectory = "/lobby/terrain";

	protected override string TextureDirectory => $"{TerrainDirectory}/textures";

	/// <summary>
	/// The lobby with the four theme islands, or with <paramref name="slots"/>: the theme island to stand at each
	/// ISLANDCAMERAPOSITION slot (the city lobby shows a city's top-10 parks by rank this way; null leaves a slot empty).
	/// </summary>
	// [BIN:TPW-EXE:0x005E3210 lobby island] islands are built from a theme's ISLAND entry at the caller's slot counter, at that slot's ISLANDCAMERAPOSITION and the ISLAND height and angle
	/// <param name="generator">The lobby generator (<see cref="LobbyEffects.Draw"/>) the flying meshes draw from; without it none fly.</param>
	public LobbyScene( LobbyDefinition definition, IReadOnlyList<LobbyIslandInfo?>? slots = null, Func<uint>? generator = null )
	{
		Definition = definition;
		FlyingMeshes = generator == null ? null : new LobbyFlyingMeshes( generator );
		var placed = slots == null
			? definition.Islands.Select( island => (Island: (LobbyIslandInfo?)island, Slot: island.Index) ).ToList()
			: slots.Select( ( island, slot ) => (Island: island, Slot: slot) ).ToList();
		try
		{
			var terrain = new ModelFile( $"{TerrainDirectory}/Base.MD2" );
			AddModel( terrain, NMatrix4x4.Identity );
			if ( terrain.Heightfield != null )
				AddHeightfield( terrain );
			foreach ( var (candidate, slot) in placed )
			{
				if ( candidate is not { } island )
					continue;
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
		}
		catch
		{
			Delete();
			throw;
		}
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
	// [APPROX:UI-018] the island clips are picked with rand() & 1 (0x5e11ea, CRT rand 0x67b5c0), whose process-wide stream is replaced by a local clock-seeded stand-in; the original also spends three rand() calls per island in its constructor (0x5dfb4a-0x5dfb7b, the default sky target), which the stand-in does not mirror; no world or guest stream is used
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
	/// One lobby frame of <paramref name="seconds"/>: the flying meshes take a lobby step (the frame's milliseconds, at most
	/// 500, × 0.01) and every flyer's model follows its instance and plays its wing cycle.
	/// </summary>
	public void Step( float seconds )
	{
		if ( !float.IsFinite( seconds ) || seconds < 0 )
			return;
		var step = Math.Clamp( seconds * 1000f, 0f, LobbyCameraMode.MaximumStepMilliseconds ) * LobbyCameraMode.StepPerMillisecond;
		FlyingMeshes?.Step( step );
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
		StepIslands( seconds );
	}

	/// <summary>
	/// Every island model whose clip has (almost) run out starts <c>M1</c> or <c>M2</c> at random, once; the island and
	/// gate models then take the frame.
	/// </summary>
	// [BIN:TPW-EXE:0x5e11d0 island clips] at the end of the islands update every island model whose channel 0 has under 1 ms left (0x5d8440 → 0x473f50) plays AnimID 5 sub rand() & 1, not looping (0x5d83f0(sub, 0)): M1 or M2
	private void StepIslands( float seconds )
	{
		foreach ( var model in IslandModels )
		{
			if ( model.Isle is { } isle && isle.Animator.RemainingMilliseconds( 0 ) < 1 )
			{
				var sub = Rand() & 1;
				var clip = model.IsleClips[sub];
				isle.Animator.Trigger( 0, clip, $"{model.Island.IslandModel}M{sub + 1}", animationId: 5, variant: sub, loop: false, speed: 1f );
			}
			model.Isle?.Advance( seconds );
			model.Gate?.Advance( seconds );
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
		var model = AddAnimatedModel( new ModelFile( path ), animatedMeshes );
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
