using System.Runtime.InteropServices;
using Veldrid;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Draws every placed staff member as a camera-facing quad of its original sprite (<c>esprites.wad</c>, tp.exe body sprite 0x4d4140), the way
/// <see cref="GuestRenderer"/> draws the kids: one draw call, one dynamic vertex buffer, the same sprite shader and atlas loading, the hotspot on the terrain.
/// A member in the player's hand is not drawn; the tool's dangling sprite (slot 6, frames 0-3 looping) hangs at the pointer's ground point instead.
/// Nothing is allocated per frame once the members have been seen.
/// </summary>
public sealed class StaffRenderer : Entity
{
	/// <summary>[EXT:pools-bridges] Optional elevated walking height, in engine units.</summary>
	public Func<float, float, float?>? FeetHeight { get; init; }
	private const int VerticesPerSprite = 12;
	/// <summary>The lift the tool adds to the mouse's world point, in original units (the terrain's own height comes on top, <see cref="DangleAnchor"/>).</summary>
	// [BIN:TPW-EXE:0x0046C480 tool frame] every frame the tool's sprite is placed at the mouse's world point (0x0045D610 returns 0x007A1C90) 5 units up, with heading 4 (0x00475480 stores x, y + 5, z at +0x88..+0x90 and the heading byte at +0xC0)
	public const float DangleLift = 5f;
	/// <summary>The heading sector of the tools' dangling person (tp.exe 0x0046C480); the sprite pass adds the camera's yaw sector (0x00542010), so the person turns with the camera.</summary>
	public const int DangleHeading = 4;

	private sealed class MemberView
	{
		/// <summary>Where the member was last drawn, in cells.</summary>
		public float X, Y;
		public bool Initialised;
		public PublishedFrame? Published;
	}

	// Value-only output of this renderer's successful world preparation. Never retain a GPU handle in a member view.
	private readonly record struct PublishedFrame( Level Owner, long WorldPass, long RendererPass, long Assets,
		StaffMember Member, int ThingId, int Atlas, int Slot, int Step, float HeadingX, float HeadingY );
	private readonly record struct Selection( int Atlas, int Frame, bool Mirror, int Slot, int Step );

	private readonly Func<IReadOnlyList<StaffMember>> members;
	private readonly ModelHeightfield field;
	private readonly StaffSpriteSet sprites;
	private readonly StaffPlacement placement;
	private readonly Material<ObjectUniformBuffer>? material;
	private readonly List<Texture> textures = new();
	private readonly Dictionary<StaffMember, MemberView> views = new( ReferenceEqualityComparer.Instance );
	private readonly List<StaffMember> stale = new();
	private DeviceBuffer? vertexBuffer;
	private Vertex[] vertices = Array.Empty<Vertex>();
	private int frames;
	private bool deleted;
	private long rendererPass;
	private long assetGeneration;

	public bool Visible { get; set; } = true;
	/// <summary>Members drawn in the last frame (the dangling sprite excluded).</summary>
	public int DrawnStaff { get; private set; }
	/// <summary>Whether the last frame drew the tool's dangling sprite.</summary>
	public bool DrewDangling { get; private set; }
	/// <summary>Where the last frame hung the tool's dangling sprite, in cell units; null when it was not drawn.</summary>
	internal (float X, float Y)? DangleAt { get; private set; }
	/// <summary>The engine-space anchor the last dangling sprite was placed at, before the depth pull; null when it was not drawn.</summary>
	internal SVector3? DangleAnchorAt { get; private set; }
	/// <summary>The size in pixels of the frame the last dangling sprite showed; null when it was not drawn.</summary>
	internal (int Width, int Height)? DangleFrameSize { get; private set; }

	/// <summary>The atlas textures, material and independently owned shader used by native lifecycle checks.</summary>
	internal IReadOnlyList<Asset> OwnedAssets => textures.Cast<Asset>().Concat( material == null ? Array.Empty<Asset>() : new Asset[] { material, material.Shader } ).ToArray();

	public StaffRenderer( Func<IReadOnlyList<StaffMember>> members, ModelHeightfield field, StaffSpriteSet sprites, StaffPlacement placement )
	{
		this.members = members;
		this.field = field;
		this.sprites = sprites;
		this.placement = placement;
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
			if ( keys != null && !keys.Contains( TexturePack.RelativeFileName( sprites.Atlases[index].PackKey ) ) )
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
		// Invalidate before replacing even one slot; a partial reload must not reuse old publications.
		assetGeneration++;
		textures[index] = replacement;
		return old;
	}

