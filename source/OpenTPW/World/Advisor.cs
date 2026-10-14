using System.Numerics;
using Veldrid;

namespace OpenTPW;

/// <summary>
/// The original advisor model (<c>global/advisor.wad/Advisor.MD2</c>) drawn flat over the
/// bottom-right of the screen without a camera (see <see cref="ModelTransform"/>), saying one global speech clip with its mouth driven by the clip's LIP marks
/// (see docs/LIPS.md). The model carries five co-located mouth meshes; the LIP data only
/// distinguishes talking from silence. As in the original, silence shows "Mouth - Normal" and
/// talking shows a mouth picked at random every 100 ms (<see cref="AdvisorMouth"/>). The original poses
/// the advisor with <c>Advisor&lt;level&gt;m&lt;n&gt;</c> clips (<see cref="AnimationClipName"/>), whose
/// records all decode. The sequence of clips (generated, or a fixed row of the sequence table) is built
/// (<see cref="AdvisorSequences"/>) and sets the response's span, when the model hides and the audio's delayed start
/// (<see cref="AdvisorSpeechStart"/>), but the clips are not drawn:
/// which hat a clip shows is not known, so hats, the spatula, the bow tie and the blink meshes are hidden and the
/// model is drawn in its bind pose (see <see cref="NodeWorld"/>).
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
	// [BIN:STP-PPC:0x1019B394 node lookup] mouth nodes 1–5 (stored in order by 0x10006264) are found by case-insensitive name "mouth - normal", "aah", "eee", "ooh", "sss" through the jump table at data 0x518B4; node 1 is shown while silent
	internal static readonly string[] Mouths = { ClosedMouth, "Mouth - Aah", "Mouth - Eee", "Mouth - Ooh", "Mouth - Sss" };
	// [DATA:global/Speech/lips.wad:members sp_001–sp_637]
	internal const int FirstClip = 1;
	internal const int LastClip = 637;
	// [APPROX:ADVISOR-002] Visible set: body, head, eyes, antennae, hands; hats, spatula, bow tie and ShutEye meshes hidden — evidence needed: original node visibility rules (dummy attributes 0x401/0x411, Advisorm* tracks) or captures per advisor role
	internal static readonly string[] BodyMeshes = { "Body", "Bug Head", "Right Antennae", "Left Antennae", "Right hand", "Left Hand", "Right Eye", "Left Eye" };

	/// <summary>Every unproven advisor/lip-sync rule (docs/LIPS.md "Approximation register"); logged once when an advisor is created.</summary>
	internal static readonly (string Id, string Rule)[] Approximations =
	{
		("ADVISOR-002", "hats, spatula, bow tie and blink meshes hidden"),
		("ADVISOR-005", "light colour 0.6, shared test.shader ambient/fog"),
		("ADVISOR-006", "bind pose: the generated (0x598B20) and fixed (0x598BF0, table 0x76DC18) sequences are built, but no clip is drawn; the draw-path bit 0x10 at 0x57A797 is a render-node deferral marker, not the clip-toggled mesh-node bit (mesh record +0, toggled at 0x472E75/0x472401), whose reader is open (RIDES-042)"),
		("ADVISOR-007", "drawn double-sided; no back-face test in 0x57A750, 0x57AA10 or 0x574110 through 0x574660; the backend submit 0x581CD0 -> [0x8BCBB8] vtable +8 is unread"),
		("ADVISOR-009", "--advisor-say plays global clips by number and starts them at its first rendered frame; the controller picks responses only for the five bound messages of game events 0/2/3/4; 135 computed score producers (descriptor table 0x76E300) remain"),
		("ADVISOR-010", "lip-sync clock = PCM consumed from the SDL queue (leads output by up to one device buffer)"),
		("ADVISOR-012", "mono speech duplicated to both channels; the original submits it unpositioned (0x51BFC0 with 0,0,0) and the library places flat sounds at (0,1,0) (0x6BC51D); the left/right levels come from QMixer, outside tp.exe (AUDIO-018)"),
		("ADVISOR-017", "the advisor clock starts at 0 for a new park and an original-save import; the original's load sets its +0x50 offset to the saved value (0x402E80 -> 0x403160), and it stands still whenever 0x4092A0 pauses the timer (eight callers, screens unidentified)"),
		("ADVISOR-020", "event 2 comes from the economy's six-month bankruptcy, not 0x4D0370's monthly guard (balance and previous balance < 0, and the red-time term from 0x4F88B0 over 2.592e13 >= 6, with the turn entered red at +0x120); events 3/4 are raised on every open/close button press as in the original"),
		("ADVISOR-022", "an original save's pending advice and history of messages without a bound descriptor (only 0, 106, 128, 129 and 323 are bound) are dropped on import; OpenTPW's own park save stores the controller"),
		("ADVISOR-023", "the advisor's unprojected coordinates span the screen from -1 to 1"),
		("ADVISOR-025", "the effect a fixed sequence's timer entry fires when the advisor clock reaches it (sequences 28 and 29: the particle effect 0x521E60 (87, 90000, 0, 7000), sound 0xC6 and 0x4B9340 in the update 0x599880) is not ported; the timer is recorded on the sequence only"),
	};

	private readonly List<(string Name, Model Model, Material Material)> parts = new();
	private readonly AdvisorMouth mouth = new( new Random() );
	private SpeechAudioPlayer? player;
	private LipSyncTimeline? timeline;
	private bool disposed;
	// The advisor clock (milliseconds that stand still while the game is paused) of the automatic advisor; null for the manual
	// --advisor-say / --advisor-response tools, which start their speech at the first rendered frame.
	private readonly Func<long>? clock;
	private AdvisorSpeechStart? speechStart;
	// The generated sequences draw from their own stream (see AdvisorRandom).
	private readonly AdvisorRandom sequenceRandom = new();
	private AdvisorClipDurations? clipDurations;
	private string? clipDurationsLevel;
	private bool clipDurationsLoaded;
	private System.Numerics.Vector3 mouthMin = new( float.MaxValue );
	private System.Numerics.Vector3 mouthMax = new( float.MinValue );
	// [BIN:TPW-EXE:0x00429BA0 advisor setup] model matrix = identity with translation (0.6, -0.6, 0.2); the root node is
	// scaled by (0.015 * 0.75, 0.001, 0.015), i.e. width, depth and height of the model as composed here (Y up).
	// [BIN:TPW-EXE:0x0057A260 SetLocalMatrix] multiplies by diag(1, -1, 1, 1), so the stored translation is (0.6, +0.6, 0.2).
	// [BIN:TPW-EXE:0x0057A750 mesh draw] the advisor's model flag 0x200 (set by 0x0045BED0) skips the camera, projection
	// and screen matrix: its vertices reach the transform stage as (root scale × model matrix) only, in the screen space
	// where the park's camera matrix ends with diag(2k, -2, 1, 1) (0x005783B0), x right and y down. Here that is clip
	// space with y up, so the centre is (0.6, -0.6) and the model stays upright.
	internal static readonly System.Numerics.Vector2 Centre = new( 0.6f, -0.6f );
	internal const float Depth = 0.2f;
	internal const float HeightScale = 0.015f;
	internal const float DepthScale = 0.001f;
	// The 0.75 in the width scale is the 4:3 screen's height/width, keeping the model's pixels square.
	internal const float OriginalAspect = 0.75f;
	// Half the side of the clip-space box the advisor is drawn in; it holds the whole model (about ±0.34 tall).
	internal const float BoxHalfSide = 0.4f;
	// [BIN:TPW-EXE:0x00429BA0 advisor setup] the advisor's parallel light, (26.67, -26.67, 800) in the same screen space:
	// it shines into the screen, slightly right and up. The shader lights from a point, so the point is placed far back
	// along that direction (y flipped for clip space).
	private static readonly System.Numerics.Vector3 LightPosition = new System.Numerics.Vector3( -26.666668f, -26.666668f, -800f ) * 1000f;

	public int? ClipNumber { get; private set; }
	public TimeSpan Position => player?.Position ?? TimeSpan.Zero;
	public bool IsSpeaking => player != null && !player.IsFinished;
	/// <summary>Length of the clip started last.</summary>
	public TimeSpan Duration { get; private set; }
	public string ClockSource => player?.ClockSource ?? "none";
	public string MouthMesh => mouth.Current;
	/// <summary>Mouth mesh used by the most recent <see cref="Render"/> call.</summary>
	public string? LastRenderedMouth { get; private set; }
	/// <summary>The sequence of the last response (generated or a fixed row; null without clip archives or for the manual tools).</summary>
	internal AdvisorSequence? Sequence { get; private set; }
	/// <summary>The span the last response returns to the controller (see <see cref="AdvisorSequences.ResponseSpan"/>).</summary>
	public uint ResponseSpanMilliseconds { get; private set; }

	/// <summary>
	/// How long after the request the model stays shown: the end of the last clip, which is the sequence sum (the response's
	/// span adds 1000 and clip 15 on top of it for the controller). Without a sequence (no clip archives) the span itself.
	/// </summary>
	// [BIN:TPW-EXE:0x004735D0 animation sampler] at the end of a clip (frame past its length, 0x473743–0x47374E) with nothing queued ([channel+0x24] == 12, 0x47375D) and model flag 8 (0x473769–0x473776) the sampler starts the idle set 12 (0x472F60) and hides a shown model with 0x45BDD0 (scene unlink 0x576540, then clears bit 0x19); the advisor model has flag 8 (load flags 0x50120 at 0x429C9D: 0x461F10 ORs 8 at 0x4621C9, 0x4557C0 copies it into the model)
	// [BIN:TPW-EXE:0x00599880 advisor update] the next clip of the sequence starts only while the channel reads the idle set 12 (0x599A23) through 0x4732A0, which shows the model again (0x4732C6–0x4732DD); after the last clip nothing starts, so the model stays hidden until the next response
	public uint DrawnSpanMilliseconds => Sequence != null ? (uint)Sequence.SumMilliseconds : ResponseSpanMilliseconds;

	/// <summary>
	/// The model is shown: set when a response starts (every sequence starts an animation other than 12), cleared when the
	/// last clip of its sequence ends (<see cref="DrawnSpanMilliseconds"/>) and by <see cref="Kill"/>.
	/// </summary>
	// [BIN:TPW-EXE:0x004732A0 animation start] the model is shown when any animation other than 12 starts (via 0x45BD30); "Kill advisor" (0x429D60: animation 12 with the immediate flag, then unlink), called by the stop path 0x5996D0 (six callers, including level exit) and the slap path 0x5994E0, hides it at once; the normal end of advice (0x599880) calls neither, the sampler hides the model at the end of the last clip (see DrawnSpanMilliseconds). The model therefore hides at the clip sum even while a long speech of a fixed row (21-27) is still playing; that follows the trace, and a capture should confirm it
	public bool ModelShown { get; private set; }

	/// <summary>The model is drawn: shown, and (automatic advisor) before the last clip ends or (manual tools) while the speech plays.</summary>
	public bool IsDrawn => !disposed && ModelShown && (clock == null || speechStart == null ? IsSpeaking : speechStart.Value.Within( clock(), DrawnSpanMilliseconds ));

	/// <summary>
	/// Pixel rectangle (y down) of a <paramref name="target"/> covered by the clip-space box the advisor is drawn in:
	/// <see cref="Centre"/> ± <see cref="BoxHalfSide"/>, the bottom-right of the screen.
	/// </summary>
	internal static (int X, int Y, int Width, int Height) BoxRectangle( Point2 target )
	{
		// [APPROX:ADVISOR-023] the advisor's unprojected coordinates span the screen from −1 to 1 (as clip space) — evidence needed: the consumer of the 0x0057A750 transform output in tp.exe, or a capture of the original advisor
		int Pixel( float clip, int size ) => (int)MathF.Round( (clip + 1f) * 0.5f * size );
		var left = Pixel( Centre.X - BoxHalfSide, target.X );
		var right = Math.Min( target.X, Pixel( Centre.X + BoxHalfSide, target.X ) );
		var top = Pixel( -(Centre.Y + BoxHalfSide), target.Y );
		var bottom = Math.Min( target.Y, Pixel( -(Centre.Y - BoxHalfSide), target.Y ) );
		return (left, top, Math.Max( 1, right - left ), Math.Max( 1, bottom - top ));
	}

	/// <summary>
	/// Composed model space (Y up, facing −Z) to the clip space of the <see cref="BoxRectangle"/> viewport, for an output of
	/// <paramref name="target"/> pixels: the original's root scale and translation, divided by the box size.
	/// </summary>
	internal static Matrix4x4 ModelTransform( Point2 target )
	{
		// [EXT:advisor-aspect] outside 4:3 the width scale is 0.015 × height/width of the output instead of the original's fixed 0.75, so the advisor keeps its proportions (the original runs at 4:3 only)
		var aspect = target.X > 0 && target.Y > 0 ? (float)target.Y / target.X : OriginalAspect;
		return Matrix4x4.CreateScale( HeightScale * aspect / BoxHalfSide, HeightScale / BoxHalfSide, DepthScale ) * Matrix4x4.CreateTranslation( 0, 0, Depth );
	}

	private static Point2 WorldTargetSize => new( (int)global::Global.Render.MultisampledFramebuffer.Width, (int)global::Global.Render.MultisampledFramebuffer.Height );

	/// <param name="clock">The advisor clock in milliseconds (the automatic advisor's pausable clock); null for the manual tools.</param>
	public Advisor( Func<long>? clock = null )
	{
		this.clock = clock;
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
				// [APPROX:ADVISOR-006] Bind pose only: every record of the Advisor<level>m<n> clips decodes (TRS, vertex groups, UV tracks, toggles; one level clip has path parameters) and the generated (0x598B20) and fixed (0x598BF0, table 0x76DC18) sequences are built (AdvisorSequences) and set the span, when the model hides and the audio start, but no clip is drawn — evidence needed: a skinned draw of the clips and a reader of the toggled mesh-node bit (mesh record +0, toggled at 0x472E75/0x472401; the draw-path bit 0x10 at 0x57A797 is a render-node deferral marker, RIDES-042)
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
				// [BIN:TPW-EXE:0x00566957 device init] the only Direct3D CULLMODE write is D3DCULL_NONE; no other constant-state SetRenderState sets it and the register-driven setter 0x567A10 only toggles ALPHABLENDENABLE, so the original draws both faces
				var material = new Material<ObjectUniformBuffer>( "content/shaders/test.shader", MaterialFlags.DoubleSided );
				material.Set( "Color", textureSlots );
				// [APPROX:ADVISOR-007] Drawn double-sided as the original's Direct3D device does (no hardware culling); the mesh path has no back-face test (0x57A750 defer/skip/matrix select, 0x57AA10 per-vertex setup, 0x574110 through 0x574660 light vectors and colours), but the final submit 0x581CD0 -> [0x8BCBB8] vtable +8 is unread — evidence needed: that backend or a capture
				parts.Add( (name, new Model( vertices, mesh.Indices, material ), material) );
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
	/// Y-up and faces −Z. The bind pose is used: the clips decode, but which one a sequence plays is
	/// not drawn (ADVISOR-006).
	/// </summary>
	internal static Matrix4x4 NodeWorld( ModelFile model, int nodeIndex ) => ModelAnimationPlayer.ComputeRestTransforms( model )[nodeIndex];

	/// <summary>
	/// Member an advisor animation id plays: the start helper triggers AnimID 5 (suffix <c>m</c>) with sub
	/// <c>id − 1</c>, flags 0x10, channel 0 and speed 1.0, and the loader names member sub + 1, so id n is
	/// <c>Advisor&lt;level&gt;m&lt;n&gt;.MD2</c> (level suffix such as <c>_FAN</c>, empty for the global advisor).
	/// </summary>
	// [BIN:STP-PPC:0x10029414 advisor clip start] sub = id - 1 (0x10029414), AnimID 5 (0x10029418), flags 0x10, channel 0, trigger 0x100A6CC0 (0x10029424); the presentation update continues a sequence only while the deferred slot reads 12 (0x10007634)
	internal static string AnimationClipName( string levelSuffix, int animationId )
	{
		ArgumentNullException.ThrowIfNull( levelSuffix );
		if ( animationId < 1 )
			throw new ArgumentOutOfRangeException( nameof( animationId ), "Advisor animation ids start at 1." );
		return $"Advisor{levelSuffix}m{animationId}.MD2";
	}

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
		// The chosen language's own bank and lips (overlay first, else the installation), for the level's speech as well as the global one:
		// the Welcome line (response 1) comes from the level bank, which every language of the CD ships (docs/LANGUAGES.md).
		var languageRoot = language == null ? null : root.TrimStart( '/' );
		byte[] bankBytes;
		string source;
		if ( languageRoot != null && language!.ResolveDataFile( $"{languageRoot}/speechHD.SDT" ) is { } languageBank )
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
			var wadPath = languageRoot != null ? language!.ResolveDataFile( $"{languageRoot}/lips.wad" ) : null;
			var loosePath = languageRoot != null ? language!.ResolveDataFile( $"{languageRoot}/lips/{lipName}" ) : null;
			if ( wadPath == null && loosePath == null && !fileSystem.FileExists( $"{root}/lips/{lipName}" ) && fileSystem.FileExists( $"{root}/lips.wad" ) )
				wadPath = fileSystem.GetAbsolutePath( $"{root}/lips.wad" );
			if ( loosePath == null && wadPath is { } lipsPath )
			{
				using var lips = new WadArchive( lipsPath );
				var member = lips.GetFiles( "" ).FirstOrDefault( file => string.Equals( file, lipName, StringComparison.OrdinalIgnoreCase ) )
					?? throw new FileNotFoundException( $"{lipName} is not in {lipsPath}." );
				lip = lips.GetFile( member ).GetData();
			}
			else
				lip = loosePath != null ? File.ReadAllBytes( loosePath ) : fileSystem.ReadAllBytes( $"{root}/lips/{lipName}" );
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
		Play( audio, lips, response.Lip, $"response {responseId} (sample {response.Sample}, {source})", response, level );
		return true;
	}

	private AdvisorClipDurations? ClipDurations( string? level )
	{
		if ( !clipDurationsLoaded || clipDurationsLevel != level )
		{
			clipDurations = AdvisorClipDurations.Load( FileSystem, level );
			clipDurationsLevel = level;
			clipDurationsLoaded = true;
		}
		return clipDurations;
	}

	private void Play( Mp2Audio audio, LipSyncTimeline lips, int clipNumber, string description, AdvisorResponse? response = null, string? level = null )
	{
		player?.Dispose();
		timeline = lips;
		ClipNumber = clipNumber;
		Duration = TimeSpan.FromSeconds( audio.DurationSeconds );
		player = CreatePlayer( audio );
		speechStart = null;
		Sequence = null;
		ModelShown = true;
		if ( response != null && clock != null )
		{
			var now = clock();
			speechStart = AdvisorSpeechStart.For( now, response.Animation );
			// [BIN:TPW-EXE:0x00599050 response player] the builder gets the sample length + 200 + 300 ms; a fixed sequence (response +12 minus 16) comes from table 0x76DC18
			var length = (int)Duration.TotalMilliseconds;
			var durations = ClipDurations( level );
			if ( durations != null )
			{
				Sequence = AdvisorSequences.Build( response.Animation, length + AdvisorSequences.BudgetSlackMilliseconds, durations, sequenceRandom.Next );
				ResponseSpanMilliseconds = AdvisorSequences.ResponseSpan( Sequence, durations );
			}
			else
			{
				// [BIN:TPW-EXE:0x005989C0 advisor clip lengths] a game without clip archives has every clip length 0, so the original's response returns 1000 and its model never plays a clip
				// [EXT:advisor-no-clips] an install without advisor clip archives (a partial data copy) keeps the speech's length + 200 + 300 + 1000 as the span and draws the model for it, so the advisor stays visible while it talks
				ResponseSpanMilliseconds = checked((uint)(length + AdvisorSequences.BudgetSlackMilliseconds + 1000));
			}
			Update();
		}
		Log.Trace( $"Advisor says {description}: {audio.DurationSeconds:F2} s, {lips.Marks.Count} LIP marks, clock: {player.ClockSource}{(player.DeviceError == null ? "" : $" ({player.DeviceError})")}{(Sequence == null ? "" : $", sequence {string.Join( ",", Sequence.Clips )} ({Sequence.SumMilliseconds} ms)")}{(speechStart is { } start ? $", audio starts at +{start.AudioStartMilliseconds - start.RequestMilliseconds} ms" : "")}." );
	}

	/// <summary>
	/// Starts the audio of the last response once the advisor clock reaches its start (the request itself, or 800 ms later for
	/// a sequence that opens with clip 14); call once per frame. The manual tools start at their first rendered frame instead.
	/// </summary>
	public void Update()
	{
		if ( disposed || clock == null || speechStart is not { } start )
			return;
		// The last clip's end hides the model (see DrawnSpanMilliseconds); the next response shows it again.
		if ( ModelShown && !start.Within( clock(), DrawnSpanMilliseconds ) )
			ModelShown = false;
		if ( player == null || player.IsStarted )
			return;
		if ( start.AudioDue( clock() ) )
			player.Start();
	}

	/// <summary>Where the mouth's LIP clock stands: the advisor clock minus the LIP base (the original's mouth never follows the audio); the audio position for the manual tools; silent before the audio starts.</summary>
	private TimeSpan LipPosition => player is not { IsStarted: true } ? TimeSpan.Zero
		: clock != null && speechStart is { } start ? TimeSpan.FromMilliseconds( start.LipMilliseconds( clock() ) )
		: Position;

	/// <summary>
	/// Speech goes through the game mixer when sound is on. Without a mixer it opens its own SDL device,
	/// except when sound is off (<c>--mute</c>): then it keeps the wall clock and stays silent.
	/// </summary>
	internal static SpeechAudioPlayer CreatePlayer( Mp2Audio audio ) =>
		GameAudio.EnsureStarted() && AudioMixer.Current is { } mixer
			? new SpeechAudioPlayer( audio, mixer )
			: new SpeechAudioPlayer( audio, null, openDevice: GameAudio.Enabled && AudioMixer.Current == null );

	/// <summary>Stops the current speech, if any; the model stays shown (<see cref="ModelShown"/>).</summary>
	public void Silence()
	{
		player?.Dispose();
		player = null;
		timeline = null;
		speechStart = null;
	}

	/// <summary>"Kill advisor": stops the speech and hides the model.</summary>
	public void Kill()
	{
		Silence();
		ModelShown = false;
	}

	public void Say( int number, GameLanguage? language = null )
	{
		var (audio, lips, source) = LoadClip( FileSystem, number, language );
		Play( audio, lips, number, $"{ClipName( number )} ({source})" );
	}

	/// <summary>Draws the advisor into the world target at its <see cref="BoxRectangle"/> (no HUD; <c>--advisor-say</c>).</summary>
	public void Render()
	{
		if ( disposed )
			return;
		var target = WorldTargetSize;
		var (x, y, width, height) = BoxRectangle( target );
		var commandList = global::Global.Render.CommandList;
		commandList.SetViewport( 0, new Viewport( x, y, width, height, 0, 1 ) );
		commandList.SetScissorRect( 0, (uint)x, (uint)y, (uint)width, (uint)height );
		commandList.ClearDepthStencil( 1 );
		DrawParts( ModelTransform( target ) );
		commandList.SetFullViewports();
		commandList.SetFullScissorRects();
	}

	/// <summary>The advisor drawn by the last <see cref="RenderImage"/>, owned by this advisor.</summary>
	public Veldrid.Texture? Image { get; private set; }
	private Framebuffer? imageTarget;
	private Veldrid.Texture? imageColor;
	private Veldrid.Texture? imageDepth;

	/// <summary>
	/// Draws the advisor into an image the size of its <see cref="BoxRectangle"/> on an output of <paramref name="output"/>
	/// pixels, for the park UI to draw after the HUD, as the original draws the advisor over everything without depth
	/// against the park. Call it in the world pass; it restores the world target afterwards.
	/// </summary>
	public Veldrid.Texture? RenderImage( Point2 output )
	{
		if ( disposed )
			return null;
		var world = global::Global.Render.MultisampledFramebuffer;
		var (_, _, width, height) = BoxRectangle( output );
		EnsureImageTarget( width, height, world );
		var commandList = global::Global.Render.CommandList;
		commandList.SetFramebuffer( imageTarget! );
		commandList.SetFullViewports();
		commandList.SetFullScissorRects();
		commandList.ClearColorTarget( 0, RgbaFloat.Clear );
		commandList.ClearDepthStencil( 1 );
		DrawParts( ModelTransform( output ) );
		commandList.ResolveTexture( imageColor!, Image! );
		commandList.SetFramebuffer( world );
		commandList.SetFullViewports();
		commandList.SetFullScissorRects();
		return Image;
	}

	/// <summary>Same formats and multisampling as the world target, so the advisor's pipelines draw into it unchanged.</summary>
	private void EnsureImageTarget( int width, int height, Framebuffer world )
	{
		if ( Image != null && Image.Width == (uint)width && Image.Height == (uint)height )
			return;
		DisposeImageTarget();
		var color = world.ColorTargets[0].Target;
		var depth = world.DepthTarget!.Value.Target;
		var factory = global::Global.Device.ResourceFactory;
		imageColor = factory.CreateTexture( TextureDescription.Texture2D( (uint)width, (uint)height, 1, 1, color.Format, TextureUsage.RenderTarget, color.SampleCount ) );
		imageDepth = factory.CreateTexture( TextureDescription.Texture2D( (uint)width, (uint)height, 1, 1, depth.Format, TextureUsage.DepthStencil, depth.SampleCount ) );
		Image = factory.CreateTexture( TextureDescription.Texture2D( (uint)width, (uint)height, 1, 1, color.Format, TextureUsage.Sampled ) );
		imageTarget = factory.CreateFramebuffer( new FramebufferDescription( imageDepth, imageColor ) );
	}

	private void DisposeImageTarget()
	{
		var target = imageTarget;
		var color = imageColor;
		var depth = imageDepth;
		var image = Image;
		imageTarget = null;
		imageColor = imageDepth = Image = null;
		if ( image == null )
			return;
		global::Global.Render.ScheduleDelete( () =>
		{
			target?.Dispose();
			color?.Dispose();
			depth?.Dispose();
			image.Dispose();
		} );
	}

	private void DrawParts( Matrix4x4 model )
	{
		// The manual tools (no advisor clock) start their speech at the first rendered frame; the automatic advisor's starts from Update.
		if ( clock == null )
			player?.Start();
		// [BIN:TPW-EXE:0x00599880 advisor update] the talking flag is set at LIP load (0x59930B), cleared on the delayed-start path (0x59943E) and set again when the speech starts (0x599987); then each mark flips it once the clock is strictly past it
		var lipPosition = LipPosition;
		var shownMouth = mouth.Update( player is { IsStarted: true } && IsTalking( timeline, lipPosition ),(long)lipPosition.TotalMilliseconds );
		foreach ( var part in parts )
		{
			if ( Mouths.Contains( part.Name ) && part.Name != shownMouth )
				continue;
			part.Material.Set( "ObjectUniformBuffer", new ObjectUniformBuffer
			{
				g_mModel = model,
				g_mView = Matrix4x4.Identity,
				g_mProj = Matrix4x4.Identity,
				// [APPROX:ADVISOR-005] Light colour 0.6, test.shader ambient 0.4 and fog — evidence needed: original advisor light colour/material captures
				g_vLightPos = LightPosition,
				g_vLightColor = new System.Numerics.Vector3( 0.6f ),
				g_vCameraPos = System.Numerics.Vector3.Zero,
				g_flTime = Time.Now,
			} );
			part.Model.Draw();
		}
		LastRenderedMouth = shownMouth;
	}

	/// <summary>World-target pixel rectangle covering the mouth meshes as <see cref="Render"/> draws them.</summary>
	public (int Left, int Top, int Right, int Bottom) MouthScreenRectangle()
	{
		var target = WorldTargetSize;
		var (x, y, width, height) = BoxRectangle( target );
		var transform = ModelTransform( target );
		float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
		for ( var corner = 0; corner < 8; corner++ )
		{
			var point = new System.Numerics.Vector3( (corner & 1) == 0 ? mouthMin.X : mouthMax.X, (corner & 2) == 0 ? mouthMin.Y : mouthMax.Y, (corner & 4) == 0 ? mouthMin.Z : mouthMax.Z );
			var clip = System.Numerics.Vector4.Transform( new System.Numerics.Vector4( point, 1 ), transform );
			var screenX = x + (clip.X / clip.W * 0.5f + 0.5f) * width;
			var screenY = y + (0.5f - clip.Y / clip.W * 0.5f) * height;
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
		DisposeImageTarget();
		foreach ( var part in parts )
		{
			part.Model.ReleaseBuffers();
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
