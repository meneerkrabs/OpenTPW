using Veldrid;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Draws every placed staff member as a camera-facing quad of its original sprite (<c>esprites.wad</c>, tp.exe body sprite 0x4d4140), the way
/// <see cref="GuestRenderer"/> draws the kids: one draw call, one dynamic vertex buffer, the same sprite shader and atlas loading, the hotspot on the terrain.
/// A member in the player's hand is not drawn; the tool's dangling sprite (slot 6, frames 0-3 looping) hangs at the pointer's ground point instead.
/// Animation and pose are immutable observations of the CPU owner, never inferred or advanced by drawing.
/// </summary>
public sealed class StaffRenderer : Entity
{
	private const int VerticesPerSprite = 12;
	/// <summary>Height of the dangling sprite above the mouse's world point, in original units.</summary>
	// [BIN:TPW-EXE:0x0046C480 tool frame] every frame the tool's sprite is placed at the mouse's world point (0x0045D610 returns 0x007A1C90) 5 units up, with heading 4 (0x00475480 stores x, y + 5, z at +0x88..+0x90 and the heading byte at +0xC0)
	public const float DangleLift = 5f;
	/// <summary>The heading sector of the tools' dangling person (tp.exe 0x0046C480); the sprite pass adds the camera's yaw sector (0x00542010), so the person turns with the camera.</summary>
	public const int DangleHeading = 4;

	private readonly Func<IReadOnlyList<StaffMember>> members;
	private readonly ModelHeightfield field;
	private readonly StaffSpriteSet sprites;
	private readonly StaffPlacement placement;
	private readonly Material<ObjectUniformBuffer>? material;
	private readonly List<Texture> textures = new();
	private readonly StaffSpriteMetadataCatalog catalog;
	private readonly Func<StaffMember, StaffSpriteDrawRecord?>? sourceRecord;
	private readonly Func<long>? session;
	private DeviceBuffer? vertexBuffer;
	private Vertex[] vertices = Array.Empty<Vertex>();
	private bool deleted;

	public bool Visible { get; set; } = true;
	/// <summary>Members drawn in the last frame (the dangling sprite excluded).</summary>
	public int DrawnStaff { get; private set; }
	/// <summary>Whether the last frame drew the tool's dangling sprite.</summary>
	public bool DrewDangling { get; private set; }
	/// <summary>Where the last frame hung the tool's dangling sprite, in cell units; null when it was not drawn.</summary>
	internal (float X, float Y)? DangleAt { get; private set; }
	/// <summary>The size in pixels of the frame the last dangling sprite showed; null when it was not drawn.</summary>
	internal (int Width, int Height)? DangleFrameSize { get; private set; }

	/// <summary>The atlas textures, material and independently owned shader used by native lifecycle checks.</summary>
	internal IReadOnlyList<Asset> OwnedAssets => textures.Cast<Asset>().Concat( material == null ? Array.Empty<Asset>() : new Asset[] { material, material.Shader } ).ToArray();

	public StaffRenderer( Func<IReadOnlyList<StaffMember>> members, ModelHeightfield field, StaffSpriteSet sprites, StaffPlacement placement,
		StaffSpriteMetadataCatalog? catalog = null, Func<StaffMember, StaffSpriteDrawRecord?>? sourceRecord = null, Func<long>? session = null )
	{
		this.members = members;
		this.field = field;
		this.sprites = sprites;
		this.placement = placement;
		this.catalog = catalog ?? new StaffSpriteMetadataCatalog( sprites );
		this.sourceRecord = sourceRecord; this.session = session;
		Name = "Staff";
		if ( sprites.Atlases.Count == 0 )
			return;
		foreach ( var atlas in sprites.Atlases.Take( 8 ) )
			textures.Add( GuestRenderer.LoadAtlas( atlas ) );
		var slots = new Texture[8];
		for ( var index = 0; index < slots.Length; index++ )
			slots[index] = textures[Math.Min( index, textures.Count - 1 )];
		material = new Material<ObjectUniformBuffer>( "content/shaders/sprite.shader" );
		material.Set( "Color", slots );
		TexturePackSwitch.PackChanged += ReloadAtlases;
	}

	/// <summary>Loaded atlas keys in owned-texture order for targeted resource-lifetime checks.</summary>
	internal IReadOnlyList<string> AtlasPackKeys => sprites.Atlases.Take( textures.Count ).Select( atlas => atlas.PackKey ).ToArray();

	/// <summary>Runs the existing targeted current-resource reload without raising a global pack event.</summary>
	internal void ReloadAtlas( int index )
	{
		if ( (uint)index >= (uint)textures.Count ) throw new ArgumentOutOfRangeException( nameof( index ) );
		ReloadAtlases( new[] { sprites.Atlases[index].PackKey } );
	}

	private void ReloadAtlases() => ReloadAtlases( null );

