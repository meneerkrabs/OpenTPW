using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Veldrid;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// A presentation-only figure <see cref="GuestRenderer.Figures"/> draws with the guests' sprites: its place in cells, the direction it faces, the cells it
/// has walked (the walk animation's step), whether it walks, and the number that chooses its sprite set.
/// </summary>
public readonly record struct SpriteFigure( float CellX, float CellY, float HeadingX, float HeadingY, float DistanceWalked, bool Moving, int Look );

/// <summary>
/// Where and how a guest is drawn this frame (<see cref="GuestRenderer.TryMount"/>): its hotspot in the engine after the depth pull, the share that pull shrinks its quad to, the sprite set's name, the frame (whose pixels per unit
/// say which table a hand is in), and the animation slot, stored direction, step and mirroring of that frame.
/// </summary>
internal readonly record struct GuestMount( System.Numerics.Vector3 Feet, float Share, string SetName, GuestAtlasFrame Frame, int Slot, int Direction, int Step, bool Mirror );

/// <summary>
/// Draws every visible guest as a camera-facing quad of its original kid sprite (one draw call, one dynamic
/// vertex buffer). Direction and walk-cycle frame come from <see cref="GuestSpriteAtlas"/>; the sprite's
/// hotspot (its feet) sits on the terrain heightfield, and the quad is pulled towards the camera as the original pulls a visitor's sprite
/// (<see cref="PullTowardsEye"/>), so the ground in front of the feet does not hide them.
/// </summary>
public sealed class GuestRenderer : Entity
{
	/// <summary>
	/// Engine units per sprite pixel at scale 1 (a kid 43 pixels tall stands 1.9 engine units, 0.95 cell): 20 / 128 original units times 1 / cos(45 degrees), the original's
	/// extent per sprite unit and its 90 degree projection (<see cref="SpriteScale"/>), times the model scale; a frame with other pixels-per-unit fields is scaled by 128 / A across and 128 / B down (<see cref="GuestAtlasFrame.ScaleX"/>).
	/// </summary>
	// [BIN:TPW-EXE:0x005890E0 sprite placement] [BIN:TPW-EXE:0x00578BE0 projection] 20 / 128 × √2 = 0.2210 original units a pixel, 0.04419 engine units; read from the quad placement and the projection builder, replacing the 0.045 that was fitted by eye
	public static readonly float UnitsPerPixel = SpriteScale.EngineUnitsPerPixel;
	/// <summary>
	/// Walk-cycle steps per cell walked: the walk step sets the sprite's interval to 62 ms over twice the speed (the walk script doubles it, the run does not), so a sprite steps
	/// 248 / (62 / (2 v)) times in a turn it covers v / 2.5098 cells in: 20.08 a cell, a run 10.04. The 8 frames of the walk script therefore span 0.4 cell.
	/// </summary>
	// [BIN:TPW-EXE:0x004FA2A0 walk step] v = step length (16.16 per turn) x 3.829657e-05 (0x007006E8), doubled unless the guest runs (the unsigned purpose-speed word +0xC2 above 0, thing types 1 and 7); 0x004D41D0 sets the interval to trunc(62 / v) ms (0x007003A0), at most 250, applied by 0x004D4190; the sprite player 0x00475360 runs one script step per interval
	public const float WalkFramesPerCell = 20.078f;
	private const int VerticesPerGuest = 12;

	private readonly GuestSimulation simulation;
	private readonly ModelHeightfield field;
	private readonly IReadOnlyList<GuestSpriteAtlas> atlases;
	private readonly Material<ObjectUniformBuffer>? material;
	private readonly List<Texture> textures = new();
	private DeviceBuffer? vertexBuffer;
	private Vertex[] vertices = Array.Empty<Vertex>();
	private bool deleted;
	private long rendererPass;
	private long assetGeneration;
	// The last successful CPU world outputs only. Tokens retain no GPU texture or UI frame.
	private readonly Dictionary<Guest, PublishedBody> publishedBodies = new( ReferenceEqualityComparer.Instance );
	private readonly ConditionalWeakTable<Veldrid.Texture, object> resourceIdentities = new();
	private readonly record struct PublishedBody( Level Owner, GuestSimulation Simulation, GuestRenderer Renderer,
		Guest Guest, int Id, long WorldPass, long RendererPass, long Assets, GuestSpriteAtlas Atlas, int AtlasIndex,
		object Resource, bool Costume, IBounceRiderSource? RideSource, int Slot, int Step, float HeadingX, float HeadingY );

