using System.Runtime.InteropServices;
using Veldrid;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Draws the litter lying on the park's cells as the original does: one flat sprite of <c>esprites.wad/Generic/Litter/SPR_LI</c> per dropped item, lying on the
/// ground at the cell offset the cell's two world draws give (<see cref="ParkLitter.SpriteOffset(int, int)"/>), its slot chosen by the litter's kind
/// (<see cref="Slot"/>), until a handyman sweeps it. Presentation only: nothing here touches the simulation.
/// </summary>
/// <remarks>
/// [BIN:TPW-EXE:0x004D93B0 littering cell] the drop creates the sprite (script 0x0074F050 + 0x28 × (kind − 1), flags 0x80000: a quad flat in the ground plane, 0x00589320), and only the drop does
/// (its callers are the guest's litter step 0x004FEC90 and the vomit step 0x004FFF20); the sweep 0x004D9620 deletes it. Litter that a saved park restores therefore has no sprite, and neither does
/// a stinkbomb's particle (<c>ParkLitterEffects</c>): both follow the cell's change events.
/// </remarks>
public sealed class LitterRenderer : Entity
{
	private const int VerticesPerSprite = 12;

	private readonly ModelHeightfield field;
	private readonly Func<ParkLitter?> source;
	private readonly GuestSpriteAtlas? atlas;
	private readonly Material<ObjectUniformBuffer>? material;
	private Texture? texture;
	private ParkLitter? litter;
	private readonly Dictionary<(int X, int Y), LitterType> sprites = new();
	private DeviceBuffer? vertexBuffer;
	private Vertex[] vertices = Array.Empty<Vertex>();
	private bool deleted;

	public bool Visible { get; set; } = true;
	/// <summary>Litter sprites drawn in the last frame.</summary>
	public int DrawnLitter { get; private set; }
	/// <summary>Litter sprites alive: the items dropped since the park started (or since its litter was replaced) and not yet swept.</summary>
	public int Count => sprites.Count;

	internal IReadOnlyList<Asset> OwnedAssets => (texture == null ? Array.Empty<Asset>() : new Asset[] { texture })
		.Concat( material == null ? Array.Empty<Asset>() : new Asset[] { material, material.Shader } ).ToArray();

	/// <param name="source">The park's litter, read again every frame because a load replaces the economy that owns it.</param>
	public LitterRenderer( ModelHeightfield field, Func<ParkLitter?> source, IReadOnlyList<GuestSpriteAtlas> atlases )
	{
		this.field = field;
		this.source = source;
		Name = "Litter";
		atlas = atlases.FirstOrDefault();
		if ( atlas == null )
			return;
		texture = GuestRenderer.LoadAtlas( atlas );
		material = new Material<ObjectUniformBuffer>( "content/shaders/sprite.shader" );
		material.Set( "Color", Enumerable.Repeat( texture, 8 ).ToArray() );
		TexturePackSwitch.PackChanged += ReloadAtlas;
	}

	private void ReloadAtlas() => ReloadAtlas( null );

	/// <summary>[EXT:texture-pack] Rebuilds the litter atlas after a pack change or a lazy download that names it, as the balloons' does.</summary>
	private void ReloadAtlas( IReadOnlyList<string>? paths )
	{
		if ( deleted || material == null || atlas == null || texture == null )
			return;
		if ( paths != null && !paths.Select( TexturePack.RelativeFileName ).Contains( TexturePack.RelativeFileName( atlas.PackKey ), StringComparer.Ordinal ) )
			return;
		var old = texture;
		texture = GuestRenderer.LoadAtlas( atlas );
		material.Set( "Color", Enumerable.Repeat( texture, 8 ).ToArray() );
		Asset.All.Remove( old );
		global::Global.Render.ScheduleDelete( () =>
		{
			old.NativeTextureView.Dispose();
			old.NativeTexture.Dispose();
		} );
	}

	/// <summary>The animation slot of a litter kind: the ordinary kinds 1-5 take slots 0-4, kind 6 and every unknown kind slot 5 (the "Dodgy litter type" script 0x0074F118), vomit (7) slot 6 and a stinkbomb (8) slot 7.</summary>
	// [BIN:TPW-EXE:0x004D943E litter switch] the jump table 0x004D95D4 sends kinds 1-5 to the scripts 0x0074F050, 0x0074F078, 0x0074F0A0, 0x0074F0C8, 0x0074F0F0 (slots 0-4), kind 6 and the default (after the "Dodgy litter type" log) to 0x0074F118 (slot 5), kind 7 to 0x0074F140 (slot 6) and kind 8 to 0x0074F168 (slot 7); every script sets the slot, flags 0x80000, frame 0 and loops
	public static int Slot( LitterType type ) => (byte)type switch
	{
		>= 1 and <= ParkLitter.OrdinaryKinds => (byte)type - 1,
		(byte)LitterType.Vomit => 6,
		(byte)LitterType.Stinkbomb => 7,
		_ => 5
	};

	/// <summary>The sprite's position in the original's world units (ten per cell): the cell's corner plus the 1..9 tenths the cell id's two draws give.</summary>
	// [BIN:TPW-EXE:0x004D94E2 litter sprite offset] x = column × 10 + (first draw % 9) + 1 and z = row × 10 + (second draw % 9) + 1, handed to the sprite by 0x00475480 with height 0 (the placement takes the ground height there, 0x00542010 through 0x004527F0)
	public static (int X, int Z) Anchor( int cellX, int cellY )
	{
		var (offsetX, offsetZ) = ParkLitter.SpriteOffset( cellX, cellY );
		return (cellX * 10 + offsetX, cellY * 10 + offsetZ);
	}

