using System.Numerics;
using Veldrid;

namespace OpenTPW;

/// <summary>
/// The original advisor model (<c>global/advisor.wad/Advisor.MD2</c>) drawn in a corner
/// viewport, saying one global speech clip with its mouth driven by the clip's LIP marks
/// (see docs/LIPS.md). The model carries five co-located mouth meshes; the LIP data only
/// distinguishes talking from silence. As in the original, silence shows "Mouth - Normal" and
/// talking shows a mouth picked at random every 100 ms (<see cref="AdvisorMouth"/>). How it poses the
/// head/hands (the <c>Advisorm*</c> clips carry undecoded non-rigid tracks) and which hat it shows are not
/// known; hats, the spatula, the bow tie and the blink meshes are hidden. The model is drawn in
/// its bind pose (see <see cref="NodeWorld"/>).
/// </summary>
internal sealed class Advisor : IDisposable
{
	// [DATA:global/advisor.wad:Advisor.MD2]
	internal const string ArchivePath = "/global/advisor";
	// [DATA:global/Speech/speechHD.SDT] [DATA:global/Speech/lips.wad]
	internal const string SpeechArchivePath = "/global/Speech/speechHD";
	internal const string LipArchivePath = "/global/Speech/lips";
	// [DATA:Advisor.MD2:mesh names "Mouth - Normal", "Mouth - Aah", "Mouth - Eee", "Mouth - Ooh", "Mouth - Sss"]
	internal const string ClosedMouth = "Mouth - Normal";
	// [APPROX:ADVISOR-001] The original's mouth nodes 1–5 are these meshes in this order (node 1, shown while silent, is Normal) — evidence needed: the node-lookup jump table at 0x1019B3DC or the MD2 node ids
	internal static readonly string[] Mouths = { ClosedMouth, "Mouth - Aah", "Mouth - Eee", "Mouth - Ooh", "Mouth - Sss" };
	// [DATA:global/Speech/lips.wad:members sp_001–sp_637]
	internal const int FirstClip = 1;
	internal const int LastClip = 637;
	// [APPROX:ADVISOR-002] Visible set: body, head, eyes, antennae, hands; hats, spatula, bow tie and ShutEye meshes hidden — evidence needed: original node visibility rules (dummy attributes 0x401/0x411, Advisorm* tracks) or captures per advisor role
	internal static readonly string[] BodyMeshes = { "Body", "Bug Head", "Right Antennae", "Left Antennae", "Right hand", "Left Hand", "Right Eye", "Left Eye" };

	/// <summary>Every unproven advisor/lip-sync rule (docs/LIPS.md "Approximation register"); logged once when an advisor is created.</summary>
	internal static readonly (string Id, string Rule)[] Approximations =
	{
		("ADVISOR-001", "mouth nodes 1–5 are the Normal, Aah, Eee, Ooh and Sss meshes in that order"),
		("ADVISOR-002", "hats, spatula, bow tie and blink meshes hidden"),
		("ADVISOR-003", "bottom-left viewport, 1/3 of the short screen side, 16 px margin"),
		("ADVISOR-004", "overlay camera at z = -70, 40° FOV, near 1, far 500"),
		("ADVISOR-005", "headlight at the camera, light colour 0.6, shared test.shader ambient/fog"),
		("ADVISOR-006", "bind pose; no Advisorm* clip is played"),
		("ADVISOR-007", "triangle corner order reversed for the clockwise front-face pipeline"),
		("ADVISOR-008", "speech starts at the first rendered advisor frame"),
		("ADVISOR-009", "--advisor-say plays global clips by number; the controller picks responses only for the five bound messages of game events 0/2/3/4"),
		("ADVISOR-010", "lip-sync clock = PCM consumed from the SDL queue (leads output by up to one device buffer)"),
		("ADVISOR-011", "wall clock drives the mouth when no audio device opens"),
		("ADVISOR-012", "mono speech duplicated to both stereo channels"),
		("ADVISOR-013", "the mouth is talking from time 0 (the unit and per-mark toggle are traced)"),
		("ADVISOR-014", "MP2 synthesis window values read from ffmpeg's table, checked against two ISO values and ≤1 LSB corpus output"),
		("ADVISOR-015", "the advisor's tutorial byte +53 is the Game Options Tutorial switch (same offset; object identity unproven)"),
		("ADVISOR-016", "returned playback span = speech length + 200 + 300 + 1000 ms (sequence and ending-clip durations not decoded)"),
		("ADVISOR-017", "advisor controller clock = wall-clock ms since the automatic advisor started, updated once per frame; not pause-aware"),
		("ADVISOR-018", "the automatic advisor is drawn only while its speech plays, only inside a level; leaving the level stops it"),
		("ADVISOR-019", "GeneralAdvisor.MinTimeAnyMessage and GeneralAdvisor.MinTimeSameMessage are loaded but not applied"),
		("ADVISOR-020", "game events 2/3/4 come from the park economy's Bankrupt/ParkOpened/ParkClosed events, not proven equal to the original producers"),
		("ADVISOR-021", "Advisor option off: advice is still picked, consumed and recorded silently; the options byte +0x34 is the Game Options Advisor switch (same offset; object identity unproven)"),
		("ADVISOR-022", "the advisor's pending advice and message history are not saved or loaded with the park"),
	};

