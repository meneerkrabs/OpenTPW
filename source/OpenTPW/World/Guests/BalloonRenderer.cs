using System.Runtime.InteropServices;
using Veldrid;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Draws the guests' balloons (<see cref="Guest.HasBalloon"/>) as camera-facing quads of the original balloon sprite
/// (<c>esprites.wad/Generic/Balloons/SPR_BL</c>), the way <see cref="GuestRenderer"/> draws the kids: the balloon trails its guest, dips while the guest
/// walks and bobs (tp.exe <c>0x004FA030</c>); a balloon that bursts shows its burst frame for a moment. Presentation only: nothing here touches the
/// simulation, and the colours come from a presentation hash.
/// </summary>
public sealed class BalloonRenderer : Entity
{
	private const int VerticesPerSprite = 12;
	/// <summary>How far behind its guest the balloon trails, in park turns (tp.exe data <c>0x0075C910</c>).</summary>
	public const float TrailTurns = 0.35f;
	/// <summary>The balloon's height above the trailing point, in original units (<c>0x0075C808</c>).</summary>
	public const float Rise = 1f;
	/// <summary>The trailing distance is divided by this before it lowers the balloon (<c>0x0075C90C</c>).</summary>
	public const float DipDivisor = 0.5f;
	/// <summary>The bob table's scale (<c>0x0075C904</c>).</summary>
	public const float BobScale = 1.5f;
	/// <summary>Original world units per map cell (<c>0x004F9F00</c> scales the 16.16 cell position by 10).</summary>
	public const float UnitsPerCell = 10f;
	/// <summary>The bob table (<c>0x0075C810</c>, 20 entries of x, y, z; only y is set): up by 0.02 a step to 0.2 and back.</summary>
	// [BIN:TPW-EXE:0x0075C810 balloon bob] 20 entries (count 0x0075C80C) of three floats; x and z are 0, y runs 0, 0.02 .. 0.2 .. 0.02, 0
	public static readonly float[] Bob = { 0f, 0.02f, 0.04f, 0.06f, 0.08f, 0.1f, 0.12f, 0.14f, 0.16f, 0.18f, 0.2f, 0.18f, 0.16f, 0.14f, 0.12f, 0.1f, 0.08f, 0.06f, 0.04f, 0f };
	/// <summary>What a balloon placement adds to the bob's accumulator (<c>0x0075C900</c>); the phase steps on the first placement that finds it at 1.0 or more (<c>0x007006E0</c>).</summary>
	public const float BobAccumulatorStep = 0.1f;
	/// <summary>Steps of the burst script (0x0074F4C0: register 7, the sprite's alpha, from 250 down by 20 while it stays above 8).</summary>
	public const int BurstSteps = 13;
	/// <summary>The alpha of the burst's first frame (register 7 set to 0xFA) and what each step takes off it.</summary>
	public const int BurstFirstAlpha = 250, BurstAlphaStep = 20;
	/// <summary>Seconds per step of a sprite script: the interval a sprite is created with, 62 ms (<c>0x004758F0</c> +0x80 = 0x3E), which nothing changes for a balloon.</summary>
	public const float ScriptStepSeconds = 0.062f;

	/// <summary>
	/// One balloon placement's effect on the shared bob phase (<c>0x004FA030</c>, the end of each balloon's placement): below 1.0 the accumulator takes 0.1, else it resets and the phase steps
	/// (modulo the 20 table entries). The original places every balloon on every rendered frame, so the phase runs faster the more balloons there are.
	/// </summary>
	// [BIN:TPW-EXE:0x004FA244 bob phase] FCOMP of the accumulator (0x007CEDD8) against 1.0 (0x007006E0): below, it takes 0x0075C900 (0.1, stored as a float); else it is zeroed and 0x007CEDD4 becomes (phase + 1) mod 20 (0x0075C80C); the people's list is walked once per frame by 0x00518F90 from the main loop 0x0054E360
	public static (int Phase, float Accumulator) AdvanceBob( int phase, float accumulator ) =>
		accumulator < 1f ? (phase, accumulator + BobAccumulatorStep) : ((phase + 1) % Bob.Length, 0f);

	/// <summary>The alpha byte of the burst's frame <paramref name="step"/> (0-based): 250 less 20 a step; null once the script has ended after <see cref="BurstSteps"/> frames.</summary>
	// [BIN:TPW-EXE:0x0074F4C0 burst script] SET reg7 = 0xFA; loop: CALL, FRAME 1, SUB reg7 0x14, OP3D0 reg7 8 (loop while above 8), HALT; register 7 is the sprite's alpha byte (+0xA0, read into the vertex colour at 0x00542091)
	public static int? BurstAlpha( int step )
	{
		var alpha = BurstFirstAlpha - BurstAlphaStep * step;
		return step < 0 || step >= BurstSteps || alpha <= 8 ? null : alpha;
	}

