using System.Runtime.InteropServices;
using Veldrid;
using SVector2 = System.Numerics.Vector2;
using SVector3 = System.Numerics.Vector3;

namespace OpenTPW;

/// <summary>
/// Draws the head of every rider whose ride has a head up (ADDHEAD, <see cref="RideHeadSpace"/>) as a camera-facing quad of
/// the kid's head sprite, hung at the position of the head's model node. A rider on a ride is otherwise not drawn
/// (<see cref="GuestRenderer"/> skips guests in the Using state), so this renderer supplies their head presentation.
/// </summary>
public sealed class RideHeadRenderer : Entity
{
	private const int VerticesPerHead = 12;

	private readonly GuestSimulation simulation;
	private readonly IReadOnlyList<GuestSpriteAtlas> atlases;
	private readonly ParkObjects objects;
	private readonly Material<ObjectUniformBuffer>? material;
	private readonly List<Texture> textures = new();
	private DeviceBuffer? vertexBuffer;
	private Vertex[] vertices = Array.Empty<Vertex>();
	private bool deleted;

	public bool Visible { get; set; } = true;
	/// <summary>Head quads submitted in the last frame; this count alone does not prove visible pixels.</summary>
	public int DrawnHeads { get; private set; }

	/// <summary>Restricts the native smoke readback to one identified attraction.</summary>
	internal int? OnlyAttraction { get; set; }
	internal IReadOnlyList<Asset> OwnedAssets => textures.Cast<Asset>().Concat( material == null ? Array.Empty<Asset>() : new Asset[] { material, material.Shader } ).ToArray();

	/// <summary>A stale slot from another attraction must never draw this rider.</summary>
	internal static bool CanDraw( Guest? guest, int attractionId ) => guest is { State: GuestState.Using } && guest.AttractionId == attractionId;

	public RideHeadRenderer( GuestSimulation simulation, IReadOnlyList<GuestSpriteAtlas> atlases, ParkObjects objects )
	{
		this.simulation = simulation;
		this.atlases = atlases;
		this.objects = objects;
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
		TexturePackSwitch.FilesAvailable += ReloadAtlases;
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
		var rightFlat = SVector2.Normalize( new SVector2( right.X, right.Y ) + new SVector2( 1e-6f, 0 ) );
		var forwardFlat = SVector2.Normalize( new SVector2( forward.X, forward.Y ) + new SVector2( 0, 1e-6f ) );
		var count = 0;
		foreach ( var ride in objects.Objects )
		{
			var heads = ride.Visitors.Heads;
			if ( heads == null || ride.IsDeleted || OnlyAttraction is int attraction && attraction != ride.Visitors.AttractionId )
				continue;
			foreach ( var (visitor, node) in heads.Hung() )
			{
				var guest = simulation.Find( visitor );
				if ( !CanDraw( guest, ride.Visitors.AttractionId ) )
					continue;
				// [APPROX:HEADS-004] pre-boarding guest heading, the body five-direction selector and stand frame 0 present the seven-direction head art; node translation omits seat orientation and world size uses the body pixel scale, none traced as original head presentation
				var atlasIndex = guest!.Type % Math.Min( 8, atlases.Count );
				var atlas = atlases[atlasIndex];
				var heading = new SVector2( guest.HeadingX, guest.HeadingY );
				var (direction, mirror) = GuestSpriteAtlas.SelectDirection( SVector2.Dot( heading, rightFlat ), SVector2.Dot( heading, forwardFlat ) );
				var frame = atlas.Frames[atlas.GetFrame( GuestSpriteAtlas.StandSlot, direction, 0 )];
				var feet = ride.GetNodeTransform( node ).Translation;
				if ( count + VerticesPerHead > vertices.Length )
					Array.Resize( ref vertices, Math.Max( VerticesPerHead * 64, vertices.Length * 2 ) );
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
				// Both windings, as GuestRenderer does, so the quad survives back-face culling.
				vertices[count++] = topLeft; vertices[count++] = topRight; vertices[count++] = bottomLeft;
				vertices[count++] = topRight; vertices[count++] = bottomRight; vertices[count++] = bottomLeft;
				vertices[count++] = topLeft; vertices[count++] = bottomLeft; vertices[count++] = topRight;
				vertices[count++] = topRight; vertices[count++] = bottomLeft; vertices[count++] = bottomRight;
				DrawnHeads++;
			}
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
		} );
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
