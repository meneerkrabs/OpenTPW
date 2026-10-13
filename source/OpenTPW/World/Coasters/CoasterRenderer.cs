using System.Runtime.InteropServices;
using Veldrid;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Draws the park's coasters (presentation only): a completed track as a box-section tube along its samples, banked by
/// the sample bank; pylon posts from the ground to each node; an incomplete track as straight links between the node
/// tops; and each train as one box per car at its position. Flat colours from a small palette texture through the
/// sprite shader, one dynamic vertex buffer, both windings.
/// </summary>
// [APPROX:COAST-026] tracks are drawn as a flat-coloured box-section tube along the samples with box pylons and box cars; the original extrudes the coaster.sam cross sections (asCrossSectionPoints/Edges, winch section) with the track textures and draws the pylon and car MD2 meshes — evidence needed: the track mesh builder and the pylon/car placement of the coaster renderer
public sealed class CoasterRenderer : Entity
{
	private const float TrackHalfWidth = 3f;
	private const float TrackHalfHeight = 0.75f;
	private const float PylonHalfWidth = 0.6f;
	private const float CarLength = 6f;
	private const float CarHalfWidth = 2.5f;
	private const float CarHeight = 3f;
	private const int PaletteBlock = 8;

	private enum Colour { RailTop, RailSide, Pylon, Car, CarSide, Winch, Link, Station }

	private static readonly RgbaByte[] Palette =
	{
		new( 196, 70, 52, 255 ), new( 120, 40, 32, 255 ), new( 150, 150, 150, 255 ), new( 240, 200, 40, 255 ),
		new( 170, 130, 20, 255 ), new( 90, 90, 100, 255 ), new( 80, 210, 90, 255 ), new( 230, 230, 230, 255 )
	};

	private readonly Func<IEnumerable<CoasterRide>> coasters;
	private readonly Func<SVector2> origin;
	private readonly float scale;
	private readonly Material<ObjectUniformBuffer>? material;
	private readonly Texture? palette;
	private DeviceBuffer? vertexBuffer;
	private readonly List<Vertex> vertices = new();
	private Vertex[] upload = Array.Empty<Vertex>();
	private bool deleted;

	/// <param name="coasters">The coasters to draw each frame.</param>
	/// <param name="origin">Engine position of grid corner (0, 0).</param>
	/// <param name="scale">Engine units per original world unit (0.2).</param>
	public CoasterRenderer( Func<IEnumerable<CoasterRide>> coasters, Func<SVector2> origin, float scale )
	{
		this.coasters = coasters;
		this.origin = origin;
		this.scale = scale;
		Name = "Coasters";
		var pixels = new byte[Palette.Length * PaletteBlock * PaletteBlock * 4];
		for ( var y = 0; y < PaletteBlock; y++ )
		{
			for ( var x = 0; x < Palette.Length * PaletteBlock; x++ )
			{
				var colour = Palette[x / PaletteBlock];
				var at = (y * Palette.Length * PaletteBlock + x) * 4;
				(pixels[at], pixels[at + 1], pixels[at + 2], pixels[at + 3]) = (colour.R, colour.G, colour.B, colour.A);
			}
		}
		palette = new Texture( pixels, Palette.Length * PaletteBlock, PaletteBlock );
		material = new Material<ObjectUniformBuffer>( "content/shaders/sprite.shader" );
		material.Set( "Color", Enumerable.Repeat( palette, 8 ).ToArray() );
	}

	/// <summary>Triangles drawn in the last frame.</summary>
	public int DrawnTriangles { get; private set; }

	/// <summary>Builds the frame's triangles in engine space (no GPU work; also used by tests through <see cref="BuildVertices"/>).</summary>
	public IReadOnlyList<Vertex> BuildVertices()
	{
		vertices.Clear();
		foreach ( var ride in coasters() )
		{
			if ( ride.IsOperational )
				AddTrack( ride );
			else
				AddLinks( ride );
			AddPylons( ride );
			AddTrains( ride );
		}
		return vertices;
	}

	private SVector3 ToEngine( SVector3 world )
	{
		var corner = origin();
		return new SVector3( corner.X + world.X * scale, corner.Y + world.Z * scale, world.Y * scale );
	}

	private static SVector2 Uv( Colour colour ) => new( ((int)colour + 0.5f) / Palette.Length, 0.5f );

	private void Quad( SVector3 a, SVector3 b, SVector3 c, SVector3 d, Colour colour )
	{
		var uv = Uv( colour );
		var (ea, eb, ec, ed) = (ToEngine( a ), ToEngine( b ), ToEngine( c ), ToEngine( d ));
		var normal = SVector3.UnitZ;
		void Add( SVector3 p ) => vertices.Add( new Vertex( p, normal, uv ) );
		Add( ea ); Add( eb ); Add( ec ); Add( ea ); Add( ec ); Add( ed );
		Add( ea ); Add( ec ); Add( eb ); Add( ea ); Add( ed ); Add( ec );
	}

	/// <summary>A box around the segment from <paramref name="from"/> to <paramref name="to"/> with the given half sizes across and up.</summary>
	private void Box( SVector3 from, SVector3 to, SVector3 right, SVector3 up, float halfWidth, float halfHeight, Colour top, Colour side )
	{
		SVector3 Corner( SVector3 centre, float x, float y ) => centre + right * (x * halfWidth) + up * (y * halfHeight);
		var (a0, a1, a2, a3) = (Corner( from, -1, 1 ), Corner( from, 1, 1 ), Corner( from, 1, -1 ), Corner( from, -1, -1 ));
		var (b0, b1, b2, b3) = (Corner( to, -1, 1 ), Corner( to, 1, 1 ), Corner( to, 1, -1 ), Corner( to, -1, -1 ));
		Quad( a0, a1, b1, b0, top );
		Quad( a1, a2, b2, b1, side );
		Quad( a2, a3, b3, b2, side );
		Quad( a3, a0, b0, b3, side );
	}