	private sealed class BalloonView
	{
		public bool Held;
		public int Slot;
		public float X, Y, Lift;
		public float LastX = float.NaN, LastY;
		public float BurstAt = float.NegativeInfinity;
	}

	private readonly GuestSimulation simulation;
	private readonly ModelHeightfield field;
	private readonly GuestSpriteAtlas? atlas;
	private readonly int[] slots;
	private readonly Material<ObjectUniformBuffer>? material;
	private Texture? texture;
	private readonly Dictionary<Guest, BalloonView> views = new( ReferenceEqualityComparer.Instance );
	private readonly HashSet<Guest> seen = new( ReferenceEqualityComparer.Instance );
	private readonly List<Guest> order = new();
	private readonly List<Guest> stale = new();
	private int bobPhase;
	private float bobAccumulator;
	private DeviceBuffer? vertexBuffer;
	private Vertex[] vertices = Array.Empty<Vertex>();
	private bool deleted;

	public bool Visible { get; set; } = true;
	/// <summary>Balloons drawn in the last frame, bursting ones included.</summary>
	public int DrawnBalloons { get; private set; }

	internal IReadOnlyList<Asset> OwnedAssets => (texture == null ? Array.Empty<Asset>() : new Asset[] { texture })
		.Concat( material == null ? Array.Empty<Asset>() : new Asset[] { material, material.Shader } ).ToArray();

	public BalloonRenderer( GuestSimulation simulation, ModelHeightfield field, IReadOnlyList<GuestSpriteAtlas> atlases )
	{
		this.simulation = simulation;
		this.field = field;
		Name = "Balloons";
		atlas = atlases.FirstOrDefault();
		slots = atlas == null ? [] : atlas.Animations.Animations.Where( slot => !slot.IsEmpty ).Select( slot => slot.Slot ).ToArray();
		if ( atlas == null || slots.Length == 0 )
			return;
		texture = GuestRenderer.LoadAtlas( atlas );
		material = new Material<ObjectUniformBuffer>( "content/shaders/sprite.shader" );
		material.Set( "Color", Enumerable.Repeat( texture, 8 ).ToArray() );
		TexturePackSwitch.PackChanged += ReloadAtlas;
	}

	private void ReloadAtlas() => ReloadAtlas( null );

	/// <summary>[EXT:texture-pack] Rebuilds the balloon atlas after a pack change or a lazy download that names it, as the guests' atlases do.</summary>
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

	/// <summary>
	/// The balloon's height above the ground at its trailing point, in original units: 1, lowered by the distance between the trailing point and the
	/// guest (over 0.5) and raised by the bob table's entry for the guest's id and the shared phase.
	/// </summary>
	// [BIN:TPW-EXE:0x004FA030 balloon placement] the guest's position at t − 0.35 (flag 0) and at t (flag 1) through 0x004F9F00; y = 1.0 − (|Δz| + |Δx|) / 0.5 + y of entry (id + phase 0x007CEDD4) % 20 × 1.5, x and z the trailing point plus the entry's x and z × 1.5; 0x004FE900 moves the balloon to the x and z of the call before
	public static float Lift( float trailX, float trailY, float drawX, float drawY, int guestId, int phase )
	{
		var distance = (MathF.Abs( trailY - drawY ) + MathF.Abs( trailX - drawX )) * UnitsPerCell;
		return Rise - distance / DipDivisor + Bob[(int)((uint)(guestId + phase) % (uint)Bob.Length)] * BobScale;
	}

