using OpenTPW.UI.Original;
using Veldrid;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>The research lab laid out from the Mac research table at <c>data:0x4c652</c>.</summary>
public sealed partial class ParkHud
{
	// [DATA:Mac research table 0x4c652] Visual row order differs from ResearchCategories[n] order.
	private static readonly (ResearchCategory Category, string Icon, UiRect Panel, UiRect Slider)[] ResearchRows =
	{
		(ResearchCategory.Ride, "h_ride", Rect( 785, 158, 1830, 308 ), Rect( 472, 178, 769, 288 )),
		(ResearchCategory.Upgrade, "h_upgrade", Rect( 785, 318, 1830, 469 ), Rect( 472, 338, 769, 449 )),
		(ResearchCategory.Shop, "h_shop", Rect( 785, 479, 1830, 630 ), Rect( 472, 499, 769, 610 )),
		(ResearchCategory.Sideshow, "h_show", Rect( 785, 640, 1830, 791 ), Rect( 472, 660, 769, 771 )),
		(ResearchCategory.Feature, "h_feature", Rect( 785, 801, 1830, 951 ), Rect( 472, 820, 769, 931 )),
	};
	// [BIN:TPW-EXE:0x35299c research panel control table (file offset)] the bottom edge of each row's pipe child (its tops are the panels' tops + 17)
	private static readonly int[] ResearchPipeBottoms = { 294, 455, 615, 776, 937 };
	private static readonly (UiRect Icon, UiRect Previous, UiRect Next, UiRect Value, UiRect Fluid, UiRect Track, UiRect Knob)[] ResearchCells =
	{
		(Rect( 816, 182, 919, 284 ), Rect( 924, 191, 1007, 275 ), Rect( 1012, 191, 1096, 275 ), Rect( 1132, 179, 1497, 284 ), Rect( 1555, 196, 1712, 264 ), Rect( 532, 199, 714, 268 ), Rect( 579, 199, 646, 267 )),
		(Rect( 816, 343, 919, 445 ), Rect( 924, 352, 1007, 435 ), Rect( 1012, 352, 1096, 435 ), Rect( 1132, 340, 1497, 445 ), Rect( 1555, 357, 1712, 425 ), Rect( 532, 355, 714, 423 ), Rect( 579, 360, 646, 427 )),
		(Rect( 816, 503, 919, 606 ), Rect( 924, 513, 1007, 596 ), Rect( 1012, 513, 1096, 596 ), Rect( 1132, 501, 1497, 606 ), Rect( 1555, 518, 1712, 586 ), Rect( 532, 517, 714, 585 ), Rect( 579, 521, 646, 588 )),
		(Rect( 816, 664, 919, 766 ), Rect( 924, 674, 1007, 757 ), Rect( 1012, 674, 1096, 757 ), Rect( 1132, 661, 1497, 767 ), Rect( 1555, 679, 1712, 746 ), Rect( 538, 678, 720, 746 ), Rect( 584, 683, 652, 750 )),
		(Rect( 816, 825, 919, 927 ), Rect( 924, 834, 1007, 917 ), Rect( 1012, 834, 1096, 917 ), Rect( 1132, 822, 1497, 927 ), Rect( 1555, 839, 1712, 907 ), Rect( 532, 838, 714, 907 ), Rect( 579, 842, 646, 909 )),
	};

	private UiScreen? researchScreen;
	public bool ResearchWindowOpen => researchScreen != null && Stack.Screens.Contains( researchScreen );
	private ParkEconomy? ResearchEconomy => Status.EconomySource as ParkEconomy;

	public void OpenResearchWindow()
	{
		if ( ResearchWindowOpen || RejectReadOnlyAction() )
			return;
		// [BIN:TPW-EXE:0x4aa480 research window refusals] the builder returns while the pause menu is open (0x48C8D0) or the window exists, shows the 0x47F020 box UITEXT 0x1D4 ("Research is automatic in Instant Action mode.") in game
		// type 2, else the box UITEXT 0x1D3 ("You need to hire some scientists before you can carry out any research!") while the researcher list (world +0x1DA742) is empty, and builds the window only past both; the boxes carry the cross
		// button and no action (0x47F020(.., 2, 0, ..)) and pause the offline game like every message box (0x47F26C)
		if ( ResearchEconomy is { Features.ResearchPanel: false } )
		{
			Stack.Push( ResearchRefusalBox( UIStrings.ResearchInstantAction ) );
			return;
		}
		if ( ResearchEconomy is { } economy && !economy.Staff.Members.Any( member => member.Type == StaffType.Researcher ) )
		{
			Stack.Push( ResearchRefusalBox( UIStrings.NeedToHireScientists ) );
			return;
		}
		SetInfoArm( false );
		level.CellTool.Cancel();
		LeaveBuildTool();
		// The original constructs new controls on each open (0x4aa480); a closed screen must not retain mouse capture.
		researchScreen = CreateResearchScreen();
		ResetResearchFluids();
		Stack.Push( researchScreen );
		researchScreen.Focus( researchScreen.Find( "exit" ) );
		// [BIN:TPW-EXE:0x4a1737 drawer open] opening the map/research drawer (message 0xA) plays cat_ui event 0x1C (the drawer's research button help is 'Click for research (R)')
		GameAudio.PlayUi( GameAudio.DrawerOpenEvent );
		StartResearchSounds();
		((ResearchWorkloadKnob)researchScreen.Find( "workloadKnob" )!).Reset();
	}

