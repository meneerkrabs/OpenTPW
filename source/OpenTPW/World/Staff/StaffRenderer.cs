using System.Runtime.InteropServices;
using Veldrid;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Draws every placed staff member as a camera-facing quad of its original sprite (<c>esprites.wad</c>, tp.exe body sprite 0x4d4140), the way
/// <see cref="GuestRenderer"/> draws the kids: one draw call, one dynamic vertex buffer, the same sprite shader and atlas loading, the hotspot on the terrain.
/// A member in the player's hand is not drawn; the tool's dangling sprite (slot 6, frames 0-3 looping) hangs over the hovered cell instead.
/// Nothing is allocated per frame once the members have been seen.
/// </summary>
public sealed class StaffRenderer : Entity
{
	private const int VerticesPerSprite = 12;
	/// <summary>A park turn in seconds (the simulation's 248 ms).</summary>
	private const float ParkTurnSeconds = 0.248f;
	/// <summary>Height of the dangling sprite above the ground, in original units.</summary>
	// [BIN:TPW-EXE:0x46c480 hover] the cursor sprite follows the mouse 5 units above the ground
	public const float DangleLift = 5f;

	private sealed class MemberView
	{
		public float X, Y, LastX, LastY;
		public float HeadingX, HeadingY = 1;
		public float MovedAt = float.NegativeInfinity;
		public int ClipKey = -1;
		public bool Initialised;
		public SpritePlayer Player = null!;
		/// <summary>The length of the member's latest step in the walk step's 16.16 units per park turn, until a clip has taken the interval it asks for; 0 when none is waiting.</summary>
		public float StepLength;
	}

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