	private bool CanRender => Visible && material != null && !deleted;
	// Keep the existing world exclusion (CellX only). Track separately requires both coordinates.
	private bool CanPlaceMember( StaffMember member ) => !ReferenceEquals( member, placement.Carried ) && member.CellX != null;
	private bool HasAtlas( int index ) => index >= 0 && index < textures.Count && index < sprites.Atlases.Count
		&& textures[index].NativeTexture is { IsDisposed: false };

	/// <summary>
	/// The selected employee's latest world output at literal camera yaw zero. Pure observation: no player, movement,
	/// clip or simulation updates. Unavailable output leaves the employee window on its existing qualified hire-loop fallback (UI-092).
	/// </summary>
	internal UI.Original.UiSpriteFrame? EmployeeFrame( StaffMember member )
	{
		if ( !CanRender || Level == null || !ReferenceEquals( Level, Level.Current ) || !ReferenceEquals( Level.StaffRenderer, this )
			|| !Entity.All.Contains( this ) || !CanPlaceMember( member ) || !StaffSpriteRules.HasBodySprite( member )
			|| !views.TryGetValue( member, out var view ) || view.Published is not { } output
			|| !ReferenceEquals( output.Owner, Level ) || !ReferenceEquals( output.Member, member ) || output.ThingId != member.ThingId
			|| output.WorldPass != Level.RenderPass || output.RendererPass != rendererPass || output.Assets != assetGeneration
			|| output.Atlas != sprites.AtlasFor( member.Type, StaffSpriteRules.KeyOf( member ) ) || !HasAtlas( output.Atlas ) )
			return null;
		var list = members();
		var found = false;
		for ( var index = 0; index < list.Count && !found; index++ )
			found = ReferenceEquals( list[index], member );
		if ( !found )
			return null;
		var atlas = sprites.Atlases[output.Atlas];
		var (direction, mirror) = GuestSpriteAtlas.SelectOriginalDirection( output.HeadingX, output.HeadingY, 0f, atlas.DirectionsOf( output.Slot ) );
		var frame = atlas.Frames[atlas.GetFrame( output.Slot, direction, output.Step % atlas.FramesPerDirection( output.Slot ) )];
		return new UI.Original.UiSpriteFrame( textures[output.Atlas].NativeTexture, atlas.Width, atlas.Height, frame.X, frame.Y, frame.Width, frame.Height,
			frame.OriginX, frame.OriginY, mirror, frame.ScaleX, frame.ScaleY );
	}

	/// <summary>Where the member was last drawn, in cells (the thought bubbles hang above it); false before its first frame.</summary>
	internal bool TryGetDrawCell( StaffMember member, out float x, out float y )
	{
		(x, y) = views.TryGetValue( member, out var view ) && view.Initialised ? (view.X, view.Y) : (0f, 0f);
		return view is { Initialised: true };
	}

	/// <summary>
	/// The hire window's figure of a role and variant <paramref name="elapsed"/> seconds after it appeared (<see cref="StaffSpriteRules.Portrait"/>);
	/// null when the role has no bank loaded.
	/// </summary>
	internal UI.Original.UiSpriteFrame? PortraitFrame( StaffType type, int variantKey, float elapsed ) =>
		PortraitFrameAtStep( type, variantKey, (int)MathF.Max( 0, elapsed * StaffSpriteRules.FramesPerSecond ) );

	/// <summary>The hire window's figure at its <paramref name="steps"/>-th script frame (<see cref="StaffSpriteRules.PortraitAtStep"/>); null when the role has no bank loaded.</summary>
	internal UI.Original.UiSpriteFrame? PortraitFrameAtStep( StaffType type, int variantKey, int steps )
	{
		var atlasIndex = sprites.AtlasFor( type, variantKey );
		if ( deleted || atlasIndex < 0 || atlasIndex >= textures.Count )
			return null;
		var atlas = sprites.Atlases[atlasIndex];
		var animations = atlas.Animations.Animations;
		var directions = StaffSpriteRules.PortraitSlot < animations.Count ? animations[StaffSpriteRules.PortraitSlot].Directions : 1;
		var (slot, direction, mirror, step) = StaffSpriteRules.PortraitAtStep( steps, directions );
		var frame = atlas.Frames[atlas.GetFrame( slot, direction, step % atlas.FramesPerDirection( slot ) )];
		return new UI.Original.UiSpriteFrame( textures[atlasIndex].NativeTexture, atlas.Width, atlas.Height, frame.X, frame.Y, frame.Width, frame.Height,
			frame.OriginX, frame.OriginY, mirror, frame.ScaleX, frame.ScaleY );
	}

