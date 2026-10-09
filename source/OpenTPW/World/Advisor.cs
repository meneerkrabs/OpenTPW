using System.Numerics;
using Veldrid;

namespace OpenTPW;

/// <summary>
/// The original advisor model (<c>global/advisor.wad/Advisor.MD2</c>) drawn in a corner
/// viewport, saying one global speech clip with its mouth driven by the clip's LIP marks
/// (see docs/LIPS.md). The model carries five co-located mouth meshes; the LIP data only
/// distinguishes talking from silence, so talking shows "Mouth - Aah" and silence shows
/// "Mouth - Normal". Which shape the original game picks while talking, how it poses the
/// head/hands (the <c>Advisorm*</c> clips carry undecoded non-rigid tracks) and which hat it shows are not
/// known; hats, the spatula, the bow tie and the blink meshes are hidden. The model is drawn in
/// its bind pose (see <see cref="NodeWorld"/>).
/// </summary>
internal sealed class Advisor : IDisposable
{
	internal const string ArchivePath = "/global/advisor";
	internal const string SpeechArchivePath = "/global/Speech/speechHD";
	internal const string LipArchivePath = "/global/Speech/lips";
	internal const string ClosedMouth = "Mouth - Normal";
	internal const string TalkingMouth = "Mouth - Aah";
	internal const int FirstClip = 1;
	internal const int LastClip = 637;
	internal static readonly string[] BodyMeshes = { "Body", "Bug Head", "Right Antennae", "Left Antennae", "Right hand", "Left Hand", "Right Eye", "Left Eye" };

	private readonly List<(string Name, Model Model, Material Material)> parts = new();
	private SpeechAudioPlayer? player;
	private LipSyncTimeline? timeline;
	private bool disposed;
	private System.Numerics.Vector3 mouthMin = new( float.MaxValue );
	private System.Numerics.Vector3 mouthMax = new( float.MinValue );
	private static readonly System.Numerics.Vector3 CameraPosition = new( 0, 0, -70 );
	private static readonly Matrix4x4 View = Matrix4x4.CreateLookAt( CameraPosition, System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitY );
	private static readonly Matrix4x4 Projection = Matrix4x4.CreatePerspectiveFieldOfView( 40f * MathF.PI / 180f, 1f, 1f, 500f );

	public int? ClipNumber { get; private set; }
	public TimeSpan Position => player?.Position ?? TimeSpan.Zero;
	public bool IsSpeaking => player != null && !player.IsFinished;
	public string ClockSource => player?.ClockSource ?? "none";
	public string MouthMesh => MouthFor( timeline, Position );
	/// <summary>Mouth mesh used by the most recent <see cref="Render"/> call.</summary>
	public string? LastRenderedMouth { get; private set; }

	/// <summary>Pixel rectangle of the advisor viewport for the current screen size.</summary>
	public static (int X, int Y, int Size) ViewportRectangle()
	{
		var size = Math.Max( 64, (int)(Math.Min( Screen.Width, Screen.Height ) / 3) );
		const int margin = 16;
		return (margin, Math.Max( 0, (int)Screen.Height - size - margin ), size);
	}

