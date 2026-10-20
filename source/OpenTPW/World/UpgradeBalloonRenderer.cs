using System.Runtime.InteropServices;
using Veldrid;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Draws the balloon the original hangs over a ride whose upgrade has just been installed: the one frame of <c>esprites.wad/Generic/SpecialFX/SPR_SF</c> (a blue
/// balloon with a yellow up arrow), a camera-facing sprite whose hotspot stands on the ground at the middle of the cell in front of the ride's entrance, until the
/// next upgrade or the ride's removal. Presentation only: nothing here touches the simulation.
/// </summary>
/// <remarks>
/// [BIN:TPW-EXE:0x004DF8F0 upgrade installed] the completion raises the level byte (+0x50) by one, deletes the ride's old sprite (+0x54, 0x00475550) and, when the new level is 1 or 2,
/// creates sprite category 12 with the script 0x0074F3A0 (slot 0, frame 0, loop) at the front cell of the ride (0x004DE040) plus 0.5 in each axis, times 10 for the four right-angle rotations (0x004DFA54); the removal
/// 0x004DD0A0 deletes it with the ride. Nothing else creates the sprite (0x00475A10's callers), so a ride resumed from a save shows none until its next upgrade.
/// </remarks>
public sealed class UpgradeBalloonRenderer : Entity
{
	private const int VerticesPerSprite = 12;
	/// <summary>The levels whose completion hangs a balloon: the new level 1 or 2 (a third upgrade shows none).</summary>
	public static bool ShowsFor( int level ) => level is 1 or 2;

	/// <summary>One balloon: its anchor in cell units and whether the ride still stands.</summary>
	public sealed record Marker( float X, float Y, Func<bool> Alive );

	/// <summary>The balloons by economy instance: an upgrade replaces the ride's balloon, the ride's removal takes it.</summary>
	public sealed class Table
	{
		private readonly Dictionary<int, Marker> markers = new();
		public int Count => markers.Count;
		public IEnumerable<Marker> Markers => markers.Values;
		public bool Contains( int instance ) => markers.ContainsKey( instance );

		/// <summary>The upgrade of <paramref name="instance"/> was installed and its level is now <paramref name="level"/>.</summary>
		public void Completed( int instance, int level, (int X, int Y) frontCell, Func<bool> alive )
		{
			markers.Remove( instance );
			if ( ShowsFor( level ) )
			{
				var (x, y) = Anchor( frontCell );
				markers[instance] = new Marker( x, y, alive );
			}
		}

		/// <summary>Drops the balloons of rides that are gone.</summary>
		public void Prune()
		{
			foreach ( var instance in markers.Where( pair => !pair.Value.Alive() ).Select( pair => pair.Key ).ToList() )
				markers.Remove( instance );
		}

		public void Clear() => markers.Clear();
	}

	private readonly ModelHeightfield field;
	private readonly GuestSpriteAtlas? atlas;
	private readonly Material<ObjectUniformBuffer>? material;
	private Texture? texture;
	private readonly Table markers = new();
	private DeviceBuffer? vertexBuffer;
	private Vertex[] vertices = Array.Empty<Vertex>();
	private bool deleted;

	public bool Visible { get; set; } = true;
	/// <summary>Balloons drawn in the last frame.</summary>
	public int DrawnBalloons { get; private set; }
	/// <summary>Balloons alive.</summary>
	public int Count => markers.Count;

	internal IReadOnlyList<Asset> OwnedAssets => (texture == null ? Array.Empty<Asset>() : new Asset[] { texture })
		.Concat( material == null ? Array.Empty<Asset>() : new Asset[] { material, material.Shader } ).ToArray();

	public UpgradeBalloonRenderer( ModelHeightfield field, IReadOnlyList<GuestSpriteAtlas> atlases )
	{
		this.field = field;
		Name = "Upgrade balloons";
		atlas = atlases.FirstOrDefault();
		if ( atlas == null )
			return;
		texture = GuestRenderer.LoadAtlas( atlas );
		material = new Material<ObjectUniformBuffer>( "content/shaders/sprite.shader" );
		material.Set( "Color", Enumerable.Repeat( texture, 8 ).ToArray() );
		TexturePackSwitch.PackChanged += ReloadAtlas;
	}

	private void ReloadAtlas() => ReloadAtlas( null );

	/// <summary>[EXT:texture-pack] Rebuilds the atlas after a pack change or a lazy download that names it, as the balloons' does.</summary>
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

	/// <summary>The balloon's anchor in cell units: the middle of the front cell.</summary>
	public static (float X, float Y) Anchor( (int X, int Y) frontCell ) => (frontCell.X + 0.5f, frontCell.Y + 0.5f);

	/// <summary>
	/// An upgrade of the ride with economy instance <paramref name="instance"/> has been installed and its level is now <paramref name="level"/>: the old balloon goes,
	/// and a new one hangs over <paramref name="frontCell"/> for level 1 or 2. <paramref name="alive"/> says whether the ride still stands.
	/// </summary>
	public void Completed( int instance, int level, (int X, int Y) frontCell, Func<bool> alive ) => markers.Completed( instance, level, frontCell, alive );

	/// <summary>The park's rides were replaced (a load): no balloon survives, as the original's sprites are not saved.</summary>
	public void Clear() => markers.Clear();

	protected override void OnRender()
	{
		DrawnBalloons = 0;
		if ( !Visible || material == null || atlas == null || deleted || markers.Count == 0 )
			return;
		markers.Prune();
		if ( markers.Count == 0 )
			return;
		var view = Camera.ViewMatrix;
		var right = new SVector3( view.M11, view.M21, view.M31 );
		var up = new SVector3( view.M12, view.M22, view.M32 );
		var forward = -new SVector3( view.M13, view.M23, view.M33 );
		var count = 0;
		var frame = atlas.Frames[atlas.GetFrame( 0, 0, 0 )];
		foreach ( var marker in markers.Markers )
		{
			if ( count + VerticesPerSprite > vertices.Length )
				Array.Resize( ref vertices, Math.Max( 16 * VerticesPerSprite, vertices.Length * 2 ) );
			var anchor = GuestRenderer.CellToEngine( field, marker.X, marker.Y );
			var unit = GuestRenderer.UnitsPerPixel;
			var x0 = frame.OriginX * unit;
			var x1 = (frame.OriginX + frame.Width) * unit;
			var yTop = -frame.OriginY * unit;
			var yBottom = -(frame.OriginY + frame.Height) * unit;
			var u0 = frame.X / (float)atlas.Width;
			var u1 = (frame.X + frame.Width) / (float)atlas.Width;
			var v0 = frame.Y / (float)atlas.Height;
			var v1 = (frame.Y + frame.Height) / (float)atlas.Height;
			var topLeft = new Vertex( anchor + right * x0 + up * yTop, -forward, new SVector2( u0, v0 ) );
			var topRight = new Vertex( anchor + right * x1 + up * yTop, -forward, new SVector2( u1, v0 ) );
			var bottomLeft = new Vertex( anchor + right * x0 + up * yBottom, -forward, new SVector2( u0, v1 ) );
			var bottomRight = new Vertex( anchor + right * x1 + up * yBottom, -forward, new SVector2( u1, v1 ) );
			vertices[count++] = topLeft; vertices[count++] = topRight; vertices[count++] = bottomLeft;
			vertices[count++] = topRight; vertices[count++] = bottomRight; vertices[count++] = bottomLeft;
			vertices[count++] = topLeft; vertices[count++] = bottomLeft; vertices[count++] = topRight;
			vertices[count++] = topRight; vertices[count++] = bottomLeft; vertices[count++] = bottomRight;
			DrawnBalloons++;
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