	/// <summary>The name of the research refusal boxes (they pause the offline park while open, as every 0x47F020 box does).</summary>
	public const string ResearchRefusedScreen = "researchRefused";

	private UiScreen ResearchRefusalBox( UIStrings text )
	{
		UiScreen? screen = null;
		screen = UiDialogs.OriginalMessageBox( ResearchRefusedScreen, () => strings[text],
			() => Stack.RemoveWhere( candidate => ReferenceEquals( candidate, screen ) ),
			(UiDialogs.MessageBoxButton.Exit, null, null) );
		return screen;
	}

	public void CloseResearchWindow()
	{
		if ( researchScreen == null || !ResearchWindowOpen )
			return;
		while ( Stack.Top != researchScreen && Stack.Screens.Count > 1 )
			Stack.Pop();
		if ( Stack.Top == researchScreen )
			Stack.Pop();
		StopResearchSounds();
		// [BIN:TPW-EXE:0x4a186f drawer close] a close plays cat_ui 0x1C when it starts and 0xC2 when the slide finishes (0x4a1947); OpenTPW has no slide, so both play at once
		// [EXT:drawer-window] the research screen is a window that opens and closes at once where the original slides a drawer out of the main panel (docs/AUDIO.md; UI register)
		GameAudio.PlayDrawerClose();
	}

	/// <summary>The two <c>cat_ui</c> loops the open research window plays (154 and 155) and the sentence an effort slider plays while it is held (156).</summary>
	public const uint ResearchHumEvent = 154, ResearchSecondHumEvent = 155, ResearchSliderEvent = 156;
	private SoundSentence? researchHum, researchSecondHum, researchSliderSound;

	/// <summary>The loops start with the window; they keep running until it closes.</summary>
	// [BIN:TPW-EXE:0x4aa4f5 research window sounds] building the window (0x4aa480, after the money, instant-research and panel checks) starts cat_ui 0x9A and 0x9B through the play helper 0x485aa0 and keeps both handles (0x7cc1d0, 0x7cc1cc); its destroy message 0x14 stops them with 0x51c2c0 (0x4a96b0)
	private void StartResearchSounds()
	{
		StopResearchSounds();
		researchHum = GameAudio.StartUiSentence( ResearchHumEvent );
		researchSecondHum = GameAudio.StartUiSentence( ResearchSecondHumEvent );
	}

	private void StopResearchSounds()
	{
		researchHum?.Stop();
		researchSecondHum?.Stop();
		researchSliderSound?.Stop();
		researchHum = researchSecondHum = researchSliderSound = null;
	}

	/// <summary>The workload knob's value as the hum's parameter: the clamped knob value times 100 over 1024, truncated toward zero.</summary>
	// [BIN:TPW-EXE:0x4a97ad workload knob sound] the master slider's change (message 0x800 from 0xd2d10, after the limit clamp) sets parameters 0xC and 0xD of both loops to value * 100 / 1024 (the signed shift of 0x4a98d5-0x4a98dd) through 0x51bc40
	internal static int ResearchHumParameter( int knobValue ) => knobValue * 100 / 1024;

	/// <summary>The hum's parameter the workload knob set last (null before any), for the tests: the hum has no sentence without the game's audio.</summary>
	internal int? LastResearchHum { get; private set; }

	private void SetResearchHum( int knobValue )
	{
		var parameter = ResearchHumParameter( knobValue );
		LastResearchHum = parameter;
		foreach ( var sentence in new[] { researchHum, researchSecondHum } )
		{
			sentence?.SetParameter( 0xC, parameter );
			sentence?.SetParameter( 0xD, parameter );
		}
	}

