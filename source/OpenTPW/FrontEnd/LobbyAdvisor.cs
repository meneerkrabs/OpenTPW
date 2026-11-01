using OpenTPW.UI.Original;

namespace OpenTPW.FrontEnd;

/// <summary>
/// The advisor of the front-end state: the global advisor model that speaks the lobby's lines, over every lobby screen. It is made when the front end is set up, updated and
/// drawn with every lobby frame and torn down with it; it shows only while a response plays, as in a park.
/// </summary>
// [BIN:TPW-EXE:0x54E5EC front-end setup] state 1 of the main state machine sets the advisor up (0x429BA0 with the model path %s\Global\Advisor, 0x74D0C4; the system 0x5989C0) before it makes the lobby (0x5D5770)
// [BIN:TPW-EXE:0x54E6DF front-end frame] each frame of state 2 updates the advisor (0x599880), draws its model (0x429DF0(0), 0x54E6E6) and runs the effect groups (0x599800), then steps the lobby's tasks (0x5D5C50), among them the queue and the islands component; no test of the lobby screen
// [BIN:TPW-EXE:0x54E7F5 front-end teardown] state 3 tears the lobby down (0x5D5CF0: the queue's teardown 0x5D6060 among the tasks), then the advisor system (0x598AD0) and its model (0x429E20(0))
internal sealed class LobbyAdvisor : IDisposable
{
	private readonly Func<Advisor> create;
	// made when the first line is said: the model's load is not paid at every lobby entry (the park's advisor does the same)
	private Advisor? advisor;
	private readonly Func<long> timer;
	private readonly Func<bool> speechOn;
	private readonly Func<int, bool> say;
	private readonly Func<bool> busy;
	private readonly Func<bool> speaking;
	private readonly Action<uint> playSlap;
	private readonly Func<int> coin;
	private readonly Func<int> slapDraw;
	private bool ended;
	private LobbyAdvisorScreen screen;

	/// <summary>The advisor, made on first use.</summary>
	internal Advisor Presentation
	{
		get
		{
			if ( advisor == null )
			{
				advisor = create();
				advisor.Ambient = LobbyAdvisorAmbient.Of( screen );
			}
			return advisor;
		}
	}

	/// <summary>The lobby's speech queue.</summary>
	internal LobbyAdvisorQueue Queue { get; } = new();

	/// <summary>The advisor's image of the last <see cref="Render"/>, null while it is not shown.</summary>
	internal Veldrid.Texture? Image { get; private set; }

	/// <param name="create">Makes the global advisor, on the clock the lobby gives it, when the first line is said.</param>
	/// <param name="timer">The timer the queue's idle delay is measured on (0x5F5FA0), in milliseconds.</param>
	/// <param name="speechOn">Whether the advisor option is on (0x78D90C); null reads the game options.</param>
	/// <param name="say">The response player; null says the response through <paramref name="advisor"/> in the selected language.</param>
	/// <param name="busy">Whether a response is active (0xF7966C); null reads the advisor.</param>
	/// <param name="speaking">Whether the speech sound still plays (the speech handle 0xF79680); null reads the advisor.</param>
	/// <param name="playSlap">Plays the cut-off slap's sound id; null plays it on the speech channel.</param>
	/// <param name="coin">The draw that picks the idle line; null uses a local unseeded generator.</param>
	/// <param name="slapDraw">The draw that picks the slap; null uses a local unseeded generator.</param>
	// [EXT:no-player-profiles] the idle line needs a current player (0x5E1830 tests [[0xF81EBC] + 0x60] != -1, 0x48BCD0): OpenTPW has the one player, who is always current
	// presentation only: the idle line's even/odd draw is the CRT rand() in the original (0x67B5C0, 0x5E184C); it does not touch the world's or the guests' streams, so a local generator stands in (the lobby's own stand-in for rand() is the same kind, UI-018)
	internal LobbyAdvisor( Func<Advisor> create, Func<long> timer, Func<bool>? speechOn = null, Func<int, bool>? say = null, Func<bool>? busy = null, Func<bool>? speaking = null,
		Action<uint>? playSlap = null, Func<int>? coin = null, Func<int>? slapDraw = null )
	{
		this.create = create ?? throw new ArgumentNullException( nameof( create ) );
		this.timer = timer ?? throw new ArgumentNullException( nameof( timer ) );
		this.speechOn = speechOn ?? (() => GameOptions.Current.Advisor);
		this.say = say ?? (id => Presentation.SayResponse( id, level: null, GameLanguage.IsSelected ? GameLanguage.Current : null ));
		this.busy = busy ?? (() => advisor?.ResponseActive == true);
		this.speaking = speaking ?? (() => advisor?.IsSpeaking == true);
		this.playSlap = playSlap ?? (sound => GameAudio.PlaySpeechEffect( sound ));
		var random = coin == null || slapDraw == null ? new Random() : null;
		this.coin = coin ?? (() => random!.Next( 0x8000 ));
		this.slapDraw = slapDraw ?? (() => random!.Next( 0x8000 ));
	}

