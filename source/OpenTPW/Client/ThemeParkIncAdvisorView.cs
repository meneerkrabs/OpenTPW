using System.Numerics;

namespace OpenTPW;

/// <summary>
/// Theme Park Inc's advisor on screen (docs/reverse/TPI-ADVISOR.md §4.4): the speech's model (the global advisor, model 0, or the level's, model 1) playing the clips of the presentation's
/// sequence with its mouth, drawn flat over the bottom right of the screen as Theme Park World's advisor is (<see cref="Advisor"/>), at Theme Park Inc's own place. It only reads the simulation
/// (<see cref="ThemeParkIncAdvisor"/>, <see cref="ThemeParkIncAdvisorPresentation"/>): nothing it does reaches the park, its saves or its hash. The models load the first time a speech shows
/// them; a model that cannot be read is logged once and never drawn.
/// </summary>
internal sealed class ThemeParkIncAdvisorView : IDisposable
{
	/// <summary>The model slots: 0 the global advisor (<c>Data\Global\Advisor</c>), 1 the level's (<c>advisor.wad</c>).</summary>
	public const int Models = 2;

	// [BIN:TPI-EXE:0x464E7F advisor placement] the model slot's matrix (0xA13AB0 + 0xB0 per slot, +8) is the identity with translation (0.75, -0.6, 0.9) (0x464E7F-0x464E8D; 0x464DB0 puts it
	// back there), where Theme Park World's 0x429BA0 has (0.6, -0.6, 0.2); the root scale (0.01125, 0.001, 0.015, 0x464FC3), the light (26.67, -26.67, 800, 0x464EE7) and the load flags
	// 0x50120 are Theme Park World's, so the model is drawn as Theme Park World's advisor is (Advisor.ModelTransform), further right and deeper
	internal static readonly Vector2 Centre = new( 0.75f, -0.6f );
	internal const float Depth = 0.9f;

	private readonly string? level;
	private readonly AdvisorRig?[] rigs = new AdvisorRig?[Models];
	private readonly bool[] attempted = new bool[Models];
	private readonly uint[]?[] lengths = new uint[]?[Models];
	private AdvisorThemeCostume? theme;
	private readonly AdvisorImageTarget target = new();
	// the speech whose clips the caches hold: its clip list (a new array for each speech), response and model
	private IReadOnlyList<int>? speechClips;
	private ThemeParkIncAdviceResponse? speechResponse;
	private readonly Dictionary<int, HashSet<int>> clipHidden = new();
	private readonly HashSet<int> hiddenScratch = new();
	private bool disposed;

	/// <param name="level">The running level (its advisor archive <c>/levels/&lt;level&gt;/advisor</c> and its theme costume).</param>
	public ThemeParkIncAdvisorView( string? level ) => this.level = level;

	/// <summary>The image the last <see cref="Render"/> drew, null while no advisor is shown.</summary>
	public Veldrid.Texture? Image { get; private set; }

	/// <summary>The mouth mesh the last <see cref="Render"/> drew (null for none), and its clip; read by the smoke.</summary>
	public string? LastMouth { get; private set; }
	public int LastClip { get; private set; }

	/// <summary>Whether a model is shown at advisor clock <paramref name="now"/>, and which clip plays (GPU-free; <see cref="Render"/> draws it).</summary>
	internal static ThemeParkIncAdvisorClip? Playing( ThemeParkIncAdvisorPresentation presentation, IReadOnlyList<uint>? lengths, uint now )
	{
		// [BIN:TPI-EXE:0x716405 hidden head] a hidden head moves both models out of sight (0x464E00: translation y -90.6) and the clip-start update leaves them there (0x715EF0)
		if ( !presentation.ModelShown || presentation.HeadHidden || lengths == null )
			return null;
		var elapsed = unchecked( now - presentation.Start );
		// a frame drawn before the speech's tick reached its start shows the speech that is not there yet as nothing
		if ( unchecked( (int)elapsed ) < 0 )
			return null;
		uint? ended = presentation.EndedAt is { } end ? unchecked( end - presentation.Start ) : null;
		return ThemeParkIncAdvisorTimeline.At( presentation.Clips, presentation.Bits, lengths, elapsed, ended );
	}

	/// <summary>
	/// World pass: draws the advisor of <paramref name="advisor"/> into its own image as a frame <paramref name="turnFraction"/> of the way through the running world tick shows it
	/// (<see cref="ThemeParkIncAdvisor.DisplayMilliseconds"/>), for an output of <paramref name="output"/> pixels; no image while none is shown.
	/// </summary>
	public void Render( ThemeParkIncAdvisor? advisor, double turnFraction, Point2 output )
	{
		Image = null;
		LastMouth = null;
		LastClip = 0;
		if ( disposed || advisor == null )
			return;
		var presentation = advisor.Presentation;
		if ( !ReferenceEquals( presentation.Clips, speechClips ) )
			StartSpeech( presentation );
		if ( speechResponse is not { } response || !presentation.ModelShown || presentation.HeadHidden )
			return;
		var model = response.Model == 1 ? 1 : 0;
		if ( Rig( model ) is not { } rig )
			return;
		var now = advisor.DisplayMilliseconds( turnFraction );
		if ( Playing( presentation, lengths[model], now ) is not { } playing )
			return;
		// [EXT:tpi-capability-gates] a clip's bits 0x20000-0x200000 start screen effects at its start (0x715FEA: 0x7BA870, 0x7BA960, 0x7BAA30, Theme Park World's effect groups) and the
		// award selectors swap textures (0x715CF0); no response OpenTPW plays has either, and neither is drawn
		if ( !clipHidden.TryGetValue( playing.Index, out var hidden ) )
			clipHidden[playing.Index] = hidden = ThemeParkIncAdvisorAccessories.ClipStartHidden( rig.Nodes, rig.Clip( playing.Clip ), response.Accessory, response.SecondAccessory,
				theme ??= AdvisorThemeCostume.Load( FileSystem, level ), playing.Bits, presentation.HeadSuppressed );
		var worlds = rig.Pose( playing.Clip, playing.Tick );
		hiddenScratch.Clear();
		hiddenScratch.UnionWith( hidden );
		AdvisorAccessories.ApplyToggles( rig.Nodes, hiddenScratch, rig.Clip( playing.Clip ), playing.Tick );
		var mouth = ThemeParkIncAdvisorAccessories.MouthMesh( presentation.MouthAt( now ), playing.Bits );
		var (_, _, width, height) = BoxRectangle( output );
		target.Begin( width, height );
		Advisor.DrawRig( rig, Advisor.ModelTransform( output, Depth ), worlds, hiddenScratch, playing.Clip, rig.SampledTick, mouth, null );
		Image = target.End();
		(LastMouth, LastClip) = (mouth, playing.Clip);
	}