	/// <summary>
	/// The four corners of a litter sprite's quad in engine space, in the order top left, top right, bottom right, bottom left: the flat quad of
	/// <see cref="SpriteScale.FlatEdges"/> around the anchor, <see cref="SpriteScale.FlatLift"/> above the ground height there.
	/// </summary>
	public static (SVector3 TopLeft, SVector3 TopRight, SVector3 BottomRight, SVector3 BottomLeft) Corners( ModelHeightfield field, int cellX, int cellY, GuestAtlasFrame frame )
	{
		var (x, z) = Anchor( cellX, cellY );
		var anchor = GuestRenderer.CellToEngine( field, x / field.CellSizeX, z / field.CellSizeZ, SpriteScale.FlatLift );
		var (left, right, top, bottom) = SpriteScale.FlatEdges( frame );
		SVector3 At( float dx, float dz ) => anchor + new SVector3( dx * ObjectPlacement.ModelScale, dz * ObjectPlacement.ModelScale, 0f );
		return (At( left, top ), At( right, top ), At( right, bottom ), At( left, bottom ));
	}

	private void Follow( ParkLitter? next )
	{
		if ( ReferenceEquals( next, litter ) )
			return;
		if ( litter != null )
			litter.Changed -= OnChanged;
		sprites.Clear();
		litter = next;
		if ( litter != null )
			litter.Changed += OnChanged;
	}

	private void OnChanged( int x, int y, LitterType type )
	{
		if ( type == LitterType.None )
			sprites.Remove( (x, y) );
		else
			sprites[(x, y)] = type;
	}

	protected override void OnRender()
	{
		DrawnLitter = 0;
		Follow( source() );
		if ( !Visible || material == null || atlas == null || deleted || sprites.Count == 0 )
			return;
		var count = 0;
		foreach ( var ((x, y), type) in sprites )
		{
			if ( count + VerticesPerSprite > vertices.Length )
				Array.Resize( ref vertices, Math.Max( 64 * VerticesPerSprite, vertices.Length * 2 ) );
			var index = atlas.GetFrame( Slot( type ), 0, 0 );
			var frame = atlas.Frames[index];
			var (topLeft, topRight, bottomRight, bottomLeft) = Corners( field, x, y, frame );
			var u0 = frame.X / (float)atlas.Width;
			var u1 = (frame.X + frame.Width) / (float)atlas.Width;
			var v0 = frame.Y / (float)atlas.Height;
			var v1 = (frame.Y + frame.Height) / (float)atlas.Height;
			var up = SVector3.UnitZ;
			var a = new Vertex( topLeft, up, new SVector2( u0, v0 ) );
			var b = new Vertex( topRight, up, new SVector2( u1, v0 ) );
			var c = new Vertex( bottomRight, up, new SVector2( u1, v1 ) );
			var d = new Vertex( bottomLeft, up, new SVector2( u0, v1 ) );
			vertices[count++] = a; vertices[count++] = b; vertices[count++] = d;
			vertices[count++] = b; vertices[count++] = c; vertices[count++] = d;
			vertices[count++] = a; vertices[count++] = d; vertices[count++] = b;
			vertices[count++] = b; vertices[count++] = d; vertices[count++] = c;
			DrawnLitter++;
		}

		var stride = Vertex.SizeInBytes;
		var bytes = (uint)vertices.Length * stride;
		if ( vertexBuffer == null || vertexBuffer.SizeInBytes < bytes )
		{
			var old = vertexBuffer;
			if ( old != null )
				global::Global.Render.ScheduleDelete( old.Dispose );
			vertexBuffer = Device.ResourceFactory.CreateBuffer( new BufferDescription( bytes, BufferUsage.VertexBuffer | BufferUsage.Dynamic ) );
		}
		var commandList = global::Global.Render.CommandList;
		commandList.UpdateBuffer( vertexBuffer, 0, ref vertices[0], (uint)count * stride );
		material.Set( "ObjectUniformBuffer", new ObjectUniformBuffer
		{
			g_mModel = System.Numerics.Matrix4x4.Identity,
			g_mView = Camera.ViewMatrix,
			g_mProj = Camera.ProjMatrix,
			g_vCameraPos = Camera.Position,
			g_flTime = Time.Now
		}.WithFog().WithSpriteTint( Level?.Look ?? EveningLook.Day ) );
		commandList.SetVertexBuffer( 0, vertexBuffer );
		commandList.SetPipeline( material.Pipeline );
		var resourceSets = material.GetResourceSets();
		for ( uint index = 0; index < resourceSets.Length; ++index )
			commandList.SetGraphicsResourceSet( index, resourceSets[index] );
		commandList.Draw( (uint)count );
	}

	protected override void OnDelete()
	{
		deleted = true;
		Follow( null );
		TexturePackSwitch.PackChanged -= ReloadAtlas;
		var owned = texture;
		texture = null;
		if ( owned != null )
			Asset.All.Remove( owned );
		material?.ReleaseOwnedResources();
		global::Global.Render.ScheduleDelete( () =>
		{
			owned?.NativeTextureView.Dispose();
			owned?.NativeTexture.Dispose();
		} );
		var buffer = vertexBuffer;
		vertexBuffer = null;
		if ( buffer != null )
			global::Global.Render.ScheduleDelete( buffer.Dispose );
	}
}