	public bool Visible { get; set; } = true;
	/// <summary>Which guests this renderer draws (null: all): the kids' renderer leaves guests in costume to the costume renderer.</summary>
	public Func<Guest, bool>? Draws { get; init; }
	/// <summary>The swimmers' renderer: drawn after the pool water in the late group of <see cref="Level.SplitLate"/>, so the water, which writes no depth, does not blend over the sprites.</summary>
	public bool DrawsAfterWater { get; init; }
	/// <summary>Consumer selection for stateful ride bodies, whose category was copied at player creation. Ordinary walking retains Draws.</summary>
	public Func<Guest, bool>? DrawsRideBody { get; init; }

	internal bool AcceptsBodyConsumer( Guest guest, bool statefulRideBody ) => (statefulRideBody ? DrawsRideBody ?? Draws : Draws)?.Invoke( guest ) != false;
	/// <summary>[EXT:pools-swim] Chooses the animation slot and step of a guest instead of the walking, waiting and standing rules (the swimmers' renderer: the swim strokes); null for the kids and the costumes.</summary>
	public Func<Guest, (int Slot, int Step)?>? SlotChooser { get; init; }
	/// <summary>
	/// The slots drawn with the original's depth pull of a visitor's sprite (<see cref="SpriteScale.PulledDepthShare"/>); null: every slot (the kids and the costumes). The swimmers' renderer
	/// limits it to the slots whose hotspot is the feet (<see cref="PoolSwimSlots.FeetAnchored"/>).
	/// </summary>
	public Func<int, bool>? DepthPulledSlots { get; init; }

	/// <summary>[EXT:pools-swim] Where a guest's hotspot lies in the engine instead of on the ground (the swimmers' renderer: on the water); null for the kids and the costumes.</summary>
	public Func<Guest, SVector3>? FeetChooser { get; init; }

	/// <summary>
	/// Opt-in, presentation only: slots (flat, lying poses) whose quad is turned in its own plane, around the sprite's hotspot, by the angle that is left over when the heading is snapped to the nearest of the
	/// atlas's directions (45 degrees apart), so a body lying along a narrow trough follows the trough instead of poking out over its walls. Upright poses must never be listed.
	/// </summary>
	public IReadOnlySet<int>? RotatedSlots { get; init; }

	/// <summary>
	/// [EXT:pools-swim] The direction is the nearest 45 degree step of the exact heading seen from the camera, which the swimmers' lying poses and their residual rotation rely on. False (the kids and the costumes) is the
	/// original's sprite pass, which turns a heading into one of eight sectors and the camera's yaw into another (<see cref="GuestSpriteAtlas.SelectOriginalDirection"/>).
	/// </summary>
	public bool ExactDirections { get; init; }

	/// <summary>
	/// The in-plane turn of the quad, radians, clockwise on the screen: the screen angle of the heading minus the screen angle of the heading the chosen sprite direction stands for. The heading is projected
	/// with the camera's own right and up axes (their horizontal parts <paramref name="projectRight"/>, <paramref name="projectUp"/>), so the foreshortening of the ground is included; <paramref name="rightFlat"/> and
	/// <paramref name="forwardFlat"/> are the camera's horizontal axes the direction was chosen with. With a camera looking straight along the ground axes the result is within a quarter of a direction step
	/// (22.5 degrees) either way; 0 when the heading is (nearly) none or matches the direction. Clamped to 0.6 radians.
	/// </summary>
	public static float ResidualRotation( SVector2 heading, SVector2 rightFlat, SVector2 forwardFlat, SVector2 projectRight, SVector2 projectUp )
	{
		if ( heading.LengthSquared() < 1e-8f )
			return 0f;
		var angle = MathF.Atan2( SVector2.Dot( heading, rightFlat ), SVector2.Dot( heading, forwardFlat ) );
		var snapped = Math.Clamp( (int)MathF.Round( MathF.Abs( angle ) / (MathF.PI / 4) ), 0, 4 ) * (MathF.PI / 4) * (angle < 0 ? -1 : 1);
		var stand = rightFlat * MathF.Sin( snapped ) + forwardFlat * MathF.Cos( snapped );
		float Screen( SVector2 v ) => MathF.Atan2( SVector2.Dot( v, projectRight ), SVector2.Dot( v, projectUp ) );
		var turn = Screen( heading ) - Screen( stand );
		while ( turn > MathF.PI ) turn -= 2 * MathF.PI;
		while ( turn <= -MathF.PI ) turn += 2 * MathF.PI;
		return Math.Clamp( turn, -0.6f, 0.6f );
	}