	public Advisor()
	{
		var modelFile = new ModelFile( $"{ArchivePath}/Advisor.MD2" );
		var restTransforms = ModelAnimationPlayer.ComputeRestTransforms( modelFile );
		var textures = new Dictionary<string, Texture>( StringComparer.OrdinalIgnoreCase );
		var textureFiles = FileSystem.GetFiles( $"{ArchivePath}/textures" );
		try
		{
			foreach ( var name in BodyMeshes.Append( ClosedMouth ).Append( TalkingMouth ) )
			{
				var mesh = modelFile.Meshes.SingleOrDefault( candidate => candidate.Name == name )
					?? throw new InvalidDataException( $"The original advisor model has no '{name}' mesh." );
				var vertices = ConvertMesh( mesh, restTransforms[mesh.NodeIndex] );
				if ( name is ClosedMouth or TalkingMouth )
					foreach ( var vertex in vertices )
					{
						mouthMin = System.Numerics.Vector3.Min( mouthMin, vertex.Position.GetSystemVector3() );
						mouthMax = System.Numerics.Vector3.Max( mouthMax, vertex.Position.GetSystemVector3() );
					}
				var textureSlots = new Texture[16];
				Array.Fill( textureSlots, Texture.Missing );
				for ( var slot = 0; slot < mesh.Materials.Length; slot++ )
				{
					var textureName = mesh.Materials[slot].Name;
					var path = textureFiles.FirstOrDefault( entry => string.Equals( Path.GetFileNameWithoutExtension( entry ), textureName, StringComparison.OrdinalIgnoreCase ) )
						?? throw new FileNotFoundException( $"Original advisor texture was not found: {textureName}" );
					if ( !textures.TryGetValue( path, out var texture ) )
					{
						texture = new Texture( path, TextureFlags.Repeat );
						textures.Add( path, texture );
					}
					textureSlots[slot] = texture;
				}
				var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader" );
				material.Set( "Color", textureSlots );
				parts.Add( (name, new Model( vertices, ReverseWinding( mesh.Indices ), material ), material) );
			}
		}
		catch
		{
			Dispose();
			throw;
		}
	}

	/// <summary>
	/// Model-space transform of a node from the shared MD2 hierarchy code
	/// (<see cref="ModelAnimationPlayer.ComputeRestTransforms"/>: parent-relative, row-vector,
	/// world = local × parent world). With the root "Position Dummy" applied the model stands
	/// Y-up and faces −Z. The bind pose is used: every <c>Advisorm*.MD2</c> clip has tracks
	/// with undecoded (non-rigid) payload, so none is played.
	/// </summary>
	internal static Matrix4x4 NodeWorld( ModelFile model, int nodeIndex ) => ModelAnimationPlayer.ComputeRestTransforms( model )[nodeIndex];

	internal static Vertex[] ConvertMesh( ModelFile.Mesh mesh, Matrix4x4 world )
	{
		var vertices = PrototypeRide.ConvertMesh( mesh );
		for ( var index = 0; index < vertices.Length; index++ )
		{
			vertices[index].Position = System.Numerics.Vector3.Transform( mesh.Vertices[index].Position.GetSystemVector3(), world );
			vertices[index].Normal = System.Numerics.Vector3.Normalize( System.Numerics.Vector3.TransformNormal( mesh.Normals[index].GetSystemVector3(), world ) );
		}
		return vertices;
	}

	/// <summary>
	/// The renderer's pipeline treats clockwise triangles as front faces; other MD2 users
	/// (<see cref="PrototypeRide"/>) reach that by swapping Y/Z, a reflection. The advisor keeps
	/// its composed node space, so each triangle's corner order is reversed instead.
	/// </summary>
	internal static uint[] ReverseWinding( uint[] indices )
	{
		var reversed = (uint[])indices.Clone();
		for ( var index = 0; index + 2 < reversed.Length; index += 3 )
			(reversed[index + 1], reversed[index + 2]) = (reversed[index + 2], reversed[index + 1]);
		return reversed;
	}

	internal static string MouthFor( LipSyncTimeline? timeline, TimeSpan position ) =>
		timeline != null && timeline.IsTalking( position ) ? TalkingMouth : ClosedMouth;

	internal static string ClipName( int number )
	{
		if ( number < FirstClip || number > LastClip )
			throw new ArgumentOutOfRangeException( nameof( number ), $"Advisor speech clips are numbered {FirstClip}–{LastClip}." );
		return $"sp_{number:000}";
	}