	/// <summary>The atlas, frame, mirroring, slot and step a member is drawn with: for the lifecycle checks and tests; null when the member is not drawn.</summary>
	internal (int Atlas, int Frame, bool Mirror, int Slot, int Step)? Describe( StaffMember member, SVector3 cameraRight, SVector3 cameraForward )
	{
		if ( !views.TryGetValue( member, out var view ) )
			return null;
		return Select( member, view, cameraRight, cameraForward ) is { } chosen
			? (chosen.Atlas, chosen.Frame, chosen.Mirror, chosen.Slot, chosen.Step) : null;
	}

	/// <summary>
	/// The frame the member's sprite source shows: the sprite pass draws every sprite whose output word is set (a source shows nothing until the scheduler's first pass has yielded a frame, and a member
	/// without a source, resting or carried, is not drawn), at the slot and frame the source's registers hold.
	/// </summary>
	// [BIN:TPW-EXE:0x00542010 sprite pass] the pass skips a record whose output word +0x114 is 0 (0x0054201D: FRAME sets it, 0x00476940) or whose frame register +0xB8 is -1 (0x00542029) and draws any other from the bank of its kind +0xAC and variant +0xB0 at the slot register +0xB4 and the frame register: the frame is whatever the scheduler's last pass left, however late the frame is drawn
	private Selection? Select( StaffMember member, MemberView view, SVector3 cameraRight, SVector3 cameraForward )
	{
		var atlasIndex = sprites.AtlasFor( member.Type, StaffSpriteRules.KeyOf( member ) );
		// the state setters delete the source of a resting or carried member; a source that outlived its state is never drawn
		if ( !HasAtlas( atlasIndex ) || !StaffSpriteRules.HasBodySprite( member ) || member.Sprite.Output is not var (slot, step) )
			return null;
		var atlas = sprites.Atlases[atlasIndex];
		if ( slot >= atlas.Animations.Animations.Count )
			return null;
		var (frame, mirror) = FrameFor( atlas, slot, step, member.HeadingX, member.HeadingY, cameraRight, cameraForward );
		return new Selection( atlasIndex, frame, mirror, slot, step );
	}

	private static (int Frame, bool Mirror) FrameFor( GuestSpriteAtlas atlas, int slot, int step, float headingX, float headingY, SVector3 cameraRight, SVector3 cameraForward )
	{
		// [BIN:TPW-EXE:0x00542010 direction] staff go through the same sprite pass as the guests: the heading sector plus the camera's yaw sector picks the direction; no pitch is read (GuestSpriteAtlas.SelectOriginalDirection)
		var yaw = GuestSpriteAtlas.CameraYaw( cameraRight, cameraForward, Camera.Mode is ParkCameraMode );
		var (direction, mirror) = GuestSpriteAtlas.SelectOriginalDirection( headingX, headingY, yaw, atlas.DirectionsOf( slot ) );
		return (atlas.GetFrame( slot, direction, step % atlas.FramesPerDirection( slot ) ), mirror);
	}

	/// <summary>
	/// The point a member is drawn at: between the position at the start of his turn and his current one (<see cref="PersonInterpolation.Point"/>, the staff world's <c>UpdatePresentation</c>), or
	/// his plain position while the world has not placed him.
	/// </summary>
	private static (float X, float Y)? DrawPoint( StaffMember member ) =>
		member.Navigation != null && !float.IsNaN( member.DrawX ) && !float.IsNaN( member.DrawY ) ? (member.DrawX, member.DrawY)
		: member.CellX is { } x && member.CellY is { } y ? (x, y) : null;

	/// <summary>Where the tool's sprite hangs this frame in cell units: the exact pointer ground point, else the hovered cell's centre; null when neither is known.</summary>
	internal static (float X, float Y)? DanglePoint( StaffPlacement placement ) =>
		placement.Cursor ?? (placement.HoverCell is { } cell ? (cell.X + 0.5f, cell.Y + 0.5f) : null);

	/// <summary>
	/// The tool sprite's engine-space anchor: the full picked point lifted by the tool's 5 original units and by the terrain's height under it, or the same on the terrain for cell-only callers.
	/// </summary>
	// [BIN:TPW-EXE:0x00542010 sprite height] a sprite whose flag 0x1000000 is clear (the dangling script sets only 0x1200, 0x0074ED98) is drawn at the terrain's height under its x and z (0x004527F0, the pass adds it at 0x005420F3) plus the stored height word +0x8C, which every other caller makes relative (a guest stores its height minus the terrain's, 0x004F9E60; a litter item 0)
	// [BIN:TPW-EXE:0x0046C480 tool height] but the tool stores the picker's absolute height (0x7A1C90 + 4: the intersection with the terrain triangle or the model, 0x0045BF90) plus 5, so the person hangs at the terrain's height again above the picked point, which only differs from the point's own on ground above the level's zero
	internal static SVector3? DangleAnchor( StaffPlacement placement, ModelHeightfield field )
	{
		if ( DanglePoint( placement ) is not var (x, y) )
			return null;
		var lift = DangleLift + field.GetHeight( x, y );
		return placement.CursorEnginePoint is { } point ? point + SVector3.UnitZ * (lift * OriginalParkPlacement.ModelScale) : GuestRenderer.CellToEngine( field, x, y, lift );
	}

