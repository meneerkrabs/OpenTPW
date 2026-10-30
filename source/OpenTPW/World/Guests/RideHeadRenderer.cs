using System.Runtime.InteropServices;
using Veldrid;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>One rider head to draw: the visitor, the attraction it must be riding, and the head node's world matrix (MD2 axes, see <see cref="HeadPresentation"/>) with the node flags.</summary>
internal readonly record struct HeadSource( int Visitor, int AttractionId, System.Numerics.Matrix4x4 NodeWorld, uint NodeFlags );

/// <summary>A producer of head sources (flat rides now; coaster seats can be added next).</summary>
internal interface IHeadSourceProvider
{
	/// <summary>Appends the heads currently up; <paramref name="onlyAttraction"/> restricts the smoke readback to one attraction.</summary>
	void Collect( List<HeadSource> sources, int? onlyAttraction );
}

/// <summary>Heads of flat rides: every script's head table of every original object, at the node of the head record.</summary>
internal sealed class FlatRideHeadSources : IHeadSourceProvider
{
	private readonly ParkObjects objects;
	private readonly List<(int Visitor, int Node)> scratch = new();

	public FlatRideHeadSources( ParkObjects objects ) => this.objects = objects;

	public void Collect( List<HeadSource> sources, int? onlyAttraction )
	{
		foreach ( var ride in objects.Objects )
		{
			if ( ride.Visitors.Heads == null || ride.IsDeleted || onlyAttraction is int attraction && attraction != ride.Visitors.AttractionId )
				continue;
			scratch.Clear();
			ride.Visitors.CollectHungHeads( scratch );
			foreach ( var (visitor, node) in scratch )
				sources.Add( new HeadSource( visitor, ride.Visitors.AttractionId, ObjectPlacement.SwapAxes( ride.GetNodeTransform( node ) ), ride.Runtime.Model.Nodes[node].Type ) );
		}
	}
}

/// <summary>
/// Draws the head of every rider whose ride has a head up (ADDHEAD, <see cref="RideHeadSpace"/>) as a camera-facing quad of
/// the kid's head sprite at the head node's position, with the frame and roll of <see cref="HeadPresentation"/>. A rider on
/// a ride is otherwise not drawn (<see cref="GuestRenderer"/> skips guests in the Using state unless their object keeps the body, <see cref="GuestSimulation.ShowsKeptBody"/>), so this renderer supplies
/// their head presentation.
/// </summary>
public sealed class RideHeadRenderer : Entity
{
	private const int VerticesPerHead = 12;

	private readonly GuestSimulation simulation;
	private readonly IReadOnlyList<GuestSpriteAtlas> atlases;
	private readonly List<IHeadSourceProvider> providers = new();
	private readonly List<HeadSource> sources = new();
	private readonly Material<ObjectUniformBuffer>? material;
	private readonly List<Texture> textures = new();
	private DeviceBuffer? vertexBuffer;
	private Vertex[] vertices = Array.Empty<Vertex>();
	private bool deleted;

	public bool Visible { get; set; } = true;

	/// <summary>True for the kid-head renderer of a theme without costume heads: the original's head pick then draws by the thing id (0x004FCAC0) and not by the guest's bank +0x20.</summary>
	public bool PickByThingId { get; init; }
	/// <summary>Which riders this renderer draws a head for (null: all): the kid heads leave riders in costume to the costume heads where the theme has them.</summary>
	public Func<Guest, bool>? Draws { get; init; }
	/// <summary>Head quads submitted in the last frame; this count alone does not prove visible pixels.</summary>
	public int DrawnHeads { get; private set; }

	/// <summary>Restricts the native smoke readback to one identified attraction.</summary>
	/// <summary>Adds a producer of head sources (the coaster seats, later).</summary>
	internal void AddSource( IHeadSourceProvider provider ) => providers.Add( provider );

	internal int? OnlyAttraction { get; set; }
	internal IReadOnlyList<Asset> OwnedAssets => textures.Cast<Asset>().Concat( material == null ? Array.Empty<Asset>() : new Asset[] { material, material.Shader } ).ToArray();

	/// <summary>A stale slot from another attraction must never draw this rider.</summary>
	internal static bool CanDraw( Guest? guest, int attractionId ) => guest is { State: GuestState.Using } && guest.AttractionId == attractionId;

	public RideHeadRenderer( GuestSimulation simulation, IReadOnlyList<GuestSpriteAtlas> atlases, ParkObjects objects )
	{
		this.simulation = simulation;
		this.atlases = atlases;
		providers.Add( new FlatRideHeadSources( objects ) );
		Name = "Ride heads";
		if ( atlases.Count == 0 )
			return;
		foreach ( var atlas in atlases.Take( 8 ) )
			textures.Add( GuestRenderer.LoadAtlas( atlas ) );
		var slots = new Texture[8];
		for ( var index = 0; index < slots.Length; index++ )
			slots[index] = textures[Math.Min( index, textures.Count - 1 )];
		material = new Material<ObjectUniformBuffer>( "content/shaders/sprite.shader" );
		material.Set( "Color", slots );
		TexturePackSwitch.PackChanged += ReloadAtlases;
	}