	private readonly List<(string Name, Model Model, Material Material)> parts = new();
	private readonly AdvisorMouth mouth = new( new Random() );
	private SpeechAudioPlayer? player;
	private LipSyncTimeline? timeline;
	private bool disposed;
	private System.Numerics.Vector3 mouthMin = new( float.MaxValue );
	private System.Numerics.Vector3 mouthMax = new( float.MinValue );
	// [APPROX:ADVISOR-004] Overlay camera at z = −70 looking at the origin, 40° FOV, near 1/far 500 — evidence needed: original advisor camera/projection (binary or capture)
	private static readonly System.Numerics.Vector3 CameraPosition = new( 0, 0, -70 );
	private static readonly Matrix4x4 View = Matrix4x4.CreateLookAt( CameraPosition, System.Numerics.Vector3.Zero, System.Numerics.Vector3.UnitY );
	private static readonly Matrix4x4 Projection = Matrix4x4.CreatePerspectiveFieldOfView( 40f * MathF.PI / 180f, 1f, 1f, 500f );

	public int? ClipNumber { get; private set; }
	public TimeSpan Position => player?.Position ?? TimeSpan.Zero;
	public bool IsSpeaking => player != null && !player.IsFinished;
	/// <summary>Length of the clip started last.</summary>
	public TimeSpan Duration { get; private set; }
	public string ClockSource => player?.ClockSource ?? "none";
	public string MouthMesh => mouth.Current;
	/// <summary>Mouth mesh used by the most recent <see cref="Render"/> call.</summary>
	public string? LastRenderedMouth { get; private set; }

	/// <summary>World-target pixel rectangle of the advisor viewport.</summary>
	public static (int X, int Y, int Size) ViewportRectangle() => ViewportRectangle(
		Screen.Size, new Point2( (int)global::Global.Render.MultisampledFramebuffer.Width, (int)global::Global.Render.MultisampledFramebuffer.Height ) );

	internal static (int X, int Y, int Size) ViewportRectangle( Point2 logicalSize, Point2 targetSize )
	{
		// [APPROX:ADVISOR-003] Bottom-left square viewport, 1/3 of the short screen side (min 64 logical px), 16 logical px margin — evidence needed: original advisor screen placement/size captures per resolution
		var scaleX = (float)targetSize.X / Math.Max( 1, logicalSize.X );
		var scaleY = (float)targetSize.Y / Math.Max( 1, logicalSize.Y );
		var logicalSide = Math.Max( 64, Math.Min( logicalSize.X, logicalSize.Y ) / 3 );
		var size = Math.Max( 1, (int)(logicalSide * Math.Min( scaleX, scaleY )) );
		var marginX = (int)(16 * scaleX);
		var marginY = (int)(16 * scaleY);
		return (marginX, Math.Max( 0, targetSize.Y - size - marginY ), size);
	}