	/// <summary>Loads and decodes global clip <c>sp_NNN</c> and its LIP timeline.</summary>
	internal static (Mp2Audio Audio, LipSyncTimeline Timeline) LoadClip( BaseFileSystem fileSystem, int number )
	{
		var name = ClipName( number );
		var entry = fileSystem.ReadAllBytes( $"{SpeechArchivePath}/{name}.mp2" );
		if ( entry.Length < 8 )
			throw new InvalidDataException( $"Speech entry {name} is truncated." );
		var headerBytes = BitConverter.ToInt32( entry, 0 );
		if ( headerBytes < 8 || headerBytes >= entry.Length )
			throw new InvalidDataException( $"Speech entry {name} has an invalid header size." );
		var audio = Mp2Decoder.Decode( entry.AsSpan( headerBytes ) );
		using var lipStream = fileSystem.OpenRead( $"{LipArchivePath}/{name}.LIP" );
		return (audio, new LipSyncTimeline( new LipSyncFile( lipStream ) ));
	}

	public void Say( int number )
	{
		var (audio, lips) = LoadClip( FileSystem, number );
		player?.Dispose();
		timeline = lips;
		ClipNumber = number;
		player = new SpeechAudioPlayer( audio );
		Log.Trace( $"Advisor says {ClipName( number )}: {audio.DurationSeconds:F2} s, {lips.Marks.Count} LIP marks, clock: {player.ClockSource}{(player.DeviceError == null ? "" : $" ({player.DeviceError})")}." );
	}

	public void Render()
	{
		if ( disposed )
			return;
		var (x, y, size) = ViewportRectangle();
		var commandList = global::Global.Render.CommandList;
		commandList.SetViewport( 0, new Viewport( x, y, size, size, 0, 1 ) );
		commandList.SetScissorRect( 0, (uint)x, (uint)y, (uint)size, (uint)size );
		commandList.ClearDepthStencil( 1 );

		player?.Start();
		var mouth = MouthMesh;
		foreach ( var part in parts )
		{
			if ( part.Name is ClosedMouth or TalkingMouth && part.Name != mouth )
				continue;
			part.Material.Set( "ObjectUniformBuffer", new ObjectUniformBuffer
			{
				g_mModel = Matrix4x4.Identity,
				g_mView = View,
				g_mProj = Projection,
				g_vLightPos = System.Numerics.Vector3.Zero,
				g_vLightColor = new System.Numerics.Vector3( 0.6f ),
				g_vCameraPos = CameraPosition,
				g_flTime = Time.Now,
			} );
			part.Model.Draw();
		}
		commandList.SetFullViewports();
		commandList.SetFullScissorRects();
		LastRenderedMouth = mouth;
	}

	/// <summary>Screen-pixel rectangle covering both mouth meshes in the advisor viewport.</summary>
	public (int Left, int Top, int Right, int Bottom) MouthScreenRectangle()
	{
		var (x, y, size) = ViewportRectangle();
		float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
		for ( var corner = 0; corner < 8; corner++ )
		{
			var point = new System.Numerics.Vector3( (corner & 1) == 0 ? mouthMin.X : mouthMax.X, (corner & 2) == 0 ? mouthMin.Y : mouthMax.Y, (corner & 4) == 0 ? mouthMin.Z : mouthMax.Z );
			var clip = System.Numerics.Vector4.Transform( new System.Numerics.Vector4( point, 1 ), View * Projection );
			var screenX = x + (clip.X / clip.W * 0.5f + 0.5f) * size;
			var screenY = y + (0.5f - clip.Y / clip.W * 0.5f) * size;
			left = MathF.Min( left, screenX );
			right = MathF.Max( right, screenX );
			top = MathF.Min( top, screenY );
			bottom = MathF.Max( bottom, screenY );
		}
		return ((int)MathF.Floor( left ), (int)MathF.Floor( top ), (int)MathF.Ceiling( right ), (int)MathF.Ceiling( bottom ));
	}

	public void Dispose()
	{
		if ( disposed )
			return;
		disposed = true;
		player?.Dispose();
		foreach ( var part in parts )
		{
			Asset.All.Remove( part.Model );
			var model = part.Model;
			global::Global.Render.ScheduleDelete( () =>
			{
				model.VertexBuffer.Dispose();
				model.IndexBuffer?.Dispose();
			} );
		}
		parts.Clear();
	}
}