	/// <summary>The texture pack changed: the head atlases are built again, as <see cref="GuestRenderer"/> does for the kid sets.</summary>
	private void ReloadAtlases() => ReloadAtlases( null );

	/// <summary>Refreshes only head atlases supplied by a lazy texture download.</summary>
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
			textures[index] = GuestRenderer.LoadAtlas( atlases[index] );
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

	protected override void OnRender()
	{
		DrawnHeads = 0;
		if ( !Visible || material == null || deleted || atlases.Count == 0 )
			return;
		var view = Camera.ViewMatrix;
		var right = new SVector3( view.M11, view.M21, view.M31 );
		var up = new SVector3( view.M12, view.M22, view.M32 );
		var forward = -new SVector3( view.M13, view.M23, view.M33 );
		// The presentation works in MD2 axes (Y up): engine Y and Z swapped.
		var camera = Camera.Position;
		var cameraMd2 = HeadPresentation.CameraFromEngineView( view, camera.GetSystemVector3() );
		sources.Clear();
		foreach ( var provider in providers )
			provider.Collect( sources, OnlyAttraction );
		var count = 0;
		foreach ( var source in sources )
		{
			var guest = simulation.Find( source.Visitor );
			if ( !CanDraw( guest, source.AttractionId ) || Draws?.Invoke( guest! ) == false )
				continue;
			var placement = HeadPresentation.Compute( source.NodeWorld, source.NodeFlags, cameraMd2 );
			if ( !placement.Visible )
				continue;
			// [BIN:STP-PPC:0x100E8F70] the kidsheads art, or costumeheads when the visitor's costume flag (+0x24) is 2 (set index +0x20); a rider in costume gets the theme's costume head (Level.SetupGuests), and in Fantasy and Hallow, which ship none, its kid head (GUEST-017)
			// [BIN:TPW-EXE:0x004FCAC0 head pick] the head hang (0x0044B410 at 0x0044B471) asks 0x004FCAC0: with costume heads loaded (the count of category 3, 0x00541F60) the guest's own bank +0x20 (costume heads for a guest in costume); with none (Fantasy and Hallow) every rider, costume or not, gets a kid head drawn by its thing id (the world generator reseeded with it, 0x00541F70 with category 1), so there is no division by zero. Not ported: that reseed and draw of the world generator on every head hang
			// [BIN:TPW-EXE:0x005890E0 sprite placement] a head is the body's pixel size (20 / 128 x sqrt(2) world units, SpriteScale) times the scale property 0.685 (0x0074CED0); the roll's reference is HEADS-004
			var atlasIndex = PickByThingId ? GuestAppearance.KidSet( guest!.Id, Math.Min( 8, atlases.Count ) ) : GuestAppearance.KidSet( guest!, Math.Min( 8, atlases.Count ) );
			var atlas = atlases[atlasIndex];
			// The head art is 7 directions (rows, from the pole) of 8 frames (azimuth steps): frame 8 * row + column.
			var frame = atlas.Frames[atlas.GetFrame( GuestSpriteAtlas.StandSlot, placement.Row, placement.Column )];
			var feet = new SVector3( placement.Anchor.X, placement.Anchor.Z, placement.Anchor.Y );
			if ( count + VerticesPerHead > vertices.Length )
				Array.Resize( ref vertices, Math.Max( VerticesPerHead * 64, vertices.Length * 2 ) );
			var angle = placement.Roll * 2 * MathF.PI;
			var (sin, cos) = MathF.SinCos( angle );
			var spriteRight = right * cos + up * sin;
			var spriteUp = up * cos - right * sin;
			var unit = GuestRenderer.UnitsPerPixel * placement.Scale;
			var (x0, x1, yTop, yBottom) = frame.Extents( false, unit );
			var u0 = frame.X / (float)atlas.Width;
			var u1 = (frame.X + frame.Width) / (float)atlas.Width;
			var v0 = frame.Y / (float)atlas.Height;
			var v1 = (frame.Y + frame.Height) / (float)atlas.Height;
			var topLeft = new Vertex( feet + spriteRight * x0 + spriteUp * yTop, -forward, new SVector2( u0, v0 ) ) { TexIndex = atlasIndex };
			var topRight = new Vertex( feet + spriteRight * x1 + spriteUp * yTop, -forward, new SVector2( u1, v0 ) ) { TexIndex = atlasIndex };
			var bottomLeft = new Vertex( feet + spriteRight * x0 + spriteUp * yBottom, -forward, new SVector2( u0, v1 ) ) { TexIndex = atlasIndex };
			var bottomRight = new Vertex( feet + spriteRight * x1 + spriteUp * yBottom, -forward, new SVector2( u1, v1 ) ) { TexIndex = atlasIndex };
			// Both windings, as GuestRenderer does, so the quad survives back-face culling.
			vertices[count++] = topLeft; vertices[count++] = topRight; vertices[count++] = bottomLeft;
			vertices[count++] = topRight; vertices[count++] = bottomRight; vertices[count++] = bottomLeft;
			vertices[count++] = topLeft; vertices[count++] = bottomLeft; vertices[count++] = topRight;
			vertices[count++] = topRight; vertices[count++] = bottomLeft; vertices[count++] = bottomRight;
			DrawnHeads++;
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