	/// <summary>
	/// Opt-in, presentation only: how far, in engine units, a guest's quad is moved towards the camera along its view axis (the screen position stays the same, perspective apart). The quad is a camera-facing
	/// plane of one depth, so a guest lying in a trough (a water slide's flume) is cut by the curved bed in front of it; pulling the quad forward by about the trough's depth stops that. 0 or null for every guest
	/// that does not need it.
	/// </summary>
	public Func<Guest, float>? DepthPullChooser { get; init; }
	/// <summary>[EXT:pools-render] Whether a slot the chosen set has no art for is drawn as the nearest swim slot (<see cref="GuestSpriteAtlas.UsableSlot"/>); the swimmers' renderer only, so any other chooser keeps the stand-slot fallback.</summary>
	public bool SwimSlotFallback { get; init; }
	/// <summary>
	/// [EXT:pools-render] The slots whose frames are drawn with their bottom edge at the guest's point instead of their hotspot (the swimmers' renderer: the swimwear slots whose hotspot is the centre of the
	/// torso, <see cref="PoolSwimSlots.BottomAnchored"/>), so a pool activity's <see cref="IPoolActivity.FeetHeight"/> is where the body rests: the feet on a rung, the back on a slide or a lounger. Null for the
	/// kids and the costumes, whose hotspot is the feet.
	/// </summary>
	public IReadOnlySet<int>? BottomAnchoredSlots { get; init; }
	/// <summary>
	/// The riders of the flat rides' bounce tables, whose bodies the original's script pass draws on the ride (<see cref="BounceRiders"/>); a guest in the Using state whose ride keeps it in a bounce slot is
	/// drawn at the pose it gives instead of being skipped. Null: no rider is drawn.
	/// </summary>
	internal IBounceRiderSource? BounceRiders { get; set; }
	private readonly Dictionary<int, BounceRiderPose> bounceRiders = new();
	/// <summary>Guests drawn in the last frame.</summary>
	public int DrawnGuests { get; private set; }