	/// <summary>An effort slider's thumb was pressed: its sentence starts with the slider's value as parameters 0xB and 0xA.</summary>
	// [BIN:TPW-EXE:0x4a9a53 effort slider sound] message 0x801 from a slider other than the master starts cat_ui 0x9C (0x4a9b87, the helper keeps the handle at 0x7cc1d4) and sets its parameters 0xB and 0xA to the value; 0x800 sets them again as the value changes (0x4a9a2c); 0x802 stops the sentence (0x4a9a5c) and clears the handle
	private void StartResearchSlider( int value )
	{
		researchSliderSound?.Stop();
		researchSliderSound = GameAudio.StartUiSentence( ResearchSliderEvent );
		SetResearchSlider( value );
	}

	private void SetResearchSlider( int value )
	{
		researchSliderSound?.SetParameter( 0xB, value );
		researchSliderSound?.SetParameter( 0xA, value );
	}

	private void StopResearchSlider()
	{
		researchSliderSound?.Stop();
		researchSliderSound = null;
	}

	private string ResearchItemName( ResearchCategory category )
	{
		if ( ResearchEconomy is not { } economy )
			return "";
		var item = economy.Research.Current( category );
		if ( item == null )
			return strings[UIStrings.AllItemsResearched];
		if ( level.Objects.Catalog.Find( item.InfoId ) is { } entry )
			return EntryName( entry );
		return economy.Catalog.TryGet( item.InfoId, out var info ) ? info.Name : "";
	}

	// The five panels' fluids in ResearchCategories[n] order, rebuilt whenever the window opens (the original builds the window anew, 0x4aa480).
	private readonly ResearchFluid[] researchFluids = Enumerable.Range( 0, 5 ).Select( _ => new ResearchFluid() ).ToArray();
	private string? researchFluidKey;
	// [EXT:frame-driven-at-60hz] one fluid step per reference frame, where the original steps once per rendered frame (message 0x1e, see ReferenceFrameClock)
	private readonly ReferenceFrameClock researchFluidClock = new( 0.25f );

	internal ResearchFluid ResearchFluidOf( ResearchCategory category ) => researchFluids[(int)category];

	private void ResetResearchFluids()
	{
		for ( var index = 0; index < researchFluids.Length; index++ )
			researchFluids[index] = new ResearchFluid();
		researchFluidKey = null;
		researchFluidClock.Reset();
		RefreshResearchFluids();
	}

	/// <summary>
	/// Applies every category's rate when the window opens and whenever a current project, an effort or the workload changes:
	/// the original runs its refresh 0x4a9c70 when the window is built (0x4aa480), on the effort and workload sliders (message 0x800),
	/// after the cursor arrows (0x4aa0c0 → 0x4aa120) and when a project completes (message 0x12, 0x4aaec0).
	/// </summary>
	private void RefreshResearchFluids()
	{
		if ( ResearchEconomy is not { } economy )
			return;
		var research = economy.Research;
		var key = string.Join( ',', Enum.GetValues<ResearchCategory>().Select( category => research.Current( category ) is { } item ? $"{item.InfoId}:{item.Level}" : "-" )
			.Concat( research.Effort.Select( value => value.ToString( System.Globalization.CultureInfo.InvariantCulture ) ) ).Append( research.Workload.ToString( System.Globalization.CultureInfo.InvariantCulture ) ) );
		if ( key == researchFluidKey )
			return;
		researchFluidKey = key;
		foreach ( var category in Enum.GetValues<ResearchCategory>() )
			researchFluids[(int)category].Apply( ResearchFluidRate( economy, category ) );
	}

	internal static int ResearchFluidRate( ParkEconomy economy, ResearchCategory category )
	{
		var settings = economy.Settings;
		var abilitySum = economy.Staff.OfType( StaffType.Researcher ).Sum( member => settings.ResearchAbility[Math.Clamp( member.Grade, 0, BalanceSettings.GradeCount - 1 )] );
		return ResearchFluid.Rate( economy.Research.Effort, (int)category, economy.Research.Workload, economy.Research.Current( category ) != null, abilitySum,
			settings[StaffType.Researcher].MaximumInPark, settings.ResearchAbility[BalanceSettings.GradeCount - 1] );
	}

	private void StepResearchFluids( float delta )
	{
		RefreshResearchFluids();
		// [BIN:TPW-EXE:0x4a9fc0 fluid tick] each panel's fluid control steps on every message 0x1e, which the UI sends once per rendered frame (0x48a580 -> 0x658f6c -> 0x658f97)
		// [EXT:frame-driven-at-60hz] OpenTPW steps the fluids at the 60 Hz reference rate (one frame of the original's frame-driven timing), whatever its own frame rate
		ResearchFluid.StepFrames( researchFluidClock, researchFluids, delta );
	}