	/// <summary>[EXT:texture-pack] Rebuilds the atlas textures after a pack change or a lazy download (all of them, or those the paths name), as the guests' do.</summary>
	private void ReloadAtlases( IReadOnlyList<string>? paths )
	{
		if ( deleted || material == null || textures.Count == 0 )
			return;
		var keys = paths?.Select( TexturePack.RelativeFileName ).ToHashSet( StringComparer.Ordinal );
		var old = new List<Texture>();
		for ( var index = 0; index < textures.Count; index++ )
		{
			if ( !catalog.Banks.Any( bank => bank.Atlas == index && bank.Matches( sprites.Atlases[index] ) )
				|| (keys != null && !keys.Contains( TexturePack.RelativeFileName( sprites.Atlases[index].PackKey ) )) )
				continue;
			old.Add( ReplaceAtlas( index, GuestRenderer.LoadAtlas( sprites.Atlases[index] ) ) );
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

	private Texture ReplaceAtlas( int index, Texture replacement )
	{
		var old = textures[index];
		// Draw readers resolve the current handle every time; no VM or window history changes here.
		textures[index] = replacement;
		return old;
	}

	private bool CanRender => Visible && material != null && !deleted;
	private bool HasAtlas( int index ) => index >= 0 && index < textures.Count && index < sprites.Atlases.Count
		&& textures[index].NativeTexture is { IsDisposed: false };

	/// <summary>Resource validation is read-only and independent of world rendering visibility or repaint frequency.</summary>
	internal StaffSpriteFrameResolution ResolveRecord( StaffSpriteDrawRecord record, float cameraYaw = 0 )
	{
		if ( deleted || Level == null || !ReferenceEquals( Level, Level.Current ) || !ReferenceEquals( Level.StaffRenderer, this )
			|| !Entity.All.Contains( this ) || (session != null && record.Session != session()) )
			return new( StaffSpriteDiagnostic.StaleIdentity, record.Route );
		var resolved = catalog.ResolveFrame( record, cameraYaw );
		if ( !resolved.CanDraw )
			return resolved;
		if ( resolved.Atlas < 0 || resolved.Atlas >= sprites.Atlases.Count
			|| !catalog.Resolve( record.Kind, record.Variant ).Matches( sprites.Atlases[resolved.Atlas] ) )
			return new( StaffSpriteDiagnostic.ResourceMismatch, record.Route );
		return HasAtlas( resolved.Atlas ) ? resolved : new( StaffSpriteDiagnostic.TextureUnavailable, record.Route );
	}

	/// <summary>The independent employee clone's screen output at literal camera yaw zero. No world-publication fallback.</summary>
	internal UI.Original.UiSpriteFrame? EmployeeFrame( StaffSpriteDrawRecord record )
	{
		var result = ResolveRecord( record );
		if ( !record.EmployeeClone || result.Route != StaffSpriteRoute.Screen || !result.CanDraw )
			return null;
		var atlas = sprites.Atlases[result.Atlas];
		var frame = result.Frame;
		return new UI.Original.UiSpriteFrame( textures[result.Atlas].NativeTexture, atlas.Width, atlas.Height,
			frame.X, frame.Y, frame.Width, frame.Height, frame.OriginX, frame.OriginY, result.Mirror, frame.ScaleX, frame.ScaleY );
	}

	/// <summary>The source's published cell position for thought bubbles; querying it does not advance presentation.</summary>
	internal bool TryGetDrawCell( StaffMember member, out float x, out float y )
	{
		var record = sourceRecord?.Invoke( member );
		(x, y) = record == null ? (0f, 0f) : (record.PositionX, record.PositionY);
		return record != null && float.IsFinite( x ) && float.IsFinite( y );
	}

	/// <summary>
	/// The hire window's figure of a role and variant <paramref name="elapsed"/> seconds after it appeared (<see cref="StaffSpriteRules.Portrait"/>);
	/// null when the role has no bank loaded.
	/// </summary>
	internal UI.Original.UiSpriteFrame? PortraitFrame( StaffType type, int variantKey, float elapsed )
	{
		var atlasIndex = sprites.AtlasFor( type, variantKey );
		if ( deleted || atlasIndex < 0 || atlasIndex >= textures.Count )
			return null;
		var atlas = sprites.Atlases[atlasIndex];
		var animations = atlas.Animations.Animations;
		var directions = StaffSpriteRules.PortraitSlot < animations.Count ? animations[StaffSpriteRules.PortraitSlot].Directions : 1;
		var (slot, direction, mirror, step) = StaffSpriteRules.Portrait( elapsed, directions );
		var frame = atlas.Frames[atlas.GetFrame( slot, direction, step % atlas.FramesPerDirection( slot ) )];
		return new UI.Original.UiSpriteFrame( textures[atlasIndex].NativeTexture, atlas.Width, atlas.Height, frame.X, frame.Y, frame.Width, frame.Height,
			frame.OriginX, frame.OriginY, mirror, frame.ScaleX, frame.ScaleY );
	}

	/// <summary>Where the tool's sprite hangs this frame in cell units: the exact pointer ground point, else the hovered cell's centre; null when neither is known.</summary>
	internal static (float X, float Y)? DanglePoint( StaffPlacement placement ) =>
		placement.Cursor ?? (placement.HoverCell is { } cell ? (cell.X + 0.5f, cell.Y + 0.5f) : null);

	/// <summary>The CPU portion of the actual world pass, including selection and quad preparation; never called by UI readers.</summary>
	internal int PrepareWorld()
	{
		DrawnStaff = 0;
		DrewDangling = false;
		DangleAt = null;
		DangleFrameSize = null;
		if ( !CanRender )
			return 0;
		var list = members();
		var view = Camera.ViewMatrix;
		var right = new SVector3( view.M11, view.M21, view.M31 );
		var up = new SVector3( view.M12, view.M22, view.M32 );
		var forward = -new SVector3( view.M13, view.M23, view.M33 );
		var eye = GuestRenderer.Eye( view );
		var needed = (list.Count + 1) * VerticesPerSprite;
		if ( vertices.Length < needed )
			vertices = new Vertex[Math.Max( needed, 16 * VerticesPerSprite ) * 2];
		var now = Time.Now;
		var count = 0;
		var yaw = GuestSpriteAtlas.CameraYaw( right, forward, Camera.Mode is ParkCameraMode );
		for ( var index = 0; index < list.Count; index++ )
		{
			var member = list[index];
			if ( sourceRecord?.Invoke( member ) is not { } record || record.EmployeeClone || record.Route != StaffSpriteRoute.World )
				continue;
			var chosen = ResolveRecord( record, yaw );
			if ( !chosen.CanDraw )
				continue;
			var feet = GuestRenderer.CellToEngine( field, record.PositionX, record.PositionY, record.PositionZ );
			var share = 1f;
			if ( (record.Flags & 0x200) != 0 )
				(feet, share) = GuestRenderer.PullTowardsEye( feet, eye, forward );
			AddSprite( ref count, chosen.Atlas, chosen.FrameIndex, chosen.Mirror, feet, right * share, up * share, forward );
			DrawnStaff++;
		}
		if ( DanglePoint( placement ) is var (danglingX, danglingY) && placement.ToolType is { } toolType
			&& sprites.AtlasFor( toolType, placement.ToolVariantKey ) is var atlasIndex and >= 0 && atlasIndex < textures.Count )
		{
			var atlas = sprites.Atlases[atlasIndex];
			var (slot, step) = StaffSpriteRules.Resolve( StaffSpriteRules.Dangle, now, atlas.Animations.Animations );
			// [BIN:TPW-EXE:0x00542010 direction] the heading byte plus the camera's yaw sector, mirrored past the last stored direction, as for any sprite (the tool's sprite has no flag 0x40000: 0x004758F0 clears +0xC4)
			var (direction, mirror) = GuestSpriteAtlas.SelectOriginalDirection( DangleHeading, GuestSpriteAtlas.CameraYaw( right, forward, Camera.Mode is ParkCameraMode ), atlas.DirectionsOf( slot ) );
			var frameIndex = atlas.GetFrame( slot, direction, step );
			// not pulled: the tool's sprite is made with cleared flags (0x00475A10 through 0x004758F0), and neither the tool's enter 0x0046C730 nor its frame 0x0046C480 (0x00475480 sets
			// only the position and heading) gives it the pick flag 0x200 that 0x00589B50 pulls
			AddSprite( ref count, atlasIndex, frameIndex, mirror, GuestRenderer.CellToEngine( field, danglingX, danglingY, DangleLift ), right, up, forward );
			DrewDangling = true;
			DangleAt = (danglingX, danglingY);
			DangleFrameSize = (atlas.Frames[frameIndex].Width, atlas.Frames[frameIndex].Height);
		}
		return count;
	}

	protected override void OnRender()
	{
		var count = PrepareWorld();
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
		material!.Set( "ObjectUniformBuffer", new ObjectUniformBuffer
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

	private void AddSprite( ref int count, int atlasIndex, int frameIndex, bool mirror, SVector3 feet, SVector3 right, SVector3 up, SVector3 forward )
	{
		var atlas = sprites.Atlases[atlasIndex];
		var frame = atlas.Frames[frameIndex];
		var (x0, x1, yTop, yBottom) = frame.Extents( mirror, GuestRenderer.UnitsPerPixel );
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
		// Both windings, so the quad survives back-face culling whatever the clip-space convention (as the guests' quads do).
		vertices[count++] = topLeft; vertices[count++] = topRight; vertices[count++] = bottomLeft;
		vertices[count++] = topRight; vertices[count++] = bottomRight; vertices[count++] = bottomLeft;
		vertices[count++] = topLeft; vertices[count++] = bottomLeft; vertices[count++] = topRight;
		vertices[count++] = topRight; vertices[count++] = bottomLeft; vertices[count++] = bottomRight;
	}

	protected override void OnDelete()
	{
		deleted = true;
		TexturePackSwitch.PackChanged -= ReloadAtlases;
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
