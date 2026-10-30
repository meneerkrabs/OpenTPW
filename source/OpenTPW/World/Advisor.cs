using System.Numerics;
using Veldrid;

namespace OpenTPW;

/// <summary>
/// The original advisor model (<c>global/advisor.wad/Advisor.MD2</c>, or the level's <c>Advisor_&lt;XXX&gt;.MD2</c> for the responses
/// that name it) drawn flat over the bottom-right of the screen without a camera (see <see cref="ModelTransform"/>), saying one
/// speech clip with its mouth driven by the clip's LIP marks (see docs/LIPS.md). The model carries five co-located mouth meshes;
/// the LIP data only distinguishes talking from silence. As in the original, silence shows "Mouth - Normal" and talking shows a
/// mouth picked at random every 100 ms (<see cref="AdvisorMouth"/>). The original poses the advisor with
/// <c>Advisor&lt;level&gt;m&lt;n&gt;</c> clips (<see cref="AnimationClipName"/>); the sequence of clips (generated, or a fixed row of
/// the sequence table) is built (<see cref="AdvisorSequences"/>), sets the response's span, when the model hides and the audio's
/// delayed start (<see cref="AdvisorSpeechStart"/>), and its clips play one after the other (<see cref="AdvisorTimeline"/>): the
/// node tracks move the model (the rise in, the gestures, the sink out), the blink toggles swap the eyes, and the response's
/// accessory selectors (<see cref="AdvisorCostume"/>, <see cref="AdvisorAccessories"/>) put on a hat; the vertex and texture-coordinate
/// tracks play as an object's do, and the ticket advice starts its screen-space effects (<see cref="AdvisorEffectGroups"/>).
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
	// [DATA:global/advisor.wad:Advisor.MD2:node names] the meshes shown without any accessory; every other mesh (hats, spatula, bow tie, blink meshes, level props) starts hidden, as the clips' node lists hide them (AdvisorAccessories.IsBaseMesh)
	internal static readonly string[] BodyMeshes = { "Body", "Bug Head", "Right Antennae", "Left Antennae", "Right hand", "Left Hand", "Right Eye", "Left Eye" };

	/// <summary>Every unproven advisor/lip-sync rule (docs/LIPS.md "Approximation register"); logged once when an advisor is created.</summary>
	internal static readonly (string Id, string Rule)[] Approximations =
	{
		("ADVISOR-005", "the advisor is lit like the original (advisor.shader: ambient 0.15 grey, 0x573FD0, plus the directional light of colour 1.0, 0x429BA0, per vertex, clamped; 0x574660 -> 0x5741B0), with the ambient of an ordinary park; the ambient words 0x879DD8-0x879DE4 are (alpha 1.0, R, G, B), 0.15 grey by default (0x573FFA-0x57404F), and the frontend states set them: black by 0x5D8B50 and 0x5D8D70 (0x5D8C3B-0x5D8C51, 0x5D8D7B-0x5D8D99), a colour by 0x5D9230(1, rgb) from 0x5DCFE0 (0x202020, 0x5DD078) and at 0x5E13A9, 0x5E1924 and 0x5E2445 (0x444444); which lobby screen each state is, which of them show the advisor, and whether the original fogs the advisor model are not read, so the park ambient is used and no fog is drawn"),
		("ADVISOR-009", "--advisor-say plays global clips by number and starts them at its first rendered frame; the controller says the messages its producers post: the game events (0, 106, 128, 129, 323), the pranks (125-127), the challenge system's short-time warning (271), the mechanics' message (70), the ride breakdown lines (66 imminent, 68 and 69 for the mechanics' state, 258 condemned), the staff strike messages (33-57), the research completions (93-97), the new research groups (133-136), all research done (167: in Instant Action, and in the full simulation once the player's records show every park's research complete, 0x5B0C40), the global golden ticket moves (234-237), the golden ticket wins (215-217, 220-222, 225-227) with the first-ticket lines (311, 315), the challenge offers, wins and losses (270, 272, 273, and 344 at the first offer) and the help messages of the HUD windows, panels and tools OpenTPW has (the buy, hire staff, all attractions, loans, finance, park information, all visitors, ticket price, research, employee, visitor and map windows, the object windows of rides, shops, sideshows, features and add-ons, the message, camera, locator and coaster panels, the path, queue, kart and water line, patrol, build, Buy Land and Clear Land tools and the Place tool of a hire); all 351 descriptors of the table 0x76E300 are bound as data and every producer is mapped (docs/reverse/ADVISOR-SCAN.md, content/data/advisor-producers.toml), the background scan (0x59C680, one scanned descriptor per update, the ones whose first word is 0) is ported (AdvisorScan, paced by the park's turn counter: ADVISOR-017) with the money callbacks 100-105 and 107 (0x59E8D0-0x59EB80, read over EconomyMoneyState: the net worth is read fresh and the offers' stored credit tests are not rewritten, which the loan list rebuilds before it reads) and the 31 park callbacks of the rides, cleanliness and guest mood batches (3-10, 60, 62, 71, 73-75, 77-79, 81, 82, 85-89, 145, 158, 159 and 259-262: AdvisorParkCallbacks over EconomyParkState, read-only; the object and hire stamps behind the days-since tests (0x4C9070, 0x4C90D0) are the advisor's own record of the newest construction and hire, kept from the construction events (a purchase, the Move tool's re-placement and the fixed items the game makes in play: an arrival vehicle at its first trip and the finale's End) and the hire events and saved in its snapshot, taken for an original-save park from the analyser's saved mLastObjectBuilt and mLastStaffHired (read back by the load at 0x4C7289 and 0x4C72B3) as the turns of the save's calendar whose park time they are, and derived at attach from the newest standing object and employed member only for an OpenTPW save from before its snapshot kept them and a save that names no analyser, and the helpers' block and toilet caches are recounted fresh), and the 38 staff callbacks (11, 13, 14, 18, 20, 23, 26, 28-32, 108-112, 114-123, 144, 146-150, 169-172 and 263: AdvisorStaffCallbacks over EconomyParkState, read-only; the staff HQ's training budgets and its mHaveEverTrained byte, which the training window sets and which the park save keeps, and the park limits MaxHandymenInPark to MaxResearchersInPark are the inputs) and the 31 callbacks of the queues, prices and research batches (15-17, 61, 63, 64, 72, 76, 80, 91, 92 and 238-257: AdvisorQueuesCallbacks over EconomyParkState, read-only; the booth's verdict fills the perception ring 0 that 91 and 92 read) and the 18 callbacks of the park-opening, congratulation, tutorial and golden ticket group (1, 2, 124, 140, 156, 157, 185, 274-276, 278-282, 314, 316 and 317: AdvisorChallengeCallbacks; the sizes of the last five arrival groups behind 124, 140 and 281 are the advisor's own record, kept from the guests' arrival-group event and saved in its snapshot, and read from the analyser's saved ring for an original-save park, with the stand-in of ADVISOR-028 for a legacy OpenTPW save); and the 9 last computed callbacks (12, 19, 21, 22, 27, 98, 113, 141 and 264: AdvisorInputsCallbacks over EconomyParkState, read-only; their inputs are the guards' chase counters, the month, got-away and lifetime words that a guard's chase raises (0x4C74F0 from 0x4D6790) and the month close zeroes (two of them), saved with the park and read from an original save, the patrol rectangles' coverage of a class of cells (0x4CD500 over the cell map), the first queue ride that joins no path with the selected ride left out (0x4CCB00, through the level) and the mean live excitement of the rides, cached while the quarter-turn age is at most 10 (40-43 turns) (0x4CCBF0, through the level)); the other 21 scanned messages score 0 as the original's constant-zero stub (0x5D60C0) does; what the callbacks leave unread: 113 and 141 also store a ride's id in the scan object's word +4, which only reaches the queued advice record (0x59B6BE, whose readers are unread, GUEST-007) and is not kept, and the researchers' flag (0x5041F0, ParkStaff.LabActive: the message 5 or 6 a researcher's hire or removal posts) reaches the advisor's subscription 0x599EDB, whose handler (0x59B060, case 5) reads an advice record at message +8 with its valid byte at +0x1C, which that plain message does not have, so the original's handler takes the posting frame's words for the record (its message id word is -1, which no descriptor holds): not ported, whether that record is queued (0x59A940, the eligibility 0x59ABC0) is not traced; game code posts no other advice than the lines listed above (218-219, 223-224 and 228-233 have no producer in tp.exe); help messages without a window or tool here: 285 (the All Staff window 0x496620) and 35 help ids that no post names (204, once counted with them, is pushed at 0x526D41 ahead of a jump into the queue tool's post 0x527629: the kart and water line tool's help); the postcard help is not part of this ID: 308 (the address book 0x4A8140) is [EXT:online-postcard-address-book] and 307 (the postcard window 0x4A8C00, which posts it only outside game type 1, where OpenTPW's online-only postcard never runs) is [EXT:online-help-window], both user-approved 2026-10-30"),
		("ADVISOR-025", "a fixed sequence's timer (sequences 28 and 29, the ticket/key advice) starts the screen-space Key effect (template 87 at native (90000, 0, 7000), the top right) and plays cat_ui 0xC6 when the advisor clock reaches it, as 0x599991 does; its call 0x4B9340 does nothing in a park and refreshes only the island lobby's key panel (window 0x7CC4B4, layout Lobby 0x757F60; created by 0x4B8CA0, which also calls it, and cleared by 0x4B8B70), which OpenTPW's lobby shows only as a snapshot taken when the menu is built: 0x4B9340 shows the key group 0x1E0EC for a positive key count outside Instant Action and formats \"%d x\" into 0x1E0EE from the player ledger object (0xF7C58C, built by 0x5AEF50), and keeps particle 0x61 running while the key count meets the selected record's threshold (0x521E60 at 50000, 0, y from the panel's rectangle x 75000/0x600); OpenTPW has no live refresh, no EA/EB affordability art and no effect 0x61 there; where the screen-space particles sit in the frame is PTCL-909"),
		("ADVISOR-028", "the advisor's last arrival-group sizes are recorded in current OpenTPW saves and imported from the original TPW and TPI analyser rings; the legacy host-save policy is not part of this ID ([EXT:legacy-save-arrival-waves], user-approved 2026-10-30): a legacy OpenTPW save without ArrivalWaves cannot recover its group count or sizes from admissions, so a park with admissions uses five zero-size stand-ins (exactly-one-group advice (281) can differ when the missing history held exactly one group, and hype (124) or big-busload (140) scores can differ until real arrivals replace the unknown values), and RestoreSnapshot retains the same admissions fallback for null or explicitly empty ArrivalWaves, even though a validated empty original ring is authoritative at AttachLevel; what stays open is the TPI advisor: TPI import and guarded manual attachment are data readiness only: the public TPI slice still creates no advisor, keeps fresh money and clocks and does not support full saved-game resume"),
	};

	private readonly AdvisorRig globalRig;
	private AdvisorRig? levelRig;
	private string? levelRigName;
	private AdvisorRig rig;
	private AdvisorCostume costume;
	private AdvisorThemeCostume theme = AdvisorThemeCostume.None;
	private readonly AdvisorMouth mouth = new( new Random() );
	private SpeechAudioPlayer? player;
	// [EXT:advisor-sliced-decode] a response whose PCM is still being decoded over frames (the player is made when it is done)
	private Mp2Decoder.SlicedDecode? pendingDecode;
	// [EXT:advisor-sliced-decode] the advisor-clock time the audio was first due while it was still being decoded, and how much later than that it started:
	// the mouth reads the clock less this lateness, so it moves with the audio heard (0 when the audio started as soon as it was due, as on the desktop)
	private long? audioDueWhileDecoding;
	private uint audioLateMilliseconds;
	// [EXT:advisor-sliced-decode] the decode of the response being said failed after it started (a damaged payload): the line was ended, as one that cannot be said
	private bool unsaidLine;
	private LipSyncTimeline? timeline;
	private bool disposed;
	// The advisor clock (milliseconds that stand still while the game is paused) of the automatic advisor; null for the manual
	// --advisor-say / --advisor-response tools, which start their speech at the first rendered frame.
	private readonly Func<long>? clock;
	private AdvisorSpeechStart? speechStart;
	// The current sequence's timer has fired (it fires once per response).
	private bool timerFired;
	/// <summary>Plays a <c>cat_ui</c> sound (the sequence timer's); tests substitute a recorder.</summary>
	internal Action<uint> PlayUiSound { get; set; } = sound => GameAudio.PlayUi( sound );
	/// <summary>The particle pool the screen effects start in: the current level's; tests substitute a recorder.</summary>
	internal IAdvisorParticlePool ParticlePool
	{
		get => particlePool;
		set
		{
			particlePool = value ?? throw new ArgumentNullException( nameof( value ) );
			effects = new AdvisorEffectGroups( value );
		}
	}
	private IAdvisorParticlePool particlePool = ParticleRendererModule.AdvisorPool;
	private AdvisorEffectGroups effects = new( ParticleRendererModule.AdvisorPool );
	// The response is active (0xF7966C): set when it starts, cleared at the end of its span or by a kill, which empties effect group 0.
	private bool responseActive;
	/// <summary>The screen effects of the accessory bits (group 0) and the others; read by tests.</summary>
	internal AdvisorEffectGroups Effects => effects;
	// The generated sequences draw from their own stream (see AdvisorRandom).
	private readonly AdvisorRandom sequenceRandom = new();
	// The clip lengths of the model that plays (a level advisor's clips 16-20 exist only there), by archive.
	private AdvisorClipDurations? clipDurations;
	private readonly Dictionary<string, AdvisorClipDurations?> durationsByArchive = new();
	// The theme costume of each level seen, read once.
	private readonly Dictionary<string, AdvisorThemeCostume> themes = new();
	// The hidden accessory nodes of the response's costume by clip accessory bits, and the per-frame scratch set.
	private readonly Dictionary<int, HashSet<int>> costumeHidden = new();
	// The hidden nodes at the start of each clip of the current sequence (SequenceHiddenNodes), by clip index.
	private readonly Dictionary<int, HashSet<int>> sequenceHidden = new();
	private readonly HashSet<int> hiddenScratch = new();
	// The clip index of the current sequence whose start has been seen (its clip-start sound played), −1 before the first.
	private int startedClipIndex = -1;
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
	public bool IsSpeaking => player != null ? !player.IsFinished : pendingDecode != null;
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
	/// How long after the request the model stays shown: the end of the last clip, which is when the clips play out, each for its
	/// own length including clip 14's (<see cref="AdvisorSequences.PlayMilliseconds"/>; the sequence sum leaves clip 14 out, and the
	/// response's span adds 1000 and clip 15 on top of that sum for the controller). Without a sequence (no clip archives) the span itself.
	/// </summary>
	// [BIN:TPW-EXE:0x004735D0 animation sampler] at the end of a clip (frame past its length, 0x473743–0x47374E) with nothing queued ([channel+0x24] == 12, 0x47375D) and model flag 8 (0x473769–0x473776) the sampler starts the idle set 12 (0x472F60) and hides a shown model with 0x45BDD0 (scene unlink 0x576540, then clears bit 0x19); the advisor model has flag 8 (load flags 0x50120 at 0x429C9D: 0x461F10 ORs 8 at 0x4621C9, 0x4557C0 copies it into the model)
	// [BIN:TPW-EXE:0x00599880 advisor update] the next clip of the sequence starts only while the channel reads the idle set 12 (0x599A23) through 0x4732A0, which shows the model again (0x4732C6–0x4732DD); after the last clip nothing starts, so the model stays hidden until the next response
	public uint DrawnSpanMilliseconds => Sequence != null && clipDurations != null ? AdvisorSequences.PlayMilliseconds( Sequence, clipDurations ) : ResponseSpanMilliseconds;

	/// <summary>
	/// The model is shown: set when a response starts (every sequence starts an animation other than 12), cleared when the
	/// last clip of its sequence ends (<see cref="DrawnSpanMilliseconds"/>) and by <see cref="Kill"/>.
	/// </summary>
	// [BIN:TPW-EXE:0x004732A0 animation start] the model is shown when any animation other than 12 starts (via 0x45BD30); "Kill advisor" (0x429D60: animation 12 with the immediate flag, then unlink), called by the stop path 0x5996D0 (six callers, including level exit) and the slap path 0x5994E0, hides it at once; the normal end of advice (0x599880) calls neither, the sampler hides the model at the end of the last clip (see DrawnSpanMilliseconds). The model therefore hides at the clip sum even while a long speech of a fixed row (21-27) is still playing; that follows the trace, and a capture should confirm it
	public bool ModelShown { get; private set; }
	/// <summary>Whether the playing clip hides the mouth (accessory bit <see cref="AdvisorAccessories.SuppressCostumeBit"/>, the ticket close-ups).</summary>
	public bool MouthSuppressed { get; private set; }

	/// <summary>The model is drawn: shown, and (automatic advisor) before the last clip ends or (manual tools) while the speech plays.</summary>
	public bool IsDrawn => !disposed && ModelShown && (clock == null || speechStart == null ? IsSpeaking : speechStart.Value.Within( clock(), DrawnSpanMilliseconds ));

	/// <summary>
	/// Pixel rectangle (y down) of a <paramref name="target"/> covered by the clip-space box the advisor is drawn in:
	/// <see cref="Centre"/> ± <see cref="BoxHalfSide"/>, the bottom-right of the screen.
	/// </summary>
	internal static (int X, int Y, int Width, int Height) BoxRectangle( Point2 target )
	{
		// [BIN:TPW-EXE:0x0057A750 mesh draw] the unprojected coordinates (centre (0.6, 0.6), y down) span the screen from −1 to 1 as clip space; a capture of the original agrees (docs/LIPS.md, ADVISOR-023 retired)
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
	public Advisor( Func<long>? clock = null ) : this( clock, AdvisorRig.LoadGlobal ) { }

	/// <summary>An advisor without a model, for tests of its speech timing (it is never drawn, and no response may select a level model).</summary>
	internal static Advisor WithoutModel( Func<long> clock ) => new( clock, () => null );

	private Advisor( Func<long>? clock, Func<AdvisorRig?> loadModel )
	{
		this.clock = clock;
		foreach ( var (id, rule) in Approximations )
			Log.Warning( $"[APPROX:{id}] {rule}" );
		globalRig = rig = loadModel()!;
		if ( globalRig == null )
			return;
		try
		{
			foreach ( var part in globalRig.Parts.Where( part => Mouths.Contains( part.Name ) ) )
				foreach ( var vertex in part.Vertices )
				{
					mouthMin = System.Numerics.Vector3.Min( mouthMin, vertex.Position.GetSystemVector3() );
					mouthMax = System.Numerics.Vector3.Max( mouthMax, vertex.Position.GetSystemVector3() );
				}
		}
		catch
		{
			globalRig.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Model-space transform of a node from the shared MD2 hierarchy code
	/// (<see cref="ModelAnimationPlayer.ComputeRestTransforms"/>: parent-relative, row-vector,
	/// world = local × parent world). With the root "Position Dummy" applied the model stands
	/// Y-up and faces −Z. The bind pose is the pose without a clip (the sequence's clips pose the rig in <see cref="CurrentPose"/>).
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
		// [APPROX:ADVISOR-009] --advisor-say plays global clips by number; responses (SayResponse) follow the traced global/level selector, and AdvisorController picks response IDs only for the messages whose producers are bound (0, 33-57, 66, 68-70, 93-97, 106, 125-129, 133-136, 215-217, 220-222, 225-227, 258, 270-273, 311, 315, 323, the scanned lines of the money, rides, cleanliness, guest mood, staff, queues, prices, research and golden ticket batches and the help of the windows, panels and tools OpenTPW has) — evidence needed: the readers of the advice record's subject and cell words (113 and 141 write the scan object's word +4; 0x59B6BE copies it into the queued record), what the type-5 handler 0x59B060 does with the researchers' flag message (it reads the posting frame as a record of the message id -1) and the windows of the help messages 285 (the All Staff window 0x496620); every computed score callback of the 155 scanned descriptors is ported (the scan 0x59C680 evaluates one per update, from the table 0x76E300 word +0x0 = 0)
		// [EXT:online-postcard-address-book] [EXT:online-help-window] the help of the postcard window (307, posted by 0x4A8C00 only outside game type 1) and of its address book (308, 0x4A8140) has no producer: OpenTPW's postcard is online only and its address book is not ported (user-approved 2026-10-30)
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
		var audio = Mp2Decoder.Decode( entry.AsSpan( headerBytes ), SdtEntryHeader.PlaybackBytes( entry ) );
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
		var (mpeg, playbackBytes, lips, source) = LoadResponseParts( fileSystem, response, level, language );
		using var decodeMark = FrameMarks.Measure( "advisor.decodeMp2" );
		return (Mp2Decoder.Decode( mpeg, playbackBytes ), lips, source);
	}

	/// <summary><see cref="LoadResponse"/> up to the decode: the response's MPEG bytes and the length to play, its lip marks and where they came from.</summary>
	internal static (byte[] Mpeg, int? PlaybackBytes, LipSyncTimeline Timeline, string Source) LoadResponseParts( BaseFileSystem fileSystem, AdvisorResponse response, string? level, GameLanguage? language = null )
	{
		if ( response.Local && level == null )
			throw new InvalidOperationException( $"Advisor response {response.Id} uses the level speech bank but no level is loaded." );
		var root = response.Local ? $"/levels/{level}/Speech" : "/global/Speech";
		// The chosen language's own bank and lips (overlay first, else the installation), for the level's speech as well as the global one:
		// the Welcome line (response 1) comes from the level bank, which every language of the CD ships (docs/LANGUAGES.md).
		var languageRoot = language == null ? null : root.TrimStart( '/' );
		string bankPath;
		string source;
		if ( languageRoot != null && language!.ResolveDataFile( $"{languageRoot}/speechHD.SDT" ) is { } languageBank )
		{
			bankPath = languageBank;
			source = $"{language.Name}: {languageBank}";
		}
		else
		{
			bankPath = fileSystem.GetAbsolutePath( $"{root}/speechHD.SDT" );
			source = $"{root}/speechHD.SDT";
		}
		// Read only the selected speech entry: the global bank can contain hundreds of unrelated responses.
		var bank = new SoundBank( () => File.OpenRead( bankPath ), new FileInfo( bankPath ).Length );
		var mpeg = bank.GetMpeg( (uint)response.Sample, int.MaxValue, out _, out var playbackBytes )
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
		return (mpeg, playbackBytes, new LipSyncTimeline( marks ), source);
	}

	/// <summary>Plays advisor response <paramref name="responseId"/> from the response table; false when the table has no such response.</summary>
	public bool SayResponse( int responseId, string? level, GameLanguage? language = null )
	{
		if ( !AdvisorResponses.Table.TryGetValue( responseId, out var response ) )
			return false;
		byte[] mpeg;
		int? playbackBytes;
		LipSyncTimeline lips;
		string source;
		using ( FrameMarks.Measure( "advisor.loadResponse" ) )
			(mpeg, playbackBytes, lips, source) = LoadResponseParts( FileSystem, response, level, language );
		SayResponseParts( response, mpeg, playbackBytes, lips, source, level );
		return true;
	}

	/// <summary><see cref="SayResponse"/> from the response's loaded parts (<see cref="LoadResponseParts"/>); tests give their own.</summary>
	internal void SayResponseParts( AdvisorResponse response, byte[] mpeg, int? playbackBytes, LipSyncTimeline lips, string source, string? level )
	{
		Mp2Audio? audio = null;
		Mp2Decoder.SlicedDecode? pending = null;
		double seconds;
		using ( FrameMarks.Measure( "advisor.loadResponse" ) )
		{
			// [EXT:advisor-sliced-decode] with the browser's single thread the response's PCM is decoded over the next frames, not in this one (17-50 ms for a line);
			// its length, which the sequence and the span the controller waits are built from, comes from the frame headers and equals the decoded one. The audio starts
			// when it is decoded and the advisor clock has reached its start, so a line can start a few frames later than it would with the whole decode first; the mouth
			// then follows the audio that started late (see LipPosition), and the speech's end and the cut-off's slap follow the player, so they move with it.
			// [EXT:park-entry-preparation] a line its park's build decoded ahead (AdvisorEntryPreparation.LineSteps; the PCM is a function of these bytes alone) is taken
			// instead of decoded, and starts as a line decoded in one go does
			if ( AdvisorEntryPreparation.TryTake<Mp2Audio>( AdvisorEntryPreparation.LineKey( source, response.Sample, playbackBytes, mpeg.Length ), FileSystem, out var decoded )
				&& decoded != null )
			{
				audio = decoded;
				seconds = audio.DurationSeconds;
			}
			else if ( SlicedDecoding && clock != null && Mp2Decoder.TryMeasure( mpeg, playbackBytes, out var sampleRate, out var sampleFrames ) )
			{
				pending = new Mp2Decoder.SlicedDecode( mpeg, playbackBytes );
				seconds = (double)sampleFrames / sampleRate;
			}
			else
			{
				using var decodeMark = FrameMarks.Measure( "advisor.decodeMp2" );
				audio = Mp2Decoder.Decode( mpeg, playbackBytes );
				seconds = audio.DurationSeconds;
			}
		}
		using ( FrameMarks.Measure( "advisor.play" ) )
			Play( audio, pending, seconds, lips, response.Lip, $"response {response.Id} (sample {response.Sample}, {source})", response, level );
	}

	private AdvisorClipDurations? ClipDurations( AdvisorRig playing )
	{
		if ( !durationsByArchive.TryGetValue( playing.Archive, out var durations ) )
		{
			// The playing model's own clips; a level model without any falls back to the global advisor's.
			durations = LoadClipDurations( playing.Archive ) ?? (playing.Archive == ArchivePath ? null : LoadClipDurations( ArchivePath ));
			durationsByArchive[playing.Archive] = durations;
		}
		return durations;
	}

	// the lengths a park build prepared (AdvisorEntryPreparation), else read now
	private static AdvisorClipDurations? LoadClipDurations( string archive ) =>
		AdvisorEntryPreparation.TryTake<AdvisorClipDurations>( AdvisorEntryPreparation.DurationsKey( archive ), FileSystem, out var prepared ) ? prepared : AdvisorClipDurations.LoadArchive( FileSystem, archive );

	/// <summary>
	/// [EXT:advisor-sliced-decode] Whether a response's PCM is decoded over frames (<see cref="Mp2Decoder.SlicedDecode"/>) instead of before it starts: the browser,
	/// which has no second thread; the desktop decodes in one go as before (tests set it).
	/// </summary>
	internal static bool SlicedDecoding { get; set; } = OperatingSystem.IsBrowser();

	/// <summary>Decoding time per frame for a response being decoded over frames.</summary>
	internal static TimeSpan DecodeSliceBudget { get; set; } = TimeSpan.FromMilliseconds( 5 );

	private void Play( Mp2Audio audio, LipSyncTimeline lips, int clipNumber, string description, AdvisorResponse? response = null, string? level = null ) =>
		Play( audio, null, audio.DurationSeconds, lips, clipNumber, description, response, level );

	private void Play( Mp2Audio? audio, Mp2Decoder.SlicedDecode? pending, double durationSeconds, LipSyncTimeline lips, int clipNumber, string description, AdvisorResponse? response = null, string? level = null )
	{
		player?.Dispose();
		timeline = lips;
		ClipNumber = clipNumber;
		Duration = TimeSpan.FromSeconds( durationSeconds );
		player = audio != null ? CreatePlayer( audio ) : null;
		pendingDecode = pending;
		audioDueWhileDecoding = null;
		audioLateMilliseconds = 0;
		unsaidLine = false;
		speechStart = null;
		Sequence = null;
		timerFired = false;
		// Choose the model first: a failed level load must not leave a disposed rig drawn.
		using ( FrameMarks.Measure( "advisor.selectRig" ) )
			rig = SelectRig( response, level );
		ModelShown = true;
		// [BIN:TPW-EXE:0x005990E3 response player] the response row's +0x14 and +0x18 words are the accessory selectors (0xF79684, 0xF79688) and its +0x10 low half the model slot (0xF79674)
		using ( FrameMarks.Measure( "advisor.costume" ) )
			costume = response != null ? AdvisorCostumeTable.For( response.Id ) : AdvisorCostume.None;
		theme = AdvisorThemeCostume.None;
		if ( (costume.Hat == -1 || costume.SecondHat == -1) && level != null && !themes.TryGetValue( level, out theme! ) )
			using ( FrameMarks.Measure( "advisor.themeCostume" ) )
				themes[level] = theme = AdvisorEntryPreparation.TryTake<AdvisorThemeCostume>( AdvisorEntryPreparation.ThemeKey( level ), FileSystem, out var preparedTheme ) && preparedTheme != null
					? preparedTheme : AdvisorThemeCostume.Load( FileSystem, level );
		costumeHidden.Clear();
		sequenceHidden.Clear();
		startedClipIndex = -1;
		// [BIN:TPW-EXE:0x005993B3 response player] a new response empties effect group 0 (0x5ED5A0(0, 0, 0))
		effects.SetCount( 0, 0, 0 );
		responseActive = response != null && clock != null;
		if ( response != null && clock != null )
		{
			var now = clock();
			speechStart = AdvisorSpeechStart.For( now, response.Animation );
			// [BIN:TPW-EXE:0x00599050 response player] the builder gets the sample length + 200 + 300 ms; a fixed sequence (response +12 minus 16) comes from table 0x76DC18
			var length = (int)Duration.TotalMilliseconds;
			AdvisorClipDurations? durations;
			using ( FrameMarks.Measure( "advisor.clipDurations" ) )
				durations = clipDurations = rig == null ? null : ClipDurations( rig );
			if ( durations != null )
			{
				using var sequenceMark = FrameMarks.Measure( "advisor.sequence" );
				Sequence = AdvisorSequences.Build( response.Animation, length + AdvisorSequences.BudgetSlackMilliseconds, durations, sequenceRandom.Next );
				ResponseSpanMilliseconds = AdvisorSequences.ResponseSpan( Sequence, durations );
			}
			else
			{
				// [BIN:TPW-EXE:0x005989C0 advisor clip lengths] a game without clip archives has every clip length 0, so the original's response returns 1000 and its model never plays a clip
				// [EXT:advisor-no-clips] an install without advisor clip archives (a partial data copy) keeps the speech's length + 200 + 300 + 1000 as the span and draws the model for it, so the advisor stays visible while it talks
				ResponseSpanMilliseconds = checked((uint)(length + AdvisorSequences.BudgetSlackMilliseconds + 1000));
			}
			using ( FrameMarks.Measure( "advisor.firstUpdate" ) )
				Update();
		}
		Log.Trace( $"Advisor says {description}: {durationSeconds:F2} s, {lips.Marks.Count} LIP marks, clock: {player?.ClockSource ?? "decoding"}{(player?.DeviceError == null ? "" : $" ({player.DeviceError})")}{(Sequence == null ? "" : $", sequence {string.Join( ",", Sequence.Clips )} ({Sequence.SumMilliseconds} ms)")}{(speechStart is { } start ? $", audio starts at +{start.AudioStartMilliseconds - start.RequestMilliseconds} ms" : "")}." );
	}

	/// <summary>
	/// Starts the audio of the last response once the advisor clock reaches its start (the request itself, or 800 ms later for
	/// a sequence that opens with clip 14); call once per frame. The manual tools start at their first rendered frame instead.
	/// </summary>
	public void Update()
	{
		if ( disposed || clock == null )
			return;
		PumpDecode();
		var now = unchecked((int)clock());
		if ( speechStart is not { } start )
		{
			effects.Update( now );
			return;
		}
		// [BIN:TPW-EXE:0x0059992D response end] the response stops being active once its speech has ended (0x51C270) and the clock has reached the request plus the builder's clip sum (0xF79668, set at 0x59949D); group 0 is emptied (0x59993E)
		if ( responseActive && !start.Within( clock(), (uint)Math.Max( 0, Sequence?.SumMilliseconds ?? 0 ) ) && player is not { IsStarted: true, IsFinished: false } )
		{
			responseActive = false;
			effects.SetCount( 0, 0, 0 );
		}
		// The accessory update runs once per clip start: the key effect's sound, then the screen effects of the accessory bits.
		if ( Sequence?.AccessoryBits is { } accessoryBits && clipDurations != null
			&& AdvisorTimeline.At( Sequence, clipDurations, unchecked((uint)(clock() - start.RequestMilliseconds)) ) is { } playing && playing.Index != startedClipIndex )
		{
			for ( var index = startedClipIndex + 1; index <= playing.Index; index++ )
			{
				if ( index >= accessoryBits.Count )
					continue;
				if ( responseActive && (accessoryBits[index] & AdvisorAccessories.EffectBits) == AdvisorAccessories.KeyEffectBit )
					PlayUiSound( AdvisorAccessories.KeySound );
				effects.ApplyAccessoryBits( accessoryBits[index], responseActive, clipDurations.Of( Sequence.Clips[index] ), now );
			}
			startedClipIndex = playing.Index;
		}
		// The sequence timer fires once while the response is current (a kill clears the request, so a cut-off response never fires it).
		if ( !timerFired && Sequence?.TimerMilliseconds is { } timer && start.TimerDue( clock(), timer ) )
		{
			timerFired = true;
			// [BIN:TPW-EXE:0x005999A0 timer effect] the Key effect (template 87, screen-space) at native (90000, 0, 7000): screen (0.8, -0.8), the top right; its handle is not kept
			particlePool.Create( AdvisorSequences.TimerEffect, AdvisorSequences.TimerEffectPosition );
			PlayUiSound( AdvisorSequences.TimerSound );
		}
		// [BIN:TPW-EXE:0x00599800 advisor frame] every frame moves the effect groups (0x5ED770)
		effects.Update( now );
		// The last clip's end hides the model (see DrawnSpanMilliseconds); the next response shows it again.
		if ( ModelShown && !start.Within( clock(), DrawnSpanMilliseconds ) )
			ModelShown = false;
		if ( player is { IsStarted: true } || !start.AudioDue( clock() ) )
			return;
		// [EXT:advisor-sliced-decode] a response still being decoded when its audio is due starts when the decode is done; the mouth waits as long (LipPosition)
		var due = clock();
		if ( player == null )
		{
			if ( pendingDecode != null )
				audioDueWhileDecoding ??= due;
			return;
		}
		audioLateMilliseconds = audioDueWhileDecoding is { } dueAt ? unchecked((uint)(due - dueAt)) : 0;
		player.Start();
	}

	/// <summary>One slice of the decode of a response started with <see cref="SlicedDecoding"/>; its player is made when the PCM is complete.</summary>
	private void PumpDecode()
	{
		if ( pendingDecode is not { } decode )
			return;
		try
		{
			using ( FrameMarks.Measure( "advisor.decodeSlice" ) )
				if ( !decode.Run( DecodeSliceBudget ) )
					return;
			pendingDecode = null;
			player = CreatePlayer( decode.Result! );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or InvalidOperationException or NotSupportedException or OverflowException )
		{
			// The frame headers were readable (the length was measured), so this is a damaged payload. With the decode before the line, the same failure makes the
			// automatic advisor skip the line (its player returns no span, so the controller reserves only the 1000 ms it adds): here the line has already started,
			// so it is ended at once (Kill: silent, the model hidden, its effects cleared) and the automatic advisor shortens the reservation to those 1000 ms
			// (TakeUnsaidLine). The caption was posted and the history recorded either way.
			pendingDecode = null;
			Log.Warning( $"Advisor response could not be played: {exception.Message}" );
			Kill();
			unsaidLine = true;
		}
		catch
		{
			// anything else is a fault, as it is when the decode runs before the line: reported once, not again every frame
			pendingDecode = null;
			throw;
		}
	}

	/// <summary>
	/// [EXT:advisor-sliced-decode] Whether the last line's decode failed after the line started (<see cref="PumpDecode"/>), once: the caller ends the line as one it could
	/// not say.
	/// </summary>
	internal bool TakeUnsaidLine()
	{
		var unsaid = unsaidLine;
		unsaidLine = false;
		return unsaid;
	}

	/// <summary>
	/// Where the mouth's LIP clock stands: the advisor clock minus the LIP base (the original's mouth never follows the audio); the audio position for the manual tools; silent
	/// before the audio starts. [EXT:advisor-sliced-decode] Audio that started late because its decode was not done when it was due moves the LIP clock back by as much, so the
	/// mouth keeps the lead over the audio it has on the desktop.
	/// </summary>
	internal TimeSpan LipPosition => player is not { IsStarted: true } ? TimeSpan.Zero
		: clock != null && speechStart is { } start ? TimeSpan.FromMilliseconds( unchecked((uint)(start.LipMilliseconds( clock() ) - audioLateMilliseconds)) )
		: Position;

	/// <summary>Whether the speech's audio has started.</summary>
	internal bool AudioStarted => player is { IsStarted: true };

	/// <summary>Makes the speech players instead of <see cref="CreatePlayer"/> (tests); null for the real ones.</summary>
	internal static Func<Mp2Audio, SpeechAudioPlayer>? PlayerFactory { get; set; }

	/// <summary>
	/// Speech goes through the game mixer when sound is on. Without a mixer it opens its own SDL device,
	/// except when sound is off (<c>--mute</c>): then it keeps the wall clock and stays silent.
	/// </summary>
	internal static SpeechAudioPlayer CreatePlayer( Mp2Audio audio ) =>
		PlayerFactory?.Invoke( audio ) ?? CreateDevicePlayer( audio );

	private static SpeechAudioPlayer CreateDevicePlayer( Mp2Audio audio ) =>
		GameAudio.EnsureStarted() && AudioMixer.Current is { } mixer
			? new SpeechAudioPlayer( audio, mixer )
			: new SpeechAudioPlayer( audio, null, openDevice: GameAudio.Enabled && AudioMixer.Current == null );

	/// <summary>Stops the current speech, if any; the model stays shown (<see cref="ModelShown"/>).</summary>
	public void Silence()
	{
		player?.Dispose();
		player = null;
		pendingDecode = null;
		audioDueWhileDecoding = null;
		audioLateMilliseconds = 0;
		timeline = null;
		speechStart = null;
	}

	/// <summary>"Kill advisor": stops the speech and hides the model.</summary>
	public void Kill()
	{
		Silence();
		ModelShown = false;
		// [BIN:TPW-EXE:0x0059972A kill path] the stop path clears the active flag and empties effect group 0
		responseActive = false;
		effects.SetCount( 0, 0, 0 );
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
		var (worlds, hidden, clip, tick) = CurrentPose();
		foreach ( var part in rig.Parts )
		{
			if ( hidden.Contains( part.NodeIndex ) || Mouths.Contains( part.Name ) && (part.Name != shownMouth || MouthSuppressed) )
				continue;
			rig.Write( part, worlds[part.NodeIndex], clip, tick );
			part.Material.Set( "ObjectUniformBuffer", new ObjectUniformBuffer
			{
				g_mModel = model,
				g_mView = Matrix4x4.Identity,
				g_mProj = Matrix4x4.Identity,
				// [BIN:TPW-EXE:0x00429BA0 advisor setup] the advisor's directional light has colour 1.0 (advisor.shader lights each vertex like 0x574660 -> 0x5741B0 with the default ambient 0.15 grey, 0x573FD0)
				// [APPROX:ADVISOR-005] the ambient words 0x879DD8-0x879DE4 are (alpha 1.0, R, G, B) and the frontend states set them (black by 0x5D8B50 and 0x5D8D70, a colour by 0x5D9230(1, rgb): 0x202020 from 0x5DCFE0, 0x444444 at 0x5E13A9, 0x5E1924 and 0x5E2445; an ordinary park keeps the 0.15 default, every writer of 0x879DDC listed); which lobby screen each state is, which of them show the advisor, and whether a fog is applied to the advisor model at all are not read; the park ambient is used and none is drawn
				g_vLightPos = LightPosition,
				g_vLightColor = System.Numerics.Vector3.One,
				g_vCameraPos = System.Numerics.Vector3.Zero,
				g_flTime = Time.Now,
			} );
			part.Model.Draw();
		}
		LastRenderedMouth = shownMouth;
	}

	/// <summary>
	/// The pose of the rig now: the node matrices of the clip the sequence plays at this moment after the request (the bind pose without
	/// a sequence), and the hidden mesh nodes (accessories, blink meshes, props; see <see cref="AdvisorAccessories.HiddenMeshNodes"/>).
	/// </summary>
	private (Matrix4x4[] Worlds, HashSet<int> Hidden, int ClipNumber, float SampledTick) CurrentPose()
	{
		ModelAnimation? clip = null;
		var tick = 0f;
		var clipNumber = 0;
		var clipIndex = -1;
		var sampledTick = 0f;
		var bits = 0;
		var worlds = rig.Rest;
		if ( Sequence != null && clipDurations != null && clock != null && speechStart is { } start
			&& AdvisorTimeline.At( Sequence, clipDurations, unchecked((uint)(clock() - start.RequestMilliseconds)) ) is { } position )
		{
			clip = rig.Clip( position.Clip );
			tick = position.Tick;
			bits = Sequence.AccessoryBits is { } list && position.Index < list.Count ? list[position.Index] : 0;
			worlds = rig.Pose( position.Clip, tick );
			clipNumber = position.Clip;
			clipIndex = position.Index;
			sampledTick = rig.SampledTick;
		}
		HashSet<int>? accessories;
		if ( clipNumber != 0 )
		{
			if ( !sequenceHidden.TryGetValue( clipIndex, out accessories ) )
				sequenceHidden[clipIndex] = accessories = AdvisorAccessories.SequenceHiddenNodes( rig.Nodes, Sequence!, rig.Clip, costume, theme, clipIndex );
		}
		else if ( !costumeHidden.TryGetValue( bits, out accessories ) )
			costumeHidden[bits] = accessories = AdvisorAccessories.CostumeHiddenNodes( rig.Nodes, costume, theme, bits );
		hiddenScratch.Clear();
		hiddenScratch.UnionWith( accessories );
		AdvisorAccessories.ApplyToggles( rig.Nodes, hiddenScratch, clip, tick );
		// [BIN:TPW-EXE:0x00599BA3 mouth update] with bit 0x10000 the mouth update shows no mouth (0x598DC0 hid all five at the clip start)
		MouthSuppressed = (bits & AdvisorAccessories.SuppressCostumeBit) != 0;
		return (worlds, hiddenScratch, clipNumber, sampledTick);
	}

	/// <summary>The model a response draws: the level's advisor for a response that names model 1 (when the level has one), else the global advisor.</summary>
	private AdvisorRig SelectRig( AdvisorResponse? response, string? level )
	{
		if ( response is not { Model: 1 } || level == null )
			return globalRig;
		if ( levelRigName != level )
		{
			levelRig?.Dispose();
			levelRig = null;
			levelRigName = level;
			try
			{
				levelRig = AdvisorEntryPreparation.TryTake<AdvisorRig>( AdvisorEntryPreparation.LevelRigKey( level ), FileSystem, out var prepared ) ? prepared : AdvisorRig.LoadLevel( FileSystem, level );
			}
			catch ( Exception exception )
			{
				// Any failure to build the level's model leaves the global advisor, never a released one.
				Log.Warning( $"The advisor model of level {level} could not be loaded: {exception.Message}" );
			}
		}
		return levelRig ?? globalRig;
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
		globalRig?.Dispose();
		levelRig?.Dispose();
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