	/// <summary>
	/// Draws the image at its <see cref="BoxRectangle"/> after the Theme Park Inc HUD. The original draws the model in its scene's model pass over the park; OpenTPW's text HUD stands in for
	/// the in-game interface (<c>[EXT:tpi-text-hud]</c>), so the advisor is drawn over it, as Theme Park World's advisor is over its HUD (<see cref="AutomaticAdvisor.Draw"/>).
	/// </summary>
	public void Draw( UI.Original.UiContext context )
	{
		if ( Image == null )
			return;
		var (x, y, width, height) = BoxRectangle( new Point2( context.Canvas.Width, context.Canvas.Height ) );
		context.Batch.AddQuad( UI.Original.UiTexture.Of( Image ), new UI.Original.UiRect( x, y, width, height ), Vector2.Zero, Vector2.One, Veldrid.RgbaByte.White );
	}

	/// <summary>
	/// The pixel rectangle (y down) of a <paramref name="target"/> the advisor is drawn in: <see cref="Centre"/> ± <see cref="Advisor.BoxHalfSide"/>. Theme Park Inc's centre is further right
	/// than Theme Park World's, so the box reaches past the right edge (the model itself ends about there); it is not cut, so the model keeps its scale.
	/// </summary>
	internal static (int X, int Y, int Width, int Height) BoxRectangle( Point2 target )
	{
		static int Pixel( float clip, int size ) => (int)MathF.Round( (clip + 1f) * 0.5f * size );
		var left = Pixel( Centre.X - Advisor.BoxHalfSide, target.X );
		var right = Pixel( Centre.X + Advisor.BoxHalfSide, target.X );
		var top = Pixel( -(Centre.Y + Advisor.BoxHalfSide), target.Y );
		var bottom = Pixel( -(Centre.Y - Advisor.BoxHalfSide), target.Y );
		return (left, top, Math.Max( 1, right - left ), Math.Max( 1, bottom - top ));
	}

	// a new speech: its response's row and the clip-start nodes are found once, not per frame
	private void StartSpeech( ThemeParkIncAdvisorPresentation presentation )
	{
		speechClips = presentation.Clips;
		speechResponse = ThemeParkIncAdviceResponse.For( presentation.Response );
		clipHidden.Clear();
	}

	/// <summary>The model of slot <paramref name="model"/>, read the first time it is asked for; null when it cannot be read (logged once).</summary>
	// [BIN:TPI-EXE:0x4B0170 advisor models] slot 0 is the global advisor ("Data\Global\Advisor", "Advisor.md2"), slot 1 the level's advisor archive; a speech plays its clips on the slot of its
	// response's +0x10 low word (0xBEF07C), and a slot without a model plays nothing (0x465330 with no instance)
	private AdvisorRig? Rig( int model )
	{
		if ( attempted[model] )
			return rigs[model];
		attempted[model] = true;
		try
		{
			using ( FrameMarks.Measure( "tpiAdvisor.loadModel" ) )
			{
				var rig = model == 1 ? level == null ? null : AdvisorRig.LoadLevel( FileSystem, level ) : AdvisorRig.LoadGlobalModel();
				if ( rig == null )
				{
					Log.Warning( $"Theme Park Inc advisor: level {level} has no advisor model; its speeches show none." );
					return null;
				}
				// the clips past Theme Park World's 20 (a level advisor's 21-26) are read now too, not in a later frame
				lengths[model] = ThemeParkIncAdvisorTimeline.Lengths( rig.Clip );
				for ( var clip = AdvisorClipDurations.Count + 1; clip <= ThemeParkIncAdvisorTimeline.MaxClip; clip++ )
					rig.Pose( clip, 0 );
				rigs[model] = rig;
			}
		}
		catch ( Exception exception ) when ( exception is IOException or InvalidDataException or NotSupportedException or UnauthorizedAccessException )
		{
			Log.Warning( $"Theme Park Inc advisor: model {model} could not be loaded ({exception.Message}); its speeches show no advisor." );
		}
		return rigs[model];
	}

	public void Dispose()
	{
		if ( disposed )
			return;
		disposed = true;
		Image = null;
		target.Dispose();
		foreach ( var rig in rigs )
			rig?.Dispose();
		Array.Clear( rigs );
	}
}
