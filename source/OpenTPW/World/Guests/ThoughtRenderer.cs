using System.Runtime.InteropServices;
using Veldrid;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Draws the thought bubbles of guests and staff (<see cref="Guest.BubbleThought"/>, <see cref="StaffMember.BubbleThought"/>) as camera-facing quads of the
/// original sprites (<c>esprites.wad/Generic/Thoughts</c>: <c>SPR_TB</c>, the guests' sixteen, and <c>SPR_TC</c>, ten more), 2.5 units above the head of
/// the one who thinks it (tp.exe <c>0x004FA030</c>). Presentation only: the simulation decides when a bubble shows and goes.
/// </summary>
public sealed class ThoughtRenderer : Entity
{
	private const int VerticesPerSprite = 12;
	/// <summary>The bubble's height above its thinker's ground position, in original units (tp.exe data <c>0x0075C804</c>).</summary>
	public const float Lift = 2.5f;
	/// <summary>Slots of the first bank; slot numbers past it continue in the second.</summary>
	public const int FirstBankSlots = 16;

	private readonly GuestSimulation simulation;
	private readonly ModelHeightfield field;
	private readonly IReadOnlyList<GuestSpriteAtlas> atlases;
	private readonly Func<IReadOnlyList<StaffMember>>? staff;
	private readonly Func<StaffMember, (float X, float Y)?>? staffCell;
	private readonly Material<ObjectUniformBuffer>? material;
	private readonly List<Texture> textures = new();
	private DeviceBuffer? vertexBuffer;
	private Vertex[] vertices = Array.Empty<Vertex>();
	private bool deleted;

	public bool Visible { get; set; } = true;
	/// <summary>
	/// Whether a guest's umbrella is drawn (<see cref="UmbrellaRenderer.ShowsBubble"/> while the umbrella renderer exists and drew it in the last frame); its bubble hangs <see cref="UmbrellaRenderer.BubbleLift"/> units above it instead of <see cref="Lift"/>, over
	/// the canopy. Null: no umbrellas, every bubble at <see cref="Lift"/>.
	/// </summary>
	public Func<Guest, bool>? UmbrellaShown { get; set; }
	/// <summary>Bubbles drawn in the last frame.</summary>
	public int DrawnBubbles { get; private set; }

	internal IReadOnlyList<Asset> OwnedAssets => textures.Cast<Asset>().Concat( material == null ? Array.Empty<Asset>() : new Asset[] { material, material.Shader } ).ToArray();

	/// <param name="atlases">The thought banks in the category's order: <c>SPR_TB</c>, then <c>SPR_TC</c>.</param>
	public ThoughtRenderer( GuestSimulation simulation, ModelHeightfield field, IReadOnlyList<GuestSpriteAtlas> atlases,
		Func<IReadOnlyList<StaffMember>>? staff = null, Func<StaffMember, (float X, float Y)?>? staffCell = null )
	{
		this.simulation = simulation;
		this.field = field;
		this.atlases = atlases;
		this.staff = staff;
		this.staffCell = staffCell;
		Name = "Thoughts";
		if ( atlases.Count == 0 )
			return;
		foreach ( var atlas in atlases.Take( 8 ) )
			textures.Add( GuestRenderer.LoadAtlas( atlas ) );
		material = new Material<ObjectUniformBuffer>( "content/shaders/sprite.shader" );
		material.Set( "Color", Enumerable.Range( 0, 8 ).Select( index => textures[Math.Min( index, textures.Count - 1 )] ).ToArray() );
		TexturePackSwitch.PackChanged += ReloadAtlases;
	}

	private void ReloadAtlases() => ReloadAtlases( null );

	/// <summary>[EXT:texture-pack] Rebuilds the bubble atlases after a pack change or a lazy download that names them, as the guests' atlases do.</summary>
	private void ReloadAtlases( IReadOnlyList<string>? paths )
	{
		if ( deleted || material == null || textures.Count == 0 )
			return;
		var keys = paths?.Select( TexturePack.RelativeFileName ).ToHashSet( StringComparer.Ordinal );
		var old = new List<Texture>();
		for ( var index = 0; index < textures.Count; index++ )
		{
			if ( keys != null && !keys.Contains( TexturePack.RelativeFileName( atlases[index].PackKey ) ) )
				continue;
			old.Add( textures[index] );
			textures[index] = GuestRenderer.LoadAtlas( atlases[index] );
		}
		if ( old.Count == 0 )
			return;
		material.Set( "Color", Enumerable.Range( 0, 8 ).Select( index => textures[Math.Min( index, textures.Count - 1 )] ).ToArray() );
		foreach ( var texture in old )
			Asset.All.Remove( texture );
		global::Global.Render.ScheduleDelete( () =>
		{
			foreach ( var texture in old )
			{
				texture.NativeTextureView.Dispose();
				texture.NativeTexture.Dispose();
			}
		} );
	}

