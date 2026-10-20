using Veldrid;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Draws the guests' open umbrellas (docs/UMBRELLAS.md, "Drawing"): one camera-facing quad of the extension's own umbrella sprite (<c>content/umbrellas/esprites/Umbrellas/SPR_UM</c>: three colours, five directions mirrored to
/// eight) for each guest whose umbrella is open or open until a close that is due (states 4 and 1, <see cref="Shows"/>), beside <see cref="BalloonRenderer"/>. The sprite's hotspot, the handle's tip, goes to the guest's
/// hand: the point of the hand anchor table for the sprite set, slot, direction and step the guest is drawn with (<see cref="UmbrellaAnchors"/>), then the whole sprite is pulled towards the camera by
/// <see cref="PullShare"/> (<c>min(0.1, 1.5 * ModelScale / depth)</c>), so it is drawn in front of the guest it covers. Presentation only: nothing here touches the simulation, no random stream is read, and a guest the
/// guest renderers do not draw (a swimmer, a rider, one in an attraction) has no umbrella shown.
/// </summary>
// [BIN:TPI-EXE:0x60ae35 umbrella drawn] the per-frame guest placement 0x60AE00 takes the umbrella state (+0x238): 4 or 1 sets the flag that places the sprite and raises the thought bubble, every other state draws nothing (a closed umbrella, state 2, has no sprite)
public sealed class UmbrellaRenderer : Entity
{
	private const int VerticesPerSprite = 12;
	/// <summary>
	/// The share of its distance to the camera the umbrella is pulled towards it, over what the guest's own depth pull gives (3.5 to 3.9 original units, under 5 % of the distances of the park view). TPI's default placement
	/// moves the hotspot that share (<c>XAdjustCamera</c>, <c>ZAdjustCamera</c>, 10 %) of the way to the camera along the ground and lifts it by <c>YAdjustCamera</c> times the camera's height over 40, so the umbrella covers the
	/// guest.
	/// </summary>
	// [BIN:TPI-EXE:0x60b236 umbrella pull] a horizontal coordinate v' = v - (v - the camera's) x GenericUmbrellaModifiers[1] (0xA01E0C), the other with [3] (0xA01E14), and the height y' = y + (the camera's height 0xA14584 - 40.0 at 0x93E5B0) x [2] (0xA01E10); the settings' shipped values (X/ZAdjustCamera 0.1) are the U0 trace's (docs/UMBRELLAS.md)
	// [APPROX:UMB-004] [EXT:umbrellas-draw] the pull runs along the sight line to the camera instead (the screen position of the hand stays, the quad is scaled down by the same share, as the guests' own pull is done), so TPI's lift, whose value is not
	// known here, is not needed to keep the umbrella over the hand
	public const float CameraPull = 0.1f;
	/// <summary>
	/// The most the umbrella is pulled towards the camera, in original units: 1.5, enough to beat the depth of its own guest's quad (which is itself pulled 3.5 to 3.9 units towards the camera) and of the ground, and no more. The
	/// pull only has to win a depth test against what is drawn at the guest's own depth. With the share TPI uses (10 % of the distance: 8 units at the zoom value 80, 13 at 130, 30 at the wide range's 300) the umbrella of a guest
	/// who stands behind a booth, a shelter or a ride came out nearer to the camera than that scenery's roof and was drawn over it while the guest was hidden (capture of the Jungle bus shelter, October 18, 2026), and the umbrella
	/// of a guest drew over neighbours up to a cell nearer to the camera.
	/// </summary>
	// [EXT:umbrellas-pull-cap] the sight-line pull stays (TPI's share of 10 % is the upper limit), but OpenTPW's depth buffer needs only a margin: TPI's own pull (0x60b236) moves the sprite 10 % of the way to the camera in the ground plane and lifts it, in TPI's own sprite ordering, which is not reproduced here
	public const float MaxPullUnits = 1.5f;
	/// <summary>
	/// The size of the umbrella's quad relative to its sprite's own extents. The set was drawn so that the canopy is 0.8 of a kid's standing height across, but the camera looks down at 45 degrees and shortens a standing kid
	/// by cos 45 while the canopy's width stays whole, so on screen the canopy was 1.2 times a kid's height and hid its head from every side. 0.85 brings it to about the kid's height.
	/// </summary>
	// [EXT:umbrellas-size] a tuning from captures (docs/UMBRELLAS.md, "Tuning"): the scale is about the hotspot, the handle's tip, which stays in the hand
	public const float SizeScale = 0.85f;
	/// <summary>The thought bubble's height above a guest while its umbrella shows, in original units (TPI 6.0 instead of <see cref="ThoughtRenderer.Lift"/>'s 2.5).</summary>
	// [BIN:TPI-EXE:0x60b0a4 bubble height] the bubble's height is the word at 0x9CFB38 (6.0) when the umbrella flag is set, else 0x9CFB34 (2.5), added to the guest's height for the bubble's placement (call 0x40A722)
	public const float BubbleLift = 6.0f;

	private readonly GuestSimulation simulation;
	private readonly ModelHeightfield field;
	private readonly GuestSpriteAtlas? atlas;
	private readonly IReadOnlyList<GuestRenderer> guests;
	private readonly UmbrellaAnchors anchors;
	private readonly Material<ObjectUniformBuffer>? material;
	private Texture? texture;
	private DeviceBuffer? vertexBuffer;
	private Vertex[] vertices = Array.Empty<Vertex>();
	private bool deleted;
	// The guests whose umbrella was drawn in the last frame (reused, so a frame allocates nothing for it): the thought bubble rises only for these.
	private readonly HashSet<Guest> drawnGuests = new();

	public bool Visible { get; set; } = true;
	/// <summary>
	/// Whether the hand is taken as the mean over an animation's steps (true) or step by step (false). A walking kid's hand swings with its arm by up to 12 pixels, which would shake the umbrella; the guest holds it still.
	/// </summary>
	public bool SteadyHand { get; init; } = true;
	/// <summary>Umbrellas drawn in the last frame.</summary>
	public int DrawnUmbrellas { get; private set; }

	internal IReadOnlyList<Asset> OwnedAssets => (texture == null ? Array.Empty<Asset>() : new Asset[] { texture })
		.Concat( material == null ? Array.Empty<Asset>() : new Asset[] { material, material.Shader } ).ToArray();

	/// <param name="atlases">The umbrella set (<see cref="UmbrellaSprites"/>); the renderer draws nothing without one.</param>
	/// <param name="guestRenderers">The renderers that draw guests (the kids' and the costumes'), asked in this order for where a guest stands.</param>
	public UmbrellaRenderer( GuestSimulation simulation, ModelHeightfield field, IReadOnlyList<GuestSpriteAtlas> atlases, IReadOnlyList<GuestRenderer> guestRenderers, UmbrellaAnchors anchors )
	{
		this.simulation = simulation;
		this.field = field;
		guests = guestRenderers;
		this.anchors = anchors;
		Name = "Umbrellas";
		atlas = atlases.FirstOrDefault();
		if ( atlas == null )
			return;
		texture = GuestRenderer.LoadAtlas( atlas );
		material = new Material<ObjectUniformBuffer>( "content/shaders/sprite.shader" );
		material.Set( "Color", Enumerable.Repeat( texture, 8 ).ToArray() );
		TexturePackSwitch.PackChanged += ReloadAtlas;
	}

	private void ReloadAtlas() => ReloadAtlas( null );

	/// <summary>[EXT:texture-pack] Rebuilds the umbrella atlas after a pack change or a lazy download that names it, as the other sprite atlases are.</summary>
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

	/// <summary>Whether an umbrella in this state is drawn: open (4), or open until a close that is due (1); a closed umbrella has no sprite.</summary>
	public static bool ShowsState( int umbrellaState ) => umbrellaState is UmbrellaRules.StateOpen or UmbrellaRules.StateClosing;

	/// <summary>Whether the guest's umbrella is drawn.</summary>
	public static bool Shows( Guest guest ) => ShowsState( guest.UmbrellaState );

	/// <summary>Whether the guest's bubble is raised: the umbrella was drawn on it in the last frame, so a hidden renderer or a guest that is not drawn keeps the bubble at its usual height.</summary>
	public bool ShowsBubble( Guest guest ) => Visible && !deleted && drawnGuests.Contains( guest );

	/// <summary>The thought bubble's height above the guest: <see cref="BubbleLift"/> while the umbrella shows, else <see cref="ThoughtRenderer.Lift"/>.</summary>
	public static float BubbleHeight( bool umbrellaShows ) => umbrellaShows ? BubbleLift : ThoughtRenderer.Lift;

	/// <summary>The share of its distance to the eye an umbrella at <paramref name="depth"/> (engine units along the view direction) is pulled: <see cref="CameraPull"/>, but never more than <see cref="MaxPullUnits"/>.</summary>
	public static float PullShare( float depth ) => depth > 0f ? MathF.Min( CameraPull, MaxPullUnits * ObjectPlacement.ModelScale / depth ) : 0f;

	/// <summary>A point pulled the given share of its way to the eye along the sight line; a point on the eye's far side is not.</summary>
	internal static SVector3 PullToEye( SVector3 point, SVector3 eye, SVector3 forward, float share ) =>
		SVector3.Dot( point - eye, forward ) > 0f ? point + (eye - point) * share : point;

	/// <summary>
	/// The umbrella's hotspot (the handle's tip), the scale of its quad and its colour slot for a guest drawn as <paramref name="mount"/>, in the engine. The hand's point from the anchor table (pixels of the guest's frame:
	/// converted with the frame's own scale, x negated when the frame is mirrored) is added to the guest's pulled hotspot at that pull's share, then the whole is pulled further by <see cref="PullShare"/>
	/// (<c>min(0.1, 1.5 * ModelScale / depth)</c>);
	/// a set without a table is held straight above the guest's position at <see cref="UmbrellaAnchors.FallbackLift"/>.
	/// </summary>
	// a kept body standing in place (state 16) is drawn where it was last placed, which a blocked exit may leave away from the guest's own position
	private float GroundX( Guest guest ) => guest.IsVisible ? guest.DrawX : simulation.KeptBodyPosition( guest ).X;
	private float GroundY( Guest guest ) => guest.IsVisible ? guest.DrawY : simulation.KeptBodyPosition( guest ).Y;

	internal (SVector3 Hotspot, float Scale) Place( Guest guest, in GuestMount mount, SVector3 right, SVector3 up, SVector3 forward, SVector3 eye, Func<Guest, SVector3>? feetChooser = null )
	{
		SVector3 hand;
		if ( anchors.Hand( mount.SetName, mount.Frame.PixelsPerUnitX, mount.Slot, mount.Direction, mount.Step, mount.Mirror, SteadyHand ) is { } point )
		{
			var unit = GuestRenderer.UnitsPerPixel;
			hand = mount.Feet + (right * (point.X * unit * mount.Frame.ScaleX) + up * (-point.Y * unit * mount.Frame.ScaleY)) * mount.Share;
		}
		else
		{
			// An elevated walking layer supplies the same unpulled feet used by the guest sprite.
			var above = feetChooser != null
				? feetChooser( guest ) + SVector3.UnitZ * (UmbrellaAnchors.FallbackLift * OriginalParkPlacement.ModelScale)
				: GuestRenderer.CellToEngine( field, GroundX( guest ), GroundY( guest ), UmbrellaAnchors.FallbackLift );
			hand = mount.Share < 1f && SVector3.Dot( above - eye, forward ) > 0f ? eye + (above - eye) * mount.Share : above;
		}
		var share = PullShare( SVector3.Dot( hand - eye, forward ) );
		var pulled = PullToEye( hand, eye, forward, share );
		return (pulled, mount.Share * (1f - share) * SizeScale);
	}

	protected override void OnRender()
	{
		DrawnUmbrellas = 0;
		drawnGuests.Clear();
		if ( !Visible || material == null || atlas == null || deleted )
			return;
		var view = Camera.ViewMatrix;
		var right = new SVector3( view.M11, view.M21, view.M31 );
		var up = new SVector3( view.M12, view.M22, view.M32 );
		var forward = -new SVector3( view.M13, view.M23, view.M33 );
		var eye = GuestRenderer.Eye( view );
		var yaw = GuestSpriteAtlas.CameraYaw( right, forward, Camera.Mode is ParkCameraMode );
		var count = 0;
		var allGuests = simulation.Guests;
		for ( var index = 0; index < allGuests.Count; ++index )
		{
			var guest = allGuests[index];
			if ( !Shows( guest ) || !float.IsFinite( guest.DrawX ) || !float.IsFinite( guest.DrawY ) )
				continue;
			GuestMount mount = default;
			var mounted = false;
			Func<Guest, SVector3>? feetChooser = null;
			for ( var renderer = 0; renderer < guests.Count && !mounted; ++renderer )
			{
				mounted = guests[renderer].TryMount( guest, right, up, forward, eye, out mount );
				if ( mounted ) feetChooser = guests[renderer].FeetChooser;
			}
			if ( !mounted )
				continue;
			var slot = atlas.HasArt( guest.UmbrellaType ) ? guest.UmbrellaType : GuestSpriteAtlas.StandSlot;
			var (direction, mirror) = GuestSpriteAtlas.SelectOriginalDirection( guest.HeadingX, guest.HeadingY, yaw, atlas.DirectionsOf( slot ) );
			var (hotspot, scale) = Place( guest, mount, right, up, forward, eye, feetChooser );
			AddSprite( ref count, atlas.Frames[atlas.GetFrame( slot, direction, 0 )], mirror, hotspot, scale, right, up, forward );
			drawnGuests.Add( guest );
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

	/// <summary>One camera-facing quad (both windings, as the guests') with the frame's hotspot at <paramref name="hotspot"/>, its extents shrunk by <paramref name="scale"/>.</summary>
	private void AddSprite( ref int count, GuestAtlasFrame frame, bool mirror, SVector3 hotspot, float scale, SVector3 right, SVector3 up, SVector3 forward )
	{
		if ( vertices.Length < count + VerticesPerSprite )
			Array.Resize( ref vertices, Math.Max( 64 * VerticesPerSprite, vertices.Length * 2 ) );
		var (x0, x1, yTop, yBottom) = frame.Extents( mirror, GuestRenderer.UnitsPerPixel );
		var u0 = frame.X / (float)atlas!.Width;
		var u1 = (frame.X + frame.Width) / (float)atlas.Width;
		if ( mirror )
			(u0, u1) = (u1, u0);
		var v0 = frame.Y / (float)atlas.Height;
		var v1 = (frame.Y + frame.Height) / (float)atlas.Height;
		SVector3 Corner( float x, float y ) => hotspot + (right * x + up * y) * scale;
		var topLeft = new Vertex( Corner( x0, yTop ), -forward, new SVector2( u0, v0 ) );
		var topRight = new Vertex( Corner( x1, yTop ), -forward, new SVector2( u1, v0 ) );
		var bottomLeft = new Vertex( Corner( x0, yBottom ), -forward, new SVector2( u0, v1 ) );
		var bottomRight = new Vertex( Corner( x1, yBottom ), -forward, new SVector2( u1, v1 ) );
		vertices[count++] = topLeft; vertices[count++] = topRight; vertices[count++] = bottomLeft;
		vertices[count++] = topRight; vertices[count++] = bottomRight; vertices[count++] = bottomLeft;
		vertices[count++] = topLeft; vertices[count++] = bottomLeft; vertices[count++] = topRight;
		vertices[count++] = topRight; vertices[count++] = bottomLeft; vertices[count++] = bottomRight;
		DrawnUmbrellas++;
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
/// The umbrella sprite set (<c>content/umbrellas/esprites/Umbrellas/SPR_UM</c>, docs/UMBRELLAS.md "Content"): the three colours as slots 0 to 2, five directions of one frame each at 256 pixels per unit, the hotspot at the handle's
/// tip. Read from the extension's folder with System.IO like the pools' sets; the browser build embeds the folder (29 KB), so there is nothing to wait for.
/// </summary>
// [EXT:umbrellas-content] OpenTPW's own art
public static class UmbrellaSprites
{
	/// <summary>The folder of the set below the umbrellas content.</summary>
	public static string? SpriteDirectory => UmbrellaContent.Directory is { } folder ? Path.Combine( folder, "esprites", "Umbrellas" ) : null;

	/// <summary>The sprite sets while the extension is on and its content is there (one set: <c>SPR_UM</c>); empty otherwise.</summary>
	public static List<GuestSpriteAtlas> Load() => FeatureGates.Umbrellas ? PoolGuestSprites.LoadFolder( SpriteDirectory, "umbrellas", "umbrellas" ) : new List<GuestSpriteAtlas>();
}