	/// <summary>Sets the lab's workload, clamped to the knob's 0..100 as the lab clamps it (<see cref="SetResearchWorkloadCommand"/>).</summary>
	public static bool TrySetResearchWorkload( Level level, int workload ) =>
		level.Commands.Issue( new SetResearchWorkloadCommand( Math.Clamp( workload, 0, 100 ) ) ).Accepted;

	/// <summary>Steps a category's current project one place (<see cref="MoveResearchCommand"/>).</summary>
	public static bool TryMoveResearchCurrent( Level level, ResearchCategory category, int direction ) =>
		level.Commands.Issue( new MoveResearchCommand( category, direction ) ).Accepted;

	/// <summary>
	/// Sets a category's effort, clamped to 0..100 as the lab clamps it (<see cref="SetResearchEffortCommand"/>); [EXT:online-coop-commands] a guest's goes to the host, the latest of a drag
	/// once (C4b).
	/// </summary>
	private void SetResearchEffort( ResearchCategory category, int effort ) =>
		IssueManagement( ManagementTarget.Research, ManagementKnob.Effort( category ), new SetResearchEffortCommand( category, Math.Clamp( effort, 0, 100 ) ) );

	/// <summary>The lab's workload set from the knob (<see cref="TrySetResearchWorkload"/>); [EXT:online-coop-commands] a guest's goes to the host, the latest of a drag once (C4b).</summary>
	private bool SetResearchWorkload( int workload ) =>
		IssueManagement( ManagementTarget.Research, ManagementKnob.Workload, new SetResearchWorkloadCommand( Math.Clamp( workload, 0, 100 ) ) );

	/// <summary>
	/// A cursor arrow's click (<see cref="TryMoveResearchCurrent"/>): [EXT:online-coop-commands] a guest's move names the project its window shows, and while one of the category waits for the
	/// host the arrows take no click, so a second quick click never skips a project (C4b).
	/// </summary>
	private void MoveResearchCurrent( ResearchCategory category, int direction )
	{
		if ( GuestManagement == null )
		{
			TryMoveResearchCurrent( level, category, direction );
			return;
		}
		if ( ResearchMoveWaits( category ) || ResearchEconomy?.Research.Current( category ) is not { } current )
			return;
		IssueManagement( ManagementTarget.Research, null, new MoveResearchCommand( category, direction, current.InfoId, current.Level ) );
	}

