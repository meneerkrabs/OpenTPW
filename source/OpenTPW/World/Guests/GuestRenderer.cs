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
	/// <summary>Engine units per sprite pixel of a frame with 128 pixels per unit (kids ≈ 42 px tall ≈ 0.95 cell); a frame with other pixels-per-unit fields is scaled by 128 / A across and 128 / B down (<see cref="GuestAtlasFrame.ScaleX"/>). Approximation; the original zoom mapping is unknown.</summary>
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
	/// <summary>Which guests this renderer draws (null: all): the kids' renderer leaves guests in costume to the costume renderer.</summary>
	public Func<Guest, bool>? Draws { get; init; }
	/// <summary>[EXT:pools-swim] Chooses the animation slot and step of a guest instead of the walking, waiting and standing rules (the swimmers' renderer: the swim strokes); null for the kids and the costumes.</summary>
	public Func<Guest, (int Slot, int Step)?>? SlotChooser { get; init; }
	/// <summary>[EXT:pools-swim] Where a guest's hotspot lies in the engine instead of on the ground (the swimmers' renderer: on the water); null for the kids and the costumes.</summary>
	public Func<Guest, SVector3>? FeetChooser { get; init; }
	/// <summary>[EXT:pools-render] Whether a slot the chosen set has no art for is drawn as the nearest swim slot (<see cref="GuestSpriteAtlas.UsableSlot"/>); the swimmers' renderer only, so any other chooser keeps the stand-slot fallback.</summary>
	public bool SwimSlotFallback { get; init; }
	/// <summary>
	/// [EXT:pools-render] The slots whose frames are drawn with their bottom edge at the guest's point instead of their hotspot (the swimmers' renderer: the swimwear slots whose hotspot is the centre of the
	/// torso, <see cref="PoolSwimSlots.BottomAnchored"/>), so a pool activity's <see cref="IPoolActivity.FeetHeight"/> is where the body rests: the feet on a rung, the back on a slide or a lounger. Null for the
	/// kids and the costumes, whose hotspot is the feet.
	/// </summary>
	public IReadOnlySet<int>? BottomAnchoredSlots { get; init; }
	/// <summary>Guests drawn in the last frame.</summary>
	public int DrawnGuests { get; private set; }

	/// <summary>The atlas textures, material and independently owned shader used by native lifecycle checks.</summary>
	internal IReadOnlyList<Asset> OwnedAssets => textures.Cast<Asset>().Concat( material == null ? Array.Empty<Asset>() : new Asset[] { material, material.Shader } ).ToArray();

	public GuestRenderer( GuestSimulation simulation, ModelHeightfield field, IReadOnlyList<GuestSpriteAtlas> atlases )
	{
		this.simulation = simulation;
		this.field = field;
		this.atlases = atlases;
		Name = "Guests";
		if ( atlases.Count == 0 )
			return;
		foreach ( var atlas in atlases.Take( 8 ) )
			textures.Add( LoadAtlas( atlas ) );
		var slots = new Texture[8];
		for ( var index = 0; index < slots.Length; index++ )
			slots[index] = textures[Math.Min( index, textures.Count - 1 )];
		material = new Material<ObjectUniformBuffer>( "content/shaders/sprite.shader" );
		material.Set( "Color", slots );
		TexturePackSwitch.PackChanged += ReloadAtlases;
		TexturePackSwitch.FilesAvailable += ReloadAtlases;
	}

	/// <summary>
	/// [EXT:texture-pack] The texture pack changed: builds the atlas textures again from the new pack (or the originals), binds
	/// them and deletes the old GPU textures after the current frame.
	/// </summary>
	private void ReloadAtlases()
	{
		ReloadAtlases( null );
	}

	/// <summary>Rebuilds matching atlas slots after a lazy download; unrelated guest sprites keep their GPU texture.</summary>
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
			textures[index] = LoadAtlas( atlases[index] );
		}
		if ( old.Count == 0 )
			return;
		var slots = new Texture[8];
		for ( var index = 0; index < slots.Length; index++ )
			slots[index] = textures[Math.Min( index, textures.Count - 1 )];
		material.Set( "Color", slots );
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
	/// The atlas texture: the texture pack's upscaled atlas when there is one with the same aspect ratio (frame UVs are
	/// relative, so a larger atlas drops in), else the original pixels.
	/// </summary>
	// [EXT:texture-pack] upscaled guest sprites from the optional local pack
	internal static Texture LoadAtlas( GuestSpriteAtlas atlas )
	{
		if ( TexturePack.Find( atlas.PackKey ) is { } file )
		{
			try
			{
				var image = StbImageSharp.ImageResult.FromMemory( File.ReadAllBytes( file ), StbImageSharp.ColorComponents.RedGreenBlueAlpha );
				if ( image.Width >= atlas.Width && (long)image.Width * atlas.Height == (long)image.Height * atlas.Width )
					return new Texture( image.Data, image.Width, image.Height );
				Log.Warning( $"Texture pack atlas {file} is {image.Width}x{image.Height}, not a multiple of {atlas.Width}x{atlas.Height}; using the original sprites." );
			}
			catch ( Exception exception ) when ( exception is IOException or InvalidOperationException or UnauthorizedAccessException )
			{
				Log.Warning( $"Texture pack atlas {file} could not be read ({exception.Message}); using the original sprites." );
			}
		}
		var texture = new Texture( atlas.Pixels, atlas.Width, atlas.Height );
		atlas.ReleasePixels();   // uploaded: the bank keeps the picture at a quarter of the size, and a texture-pack reload decodes it again
		return texture;
	}

	/// <summary>Engine position of a point in game-cell coordinates, on the cell's ground triangle (<see cref="ModelHeightfield.GetHeight"/>; holes keep their corner heights).</summary>
	public static SVector3 CellToEngine( ModelHeightfield field, float cellX, float cellY ) => CellToEngine( field, cellX, cellY, 0f );

	/// <summary>The same point lifted by <paramref name="lift"/> original height units (the staff cursor sprite hangs 5 units above the ground).</summary>
	public static SVector3 CellToEngine( ModelHeightfield field, float cellX, float cellY, float lift )
	{
		var height = field.GetHeight( cellX, cellY );
		return OriginalParkPlacement.ToEngine( field, new SVector3( cellX * field.CellSizeX, height + lift, cellY * field.CellSizeZ ) );
	}

	/// <summary>Atlas, frame and mirroring for a guest seen with the given camera axes (engine space).</summary>
	public (int Atlas, int Frame, bool Mirror) SelectSprite( Guest guest, SVector3 cameraRight, SVector3 cameraForward )
	{
		var (atlas, frame, mirror, _) = SelectSpriteAndSlot( guest, cameraRight, cameraForward );
		return (atlas, frame, mirror);
	}

	private (int Atlas, int Frame, bool Mirror, int Slot) SelectSpriteAndSlot( Guest guest, SVector3 cameraRight, SVector3 cameraForward )
	{
		var atlasIndex = guest.Type % atlases.Count;
		var atlas = atlases[atlasIndex];
		var rightFlat = SVector2.Normalize( new SVector2( cameraRight.X, cameraRight.Y ) + new SVector2( 1e-6f, 0 ) );
		var forwardFlat = SVector2.Normalize( new SVector2( cameraForward.X, cameraForward.Y ) + new SVector2( 0, 1e-6f ) );
		var heading = new SVector2( guest.HeadingX, guest.HeadingY );
		var (direction, mirror) = GuestSpriteAtlas.SelectDirection( SVector2.Dot( heading, rightFlat ), SVector2.Dot( heading, forwardFlat ) );
		int slot, step;
		if ( SlotChooser?.Invoke( guest ) is { } chosen )
			(slot, step) = (SwimSlotFallback ? atlas.UsableSlot( chosen.Slot ) : chosen.Slot, chosen.Step);
		else if ( guest.IsMoving )
		{
			slot = GuestSpriteAtlas.WalkSlot;
			step = (int)(guest.DrawDistanceWalked * WalkFramesPerCell);
		}
		else if ( guest.State is GuestState.Queueing or GuestState.WaitingToBoard or GuestState.WaitingAtBooth or GuestState.AtTicketBooth or GuestState.WaitingToGoHome )
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
		return (atlasIndex, atlas.GetFrame( slot, direction, step ), mirror, slot);
	}

	/// <summary>
	/// The engine units a frame of <paramref name="slot"/> is raised along the camera's up axis so its bottom edge, not its hotspot, sits on the guest's point (<see cref="BottomAnchoredSlots"/>); 0 for
	/// any other slot. Presentation only.
	/// </summary>
	internal float AnchorLift( int slot, GuestAtlasFrame frame ) => AnchorLift( BottomAnchoredSlots, slot, frame );

	/// <summary>The lift of <see cref="AnchorLift(int, GuestAtlasFrame)"/> for a set of bottom-anchored slots.</summary>
	internal static float AnchorLift( IReadOnlySet<int>? bottomAnchored, int slot, GuestAtlasFrame frame ) =>
		bottomAnchored?.Contains( slot ) == true ? Math.Max( 0, frame.OriginY + frame.Height ) * UnitsPerPixel * frame.ScaleY : 0f;

	/// <summary>Picks the current visible in-park sprite without touching render resources or simulation state.</summary>
	internal Guest? PickVisitor(SVector2 mouse, SVector2 viewport, System.Numerics.Matrix4x4 view, System.Numerics.Matrix4x4 projection) =>
		PickVisitor(mouse, viewport, view, projection, out _);

	/// <summary>The picked guest and its depth, so the kids' and the costumes' renderers can be picked together.</summary>
	internal Guest? PickVisitor(SVector2 mouse, SVector2 viewport, System.Numerics.Matrix4x4 view, System.Numerics.Matrix4x4 projection, out float depth)
	{
		depth = float.PositiveInfinity;
		if (!Visible || deleted || material == null || atlases.Count == 0)
			return null;
		var right = new SVector3(view.M11, view.M21, view.M31);
		var forward = -new SVector3(view.M13, view.M23, view.M33);
		Guest? picked = null;
		var best = float.PositiveInfinity;
		foreach (var guest in simulation.Guests)
		{
			if (!guest.IsVisible || Draws?.Invoke(guest) == false || !simulation.IsInPark(guest) || !float.IsFinite(guest.DrawX) || !float.IsFinite(guest.DrawY) ||
				!float.IsFinite(guest.HeadingX) || !float.IsFinite(guest.HeadingY) || !float.IsFinite(guest.DrawDistanceWalked) || guest.Type < 0)
				continue;
			var (atlasIndex, frameIndex, mirror, slot) = SelectSpriteAndSlot(guest, right, forward);
			var frame = atlases[atlasIndex].Frames[frameIndex];
			var feet = (FeetChooser?.Invoke( guest ) ?? CellToEngine(field, guest.DrawX, guest.DrawY)) + new SVector3(view.M12, view.M22, view.M32) * AnchorLift(slot, frame);
			if (!Hud.GuestWorldPicker.TryHit(feet, frame, mirror, view, projection, viewport, mouse, out var hit))
				continue;
			if (hit < best || (hit == best && (picked == null || guest.Id < picked.Id)))
			{
				best = hit;
				picked = guest;
			}
		}
		depth = best;
		return picked;
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
			if ( !guest.IsVisible || Draws?.Invoke( guest ) == false )
				continue;
			var (atlasIndex, frameIndex, mirror, slot) = SelectSpriteAndSlot( guest, right, forward );
			var atlas = atlases[atlasIndex];
			var frame = atlas.Frames[frameIndex];
			var feet = (FeetChooser?.Invoke( guest ) ?? CellToEngine( field, guest.DrawX, guest.DrawY )) + up * AnchorLift( slot, frame );
			var (x0, x1, yTop, yBottom) = frame.Extents( mirror, UnitsPerPixel );
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
		}.WithFog() );
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
		TexturePackSwitch.PackChanged -= ReloadAtlases;
		TexturePackSwitch.FilesAvailable -= ReloadAtlases;
		var ownedTextures = textures.ToArray();
		textures.Clear();
		foreach ( var texture in ownedTextures )
			Asset.All.Remove( texture );
		material?.ReleaseOwnedResources();
		global::Global.Render.ScheduleDelete( () =>
		{
			foreach ( var texture in ownedTextures )
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