	/// <summary>
	/// A lobby frame: the advisor's update, then the lobby's tasks in registration order, the islands component (which sets the idle line) before the queue (which speaks).
	/// </summary>
	public void Update( LobbyAdvisorScreen screen )
	{
		if ( ended )
			return;
		this.screen = screen;
		if ( advisor != null )
		{
			advisor.Ambient = LobbyAdvisorAmbient.Of( screen );
			advisor.Update();
		}
		// [BIN:TPW-EXE:0x5E1830 islands update] every frame, with a current player: 0x5D61A0(0x18B for an odd draw, 0x18A for an even one, 0x15F90); the last frame's draw is the line the queue says
		Queue.SetIdle( (coin() & 1) != 0 ? LobbyAdvisorQueue.IdleLineOdd : LobbyAdvisorQueue.IdleLineEven, LobbyAdvisorQueue.IdleDelayMilliseconds );
		Queue.Step( unchecked((uint)timer()), busy(), Say );
	}

	// [BIN:TPW-EXE:0x599050 response player] nothing is said with the speech off (0x78D90C) or while a response is active (0xF7966C)
	private void Say( int id )
	{
		if ( !speechOn() || busy() )
			return;
		try
		{
			if ( !say( id ) )
				Log.Warning( $"Lobby advisor response {id} is not in {AdvisorResponses.RelativePath}." );
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or InvalidOperationException or NotSupportedException or OverflowException or UnauthorizedAccessException )
		{
			Log.Warning( $"Lobby advisor response {id} could not be played: {exception.Message}" );
		}
	}

	/// <summary>World pass: renders the shown advisor into its own image (see <see cref="Draw"/>).</summary>
	public void Render()
	{
		Image = !ended && advisor is { IsDrawn: true } shown ? shown.RenderImage( Screen.PixelSize ) : null;
		// one wrapper per image, not one per drawn frame
		if ( Image == null )
			imageTexture = null;
		else if ( !ReferenceEquals( imageTextureOf, Image ) )
		{
			imageTexture = UiTexture.Of( Image );
			imageTextureOf = Image;
		}
	}

	private UiTexture? imageTexture;
	private Veldrid.Texture? imageTextureOf;

	/// <summary>Draws the advisor image at its box, bottom right, over the lobby screens.</summary>
	public void Draw( UiContext context )
	{
		if ( Image == null || imageTexture == null )
			return;
		var (x, y, width, height) = Advisor.BoxRectangle( new Point2( context.Canvas.Width, context.Canvas.Height ) );
		context.Batch.AddQuad( imageTexture, new UiRect( x, y, width, height ), System.Numerics.Vector2.Zero, System.Numerics.Vector2.One, Veldrid.RgbaByte.White );
	}

	/// <summary>The front end is left: the queue's teardown cuts a response that plays off (with the slap that sounds while its speech does), then the advisor goes.</summary>
	public void End()
	{
		if ( ended )
			return;
		Queue.Teardown( busy(), CutOff );
		ended = true;
		Image = null;
	}

	// [BIN:TPW-EXE:0x5994E0 cut-off path] the speech is stopped, the advisor killed (0x429D60) and, while its speech sound was still playing, one of the three slaps (0x27F + rand() % 3, 0x5995A4) sounds
	private void CutOff()
	{
		var wasSpeaking = speaking();
		advisor?.Kill();
		if ( wasSpeaking )
			playSlap( AutomaticAdvisor.SlapSound( slapDraw() ) );
	}

	public void Dispose()
	{
		End();
		advisor?.Dispose();
	}
}

/// <summary>The lobby screens whose ambient the advisor is lit with.</summary>
internal enum LobbyAdvisorScreen
{
	/// <summary>The island view, and the enter sequence that follows it.</summary>
	Islands,
	/// <summary>The online globe and city views.</summary>
	Globe,
}

/// <summary>The ambient words of the lobby screens (0x879DD8-0x879DE4): alpha 1.0, then R, G, B as the byte of the colour over 255.</summary>
internal static class LobbyAdvisorAmbient
{
	// [BIN:TPW-EXE:0x5E1924 island ambient] the island view's enter sets 0x5D9230(1, 0x444444), as do 0x5E13A9 (the islands component's enter) and 0x5E2445 (the enter of the island screen of vtable 0x702DD0)
	public static readonly System.Numerics.Vector3 IslandScreens = new System.Numerics.Vector3( 0x44 ) / 255f;
	// [BIN:TPW-EXE:0x5DD078 globe ambient] the globe's enter 0x5DCFE0 sets 0x5D9230(1, 0x202020); the city view of vtable 0x702E20 has the globe's base class (0x5DC580)
	public static readonly System.Numerics.Vector3 Globe = new System.Numerics.Vector3( 0x20 ) / 255f;

	public static System.Numerics.Vector3 Of( LobbyAdvisorScreen screen ) => screen == LobbyAdvisorScreen.Globe ? Globe : IslandScreens;
}