	private UiScreen CreateResearchScreen()
	{
		var screen = new UiScreen( "research" );
		void Model( string id, string model, UiRect bounds ) => screen.Add( new UiModelImage { Id = id, Model = model, Bounds = bounds, Anchor = UiAnchor.Center } );
		// [DATA:Mac research table 0x4c652] Root, title, five panels and the main workload track.
		Model( "window", "research_wind", Rect( 186, 30, 2018, 1032 ) );
		screen.Add( new UiLabel { Id = "title", Text = () => strings[UIStrings.ResearchLab], Font = fonts => fonts.Title,
			Shadow = true, Color = UiColors.Title, Align = UiAlign.Center, Fit = true, Bounds = Rect( 866, 67, 1338, 146 ), Anchor = UiAnchor.Center } );
		Model( "workload", "effort", Rect( 234, 148, 464, 968 ) );
		var workloadKnob = screen.Add( new ResearchWorkloadKnob( this ) { Id = "workloadKnob",
			Bounds = Rect( 267, 220, 334, 701 ), Anchor = UiAnchor.Center } );
		screen.Add( new UiModelImage { Id = "cameo", Model = "cameo",
			Frame = () => ResearchCameoFrame( ResearchEconomy?.Research.Workload ?? MinimumKnobWorkload ),
			Bounds = Rect( 262, 760, 438, 936 ), Anchor = UiAnchor.Center } );
		screen.Add( new ResearchCapacityGauge { Id = "capacityGauge", Value = () => workloadKnob.Limit,
			Bounds = Rect( 354, 238, 422, 683 ), Anchor = UiAnchor.Center } );
		for ( var row = 0; row < ResearchRows.Length; row++ )
		{
			var index = row;
			var (category, icon, panel, slider) = ResearchRows[row];
			var cells = ResearchCells[row];
			Model( $"panel{index}", "research", panel );
			Model( $"icon{index}", icon, cells.Icon );
			// [EXTENSION:UI] Cursor arrows select eligible research targets using the economy model.
			screen.Add( new UiButton { Id = $"previous{index}", Model = "b_sleft", Enabled = false,
				Clicked = () => MoveResearchCurrent( category, -1 ),
				Bounds = cells.Previous, Anchor = UiAnchor.Center } );
			screen.Add( new UiButton { Id = $"next{index}", Model = "b_sright", Enabled = false,
				Clicked = () => MoveResearchCurrent( category, 1 ),
				Bounds = cells.Next, Anchor = UiAnchor.Center } );
			screen.Add( new UiLabel { Id = $"item{index}", Text = () => ResearchItemName( category ), Font = fonts => fonts.Label,
				Color = UiColors.Text, Align = UiAlign.Center, Fit = true,
				Bounds = cells.Value, Anchor = UiAnchor.Center } );
			screen.Add( new ResearchFluidCell( this, category ) { Id = $"fluid{index}",
				Bounds = cells.Fluid, Anchor = UiAnchor.Center } );
			// [BIN:TPW-EXE:0x0035299c research panel control table (file offset)] each category parent control holds a second model child besides the fluid: the pipe (root name "pipe", res_pipe.md2), a rect 1497..1804 wide that starts 17 units below the panel's top in every row (y 175..294, 335..455, 496..615, 657..776, 818..937 for the five rows); it frames the fluid with the orange end caps the original's tube shows (720p video 34:30) and is drawn over the fluid
			Model( $"pipe{index}", "pipe", Rect( 1497, panel.Y + 17, 1804, ResearchPipeBottoms[row] ) );
			Model( $"effortTrack{index}", "effortslidea", slider );
			// [BIN:TPW-EXE:0x4aa6bd effort sliders] SetRange(0,100) on the generic slider class: integer value 0..100, a drag position is truncated (0x66afc9), the ball runs linearly across the table's slider rectangle
			screen.Add( new UiSlider { Id = $"effort{index}", Track = cells.Track,
				KnobSize = new NVector2( cells.Knob.Width, cells.Knob.Height ), KnobTop = cells.Knob.Y,
				// [EXT:online-coop-commands] a guest's effort not back from the host yet is drawn as asked, and the slider's steps and change test count from it (C4b)
				Steps = () => 101, Truncates = true, Value = () => DrawnResearchEffort( category ),
				// [EXT:online-coop-commands] a guest's gesture asks for each step once, and nothing at its release (C4b-2)
				AsksOncePerStep = () => GuestManagement != null,
				Changed = value => { SetResearchEffort( category, value ); SetResearchSlider( value ); },
				Grabbed = StartResearchSlider, Released = StopResearchSlider,
				Bounds = slider, Anchor = UiAnchor.Center } );
		}
		// [DATA:Mac research table 0x4c652:-1] b_okay exits; help 1.
		screen.Add( new UiButton { Id = "exit", Model = "b_okay", Help = strings.Help( 1 ), Clicked = CloseResearchWindow,
			Bounds = Rect( 1889, 844, 1972, 927 ), Anchor = UiAnchor.Center } );
		screen.Updating = context =>
		{
			StepResearchFluids( context.Delta );
			var editable = !level.IsReadOnlyVisit && ResearchEconomy is { Features: { ResearchPanel: true } };
			workloadKnob.Enabled = editable;
			if ( !editable ) workloadKnob.CancelInteraction();
			workloadKnob.RefreshStaffing();
			workloadKnob.FollowPark();
			foreach ( var index in Enumerable.Range( 0, ResearchRows.Length ) )
				{
				if ( screen.Find( $"effort{index}" ) is UiSlider effort ) effort.Enabled = editable;
				var canSelect = editable && ResearchEconomy!.Research.SelectableItems( ResearchRows[index].Category ).Count > 1;
				if ( screen.Find( $"previous{index}" ) is UiButton previous ) previous.Enabled = canSelect;
				if ( screen.Find( $"next{index}" ) is UiButton next ) next.Enabled = canSelect;
			}
		};
		screen.Removed = () =>
		{
			workloadKnob.CancelInteraction();
			if ( ReferenceEquals( researchScreen, screen ) )
			{
				StopResearchSounds();
				researchScreen = null;
			}
		};
		screen.Back = CloseResearchWindow;
		return screen;
	}

	private sealed class ResearchFluidCell : UiElement
	{
		private readonly ParkHud owner;
		private readonly ResearchCategory category;
		public ResearchFluidCell( ParkHud owner, ResearchCategory category ) { this.owner = owner; this.category = category; }
		// [BIN:TPW-EXE:0x0065d3a3 control frame] the fluid child 0xd2d06 draws res_fluid at the frame its panel's animation last set (ResearchFluid); there is no progress fill
		public override void Draw( UiContext context, bool focused, bool pressed ) =>
			context.DrawModel( "fluid", owner.ResearchFluidOf( category ).Frame, ScreenRect( context.Canvas ) );
	}