	public bool Visible { get; set; } = true;
	/// <summary>Members drawn in the last frame (the dangling sprite excluded).</summary>
	public int DrawnStaff { get; private set; }
	/// <summary>Whether the last frame drew the tool's dangling sprite.</summary>
	public bool DrewDangling { get; private set; }

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
		TexturePackSwitch.FilesAvailable += ReloadAtlases;
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
			old.Add( textures[index] );
			textures[index] = GuestRenderer.LoadAtlas( sprites.Atlases[index] );
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
			frame.OriginX, frame.OriginY, mirror );
	}

	/// <summary>The atlas, slot and frame a member is drawn with: for the lifecycle checks and tests; null when the member is not drawn.</summary>
	internal (int Atlas, int Frame, bool Mirror, StaffClip Clip)? Describe( StaffMember member, SVector3 cameraRight, SVector3 cameraForward )
	{
		if ( !views.TryGetValue( member, out var view ) )
			return null;
		return Select( member, view, Time.Now, cameraRight, cameraForward );
	}

	private (int Atlas, int Frame, bool Mirror, StaffClip Clip)? Select( StaffMember member, MemberView view, float now, SVector3 cameraRight, SVector3 cameraForward )
	{
		var atlasIndex = sprites.AtlasFor( member.Type, StaffSpriteRules.VariantKey( member.NameIndex, member.Id ) );
		if ( atlasIndex < 0 || atlasIndex >= textures.Count )
			return null;
		var atlas = sprites.Atlases[atlasIndex];
		// the world-driven roles steer continuously; mechanics are moved one cell per second, so their steps are further apart
		var moving = now - view.MovedAt < (member.Type == StaffType.Mechanic ? 1.3f : 0.6f);
		if ( StaffSpriteRules.SelectClip( member, moving, atlas.Animations.Performances, atlas.Animations.PerformanceCount ) is not { } clip )
			return null;
		var key = clip.Script << 8 | clip.Slot;
		if ( view.ClipKey != key )
		{
			view.ClipKey = key;
			view.Player.Restart();
		}
		if ( view.StepLength > 0f )
		{
			// [BIN:TPW-EXE:0x004FA2A0 walk step] the interval a step of that length asks for takes effect with the clip playing (the run clip does not double the speed); a clip that is not a walk keeps the interval the last step left
			view.Player.SetInterval( SpritePlayer.IntervalFor( view.StepLength, clip == StaffSpriteRules.Run ) );
			view.StepLength = 0f;
		}
		var (slot, step) = StaffSpriteRules.ResolveSteps( clip, view.Player.Steps, atlas.Animations.Animations );
		var (frame, mirror) = FrameFor( atlas, slot, step, view.HeadingX, view.HeadingY, cameraRight, cameraForward );
		return (atlasIndex, frame, mirror, clip);
	}

	private static (int Frame, bool Mirror) FrameFor( GuestSpriteAtlas atlas, int slot, int step, float headingX, float headingY, SVector3 cameraRight, SVector3 cameraForward )
	{
		var rightFlat = SVector2.Normalize( new SVector2( cameraRight.X, cameraRight.Y ) + new SVector2( 1e-6f, 0 ) );
		var forwardFlat = SVector2.Normalize( new SVector2( cameraForward.X, cameraForward.Y ) + new SVector2( 0, 1e-6f ) );
		var heading = new SVector2( headingX, headingY );
		var (direction, mirror) = GuestSpriteAtlas.SelectDirection( SVector2.Dot( heading, rightFlat ), SVector2.Dot( heading, forwardFlat ) );
		return (atlas.GetFrame( slot, direction, step % atlas.FramesPerDirection( slot ) ), mirror);
	}

	/// <summary>Follows the member's position: smooths the drawn position, tracks the heading and when it last moved.</summary>
	private static void Track( StaffMember member, MemberView view, float now, float delta )
	{
		if ( member.CellX is not { } x || member.CellY is not { } y )
			return;
		if ( !view.Initialised )
		{
			(view.X, view.Y, view.LastX, view.LastY, view.Initialised) = (x, y, x, y, true);
			return;
		}
		var dx = x - view.LastX;
		var dy = y - view.LastY;
		var step = MathF.Sqrt( dx * dx + dy * dy );
		if ( step > 1e-4f )
		{
			view.HeadingX = dx / step;
			view.HeadingY = dy / step;
			// the step of one park turn: the displacement over the time since the previous change, scaled to a turn (the world moves most roles once a turn, mechanics once a second)
			var elapsed = now - view.MovedAt;
			var turns = float.IsFinite( elapsed ) ? Math.Clamp( elapsed, 0.05f, 2f ) / ParkTurnSeconds : 1f;
			view.StepLength = step / turns * Fixed16.One;
			view.MovedAt = now;
			(view.LastX, view.LastY) = (x, y);
		}
		// a drop or a placement jumps; a walk is followed smoothly between the world's steps
		var gap = MathF.Sqrt( (x - view.X) * (x - view.X) + (y - view.Y) * (y - view.Y) );
		if ( gap > 2f || step > 1.5f )
		{
			(view.X, view.Y) = (x, y);
			view.MovedAt = float.NegativeInfinity;
		}
		else
		{
			var blend = MathF.Min( 1f, MathF.Max( 0f, delta ) * 10f );
			view.X += (x - view.X) * blend;
			view.Y += (y - view.Y) * blend;
		}
	}

	protected override void OnRender()
	{
		DrawnStaff = 0;
		DrewDangling = false;
		if ( !Visible || material == null || deleted )
			return;
		var list = members();
		var view = Camera.ViewMatrix;
		var right = new SVector3( view.M11, view.M21, view.M31 );
		var up = new SVector3( view.M12, view.M22, view.M32 );
		var forward = -new SVector3( view.M13, view.M23, view.M33 );
		var needed = (list.Count + 1) * VerticesPerSprite;
		if ( vertices.Length < needed )
			vertices = new Vertex[Math.Max( needed, 16 * VerticesPerSprite ) * 2];
		var now = Time.Now;
		var nowMs = (long)(now * 1000f);
		var count = 0;
		for ( var index = 0; index < list.Count; index++ )
		{
			var member = list[index];
			if ( !views.TryGetValue( member, out var memberView ) )
				views[member] = memberView = new MemberView { Player = new SpritePlayer( nowMs ) };
			if ( ReferenceEquals( member, placement.Carried ) || member.CellX == null )
				continue;
			Track( member, memberView, now, Time.Delta );
			memberView.Player.Tick( nowMs );
			if ( Select( member, memberView, now, right, forward ) is not { } chosen )
				continue;
			AddSprite( ref count, chosen.Atlas, chosen.Frame, chosen.Mirror, GuestRenderer.CellToEngine( field, memberView.X, memberView.Y ), right, up, forward );
			DrawnStaff++;
		}
		if ( ++frames % 256 == 0 && views.Count > list.Count )
			Prune( list );
		if ( placement.HoverCell is { } cell && placement.ToolType is { } toolType
			&& sprites.AtlasFor( toolType, placement.ToolVariantKey ) is var atlasIndex and >= 0 && atlasIndex < textures.Count )
		{
			var atlas = sprites.Atlases[atlasIndex];
			var (slot, step) = StaffSpriteRules.Resolve( StaffSpriteRules.Dangle, now, atlas.Animations.Animations );
			// the cursor sprite faces the player: sprite direction 4
			var frameIndex = atlas.GetFrame( slot, 4, step );
			AddSprite( ref count, atlasIndex, frameIndex, false, GuestRenderer.CellToEngine( field, cell.X + 0.5f, cell.Y + 0.5f, DangleLift ), right, up, forward );
			DrewDangling = true;
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

	private void AddSprite( ref int count, int atlasIndex, int frameIndex, bool mirror, SVector3 feet, SVector3 right, SVector3 up, SVector3 forward )
	{
		var atlas = sprites.Atlases[atlasIndex];
		var frame = atlas.Frames[frameIndex];
		var left = mirror ? -(frame.OriginX + frame.Width) : frame.OriginX;
		var x0 = left * GuestRenderer.UnitsPerPixel;
		var x1 = (left + frame.Width) * GuestRenderer.UnitsPerPixel;
		var yTop = -frame.OriginY * GuestRenderer.UnitsPerPixel;
		var yBottom = -(frame.OriginY + frame.Height) * GuestRenderer.UnitsPerPixel;
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