	protected override void OnRender()
	{
		DrawnBalloons = 0;
		if ( !Visible || material == null || atlas == null || deleted )
			return;
		var now = Time.Now;
		// [BIN:TPW-EXE:0x00518F90 people list] the balloons are placed in the order tp.exe walks its live thing list (head 0x7CF56C, the newest thing first, class 1, 4 to 8 and 18 call the placement 0x004FA030), so each balloon takes the bob phase the original gives it
		// [APPROX:GUEST-017] the burst's first frame shows at the burst, where the original's script starts at its next tick (evidence needed: when the sprite script player ticks a script started in the same frame)
		var view = Camera.ViewMatrix;
		var right = new SVector3( view.M11, view.M21, view.M31 );
		var up = new SVector3( view.M12, view.M22, view.M32 );
		var forward = -new SVector3( view.M13, view.M23, view.M33 );
		seen.Clear();
		var count = 0;
		simulation.FillInThingListOrder( order );
		foreach ( var guest in order )
		{
			seen.Add( guest );
			if ( !views.TryGetValue( guest, out var balloon ) )
			{
				if ( !guest.HasBalloon )
					continue;
				views[guest] = balloon = new BalloonView();
			}
			if ( guest.HasBalloon && !balloon.Held )
			{
				// A new balloon model (the shop, or back after a ride): the colour the guest's id draws (0x00541FD0) and no trail yet.
				balloon.Held = true;
				balloon.Slot = slots[GuestAppearance.BalloonColour( guest.Id, slots.Length ) % slots.Length];
				balloon.LastX = float.NaN;
			}
			else if ( !guest.HasBalloon && balloon.Held )
			{
				balloon.Held = false;
				// A ride removes the model (0x00475550); a burst (0x004FE950) runs the burst script 0x0074F4C0 where the balloon was.
				if ( guest.State is not (GuestState.Using or GuestState.Gone) )
					balloon.BurstAt = now;
			}
			if ( balloon.Held )
			{
				if ( guest.IsVisible )
				{
					var (trailX, trailY) = simulation.DrawPositionBefore( guest, TrailTurns );
					balloon.Lift = Lift( trailX, trailY, guest.DrawX, guest.DrawY, guest.Id, bobPhase );
					(balloon.X, balloon.Y) = float.IsNaN( balloon.LastX ) ? (trailX, trailY) : (balloon.LastX, balloon.LastY);
					(balloon.LastX, balloon.LastY) = (trailX, trailY);
					AddSprite( ref count, atlas.GetFrame( balloon.Slot, 0, 0 ), balloon, right, up, forward, 0 );
				}
				// every balloon's placement, seen or not, advances the shared phase after its own lift was taken
				(bobPhase, bobAccumulator) = AdvanceBob( bobPhase, bobAccumulator );
			}
			else if ( BurstAlpha( (int)MathF.Floor( (now - balloon.BurstAt) / ScriptStepSeconds ) ) is { } alpha )
				AddSprite( ref count, atlas.GetFrame( balloon.Slot, 0, 1 ), balloon, right, up, forward, (uint)alpha );
			else if ( !balloon.Held )
				views.Remove( guest );
		}
		stale.Clear();
		foreach ( var guest in views.Keys )
			if ( !seen.Contains( guest ) )
				stale.Add( guest );
		foreach ( var guest in stale )
			views.Remove( guest );
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
			g_flTime = now
		}.WithFog() );
		commandList.SetVertexBuffer( 0, vertexBuffer );
		commandList.SetPipeline( material.Pipeline );
		var resourceSets = material.GetResourceSets();
		for ( uint index = 0; index < resourceSets.Length; ++index )
			commandList.SetGraphicsResourceSet( index, resourceSets[index] );
		commandList.Draw( (uint)count );
	}

	/// <summary>One camera-facing quad with the frame's hotspot at the balloon's point (both windings, as the guests').</summary>
	private void AddSprite( ref int count, int frameIndex, BalloonView balloon, SVector3 right, SVector3 up, SVector3 forward, uint alpha )
	{
		if ( count + VerticesPerSprite > vertices.Length )
			Array.Resize( ref vertices, Math.Max( 64 * VerticesPerSprite, vertices.Length * 2 ) );
		var frame = atlas!.Frames[frameIndex];
		var anchor = GuestRenderer.CellToEngine( field, balloon.X, balloon.Y, balloon.Lift );
		var unit = GuestRenderer.UnitsPerPixel;
		var (x0, x1, yTop, yBottom) = frame.Extents( false, unit );
		var u0 = frame.X / (float)atlas.Width;
		var u1 = (frame.X + frame.Width) / (float)atlas.Width;
		var v0 = frame.Y / (float)atlas.Height;
		var v1 = (frame.Y + frame.Height) / (float)atlas.Height;
		var topLeft = new Vertex( anchor + right * x0 + up * yTop, -forward, new SVector2( u0, v0 ) );
		var topRight = new Vertex( anchor + right * x1 + up * yTop, -forward, new SVector2( u1, v0 ) );
		var bottomLeft = new Vertex( anchor + right * x0 + up * yBottom, -forward, new SVector2( u0, v1 ) );
		var bottomRight = new Vertex( anchor + right * x1 + up * yBottom, -forward, new SVector2( u1, v1 ) );
		// the alpha byte rides in the vertex's flags word (0 = opaque): the sprite shader blends with it
		topLeft.MatFlags = topRight.MatFlags = bottomLeft.MatFlags = bottomRight.MatFlags = alpha;
		vertices[count++] = topLeft; vertices[count++] = topRight; vertices[count++] = bottomLeft;
		vertices[count++] = topRight; vertices[count++] = bottomRight; vertices[count++] = bottomLeft;
		vertices[count++] = topLeft; vertices[count++] = bottomLeft; vertices[count++] = topRight;
		vertices[count++] = topRight; vertices[count++] = bottomLeft; vertices[count++] = bottomRight;
		DrawnBalloons++;
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
