using Veldrid;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Draws the warm items the guests wear (docs/WINTER.md, "Drawing"): a hat or earmuffs on the head and mittens on the hands, camera-facing quads of the extension's own item sprites
/// (<c>content/winter/esprites/Winter/SPR_WI</c>: slots 0 to 2 hats, 3 and 4 earmuffs, 5 to 7 mittens, five directions mirrored to eight), beside <see cref="UmbrellaRenderer"/>. The hotspot of every sprite is its attach point, and the
/// point goes to the anchor of the guest's frame: the head's top for a hat, its centre for earmuffs, a hand's end point for a mitten (<see cref="WinterAnchorTable"/>, per sprite set, animation slot, direction and step the guest is drawn
/// with, so an item follows the walk's bob and a mitten the swinging arm), then the sprite is pulled <see cref="CameraPull"/> of the way to the camera, so it is drawn in front of the guest it sits on (and behind an umbrella, which is
/// pulled further). Hats and earmuffs were rendered over a holdout head, so the part behind the head is already cut out. The colour is a pure function of the guest and the item (<see cref="WinterRules.SpriteSlotOf"/>).
/// Presentation only: nothing here touches the simulation, no random stream is read, and a guest the guest renderers do not draw (a swimmer, a rider, one in an attraction) wears nothing on screen. The original 1x sets and the costume sets have a head
/// anchor only, so their guests show a hat or earmuffs and no mittens.
/// </summary>
// [EXT:winter-draw] OpenTPW's own drawing; neither Theme Park World nor Theme Park Inc has warm items
public sealed class WarmItemRenderer : Entity
{
	private const int VerticesPerSprite = 12;
	/// <summary>
	/// The share of its distance to the camera an item is pulled towards it, over what the guest's own depth pull gives: a hat only has to be in front of the quad it sits on (1 % of a park view's distance, under a unit), so that a guest standing
	/// right in front of the wearer still covers it, and well behind <see cref="UmbrellaRenderer.CameraPull"/>, so a guest's umbrella covers its hat.
	/// </summary>
	// [EXT:winter-draw] our own value (docs/WINTER.md); the umbrella's pull is TPI's 10 %
	public const float CameraPull = 0.01f;
	/// <summary>
	/// How much of a hand's forearm angle a mitten is turned by, about its hotspot: the sprite is rendered with the hand hanging at the kid's side (abducted a little), the table's angle is the forearm's on screen, so a mitten follows the arm's swing.
	/// </summary>
	// [EXT:winter-draw] checked in captures
	public const float ForearmTurn = 1f;

	private readonly GuestSimulation simulation;
	private readonly GuestSpriteAtlas? atlas;
	private readonly IReadOnlyList<GuestRenderer> guests;
	private readonly WinterAnchorTable anchors;
	private readonly Material<ObjectUniformBuffer>? material;
	private Texture? texture;
	private DeviceBuffer? vertexBuffer;
	private Vertex[] vertices = Array.Empty<Vertex>();
	private bool deleted;

	public bool Visible { get; set; } = true;
	/// <summary>Items drawn in the last frame (a hat, a pair of earmuffs and each mitten count one).</summary>
	public int DrawnItems { get; private set; }

	internal IReadOnlyList<Asset> OwnedAssets => (texture == null ? Array.Empty<Asset>() : new Asset[] { texture })
		.Concat( material == null ? Array.Empty<Asset>() : new Asset[] { material, material.Shader } ).ToArray();

	/// <param name="atlases">The item set (<see cref="WinterSprites"/>); the renderer draws nothing without one.</param>
	/// <param name="guestRenderers">The renderers that draw guests (the kids' and the costumes'), asked in this order for where a guest stands.</param>
	public WarmItemRenderer( GuestSimulation simulation, IReadOnlyList<GuestSpriteAtlas> atlases, IReadOnlyList<GuestRenderer> guestRenderers, WinterAnchorTable anchors )
	{
		this.simulation = simulation;
		guests = guestRenderers;
		this.anchors = anchors;
		Name = "Warm items";
		atlas = atlases.FirstOrDefault();
		if ( atlas == null )
			return;
		texture = GuestRenderer.LoadAtlas( atlas );
		material = new Material<ObjectUniformBuffer>( "content/shaders/sprite.shader" );
		material.Set( "Color", Enumerable.Repeat( texture, 8 ).ToArray() );
		TexturePackSwitch.PackChanged += ReloadAtlas;
	}

	private void ReloadAtlas() => ReloadAtlas( null );