	public Advisor()
	{
		foreach ( var (id, rule) in Approximations )
			Log.Warning( $"[APPROX:{id}] {rule}" );
		var modelFile = new ModelFile( $"{ArchivePath}/Advisor.MD2" );
		var restTransforms = ModelAnimationPlayer.ComputeRestTransforms( modelFile );
		var textures = new Dictionary<string, Texture>( StringComparer.OrdinalIgnoreCase );
		var textureFiles = FileSystem.GetFiles( $"{ArchivePath}/textures" );
		try
		{
			foreach ( var name in BodyMeshes.Concat( Mouths ) )
			{
				var mesh = modelFile.Meshes.SingleOrDefault( candidate => candidate.Name == name )
					?? throw new InvalidDataException( $"The original advisor model has no '{name}' mesh." );
				// [APPROX:ADVISOR-006] Bind pose only; every Advisorm*.MD2 clip has undecoded non-rigid tracks — evidence needed: decoded vertex/visibility track payloads
				var vertices = ConvertMesh( mesh, restTransforms[mesh.NodeIndex] );
				if ( Mouths.Contains( name ) )
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
				// [APPROX:ADVISOR-007] Corner order reversed so faces survive the renderer's clockwise back-face culling (chosen from a capture of this renderer, not the original) — evidence needed: original MD2 front-face convention
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

	internal static bool IsTalking( LipSyncTimeline? timeline, TimeSpan position ) =>
		timeline != null && timeline.IsTalking( position );

	internal static string ClipName( int number )
	{
		if ( number < FirstClip || number > LastClip )
			throw new ArgumentOutOfRangeException( nameof( number ), $"Advisor speech clips are numbered {FirstClip}–{LastClip}." );
		return $"sp_{number:000}";
	}

	/// <summary>
	/// Loads and decodes global clip <c>sp_NNN</c> and its LIP timeline. With a
	/// <paramref name="language"/>, <c>global/Speech/speechHD.SDT</c> and <c>lips.wad</c> are taken
	/// from that language (overlay first, case-insensitive); otherwise from the game file system.
	/// </summary>
	internal static (Mp2Audio Audio, LipSyncTimeline Timeline, string Source) LoadClip( BaseFileSystem fileSystem, int number, GameLanguage? language = null )
	{
		var name = ClipName( number );
		byte[] entry;
		byte[] lip;
		string source;
		// [APPROX:ADVISOR-009] --advisor-say plays global clips by number; responses (SayResponse) follow the traced global/level selector, and AdvisorController picks response IDs only for messages 0, 106, 128, 129 and 323 — evidence needed: the remaining 346 descriptors and their score producers
		if ( language != null )
		{
			var bankPath = language.ResolveDataFile( "global/Speech/speechHD.SDT" )
				?? throw new FileNotFoundException( $"No global speech bank for {language.Name}." );
			var lipsPath = language.ResolveDataFile( "global/Speech/lips.wad" )
				?? throw new FileNotFoundException( $"No global lips.wad for {language.Name}." );
			using var bank = new SdtArchive( bankPath );
			entry = bank.soundFiles.FirstOrDefault( file => string.Equals( Path.GetFileNameWithoutExtension( file.Name ), name, StringComparison.OrdinalIgnoreCase ) )?.GetData()
				?? throw new FileNotFoundException( $"Speech clip {name} is not in {bankPath}." );
			using var lips = new WadArchive( lipsPath );
			var lipName = lips.GetFiles( "" ).FirstOrDefault( file => string.Equals( file, $"{name}.LIP", StringComparison.OrdinalIgnoreCase ) )
				?? throw new FileNotFoundException( $"{name}.LIP is not in {lipsPath}." );
			lip = lips.GetFile( lipName ).GetData();
			source = $"{language.Name}: {bankPath}";
		}
		else
		{
			entry = fileSystem.ReadAllBytes( $"{SpeechArchivePath}/{name}.mp2" );
			lip = fileSystem.ReadAllBytes( $"{LipArchivePath}/{name}.LIP" );
			source = SpeechArchivePath;
		}
		if ( entry.Length < 8 )
			throw new InvalidDataException( $"Speech entry {name} is truncated." );
		// [DATA:speechHD.SDT:entry word 0 = header size (40)]
		var headerBytes = BitConverter.ToInt32( entry, 0 );
		if ( headerBytes < 8 || headerBytes >= entry.Length )
			throw new InvalidDataException( $"Speech entry {name} has an invalid header size." );
		var audio = Mp2Decoder.Decode( entry.AsSpan( headerBytes ) );
		return (audio, new LipSyncTimeline( new LipSyncFile( new MemoryStream( lip ) ) ), source);
	}

	/// <summary>
	/// Loads advisor response <paramref name="response"/>: its sample from the global speech bank, or for a
	/// local response from <paramref name="level"/>'s <c>Speech/speechHD.SDT</c>, with <c>sp_NNN.lip</c>
	/// from the matching <c>lips</c> (none for LIP 0).
	/// </summary>
	// [BIN:STP-PPC:0x10006B7C advisor playback] the response record's +16 high half selects the current level's speech folder and bank instead of Data:Global; +4 is the sample and +8 the sp_%03d.lip number
	internal static (Mp2Audio Audio, LipSyncTimeline Timeline, string Source) LoadResponse( BaseFileSystem fileSystem, AdvisorResponse response, string? level, GameLanguage? language = null )
	{
		if ( response.Local && level == null )
			throw new InvalidOperationException( $"Advisor response {response.Id} uses the level speech bank but no level is loaded." );
		var root = response.Local ? $"/levels/{level}/Speech" : "/global/Speech";
		byte[] bankBytes;
		string source;
		if ( language != null && !response.Local && language.ResolveDataFile( "global/Speech/speechHD.SDT" ) is { } languageBank )
		{
			bankBytes = File.ReadAllBytes( languageBank );
			source = $"{language.Name}: {languageBank}";
		}
		else
		{
			bankBytes = File.ReadAllBytes( fileSystem.GetAbsolutePath( $"{root}/speechHD.SDT" ) );
			source = $"{root}/speechHD.SDT";
		}
		var mpeg = new SoundBank( bankBytes ).GetMpeg( (uint)response.Sample )
			?? throw new FileNotFoundException( $"Speech sample {response.Sample} of response {response.Id} is not in {source}." );
		var marks = Array.Empty<uint>() as IReadOnlyList<uint>;
		if ( response.Lip > 0 )
		{
			var lipName = $"sp_{response.Lip:000}.LIP";
			byte[] lip;
			var wadPath = language != null && !response.Local ? language.ResolveDataFile( "global/Speech/lips.wad" ) : null;
			if ( wadPath == null && !fileSystem.FileExists( $"{root}/lips/{lipName}" ) && fileSystem.FileExists( $"{root}/lips.wad" ) )
				wadPath = fileSystem.GetAbsolutePath( $"{root}/lips.wad" );
			if ( wadPath is { } lipsPath )
			{
				using var lips = new WadArchive( lipsPath );
				var member = lips.GetFiles( "" ).FirstOrDefault( file => string.Equals( file, lipName, StringComparison.OrdinalIgnoreCase ) )
					?? throw new FileNotFoundException( $"{lipName} is not in {lipsPath}." );
				lip = lips.GetFile( member ).GetData();
			}
			else
				lip = fileSystem.ReadAllBytes( $"{root}/lips/{lipName}" );
			marks = new LipSyncFile( new MemoryStream( lip ) ).Marks;
		}
		return (Mp2Decoder.Decode( mpeg ), new LipSyncTimeline( marks ), source);
	}

	/// <summary>Plays advisor response <paramref name="responseId"/> from the response table; false when the table has no such response.</summary>
	public bool SayResponse( int responseId, string? level, GameLanguage? language = null )
	{
		if ( !AdvisorResponses.Table.TryGetValue( responseId, out var response ) )
			return false;
		var (audio, lips, source) = LoadResponse( FileSystem, response, level, language );
		Play( audio, lips, response.Lip, $"response {responseId} (sample {response.Sample}, {source})" );
		return true;
	}

	private void Play( Mp2Audio audio, LipSyncTimeline lips, int clipNumber, string description )
	{
		player?.Dispose();
		timeline = lips;
		ClipNumber = clipNumber;
		Duration = TimeSpan.FromSeconds( audio.DurationSeconds );
		player = CreatePlayer( audio );
		Log.Trace( $"Advisor says {description}: {audio.DurationSeconds:F2} s, {lips.Marks.Count} LIP marks, clock: {player.ClockSource}{(player.DeviceError == null ? "" : $" ({player.DeviceError})")}." );
	}

	/// <summary>
	/// Speech goes through the game mixer when sound is on. Without a mixer it opens its own SDL device,
	/// except when sound is off (<c>--mute</c>): then it keeps the wall clock and stays silent.
	/// </summary>
	internal static SpeechAudioPlayer CreatePlayer( Mp2Audio audio ) =>
		GameAudio.EnsureStarted() && AudioMixer.Current is { } mixer
			? new SpeechAudioPlayer( audio, mixer )
			: new SpeechAudioPlayer( audio, null, openDevice: GameAudio.Enabled && AudioMixer.Current == null );

	/// <summary>Stops the current speech, if any.</summary>
	public void Silence()
	{
		player?.Dispose();
		player = null;
		timeline = null;
	}

	public void Say( int number, GameLanguage? language = null )
	{
		var (audio, lips, source) = LoadClip( FileSystem, number, language );
		Play( audio, lips, number, $"{ClipName( number )} ({source})" );
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

		// [APPROX:ADVISOR-008] Speech starts at the first rendered advisor frame — evidence needed: original advisor speech trigger timing
		player?.Start();
		var shownMouth = mouth.Update( IsTalking( timeline, Position ), (long)Position.TotalMilliseconds );
		foreach ( var part in parts )
		{
			if ( Mouths.Contains( part.Name ) && part.Name != shownMouth )
				continue;
			part.Material.Set( "ObjectUniformBuffer", new ObjectUniformBuffer
			{
				g_mModel = Matrix4x4.Identity,
				g_mView = View,
				g_mProj = Projection,
				// [APPROX:ADVISOR-005] Headlight at the camera, colour 0.6, test.shader ambient 0.4 and fog — evidence needed: original advisor lighting/material captures
				g_vLightPos = System.Numerics.Vector3.Zero,
				g_vLightColor = new System.Numerics.Vector3( 0.6f ),
				g_vCameraPos = CameraPosition,
				g_flTime = Time.Now,
			} );
			part.Model.Draw();
		}
		commandList.SetFullViewports();
		commandList.SetFullScissorRects();
		LastRenderedMouth = shownMouth;
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

/// <summary>
/// The original advisor's mouth choice. While the LIP data says the advisor is silent the first
/// mouth node is shown; while talking, a new mouth is picked at random among the five whenever
/// more than <see cref="ChangeIntervalMilliseconds"/> of speech time have passed since the last pick.
/// The pick timer is not reset by silence, so a talking stretch may start with the previous mouth.
/// </summary>
internal sealed class AdvisorMouth
{
	// [BIN:STP-PPC:0x10007434 advisor update] silent or no LIP data: node 1; talking: when the speech clock (ms) passes the next-change time, node = rand() % 5 + 1 and the next change is 100 ms later
	public const int ChangeIntervalMilliseconds = 100;
	private readonly Random random;
	private long nextChange;
	private int current;

	public AdvisorMouth( Random random ) => this.random = random ?? throw new ArgumentNullException( nameof( random ) );

	public string Current => Advisor.Mouths[current];

	public string Update( bool talking, long speechMilliseconds )
	{
		if ( !talking )
			current = 0;
		else if ( nextChange < speechMilliseconds )
		{
			current = random.Next( Advisor.Mouths.Length );
			nextChange = speechMilliseconds + ChangeIntervalMilliseconds;
		}
		return Current;
	}
}