	/// <summary>
	/// Figures drawn after the guests with the same sprite sets, which are not guests: the online players of a visited park (<see cref="ParkPresence"/>).
	/// The simulation, the visitor picking and the world hash never see them. Null: none.
	/// </summary>
	public Func<IReadOnlyCollection<SpriteFigure>>? Figures { get; set; }
	/// <summary>Figures drawn in the last frame.</summary>
	public int DrawnFigures { get; private set; }

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
			old.Add( ReplaceAtlas( index, LoadAtlas( atlases[index] ) ) );
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
		// A partial reload invalidates every publication before its first resource is replaced.
		assetGeneration++;
		publishedBodies.Clear();
		textures[index] = replacement;
		return old;
	}

	private bool CanRender => Visible && material != null && !deleted;
	private bool HasAtlas( int index ) => index >= 0 && index < atlases.Count && index < textures.Count
		&& textures[index].NativeTexture is { IsDisposed: false };
	// These are owned-state checks, never extension callbacks. Registered kids/costumes only; no ride or swim route.
	private bool OrdinaryRenderer => Level != null && ReferenceEquals( Level, Level.Current )
		&& ReferenceEquals( Level.Guests, simulation ) && Entity.All.Contains( this )
		&& (ReferenceEquals( Level.GuestRenderer, this ) || ReferenceEquals( Level.CostumeRenderer, this ))
		&& SlotChooser == null && FeetChooser == null && DepthPullChooser == null && DepthPulledSlots == null
		&& RotatedSlots == null && BottomAnchoredSlots == null && !ExactDirections && !SwimSlotFallback;
	private bool OrdinaryGuest( Guest guest ) =>
		(ReferenceEquals( Level.CostumeRenderer, this ) ? guest.InCostume : !guest.InCostume || Level.CostumeRenderer == null)
		&& guest.IsVisible && !guest.InSwimSession && guest.Id > 0 && guest.Type >= 0
		&& float.IsFinite( guest.DrawX ) && float.IsFinite( guest.DrawY )
		&& float.IsFinite( guest.HeadingX ) && float.IsFinite( guest.HeadingY ) && float.IsFinite( guest.DrawDistanceWalked )
		&& guest.DrawDistanceWalked >= 0 && double.IsFinite( simulation.TimeSeconds );

	private static bool ValidFrame( GuestSpriteAtlas atlas, GuestAtlasFrame frame ) => atlas.Width > 0 && atlas.Height > 0
		&& frame.X >= 0 && frame.Y >= 0 && frame.Width > 0 && frame.Height > 0
		&& (long)frame.X + frame.Width <= atlas.Width && (long)frame.Y + frame.Height <= atlas.Height;

	/// <summary>
	/// The selected ordinary visitor's successful current world body output, at literal camera yaw zero. Pure observation:
	/// no chooser, pose collection, animation player, movement or RNG work. UI-910 retains native clone/lifecycle qualifications.
	/// </summary>
	internal UI.Original.UiSpriteFrame? VisitorFrame( Guest guest )
	{
		if ( !CanRender || !OrdinaryRenderer || !OrdinaryGuest( guest ) || !publishedBodies.TryGetValue( guest, out var output )
			|| !ReferenceEquals( output.Owner, Level ) || !ReferenceEquals( output.Simulation, simulation ) || !ReferenceEquals( output.Renderer, this )
			|| !ReferenceEquals( output.Guest, guest ) || output.Id != guest.Id || output.Costume != guest.InCostume
			|| !ReferenceEquals( output.RideSource, BounceRiders ) || output.WorldPass != Level.RenderPass
			|| output.RendererPass != rendererPass || output.Assets != assetGeneration || !HasAtlas( output.AtlasIndex )
			|| !ReferenceEquals( output.Atlas, atlases[output.AtlasIndex] )
			|| !resourceIdentities.TryGetValue( textures[output.AtlasIndex].NativeTexture, out var resource ) || !ReferenceEquals( resource, output.Resource )
			|| !simulation.Guests.Any( item => ReferenceEquals( item, guest ) ) )
			return null;
		var atlas = output.Atlas;
		if ( output.Slot < 0 || output.Slot >= atlas.Animations.Animations.Count || atlas.Frames.Length == 0
			|| atlas.DirectionsOf( output.Slot ) <= 0 || output.Step < 0 || output.Step >= Math.Max( 1, atlas.FramesPerDirection( output.Slot ) ) )
			return null;
		var (direction, mirror) = GuestSpriteAtlas.SelectOriginalDirection( output.HeadingX, output.HeadingY, 0f, atlas.DirectionsOf( output.Slot ) );
		var frame = atlas.Frames[atlas.GetFrame( output.Slot, direction, output.Step )];
		if ( !ValidFrame( atlas, frame ) )
			return null;
		return new UI.Original.UiSpriteFrame( textures[output.AtlasIndex].NativeTexture, atlas.Width, atlas.Height,
			frame.X, frame.Y, frame.Width, frame.Height, frame.OriginX, frame.OriginY, mirror, frame.ScaleX, frame.ScaleY );
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

	/// <summary>The camera's position in engine space, from its view matrix.</summary>
	internal static SVector3 Eye( System.Numerics.Matrix4x4 view ) => System.Numerics.Matrix4x4.Invert( view, out var inverse ) ? inverse.Translation : SVector3.Zero;

	/// <summary>
	/// A visitor's or a staff member's sprite after the original's depth pull (<see cref="SpriteScale.PulledDepthShare"/>): the hotspot moves along its view ray towards the
	/// <paramref name="eye"/> and the quad's extents shrink by the same share, so the quad covers the same pixels, nearer the camera. A hotspot behind the eye stays.
	/// </summary>
	internal static (SVector3 Feet, float Share) PullTowardsEye( SVector3 feet, SVector3 eye, SVector3 forward )
	{
		var depth = SVector3.Dot( feet - eye, forward );
		if ( !(depth > 0f) )
			return (feet, 1f);
		var share = SpriteScale.PulledDepthShare( depth / ObjectPlacement.ModelScale );
		return (eye + (feet - eye) * share, share);
	}

	/// <summary>Atlas, frame and mirroring for a guest seen with the given camera axes (engine space).</summary>
	public (int Atlas, int Frame, bool Mirror) SelectSprite( Guest guest, SVector3 cameraRight, SVector3 cameraForward )
	{
		var (atlas, frame, mirror, _, _, _, _) = SelectSpriteAndSlot( guest, cameraRight, cameraForward );
		return (atlas, frame, mirror);
	}

	private (int Atlas, int Frame, bool Mirror, int Slot, int Direction, float Rotation, int Step) SelectSpriteAndSlot( Guest guest, SVector3 cameraRight, SVector3 cameraForward, SVector3 cameraUp = default, BounceRiderPose? rider = null )
	{
		// [BIN:TPW-EXE:0x004FB19E guest sprite variant] the kid set is the guest's own draw at its creation, a function of its thing id alone (GuestAppearance.KidSet), not of its peep type
		var atlasIndex = GuestAppearance.KidSet( guest.Id, atlases.Count );
		var atlas = atlases[atlasIndex];
		var rightFlat = SVector2.Normalize( new SVector2( cameraRight.X, cameraRight.Y ) + new SVector2( 1e-6f, 0 ) );
		var forwardFlat = SVector2.Normalize( new SVector2( cameraForward.X, cameraForward.Y ) + new SVector2( 0, 1e-6f ) );
		var heading = new SVector2( guest.HeadingX, guest.HeadingY );
		int direction;
		bool mirror;
		int slot, step;
		if ( rider is { } bouncing )
		{
			// [BIN:TPW-EXE:0x00557CFC bounce template] the bouncing body is the run animation (template 0x0074DBC0, slot 2), the broken ride's the standing one (template 0x0074DC20, slot 0); the heading byte is the pose's own
			(slot, step) = (bouncing.Slot >= 0 ? (atlas.HasArt( bouncing.Slot ) ? bouncing.Slot : GuestSpriteAtlas.StandSlot) : bouncing.Broken ? GuestSpriteAtlas.StandSlot : GuestSpriteAtlas.RunSlot, bouncing.Step);
			heading = default;
		}
		else if ( SlotChooser?.Invoke( guest ) is { } chosen )
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
		step %= Math.Max( 1, atlas.FramesPerDirection( slot ) );
		if ( rider is { } turned )
		{
			var yaw = GuestSpriteAtlas.CameraYaw( cameraRight, cameraForward, Camera.Mode is ParkCameraMode );
			(direction, mirror) = GuestSpriteAtlas.SelectOriginalDirection( turned.Sector, yaw, atlas.DirectionsOf( slot ) );
		}
		else if ( ExactDirections )
			(direction, mirror) = GuestSpriteAtlas.SelectDirection( SVector2.Dot( heading, rightFlat ), SVector2.Dot( heading, forwardFlat ) );
		else
		{
			// [BIN:TPW-EXE:0x00542010 direction] the original's sprite pass: the heading sector plus the camera's yaw sector, no pitch (GuestSpriteAtlas.SelectOriginalDirection)
			var yaw = GuestSpriteAtlas.CameraYaw( cameraRight, cameraForward, Camera.Mode is ParkCameraMode );
			(direction, mirror) = GuestSpriteAtlas.SelectOriginalDirection( heading.X, heading.Y, yaw, atlas.DirectionsOf( slot ) );
		}
		var rotation = 0f;
		if ( RotatedSlots?.Contains( slot ) == true && cameraUp != default )
			rotation = ResidualRotation( heading, rightFlat, forwardFlat, new SVector2( cameraRight.X, cameraRight.Y ), new SVector2( cameraUp.X, cameraUp.Y ) );
		return (atlasIndex, atlas.GetFrame( slot, direction, step ), mirror, slot, direction, rotation, step);
	}

	private readonly Dictionary<(int Atlas, int Slot, int Direction), float> anchorLifts = new();

	/// <summary>
	/// The engine units the frames of one animation (atlas, slot, direction) are raised along the camera's up axis so their bottom edge, not their hotspot, sits on the guest's point (<see cref="BottomAnchoredSlots"/>);
	/// 0 for any other slot. One lift for the whole animation, the largest over its frames (<see cref="AnchorLift(IReadOnlySet{int}?, int, IEnumerable{GuestAtlasFrame})"/>), so the sprite does not jump between frames
	/// whose bodies hang different rows below the hotspot. Cached per animation. Presentation only.
	/// </summary>
	internal float AnchorLift( int atlasIndex, int slot, int direction )
	{
		if ( BottomAnchoredSlots?.Contains( slot ) != true )
			return 0f;
		if ( !anchorLifts.TryGetValue( (atlasIndex, slot, direction), out var lift ) )
		{
			var atlas = atlases[atlasIndex];
			lift = AnchorLift( BottomAnchoredSlots, slot, Enumerable.Range( 0, Math.Max( 1, atlas.FramesPerDirection( slot ) ) ).Select( step => atlas.Frames[atlas.GetFrame( slot, direction, step )] ) );
			anchorLifts[(atlasIndex, slot, direction)] = lift;
		}
		return lift;
	}

	/// <summary>The lift of one frame: the rows below its hotspot, in engine units (see <see cref="AnchorLift(int, int, int)"/>); 0 for a slot that is not bottom-anchored.</summary>
	internal static float AnchorLift( IReadOnlySet<int>? bottomAnchored, int slot, GuestAtlasFrame frame ) =>
		bottomAnchored?.Contains( slot ) == true ? Math.Max( 0, frame.OriginY + frame.Height ) * UnitsPerPixel * frame.ScaleY : 0f;

	/// <summary>The lift of an animation: the largest <see cref="AnchorLift(IReadOnlySet{int}?, int, GuestAtlasFrame)"/> over its frames.</summary>
	internal static float AnchorLift( IReadOnlySet<int>? bottomAnchored, int slot, IEnumerable<GuestAtlasFrame> animation ) =>
		animation.Select( frame => AnchorLift( bottomAnchored, slot, frame ) ).DefaultIfEmpty( 0f ).Max();

	/// <summary>
	/// Where a guest this renderer draws stands, for what is carried or held at its hand (<see cref="UmbrellaRenderer"/>): the hotspot after the depth pull and that pull's share, and the frame the guest is drawn
	/// with (set, pixels per unit, slot, direction, step, mirroring), so a table of hand points can be read for it. False when this renderer does not draw the guest now (a rider, a swimmer, one in the other set).
	/// </summary>
	internal bool TryMount( Guest guest, SVector3 right, SVector3 up, SVector3 forward, SVector3 eye, out GuestMount mount )
	{
		mount = default;
		// a user whose kept body stands where it stood (GuestSimulation.ShowsKeptBody, no ride pose) keeps what it holds, as its body stays in view; a body the ride poses mounts nothing, as for any rider
		if ( !Visible || deleted || atlases.Count == 0 || !guest.IsVisible && !StandsInPlace( guest ) || Draws?.Invoke( guest ) == false )
			return false;
		var (atlasIndex, frameIndex, mirror, slot, direction, _, step) = SelectSpriteAndSlot( guest, right, forward, up );
		var atlas = atlases[atlasIndex];
		var feet = (FeetChooser?.Invoke( guest ) ?? GroundPoint( guest, !guest.IsVisible )) + up * AnchorLift( atlasIndex, slot, direction ) - forward * (DepthPullChooser?.Invoke( guest ) ?? 0f);
		var share = 1f;
		if ( DepthPulledSlots?.Invoke( slot ) != false )
			(feet, share) = PullTowardsEye( feet, eye, forward );
		mount = new GuestMount( feet, share, atlas.Name, atlas.Frames[frameIndex], slot, direction, step, mirror );
		return true;
	}

	/// <summary>The ground point a guest is drawn at: its own drawn position, or for a kept body standing in place where that body was last placed (<see cref="GuestSimulation.KeptBodyPosition"/>).</summary>
	private SVector3 GroundPoint( Guest guest, bool keptInPlace )
	{
		var (x, y) = keptInPlace ? simulation.KeptBodyPosition( guest ) : (guest.DrawX, guest.DrawY);
		return CellToEngine( field, x, y );
	}

	/// <summary>A guest in state 16 whose kept body stands where it stood: no ride pose (walk, bounce or a held one) draws it.</summary>
	private bool StandsInPlace( Guest guest ) => simulation.ShowsKeptBody( guest )
		&& !(BounceRiders?.TryGetPose( guest.Id, out var pose ) == true && RideHeadRenderer.CanDraw( guest, pose.AttractionId ));

	// The body player proves only scale-one provenance, never the renderer's selected frame or phase.
	// Legacy/imported reconstructed histories deliberately remain on the rectangle, potentially for their lifetime.
	private bool OrdinaryCircleSource( Guest guest, int atlasIndex, GuestAtlasFrame frame ) =>
		OrdinaryRenderer && Level.OriginalPark != null && OrdinaryGuest( guest ) && guest.Type < simulation.Settings.Types.Count
		&& material?.Pipeline is { IsDisposed: false } && HasAtlas( atlasIndex ) && ValidFrame( atlases[atlasIndex], frame )
		&& !guest.BodySpriteHistoryReconstructed && guest.BodySpriteInCostume == guest.InCostume
		&& guest.BodySprite.Player is { Output: { } output } player
		&& VisitorSpritePlayer.IsSupported( player.ActiveTemplate ) && VisitorSpritePlayer.IsSupported( player.CurrentProgram )
		&& VisitorSpritePlayer.IsValidOutput( output )
		&& (guest.BodySprite.PendingTemplate is not { } pending || VisitorSpritePlayer.IsSupported( pending ));

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
		var canonicalView = Hud.VisitorCirclePicker.IsCanonicalView( view, projection, viewport );
		Guest? picked = null;
		var best = float.PositiveInfinity;
		foreach (var guest in simulation.Guests)
		{
			if (!guest.IsVisible || Draws?.Invoke(guest) == false || !simulation.IsInPark(guest) || !float.IsFinite(guest.DrawX) || !float.IsFinite(guest.DrawY) ||
				!float.IsFinite(guest.HeadingX) || !float.IsFinite(guest.HeadingY) || !float.IsFinite(guest.DrawDistanceWalked) || guest.Type < 0)
				continue;
			var (atlasIndex, frameIndex, mirror, slot, direction, rotation, _) = SelectSpriteAndSlot(guest, right, forward, new SVector3(view.M12, view.M22, view.M32));
			var frame = atlases[atlasIndex].Frames[frameIndex];
			var feet = (FeetChooser?.Invoke( guest ) ?? CellToEngine(field, guest.DrawX, guest.DrawY)) + new SVector3(view.M12, view.M22, view.M32) * AnchorLift(atlasIndex, slot, direction) - forward * (DepthPullChooser?.Invoke(guest) ?? 0f);
			float hit;
			// Eligibility and hit outcome are separate: a supported circle MISS must never retry the rectangle.
			if (canonicalView && OrdinaryCircleSource(guest, atlasIndex, frame)
				&& Hud.VisitorCirclePicker.TryCreate(feet, frame, mirror, view, projection, viewport, mouse, out var circle, out var pointer, out hit))
			{
				if (!circle.Contains(pointer.X, pointer.Y))
					continue;
			}
			else if (!Hud.GuestWorldPicker.TryHit(feet, frame, mirror, view, projection, viewport, mouse, out hit, rotation))
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

	/// <summary>
	/// Atlas, frame and mirroring of a <see cref="SpriteFigure"/>: walking or standing as a guest is, in the sprite set its look chooses, facing
	/// its heading through the original's sprite pass (<see cref="GuestSpriteAtlas.SelectOriginalDirection"/>).
	/// </summary>
	internal (int Atlas, int Frame, bool Mirror) SelectFigureSprite( SpriteFigure figure, SVector3 cameraRight, SVector3 cameraForward )
	{
		// the material binds the first eight sets (the constructor's slots)
		var atlasIndex = (int)((uint)figure.Look % (uint)Math.Min( atlases.Count, 8 ));
		var atlas = atlases[atlasIndex];
		var slot = figure.Moving ? GuestSpriteAtlas.WalkSlot : GuestSpriteAtlas.StandSlot;
		var step = figure.Moving ? (int)(figure.DistanceWalked * WalkFramesPerCell) : 0;
		step %= Math.Max( 1, atlas.FramesPerDirection( slot ) );
		var yaw = GuestSpriteAtlas.CameraYaw( cameraRight, cameraForward, Camera.Mode is ParkCameraMode );
		var (direction, mirror) = GuestSpriteAtlas.SelectOriginalDirection( figure.HeadingX, figure.HeadingY, yaw, atlas.DirectionsOf( slot ) );
		return (atlasIndex, atlas.GetFrame( slot, direction, step ), mirror);
	}

	/// <summary>The actual world pass's CPU selection and quad preparation, shared with asset-free tests. UI never calls this.</summary>
	internal int PrepareWorld()
	{
		rendererPass++;
		publishedBodies.Clear();
		DrawnGuests = 0;
		DrawnFigures = 0;
		if ( !CanRender || atlases.Count == 0 )
			return 0;
		var view = Camera.ViewMatrix;
		var right = new SVector3( view.M11, view.M21, view.M31 );
		var up = new SVector3( view.M12, view.M22, view.M32 );
		var forward = -new SVector3( view.M13, view.M23, view.M33 );
		var eye = Eye( view );
		var figures = Figures?.Invoke();
		var needed = (simulation.Guests.Count + (figures?.Count ?? 0)) * VerticesPerGuest;
		if ( vertices.Length < needed )
			vertices = new Vertex[Math.Max( needed, 64 * VerticesPerGuest ) * 2];
		var count = 0;
		bounceRiders.Clear();
		BounceRiders?.Collect( bounceRiders );
		var canPublish = OrdinaryRenderer;
		foreach ( var guest in simulation.Guests )
		{
			// a guest on a bounce ride is hidden like any rider, and drawn from the ride's own pose (a stale slot of another attraction never draws it)
			BounceRiderPose? rider = bounceRiders.Count > 0 && bounceRiders.TryGetValue( guest.Id, out var pose ) && RideHeadRenderer.CanDraw( guest, pose.AttractionId ) ? pose : null;
			// a kept body the object's script does not pose stays in view where it stood (GuestSimulation.ShowsKeptBody), drawn by the renderer of its kept body's appearance
			var inPlace = rider == null && !guest.IsVisible && simulation.ShowsKeptBody( guest );
			if ( !guest.IsVisible && rider == null && !inPlace || rider is { Visible: false } || !AcceptsBodyConsumer( guest, rider?.UsesSpriteState == true || inPlace ) )
				continue;
			var (atlasIndex, frameIndex, mirror, slot, direction, rotation, step) = SelectSpriteAndSlot( guest, right, forward, up, rider );
			var atlas = atlases[atlasIndex];
			var frame = atlas.Frames[frameIndex];
			var feet = (rider?.Feet ?? FeetChooser?.Invoke( guest ) ?? GroundPoint( guest, inPlace )) + up * AnchorLift( atlasIndex, slot, direction ) - forward * (DepthPullChooser?.Invoke( guest ) ?? 0f);
			var share = 1f;
			if ( DepthPulledSlots?.Invoke( slot ) != false )
				(feet, share) = PullTowardsEye( feet, eye, forward );
			AddQuad( ref count, atlas, atlasIndex, frame, mirror, feet, share, rotation, right, up, forward );
			// Publication follows the successful world quad, never candidate selection, fake Figures or a ride pose.
			if ( rider == null && canPublish && OrdinaryGuest( guest ) && HasAtlas( atlasIndex ) && ValidFrame( atlas, frame )
				&& float.IsFinite( feet.X ) && float.IsFinite( feet.Y ) && float.IsFinite( feet.Z ) && float.IsFinite( share ) )
				publishedBodies[guest] = new PublishedBody( Level, simulation, this, guest, guest.Id, Level.RenderPass, rendererPass,
					assetGeneration, atlas, atlasIndex, resourceIdentities.GetValue( textures[atlasIndex].NativeTexture, _ => new object() ),
					guest.InCostume, BounceRiders, slot, step, guest.HeadingX, guest.HeadingY );
			DrawnGuests++;
		}
		if ( figures != null )
			foreach ( var figure in figures )
			{
				if ( !float.IsFinite( figure.CellX ) || !float.IsFinite( figure.CellY ) || !float.IsFinite( figure.HeadingX ) || !float.IsFinite( figure.HeadingY ) || !float.IsFinite( figure.DistanceWalked ) )
					continue;
				var (atlasIndex, frameIndex, mirror) = SelectFigureSprite( figure, right, forward );
				var atlas = atlases[atlasIndex];
				var (feet, share) = PullTowardsEye( CellToEngine( field, figure.CellX, figure.CellY ), eye, forward );
				AddQuad( ref count, atlas, atlasIndex, atlas.Frames[frameIndex], mirror, feet, share, 0f, right, up, forward );
				DrawnFigures++;
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

	/// <summary>Adds one camera-facing quad (both windings) of <paramref name="frame"/> with its hotspot at <paramref name="feet"/>, shrunk by <paramref name="share"/> and turned by <paramref name="rotation"/> in its plane.</summary>
	private void AddQuad( ref int count, GuestSpriteAtlas atlas, int atlasIndex, GuestAtlasFrame frame, bool mirror, SVector3 feet, float share, float rotation, SVector3 right, SVector3 up, SVector3 forward )
	{
		var (x0, x1, yTop, yBottom) = frame.Extents( mirror, UnitsPerPixel );
		var u0 = frame.X / (float)atlas.Width;
		var u1 = (frame.X + frame.Width) / (float)atlas.Width;
		if ( mirror )
			(u0, u1) = (u1, u0);
		var v0 = frame.Y / (float)atlas.Height;
		var v1 = (frame.Y + frame.Height) / (float)atlas.Height;
		// a lying pose may be turned in its own plane about the hotspot (RotatedSlots); (sin, cos) of the clockwise screen angle
		var (sin, cos) = MathF.SinCos( rotation );
		SVector3 Corner( float x, float y ) => feet + (right * (x * cos + y * sin) + up * (-x * sin + y * cos)) * share;
		var topLeft = new Vertex( Corner( x0, yTop ), -forward, new SVector2( u0, v0 ) ) { TexIndex = atlasIndex };
		var topRight = new Vertex( Corner( x1, yTop ), -forward, new SVector2( u1, v0 ) ) { TexIndex = atlasIndex };
		var bottomLeft = new Vertex( Corner( x0, yBottom ), -forward, new SVector2( u0, v1 ) ) { TexIndex = atlasIndex };
		var bottomRight = new Vertex( Corner( x1, yBottom ), -forward, new SVector2( u1, v1 ) ) { TexIndex = atlasIndex };
		// Both windings, so the quad survives back-face culling whatever the clip-space convention.
		vertices[count++] = topLeft; vertices[count++] = topRight; vertices[count++] = bottomLeft;
		vertices[count++] = topRight; vertices[count++] = bottomRight; vertices[count++] = bottomLeft;
		vertices[count++] = topLeft; vertices[count++] = bottomLeft; vertices[count++] = topRight;
		vertices[count++] = topRight; vertices[count++] = bottomLeft; vertices[count++] = bottomRight;
	}

	protected override void OnDelete()
	{
		deleted = true;
		assetGeneration++;
		publishedBodies.Clear();
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