	/// <summary>[EXT:texture-pack] Rebuilds the item atlas after a pack change or a lazy download that names it, as the other sprite atlases are.</summary>
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

	/// <summary>The items a guest wears that are drawn: its defined bits only (a guest with a value outside them, which a saved or hand-made value could be, shows nothing it cannot name).</summary>
	public static int ShownItems( Guest guest ) => WinterRules.IsValidItems( guest.WarmItems ) ? guest.WarmItems : 0;

	/// <summary>
	/// The point and the scale of a sprite whose hotspot belongs at the anchor (<paramref name="x"/>, <paramref name="y"/>) pixels from the feet of a guest drawn as <paramref name="mount"/>, in the engine: the point is added to the guest's pulled
	/// feet (converted with the frame's own scale, at the guest's pull share, x negated when the frame is mirrored), then the whole is pulled <see cref="CameraPull"/> further.
	/// </summary>
	internal static (SVector3 Hotspot, float Scale) Place( in GuestMount mount, float x, float y, SVector3 right, SVector3 up, SVector3 forward, SVector3 eye )
	{
		var unit = GuestRenderer.UnitsPerPixel;
		var point = mount.Feet + (right * ((mount.Mirror ? -x : x) * unit * mount.Frame.ScaleX) + up * (-y * unit * mount.Frame.ScaleY)) * mount.Share;
		var pulled = UmbrellaRenderer.PullToEye( point, eye, forward, CameraPull );
		return (pulled, SVector3.Dot( point - eye, forward ) > 0f ? mount.Share * (1f - CameraPull) : mount.Share);
	}