	/// <summary>
	/// The sprite slot a thought's bubble script shows (tp.exe <c>0x0074F190</c> + 0x18 per script: field <c>+0x30</c> = the slot, frame 0, then a
	/// loop), or −1 for an unknown thought. Slots 0–15 are <c>SPR_TB</c>'s, 16 on <c>SPR_TC</c>'s.
	/// </summary>
	// [BIN:TPW-EXE:0x0050BE80 bubble scripts] ids 1-22 take the scripts 0x0074F250, 0x0074F280, 0x0074F2B0, 0x0074F2C8, 0x0074F310, 0x0074F268, 0x0074F2E0, 0x0074F208, 0x0074F190, 0x0074F1A8, 0x0074F1D8, 0x0074F1F0, 0x0074F1C0, 0x0074F220, 0x0074F238, 0x0074F298, 0x0074F2F8, 0x0074F328, 0x0074F340, 0x0074F358, 0x0074F370, 0x0074F388, whose first word (0x004768B0) sets these slots
	public static int Slot( int thought ) => thought switch
	{
		1 => 8, 2 => 10, 3 => 12, 4 => 13, 5 => 21, 6 => 9, 7 => 14, 8 => 5, 9 => 0, 10 => 1, 11 => 3,
		12 => 4, 13 => 2, 14 => 6, 15 => 7, 16 => 11, 17 => 15, 18 => 16, 19 => 17, 20 => 18, 21 => 19, 22 => 20,
		_ => -1
	};

	protected override void OnRender()
	{
		DrawnBubbles = 0;
		if ( !Visible || material == null || deleted )
			return;
		var view = Camera.ViewMatrix;
		var right = new SVector3( view.M11, view.M21, view.M31 );
		var up = new SVector3( view.M12, view.M22, view.M32 );
		var forward = -new SVector3( view.M13, view.M23, view.M33 );
		var count = 0;
		foreach ( var guest in simulation.Guests )
			if ( guest.BubbleThought != GuestThought.None && guest.IsVisible )
				AddBubble( ref count, (int)guest.BubbleThought, guest.DrawX, guest.DrawY, right, up, forward, UmbrellaRenderer.BubbleHeight( UmbrellaShown?.Invoke( guest ) == true ) );
		if ( staff != null && staffCell != null )
			foreach ( var member in staff() )
				if ( member.BubbleThought != 0 && staffCell( member ) is { } cell )
					AddBubble( ref count, member.BubbleThought, cell.X, cell.Y, right, up, forward, Lift );
		if ( count == 0 )
			return;

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
		}.WithFog() );
		commandList.SetVertexBuffer( 0, vertexBuffer );
		commandList.SetPipeline( material.Pipeline );
		var resourceSets = material.GetResourceSets();
		for ( uint index = 0; index < resourceSets.Length; ++index )
			commandList.SetGraphicsResourceSet( index, resourceSets[index] );
		commandList.Draw( (uint)count );
	}

	/// <summary>One camera-facing quad of the thought's slot with its hotspot <paramref name="lift"/> units above (<paramref name="x"/>, <paramref name="y"/>).</summary>
	private void AddBubble( ref int count, int thought, float x, float y, SVector3 right, SVector3 up, SVector3 forward, float lift )
	{
		var slot = Slot( thought );
		var bank = slot < FirstBankSlots ? 0 : 1;
		if ( slot < 0 || bank >= atlases.Count )
			return;
		var atlas = atlases[bank];
		var frame = atlas.Frames[atlas.GetFrame( slot - bank * FirstBankSlots, 0, 0 )];
		if ( count + VerticesPerSprite > vertices.Length )
			Array.Resize( ref vertices, Math.Max( 64 * VerticesPerSprite, vertices.Length * 2 ) );
		var anchor = GuestRenderer.CellToEngine( field, x, y, lift );
		var unit = GuestRenderer.UnitsPerPixel;
		var (x0, x1, yTop, yBottom) = frame.Extents( false, unit );
		var u0 = frame.X / (float)atlas.Width;
		var u1 = (frame.X + frame.Width) / (float)atlas.Width;
		var v0 = frame.Y / (float)atlas.Height;
		var v1 = (frame.Y + frame.Height) / (float)atlas.Height;
		var topLeft = new Vertex( anchor + right * x0 + up * yTop, -forward, new SVector2( u0, v0 ) ) { TexIndex = bank };
		var topRight = new Vertex( anchor + right * x1 + up * yTop, -forward, new SVector2( u1, v0 ) ) { TexIndex = bank };
		var bottomLeft = new Vertex( anchor + right * x0 + up * yBottom, -forward, new SVector2( u0, v1 ) ) { TexIndex = bank };
		var bottomRight = new Vertex( anchor + right * x1 + up * yBottom, -forward, new SVector2( u1, v1 ) ) { TexIndex = bank };
		vertices[count++] = topLeft; vertices[count++] = topRight; vertices[count++] = bottomLeft;
		vertices[count++] = topRight; vertices[count++] = bottomRight; vertices[count++] = bottomLeft;
		vertices[count++] = topLeft; vertices[count++] = bottomLeft; vertices[count++] = topRight;
		vertices[count++] = topRight; vertices[count++] = bottomLeft; vertices[count++] = bottomRight;
		DrawnBubbles++;
	}

	protected override void OnDelete()
	{
		deleted = true;
		TexturePackSwitch.PackChanged -= ReloadAtlases;
		var owned = textures.ToArray();
		textures.Clear();
		foreach ( var texture in owned )
			Asset.All.Remove( texture );
		material?.ReleaseOwnedResources();
		global::Global.Render.ScheduleDelete( () =>
		{
			foreach ( var texture in owned )
			{
				texture.NativeTextureView.Dispose();
				texture.NativeTexture.Dispose();
			}
		} );
		var buffer = vertexBuffer;
		vertexBuffer = null;
		if ( buffer != null )
			global::Global.Render.ScheduleDelete( buffer.Dispose );
	}
}