	/// <summary>The CPU portion of the actual world pass, including selection and quad preparation; never called by UI readers.</summary>
	internal int PrepareWorld()
	{
		rendererPass++;
		DrawnStaff = 0;
		DrewDangling = false;
		DangleAt = null;
		DangleAnchorAt = null;
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
		var count = 0;
		for ( var index = 0; index < list.Count; index++ )
		{
			var member = list[index];
			if ( !views.TryGetValue( member, out var memberView ) )
				views[member] = memberView = new MemberView();
			if ( !CanPlaceMember( member ) )
				continue;
			// a member without a point (one coordinate missing) keeps the place he was last drawn at, the established world pass gate
			if ( DrawPoint( member ) is { } point )
				(memberView.X, memberView.Y, memberView.Initialised) = (point.X, point.Y, true);
			if ( Select( member, memberView, right, forward ) is not { } chosen )
				continue;
			// a member's sprite is pickable, so the original pulls it towards the camera like a visitor's (SpriteScale.PulledDepthShare)
			var position = GuestRenderer.CellToEngine( field, memberView.X, memberView.Y );
			if ( FeetHeight?.Invoke( memberView.X, memberView.Y ) is { } height ) position.Z = height;
			var (feet, share) = GuestRenderer.PullTowardsEye( position, eye, forward );
			AddSprite( ref count, chosen.Atlas, chosen.Frame, chosen.Mirror, feet, right * share, up * share, forward );
			memberView.Published = new PublishedFrame( Level, Level?.RenderPass ?? 0, rendererPass, assetGeneration,
				member, member.ThingId, chosen.Atlas, chosen.Slot, chosen.Step, member.HeadingX, member.HeadingY );
			DrawnStaff++;
		}
		if ( ++frames % 256 == 0 && views.Count > list.Count )
			Prune( list );
		if ( DanglePoint( placement ) is var (danglingX, danglingY) && placement.ToolType is { } toolType
			&& sprites.AtlasFor( toolType, placement.ToolVariantKey ) is var atlasIndex and >= 0 && atlasIndex < textures.Count
			&& placement.ToolSpriteOutput is var (slot, step) && slot < sprites.Atlases[atlasIndex].Animations.Animations.Count
			&& DangleAnchor( placement, field ) is { } danglingAnchor )
		{
			var atlas = sprites.Atlases[atlasIndex];
			// [BIN:TPW-EXE:0x00542010 direction] the heading byte plus the camera's yaw sector, mirrored past the last stored direction, as for any sprite (the tool's sprite has no flag 0x40000: 0x004758F0 clears +0xC4)
			var (direction, mirror) = GuestSpriteAtlas.SelectOriginalDirection( DangleHeading, GuestSpriteAtlas.CameraYaw( right, forward, Camera.Mode is ParkCameraMode ), atlas.DirectionsOf( slot ) );
			var frameIndex = atlas.GetFrame( slot, direction, step % atlas.FramesPerDirection( slot ) );
			// not pulled: the tool's sprite is made with cleared flags (0x00475A10 through 0x004758F0), and neither the tool's enter 0x0046C730 nor its frame 0x0046C480 (0x00475480 sets
			// only the position and heading) gives it the pick flag 0x200 that 0x00589B50 pulls
			AddSprite( ref count, atlasIndex, frameIndex, mirror, danglingAnchor, right, up, forward );
			DrewDangling = true;
			DangleAt = (danglingX, danglingY);
			DangleAnchorAt = danglingAnchor;
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

	/// <summary>Forgets the views of members that left the list (dismissed, or the park was replaced by a load).</summary>
	private void Prune( IReadOnlyList<StaffMember> list )
	{
		stale.Clear();
		foreach ( var member in views.Keys )
		{
			var found = false;
			for ( var index = 0; index < list.Count && !found; index++ )
				found = ReferenceEquals( list[index], member );
			if ( !found )
				stale.Add( member );
		}
		foreach ( var member in stale )
			views.Remove( member );
		stale.Clear();
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
