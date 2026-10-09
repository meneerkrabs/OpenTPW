using System.Runtime.InteropServices;
using Veldrid;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Draws every visible guest as a camera-facing quad of its original kid sprite (one draw call, one dynamic
/// vertex buffer). Direction and walk-cycle frame come from <see cref="GuestSpriteAtlas"/>; the sprite's
/// hotspot (its feet) sits on the terrain heightfield. The world size of a sprite pixel is an approximation.
/// </summary>
public sealed class GuestRenderer : Entity
{
	/// <summary>Engine units per sprite pixel (kids ≈ 42 px tall ≈ 0.95 cell). Approximation; the original zoom mapping is unknown.</summary>
	public const float UnitsPerPixel = 0.045f;
	/// <summary>Walk-cycle frames per cell walked (8 frames per 0.8 cells). Approximation.</summary>
	public const float WalkFramesPerCell = 10f;
	private const int VerticesPerGuest = 12;

	private readonly GuestSimulation simulation;
	private readonly ModelHeightfield field;
	private readonly IReadOnlyList<GuestSpriteAtlas> atlases;
	private readonly Material<ObjectUniformBuffer>? material;
	private readonly List<Texture> textures = new();
	private DeviceBuffer? vertexBuffer;
	private Vertex[] vertices = Array.Empty<Vertex>();
	private bool deleted;

	public bool Visible { get; set; } = true;
	/// <summary>Guests drawn in the last frame.</summary>
	public int DrawnGuests { get; private set; }

	public GuestRenderer( GuestSimulation simulation, ModelHeightfield field, IReadOnlyList<GuestSpriteAtlas> atlases )
	{
		this.simulation = simulation;
		this.field = field;
		this.atlases = atlases;
		Name = "Guests";
		if ( atlases.Count == 0 )
			return;
		foreach ( var atlas in atlases.Take( 8 ) )
			textures.Add( new Texture( atlas.Pixels, atlas.Width, atlas.Height ) );
		var slots = new Texture[8];
		for ( var index = 0; index < slots.Length; index++ )
			slots[index] = textures[Math.Min( index, textures.Count - 1 )];
		material = new Material<ObjectUniformBuffer>( "content/shaders/sprite.shader" );
		material.Set( "Color", slots );
	}

	/// <summary>Engine position of a point in game-cell coordinates, on the heightfield (bilinear corner heights; holes keep their corner heights).</summary>
	public static SVector3 CellToEngine( ModelHeightfield field, float cellX, float cellY )
	{
		var x0 = Math.Clamp( (int)MathF.Floor( cellX ), 0, field.CellCountX - 1 );
		var y0 = Math.Clamp( (int)MathF.Floor( cellY ), 0, field.CellCountZ - 1 );
		var fx = Math.Clamp( cellX - x0, 0, 1 );
		var fy = Math.Clamp( cellY - y0, 0, 1 );
		var height = (field.GetCornerHeight( x0, y0 ) * (1 - fx) + field.GetCornerHeight( x0 + 1, y0 ) * fx) * (1 - fy)
			+ (field.GetCornerHeight( x0, y0 + 1 ) * (1 - fx) + field.GetCornerHeight( x0 + 1, y0 + 1 ) * fx) * fy;
		return OriginalParkPlacement.ToEngine( field, new SVector3( cellX * field.CellSizeX, height, cellY * field.CellSizeZ ) );
	}

	/// <summary>Atlas, frame and mirroring for a guest seen with the given camera axes (engine space).</summary>
	public (int Atlas, int Frame, bool Mirror) SelectSprite( Guest guest, SVector3 cameraRight, SVector3 cameraForward )
	{
		var atlasIndex = guest.Type % atlases.Count;
		var atlas = atlases[atlasIndex];
		var rightFlat = SVector2.Normalize( new SVector2( cameraRight.X, cameraRight.Y ) + new SVector2( 1e-6f, 0 ) );
		var forwardFlat = SVector2.Normalize( new SVector2( cameraForward.X, cameraForward.Y ) + new SVector2( 0, 1e-6f ) );
		var heading = new SVector2( guest.HeadingX, guest.HeadingY );
		var (direction, mirror) = GuestSpriteAtlas.SelectDirection( SVector2.Dot( heading, rightFlat ), SVector2.Dot( heading, forwardFlat ) );
		int slot, step;
		if ( guest.IsMoving )
		{
			slot = GuestSpriteAtlas.WalkSlot;
			step = (int)(guest.DistanceWalked * WalkFramesPerCell);
		}
		else if ( guest.State is GuestState.Queueing or GuestState.WaitingToBoard or GuestState.AtTicketBooth or GuestState.WaitingToGoHome )
		{
			slot = GuestSpriteAtlas.WaitSlot;
			step = (int)(simulation.TimeSeconds * 2 + guest.Id) ;
		}
		else
		{
			slot = GuestSpriteAtlas.StandSlot;
			step = 0;
		}
		step %= atlas.FramesPerDirection( slot );
		return (atlasIndex, atlas.GetFrame( slot, direction, step ), mirror);
	}

	protected override void OnRender()
	{
		DrawnGuests = 0;
		if ( !Visible || material == null || deleted )
			return;
		var view = Camera.ViewMatrix;
		var right = new SVector3( view.M11, view.M21, view.M31 );
		var up = new SVector3( view.M12, view.M22, view.M32 );
		var forward = -new SVector3( view.M13, view.M23, view.M33 );
		var needed = simulation.Guests.Count * VerticesPerGuest;
		if ( vertices.Length < needed )
			vertices = new Vertex[Math.Max( needed, 64 * VerticesPerGuest ) * 2];
		var count = 0;
		foreach ( var guest in simulation.Guests )
		{
			if ( !guest.IsVisible )
				continue;
			var (atlasIndex, frameIndex, mirror) = SelectSprite( guest, right, forward );
			var atlas = atlases[atlasIndex];
			var frame = atlas.Frames[frameIndex];
			var feet = CellToEngine( field, guest.X, guest.Y );
			var left = mirror ? -(frame.OriginX + frame.Width) : frame.OriginX;
			var x0 = left * UnitsPerPixel;
			var x1 = (left + frame.Width) * UnitsPerPixel;
			var yTop = -frame.OriginY * UnitsPerPixel;
			var yBottom = -(frame.OriginY + frame.Height) * UnitsPerPixel;
			var u0 = frame.X / (float)atlas.Width;
			var u1 = (frame.X + frame.Width) / (float)atlas.Width;
			if ( mirror )
				(u0, u1) = (u1, u0);
			var v0 = frame.Y / (float)atlas.Height;
			var v1 = (frame.Y + frame.Height) / (float)atlas.Height;
			var topLeft = new Vertex( feet + right * x0 + up * yTop, -forward, new SVector2( u0, v0 ) ) { TexIndex = atlasIndex };
			var topRight = new Vertex( feet + right * x1 + up * yTop, -forward, new SVector2( u1, v0 ) ) { TexIndex = atlasIndex };
			var bottomLeft = new Vertex( feet + right * x0 + up * yBottom, -forward, new SVector2( u0, v1 ) ) { TexIndex = atlasIndex };
			var bottomRight = new Vertex( feet + right * x1 + up * yBottom, -forward, new SVector2( u1, v1 ) ) { TexIndex = atlasIndex };
			// Both windings, so the quad survives back-face culling whatever the clip-space convention.
			vertices[count++] = topLeft; vertices[count++] = topRight; vertices[count++] = bottomLeft;
			vertices[count++] = topRight; vertices[count++] = bottomRight; vertices[count++] = bottomLeft;
			vertices[count++] = topLeft; vertices[count++] = bottomLeft; vertices[count++] = topRight;
			vertices[count++] = topRight; vertices[count++] = bottomLeft; vertices[count++] = bottomRight;
			DrawnGuests++;
		}
		if ( count == 0 )
			return;

		var stride = (uint)Marshal.SizeOf<Vertex>();
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
		} );
		commandList.SetVertexBuffer( 0, vertexBuffer );
		commandList.SetPipeline( material.Pipeline );
		material.CreateEphemeralResourceSet( out var resourceSets );
		for ( uint index = 0; index < resourceSets.Length; ++index )
			commandList.SetGraphicsResourceSet( index, resourceSets[index] );
		commandList.Draw( (uint)count );
	}

	protected override void OnDelete()
	{
		deleted = true;
		var buffer = vertexBuffer;
		vertexBuffer = null;
		if ( buffer != null )
			global::Global.Render.ScheduleDelete( buffer.Dispose );
	}
}