	/// <summary>The workload knob's slider range: its value runs from 1023 at the top to 0 at the bottom (tp.exe 0x4aa634).</summary>
	public const int WorkloadKnobRange = 1023;
	/// <summary>The lowest and highest research workload the knob sets (tp.exe 0x4a96b0: 70 + 30 × value / limit).</summary>
	public const int MinimumKnobWorkload = 70, MaximumKnobWorkload = 100;

	/// <summary>
	/// The highest knob value the researchers allow: ((100 - c) / 3 + c) × 1024 / (MaxResearchersInPark × the grade-4 ResearchAbility), c being the summed
	/// ResearchAbility of the park's researchers, in integers.
	/// </summary>
	// [BIN:TPW-EXE:0x4aac80 research window] the builder stores 0x4c9ed0 (MaxResearchersInPark 0x7852f8 × ResearcherConstsPerGrade[4].ResearchAbility 0x78548c) to 0x7cc1b4 and
	// 0x4c9e60 (the researchers' ResearchAbility by grade +0x1e4, summed) to 0x7cc1c8, sets child 0xd2d12 to the limit ((100 - c) / 3 + c) × 1024 / 0x7cc1b4 and the knob 0xd2d10 to
	// (workload - 70) × limit / 30 (0x4aacce-0x4aad3e); message 0x1e repeats this when the sum changes (0x4a97e6)
	public static int WorkloadKnobLimit( int abilitySum, int maximumResearchers, int topAbility )
	{
		var divisor = maximumResearchers * topAbility;
		return divisor <= 0 ? WorkloadKnobRange : ((100 - abilitySum) / 3 + abilitySum) * 1024 / divisor;
	}

	/// <summary>The workload a knob value sets: a value above the limit is pulled back to it, then 70 + 30 × value / limit.</summary>
	// [BIN:TPW-EXE:0x4a985b workload knob] on a change (message 0x800 from 0xd2d10) a value above the limit is set back to the limit and the workload becomes 70 + value × 30 / limit (0x503900)
	public static int WorkloadFromKnob( int value, int limit )
	{
		if ( limit <= 0 )
			return MaximumKnobWorkload;
		value = Math.Clamp( value, 0, Math.Min( limit, WorkloadKnobRange ) );
		return MinimumKnobWorkload + value * 30 / limit;
	}

	/// <summary>The knob value that shows <paramref name="workload"/>: (workload - 70) × limit / 30 within the slider's range.</summary>
	public static int KnobFromWorkload( int workload, int limit ) => Math.Clamp( (workload - MinimumKnobWorkload) * limit / 30, 0, WorkloadKnobRange );

	/// <summary>The knob ball's top in table units for a knob value: the slider class's top + (1023 - value) × (481 - 67) / 1023.</summary>
	// [BIN:TPW-EXE:0x66b088 slider ball] the ball's top is top + (min - value) × (height - ball height) / (min - max) in integers; the knob's range is min 1023, max 0 (0x66b47d at 0x4aa63a)
	public static float WorkloadKnobTop( int value ) => 220 + (WorkloadKnobRange - Math.Clamp( value, 0, WorkloadKnobRange )) * (701 - 220 - 67) / WorkloadKnobRange;

	/// <summary>The knob value for a ball centred at the authored height <paramref name="y"/>: the distance of the ball's bottom above the track's, truncated.</summary>
	public static int KnobValueAt( float y )
	{
		var above = Math.Clamp( 701 - (y + 67 / 2f), 0, 701 - 220 - 67 );
		return (int)MathF.Floor( above * WorkloadKnobRange / (701 - 220 - 67) + 1e-4f );
	}

	/// <summary>The ball's height in table units (the b_scroller model, 67).</summary>
	public const float KnobBallHeight = 67;

	/// <summary>The ball's centre after the pointer moved from <paramref name="from"/> to <paramref name="to"/>: moved by the difference, held inside the track.</summary>
	public static float DragBall( float centre, float from, float to ) =>
		Math.Clamp( centre + (to - from), 220 + KnobBallHeight / 2f, 701 - KnobBallHeight / 2f );

	/// <summary>The nine cameo states of child 0xd2d11 (0x4a9a5e): trunc(2 × (workload - 70) / 7).</summary>
	internal static int ResearchCameoFrame( int workload ) => 2 * (Math.Clamp( workload, MinimumKnobWorkload, MaximumKnobWorkload ) - MinimumKnobWorkload) / 7;