	protected override void OnRender()
	{
		DrawnItems = 0;
		if ( !Visible || material == null || atlas == null || deleted )
			return;
		var view = Camera.ViewMatrix;
		var right = new SVector3( view.M11, view.M21, view.M31 );
		var up = new SVector3( view.M12, view.M22, view.M32 );
		var forward = -new SVector3( view.M13, view.M23, view.M33 );
		var eye = GuestRenderer.Eye( view );
		var count = 0;
		var allGuests = simulation.Guests;
		for ( var index = 0; index < allGuests.Count; ++index )
		{
			var guest = allGuests[index];
			var items = ShownItems( guest );
			if ( items == 0 || !float.IsFinite( guest.DrawX ) || !float.IsFinite( guest.DrawY ) )
				continue;
			GuestMount mount = default;
			var mounted = false;
			for ( var renderer = 0; renderer < guests.Count && !mounted; ++renderer )
				mounted = guests[renderer].TryMount( guest, right, up, forward, eye, out mount );
			if ( !mounted || !anchors.TryGet( mount.SetName, mount.Frame.PixelsPerUnitX, mount.Slot, mount.Direction, mount.Step, out var anchor ) )
				continue;
			if ( (items & WinterRules.HeadItems) != 0 )
			{
				var item = items & WinterRules.HeadItems;
				var slot = WinterRules.SpriteSlotOf( guest.Id, item );
				var (hotspot, scale) = item == WinterRules.Hat
					? Place( mount, anchor.HeadTopX, anchor.HeadTopY, right, up, forward, eye )
					: Place( mount, anchor.HeadX, anchor.HeadY, right, up, forward, eye );
				AddItem( ref count, slot, mount, 0, hotspot, scale, 0f, 1f, right, up, forward );
			}
			if ( (items & WinterRules.Mittens) != 0 && anchor.HasHands )
			{
				var slot = WinterRules.SpriteSlotOf( guest.Id, WinterRules.Mittens );
				if ( (anchor.Seen & WinterAnchor.RightSeen) != 0 )
				{
					var (hotspot, scale) = Place( mount, anchor.RightX, anchor.RightY, right, up, forward, eye );
					AddItem( ref count, slot, mount, 0, hotspot, scale, ForearmSin( anchor.RightSin, mount.Mirror ), anchor.RightCos, right, up, forward );
				}
				if ( (anchor.Seen & WinterAnchor.LeftSeen) != 0 )
				{
					var (hotspot, scale) = Place( mount, anchor.LeftX, anchor.LeftY, right, up, forward, eye );
					AddItem( ref count, slot, mount, 1, hotspot, scale, ForearmSin( anchor.LeftSin, mount.Mirror ), anchor.LeftCos, right, up, forward );
				}
			}
		}
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
		}.WithFog().WithSpriteTint( Level?.Look ?? EveningLook.Day ) );
		commandList.SetVertexBuffer( 0, vertexBuffer );
		commandList.SetPipeline( material.Pipeline );
		var resourceSets = material.GetResourceSets();
		for ( uint index = 0; index < resourceSets.Length; ++index )
			commandList.SetGraphicsResourceSet( index, resourceSets[index] );
		commandList.Draw( (uint)count );
	}

	/// <summary>
	/// The sine of the turn of a mitten: the forearm's angle (positive towards the right of the picture, negative when the frame is mirrored) times <see cref="ForearmTurn"/>, as the sine of the clockwise screen angle the quad is turned by
	/// (a hand pointing to the right is a counter-clockwise turn of the hanging mitten, so the sign flips).
	/// </summary>
	private static float ForearmSin( float sin, bool mirror ) => -(mirror ? -sin : sin) * ForearmTurn;

	/// <summary>One camera-facing quad (both windings, as the guests') of a frame of the item bank with its hotspot at <paramref name="hotspot"/>, extents shrunk by <paramref name="scale"/>, turned by (<paramref name="sin"/>, <paramref name="cos"/>) in its plane.</summary>
	private void AddItem( ref int count, int slot, in GuestMount mount, int step, SVector3 hotspot, float scale, float sin, float cos, SVector3 right, SVector3 up, SVector3 forward )
	{
		if ( !atlas!.HasArt( slot ) )
			return;
		if ( vertices.Length < count + VerticesPerSprite )
			Array.Resize( ref vertices, Math.Max( 64 * VerticesPerSprite, vertices.Length * 2 ) );
		var direction = Math.Clamp( mount.Direction, 0, atlas.DirectionsOf( slot ) - 1 );
		var frame = atlas.Frames[atlas.GetFrame( slot, direction, step )];
		var mirror = mount.Mirror;
		var (x0, x1, yTop, yBottom) = frame.Extents( mirror, GuestRenderer.UnitsPerPixel );
		var u0 = frame.X / (float)atlas.Width;
		var u1 = (frame.X + frame.Width) / (float)atlas.Width;
		if ( mirror )
			(u0, u1) = (u1, u0);
		var v0 = frame.Y / (float)atlas.Height;
		var v1 = (frame.Y + frame.Height) / (float)atlas.Height;
		// a mitten is turned about its hotspot like a lying pose of the guests' own renderer: (sin, cos) of the clockwise screen angle
		SVector3 Corner( float x, float y ) => hotspot + (right * (x * cos + y * sin) + up * (-x * sin + y * cos)) * scale;
		var topLeft = new Vertex( Corner( x0, yTop ), -forward, new SVector2( u0, v0 ) );
		var topRight = new Vertex( Corner( x1, yTop ), -forward, new SVector2( u1, v0 ) );
		var bottomLeft = new Vertex( Corner( x0, yBottom ), -forward, new SVector2( u0, v1 ) );
		var bottomRight = new Vertex( Corner( x1, yBottom ), -forward, new SVector2( u1, v1 ) );
		vertices[count++] = topLeft; vertices[count++] = topRight; vertices[count++] = bottomLeft;
		vertices[count++] = topRight; vertices[count++] = bottomRight; vertices[count++] = bottomLeft;
		vertices[count++] = topLeft; vertices[count++] = bottomLeft; vertices[count++] = topRight;
		vertices[count++] = topRight; vertices[count++] = bottomLeft; vertices[count++] = bottomRight;
		DrawnItems++;
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

/// <summary>
/// The warm item sprite set (<c>content/winter/esprites/Winter/SPR_WI</c>, docs/WINTER.md "Content"): eight colourways as slots (hats 0 to 2, earmuffs 3 and 4, mittens 5 to 7), five directions of one frame each (two for a mitten: the right and the
/// left), 256 pixels per unit, the hotspot at the attach point. Read from the extension's folder with System.IO like the umbrellas' set; the browser build embeds the folder.
/// </summary>
// [EXT:winter-content] OpenTPW's own art
public static class WinterSprites
{
	/// <summary>The folder of the set below the winter content.</summary>
	public static string? SpriteDirectory => WinterContent.Directory is { } folder ? Path.Combine( folder, "esprites", "Winter" ) : null;

	/// <summary>The sprite sets while the extension is on and its content is there (one set: <c>SPR_WI</c>); empty otherwise.</summary>
	public static List<GuestSpriteAtlas> Load() => FeatureGates.Winter ? PoolGuestSprites.LoadFolder( SpriteDirectory, "winter", "winter" ) : new List<GuestSpriteAtlas>();
}