	private static (SVector3 Right, SVector3 Up) Frame( SVector3 direction, float bank )
	{
		var right = SVector3.Cross( direction, SVector3.UnitY );
		right = right.LengthSquared() < 1e-6f ? SVector3.UnitX : SVector3.Normalize( right );
		var up = SVector3.Normalize( SVector3.Cross( right, direction ) );
		var (sin, cos) = MathF.SinCos( bank );
		return (right * cos + up * sin, up * cos - right * sin);
	}

	private void AddTrack( CoasterRide ride )
	{
		var samples = ride.Samples;
		var winch = ride.WinchSections;
		for ( var index = 0; index < samples.Count; index++ )
		{
			var a = samples[index];
			var b = samples[(index + 1) % samples.Count];
			var (right, up) = Frame( a.Direction, a.Bank );
			var colour = winch.Contains( a.Section ) ? Colour.Winch : Colour.RailTop;
			Box( a.Position, b.Position, right, up, TrackHalfWidth, TrackHalfHeight, colour, Colour.RailSide );
		}
	}

	private void AddLinks( CoasterRide ride )
	{
		var track = ride.Track;
		var nodes = track.Nodes;
		for ( var index = 1; index < nodes.Count; index++ )
		{
			var a = track.NodePosition( ride.Site, nodes[index - 1] );
			var b = track.NodePosition( ride.Site, nodes[index] );
			if ( (b - a).LengthSquared() < 1e-6f )
				continue;
			var (right, up) = Frame( SVector3.Normalize( b - a ), 0 );
			Box( a, b, right, up, TrackHalfWidth * 0.5f, TrackHalfHeight, Colour.Link, Colour.RailSide );
		}
		if ( track.ReturnCell is { } back && track.DepartCell is { } depart && nodes.Count > 0 )
		{
			var a = track.CellPoint( ride.Site, back.X, back.Y, nodes[0].Height );
			var b = track.NodePosition( ride.Site, nodes[0] );
			if ( (b - a).LengthSquared() > 1e-6f )
			{
				var (right, up) = Frame( SVector3.Normalize( b - a ), 0 );
				Box( a, b, right, up, TrackHalfWidth, TrackHalfHeight, Colour.Station, Colour.RailSide );
			}
		}
	}

	private void AddPylons( CoasterRide ride )
	{
		var track = ride.Track;
		foreach ( var node in track.Nodes )
		{
			if ( node.IsStation )
				continue;
			var top = track.NodePosition( ride.Site, node );
			var ground = new SVector3( top.X, ride.Site.GroundHeight( node.X, node.Y ), top.Z );
			if ( top.Y - ground.Y < 0.01f )
				continue;
			Box( ground, top, SVector3.UnitX, SVector3.UnitZ, PylonHalfWidth, PylonHalfWidth, Colour.Pylon, Colour.Pylon );
		}
	}

	private void AddTrains( CoasterRide ride )
	{
		if ( !ride.IsOperational )
			return;
		foreach ( var train in ride.Trains )
		{
			for ( var car = 0; car < Math.Max( 1, train.Cars.Count ); car++ )
			{
				// Train positions are in samples; cars sit CoasterRide.CarSpacing world units apart.
				var front = train.Position - car * CoasterRide.CarSpacing / ride.Spacing;
				var head = ride.SampleAt( front );
				var tail = ride.SampleAt( front - CarLength / ride.Spacing );
				var (right, up) = Frame( head.Direction, head.Bank );
				var lift = up * (TrackHalfHeight + CarHeight / 2);
				Box( tail.Position + lift, head.Position + lift, right, up, CarHalfWidth, CarHeight / 2, Colour.Car, Colour.CarSide );
			}
		}
	}

	protected override void OnRender()
	{
		DrawnTriangles = 0;
		if ( deleted || material == null )
			return;
		var built = BuildVertices();
		if ( built.Count == 0 )
			return;
		if ( upload.Length < built.Count )
			upload = new Vertex[built.Count * 2];
		for ( var index = 0; index < built.Count; index++ )
			upload[index] = built[index];
		var stride = (uint)Marshal.SizeOf<Vertex>();
		var bytes = (uint)upload.Length * stride;
		if ( vertexBuffer == null || vertexBuffer.SizeInBytes < bytes )
		{
			var old = vertexBuffer;
			if ( old != null )
				global::Global.Render.ScheduleDelete( old.Dispose );
			vertexBuffer = Device.ResourceFactory.CreateBuffer( new BufferDescription( bytes, BufferUsage.VertexBuffer | BufferUsage.Dynamic ) );
		}
		var commandList = global::Global.Render.CommandList;
		commandList.UpdateBuffer( vertexBuffer, 0, ref upload[0], (uint)built.Count * stride );
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
		var resourceSets = material.GetResourceSets();
		for ( uint index = 0; index < resourceSets.Length; ++index )
			commandList.SetGraphicsResourceSet( index, resourceSets[index] );
		commandList.Draw( (uint)built.Count );
		DrawnTriangles = built.Count / 3;
	}

	protected override void OnDelete()
	{
		deleted = true;
		// The material and its palette are this renderer's own; they used to stay on the GPU after its park.
		material?.ReleaseOwnedResources();
		palette?.ReleaseOwned();
		var buffer = vertexBuffer;
		vertexBuffer = null;
		if ( buffer != null )
			global::Global.Render.ScheduleDelete( buffer.Dispose );
	}
}
