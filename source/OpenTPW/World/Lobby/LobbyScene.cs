using OpenTPW.FrontEnd;
using NMatrix4x4 = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// The original 3D front-end lobby: the lobby terrain (<c>lobby/terrain/Base.MD2</c>: sea meshes and
/// heightfield) with the four theme islands and their gates placed at the ISLANDCAMERAPOSITION of
/// their index, turned by the ISLAND angle (docs/UI.md). Node transforms are composed up the
/// hierarchy (parent-relative, docs/MD2-MODELS.md); MD2 (x, y-up, z) maps to engine (x, z, y-up).
/// The flying meshes follow <see cref="LobbyFlyingMeshes"/>; rain and lightning come from LobbyEffects and the weather renderer.
/// </summary>
// [APPROX:UI-018] the island and gate animations are not drawn
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
				foreach ( var name in new[] { island.IslandModel, island.GateModel } )
				{
					var file = FindModel( name );
					if ( file != null )
						AddModel( new ModelFile( file ), placement );
				}
				IslandCount++;
				Slots.Add( (island, slot) );
				// [BIN:TPW-EXE:0x5e36b3 flying-mesh centre] a FLYINGMESH group's centre is the island's camera target (x, height, z) (+0x3c) with 50 added to the height
				foreach ( var flight in island.Flights )
					FlyingMeshes?.Add( flight, new NVector3( x, island.Height, z ) );
			}
		}
		catch
		{
			Delete();
			throw;
		}
	}

	public LobbyDefinition Definition { get; }
	/// <summary>The bats and butterflies of the placed islands (null without a generator).</summary>
	public LobbyFlyingMeshes? FlyingMeshes { get; }

	private readonly Dictionary<LobbyFlyingMeshes.Instance, AnimatedModel> flyers = new();
	private readonly Dictionary<string, (ModelFile? Model, ModelAnimation? Clip)> flyerAssets = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>
	/// One lobby frame of <paramref name="seconds"/>: the flying meshes take a lobby step (the frame's milliseconds, at most
	/// 500, × 0.01) and every flyer's model follows its instance and plays its wing cycle.
	/// </summary>
	public void Step( float seconds )
	{
		if ( FlyingMeshes == null || !float.IsFinite( seconds ) || seconds < 0 )
			return;
		var step = Math.Clamp( seconds * 1000f, 0f, LobbyCameraMode.MaximumStepMilliseconds ) * LobbyCameraMode.StepPerMillisecond;
		FlyingMeshes.Step( step );
		foreach ( var group in FlyingMeshes.Groups )
		{
			foreach ( var instance in group.Instances )
			{
				if ( !flyers.TryGetValue( instance, out var flyer ) )
				{
					if ( CreateFlyer( group.Info.Mesh ) is not { } created )
						continue;
					flyers[instance] = flyer = created;
				}
				flyer.Placement = instance.Matrix;
				flyer.Advance( seconds );
			}
		}
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
		flyer.UpdateParts();
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