	private (int Ability, int Limit) WorkloadStaffing()
	{
		if ( ResearchEconomy is not { } economy )
			return (0, WorkloadKnobRange);
		var abilities = economy.Settings.ResearchAbility;
		var sum = economy.Staff.OfType( StaffType.Researcher ).Sum( member => abilities[Math.Clamp( member.Grade, 0, abilities.Count - 1 )] );
		return (sum, WorkloadKnobLimit( sum, economy.Settings[StaffType.Researcher].MaximumInPark, abilities[BalanceSettings.GradeCount - 1] ));
	}

	/// <summary>Research child 0xd2d12: the staffing limit fills a clipped vertical cylinder from the bottom.</summary>
	internal sealed class ResearchCapacityGauge : UiElement
	{
		internal const string ArtPath = "/ui/textures/effortguage.wct";
		internal Func<int> Value { get; init; } = () => 0;
		public override void Draw( UiContext context, bool focused, bool pressed ) => DrawGauge( context.Batch, ScreenRect( context.Canvas ), Value() );

		internal static void DrawGauge( UiBatch batch, UiRect bounds, int value ) => UiVerticalGauge.Draw( batch, bounds, value, ArtPath );
	}

	internal sealed class ResearchWorkloadKnob : UiElement, IUiDragTarget, IUiDragStart, IUiCapturedClickTarget
	{
		private readonly ParkHud owner;
		public ResearchWorkloadKnob( ParkHud owner ) => this.owner = owner;
		public override bool Focusable => Visible && Enabled;
		private bool Editable => Visible && Enabled && !owner.level.IsReadOnlyVisit && owner.ResearchEconomy is { Features.ResearchPanel: true };
		internal int Value { get; private set; }
		internal int Limit { get; private set; } = WorkloadKnobRange;
		internal float BallTop { get; private set; } = WorkloadKnobTop( 0 );
		internal UiRect BallRect => Rect( 267, BallTop, 334, BallTop + KnobBallHeight );
		private int ability;
		/// <summary>[EXT:online-coop-commands] The workload the knob stands for: what its player last asked for, or the workload it last projected (C4b).</summary>
		private int shownWorkload = -1;
		private enum PressMode { None, Page, Grab }
		private PressMode pressMode;
		private bool releasedPage;
		private float pressCentre;
		private NVector2 pressPoint, lastPoint;
		private static readonly UiRect Track = Rect( 273, 220, 330, 701 );

		// [BIN:TPW-EXE:0x4aacce initial workload] new slider value 0; project the saved workload through SetPos, which only notifies when its raw value changes.
		internal void Reset()
		{
			CancelInteraction();
			Value = 0;
			// [APPROX:UI-064] raw-zero startup keeps the existing host-normalized bottom (634); native construction can retain authored top399 when SetPos is unchanged, but later startup normalization is not fully traced.
			BallTop = WorkloadKnobTop( Value );
			(ability, Limit) = owner.WorkloadStaffing();
			SetValue( KnobFromWorkload( owner.DrawnResearchWorkload, Limit ), writeWorkload: WritesProjection );
			shownWorkload = owner.DrawnResearchWorkload;
		}

		/// <summary>
		/// Whether the knob's projection of the lab's workload writes it back, as the original's SetPos notifies its change handler; [EXT:online-coop-commands] a guest's does not: its
		/// window's opening would be a request the guest never made.
		/// </summary>
		private bool WritesProjection => !owner.level.Commands.SendsRequests;

		// [BIN:TPW-EXE:0x4a97d4 staffing refresh] only a changed ability sum reprojects workload; ordinary frames, efforts and targets leave the raw value alone.
		internal void RefreshStaffing()
		{
			var staffing = owner.WorkloadStaffing();
			if ( staffing.Ability == ability ) return;
			(ability, Limit) = staffing;
			SetValue( KnobFromWorkload( owner.DrawnResearchWorkload, Limit ), writeWorkload: WritesProjection );
			shownWorkload = owner.DrawnResearchWorkload;
			// SetPos changes the live rectangle, not the press-time rectangle (+0x20); the next actual move still uses the original baseline.
		}

		/// <summary>
		/// [EXT:online-coop-commands] A guest's knob follows its player while its workload request waits for the host; once the last one came back and the knob is not held, it shows the lab's
		/// workload: when that is not the workload the knob stands for (a refusal, another player's change), its raw value, ball and hum are reprojected from it without writing it (C4b). An
		/// accepted request leaves the raw value its player set.
		/// </summary>
		internal void FollowPark()
		{
			if ( !owner.level.Commands.SendsRequests || owner.ResearchWorkloadWaits || pressMode != PressMode.None || owner.ResearchEconomy is not { } economy )
				return;
			var workload = economy.Research.Workload;
			if ( workload == shownWorkload )
				return;
			SetValue( KnobFromWorkload( workload, Limit ), writeWorkload: false );
			shownWorkload = workload;
		}

		private void SetValue( int value, bool positionBall = true, bool writeWorkload = true )
		{
			value = Math.Clamp( value, 0, WorkloadKnobRange );
			if ( value == Value ) return;
			// [BIN:TPW-EXE:0x4a99ea capacity clamp] the change handler calls SetPos again when the raw value exceeds the staffing limit.
			var capped = Math.Min( value, Math.Max( 0, Limit ) );
			Value = capped;
			if ( positionBall || capped != value ) BallTop = WorkloadKnobTop( Value );
			if ( writeWorkload )
				shownWorkload = WorkloadFromKnob( Value, Limit );
			if ( !writeWorkload || owner.SetResearchWorkload( WorkloadFromKnob( Value, Limit ) ) )
				owner.SetResearchHum( Value );
		}

		// [EXT:ui-key-navigation] keys move the workload one percentage point and explicitly synchronize the raw knob without rounding that requested workload back down.
		public override void Adjust( int direction )
		{
			if ( !Editable ) return;
			// [EXT:online-coop-commands] a guest's keys count from its workload not back from the host yet (C4b)
			var workload = Math.Clamp( owner.DrawnResearchWorkload + direction, MinimumKnobWorkload, MaximumKnobWorkload );
			if ( !owner.SetResearchWorkload( workload ) ) return;
			CancelInteraction();
			SetValue( KnobFromWorkload( workload, Limit ), writeWorkload: false );
			shownWorkload = workload;
		}

		private NVector2 AuthoredPoint( UiCanvas canvas, NVector2 point )
		{
			var rect = ScreenRect( canvas );
			return new NVector2( Bounds.X, Bounds.Y ) + (point - new NVector2( rect.X, rect.Y )) / canvas.Scale;
		}

		public void DragStart( UiCanvas canvas, NVector2 point )
		{
			CancelInteraction();
			if ( !Editable ) return;
			pressPoint = lastPoint = AuthoredPoint( canvas, point );
			// [BIN:TPW-EXE:0x65db72 child hit dispatch] the ball child wins before the parent's narrower track rectangle is tested.
			if ( BallRect.Contains( pressPoint ) )
			{
				pressMode = PressMode.Grab;
				pressCentre = BallTop + KnobBallHeight / 2;
			}
			else if ( Track.Contains( pressPoint ) )
				pressMode = PressMode.Page;
		}

		public void DragTo( UiCanvas canvas, NVector2 point )
		{
			if ( !Editable || pressMode != PressMode.Grab ) return;
			var at = AuthoredPoint( canvas, point );
			// [BIN:TPW-EXE:0x658840 pointer dispatch] a stationary pointer emits no movement. UiScreen calls every held frame, including the press itself.
			if ( at == lastPoint ) return;
			lastPoint = at;
			// [BIN:TPW-EXE:0x66be55 saved rectangle] translate the immutable press-time rectangle by the full press-relative delta, then clamp and read the value.
			BallTop = DragBall( pressCentre, pressPoint.Y, at.Y ) - KnobBallHeight / 2;
			SetValue( KnobValueAt( BallTop + KnobBallHeight / 2 ), positionBall: false );
		}

		public void DragEnd( UiCanvas canvas, NVector2 point )
		{
			releasedPage = pressMode == PressMode.Page;
			// [BIN:TPW-EXE:0x66bd75 release] mode -2 repositions the ball from the raw value even when that value did not change.
			if ( pressMode == PressMode.Grab ) BallTop = WorkloadKnobTop( Value );
			pressMode = PressMode.None;
		}

		public void ClickCaptured( UiCanvas canvas, NVector2 point )
		{
			if ( !releasedPage || !Editable ) return;
			releasedPage = false;
			// [BIN:TPW-EXE:0x66bb38 track click] 0x11006 uses the saved press point, only after the completed-click qualification; constructor 0x66adc9 leaves page +0x148 at 1.
			SetValue( Value + (pressPoint.Y <= BallTop + KnobBallHeight ? 1 : -1) );
		}

		public void CancelCapturedClick() => CancelInteraction();

		internal void CancelInteraction()
		{
			pressMode = PressMode.None;
			releasedPage = false;
			BallTop = WorkloadKnobTop( Value );
		}

		public override void Draw( UiContext context, bool focused, bool pressed ) =>
			context.DrawModel( "b_scroller", Editable ? UiButton.NormalFrame : UiButton.DisabledFrame, context.Canvas.Map( BallRect, Anchor ) );
	}
}
