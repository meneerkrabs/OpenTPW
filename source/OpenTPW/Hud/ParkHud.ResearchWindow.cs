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
		if ( ResearchEconomy is { Features.ResearchPanel: false } )
		{
			PostMessage( strings[UIStrings.ResearchInstantAction] );
			return;
		}
		SetInfoArm( false );
		level.CellTool.Cancel();
		LeaveBuildTool();
		researchScreen ??= CreateResearchScreen();
		ResetResearchFluids();
		Stack.Push( researchScreen );
		researchScreen.Focus( researchScreen.Find( "exit" ) );
		// [BIN:TPW-EXE:0x4a1737 drawer open] opening the map/research drawer (message 0xA) plays cat_ui event 0x1C (the drawer's research button help is 'Click for research (R)')
		GameAudio.PlayUi( GameAudio.DrawerOpenEvent );
	}

	public void CloseResearchWindow()
	{
		if ( researchScreen == null || !ResearchWindowOpen )
			return;
		while ( Stack.Top != researchScreen && Stack.Screens.Count > 1 )
			Stack.Pop();
		if ( Stack.Top == researchScreen )
			Stack.Pop();
		// [BIN:TPW-EXE:0x4a186f drawer close] a close plays cat_ui 0x1C when it starts and 0xC2 when the slide finishes (0x4a1947); OpenTPW has no slide, so both play at once
		// [EXT:drawer-window] the research screen is a window that opens and closes at once where the original slides a drawer out of the main panel (docs/AUDIO.md; UI register)
		GameAudio.PlayDrawerClose();
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
	private float researchFluidTime;

	/// <summary>Seconds per fluid step (one message 0x1e, assumed at 30 frames a second).</summary>
	public const float ResearchFluidTickSeconds = 1f / 30f;

	internal ResearchFluid ResearchFluidOf( ResearchCategory category ) => researchFluids[(int)category];

	private void ResetResearchFluids()
	{
		for ( var index = 0; index < researchFluids.Length; index++ )
			researchFluids[index] = new ResearchFluid();
		researchFluidKey = null;
		researchFluidTime = 0;
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
		// [APPROX:UI-066] the fluids step at a fixed 30 ticks a second; the original steps them on every message 0x1e, sent once per rendered frame (0x48a580 → 0x658f6c) — evidence needed: the original's frame rate
		researchFluidTime = Math.Min( researchFluidTime + Math.Max( 0, delta ), ResearchFluidTickSeconds * 8 );
		while ( researchFluidTime >= ResearchFluidTickSeconds )
		{
			researchFluidTime -= ResearchFluidTickSeconds;
			foreach ( var fluid in researchFluids )
				fluid.Tick();
		}
	}

	public static bool TrySetResearchWorkload( ParkEconomy? economy, bool readOnly, int workload )
	{
		if ( readOnly || economy is not { Features.ResearchPanel: true } ) return false;
		economy.Research.SetWorkload( workload );
		return true;
	}

	public static bool TryMoveResearchCurrent( ParkEconomy? economy, bool readOnly, ResearchCategory category, int direction )
	{
		if ( readOnly || economy is not { Features.ResearchPanel: true } || !Enum.IsDefined( category ) ) return false;
		return economy.Research.MoveCurrent( category, direction );
	}

	private void SetResearchEffort( ResearchCategory category, int effort )
	{
		if ( !level.IsReadOnlyVisit && ResearchEconomy is { Features: { ResearchPanel: true } } economy )
			economy.SetResearchEffort( category, effort );
	}

	private UiScreen CreateResearchScreen()
	{
		var screen = new UiScreen( "research" );
		void Model( string id, string model, UiRect bounds ) => screen.Add( new UiModelImage { Id = id, Model = model, Bounds = bounds, Anchor = UiAnchor.Center } );
		// [DATA:Mac research table 0x4c652] Root, title, five panels and the main workload track.
		Model( "window", "research_wind", Rect( 186, 30, 2018, 1032 ) );
		screen.Add( new UiLabel { Id = "title", Text = () => strings[UIStrings.ResearchLab], Font = fonts => fonts.Title,
			Color = UiColors.Title, Align = UiAlign.Center, Fit = true, Bounds = Rect( 866, 67, 1338, 146 ), Anchor = UiAnchor.Center } );
		Model( "workload", "effort", Rect( 234, 148, 464, 968 ) );
		// [APPROX:UI-064] The table supplies the workload knob's initial rectangle and travel inset.
		// The current percentage is shown with linear travel; the original mapping is untraced.
		screen.Add( new ResearchWorkloadKnob( this ) { Id = "workloadKnob",
			Bounds = Rect( 267, 220, 334, 701 ), Anchor = UiAnchor.Center } );
		Model( "cameo", "cameo", Rect( 262, 760, 438, 936 ) );
		for ( var row = 0; row < ResearchRows.Length; row++ )
		{
			var index = row;
			var (category, icon, panel, slider) = ResearchRows[row];
			var cells = ResearchCells[row];
			Model( $"panel{index}", "research", panel );
			Model( $"icon{index}", icon, cells.Icon );
			// [EXTENSION:UI] Cursor arrows select eligible research targets using the economy model.
			screen.Add( new UiButton { Id = $"previous{index}", Model = "b_sleft", Enabled = false,
				Clicked = () => TryMoveResearchCurrent( ResearchEconomy, level.IsReadOnlyVisit, category, -1 ),
				Bounds = cells.Previous, Anchor = UiAnchor.Center } );
			screen.Add( new UiButton { Id = $"next{index}", Model = "b_sright", Enabled = false,
				Clicked = () => TryMoveResearchCurrent( ResearchEconomy, level.IsReadOnlyVisit, category, 1 ),
				Bounds = cells.Next, Anchor = UiAnchor.Center } );
			screen.Add( new UiLabel { Id = $"item{index}", Text = () => ResearchItemName( category ), Font = fonts => fonts.Label,
				Color = UiColors.Text, Align = UiAlign.Center, Fit = true,
				Bounds = cells.Value, Anchor = UiAnchor.Center } );
			screen.Add( new ResearchFluidCell( this, category ) { Id = $"fluid{index}",
				Bounds = cells.Fluid, Anchor = UiAnchor.Center } );
			Model( $"effortTrack{index}", "effortslidea", slider );
			// [BIN:TPW-EXE:0x4aa6bd effort sliders] SetRange(0,100) on the generic slider class: integer value 0..100, a drag position is truncated (0x66afc9), the ball runs linearly across the table's slider rectangle
			screen.Add( new UiSlider { Id = $"effort{index}", Track = cells.Track,
				KnobSize = new NVector2( cells.Knob.Width, cells.Knob.Height ), KnobTop = cells.Knob.Y,
				Steps = () => 101, Truncates = true, Value = () => ResearchEconomy?.Research.Effort[(int)category] ?? 0,
				Changed = value => SetResearchEffort( category, value ),
				Bounds = slider, Anchor = UiAnchor.Center } );
		}
		// [DATA:Mac research table 0x4c652:-1] b_okay exits; help 1.
		screen.Add( new UiButton { Id = "exit", Model = "b_okay", Help = strings.Help( 1 ), Clicked = CloseResearchWindow,
			Bounds = Rect( 1889, 844, 1972, 927 ), Anchor = UiAnchor.Center } );
		screen.Updating = context =>
		{
			StepResearchFluids( context.Delta );
			var editable = !level.IsReadOnlyVisit && ResearchEconomy is { Features: { ResearchPanel: true } };
			if ( screen.Find( "workloadKnob" ) is UiElement workload ) workload.Enabled = editable;
			foreach ( var index in Enumerable.Range( 0, ResearchRows.Length ) )
				{
				if ( screen.Find( $"effort{index}" ) is UiSlider effort ) effort.Enabled = editable;
				var canSelect = editable && ResearchEconomy!.Research.SelectableItems( ResearchRows[index].Category ).Count > 1;
				if ( screen.Find( $"previous{index}" ) is UiButton previous ) previous.Enabled = canSelect;
				if ( screen.Find( $"next{index}" ) is UiButton next ) next.Enabled = canSelect;
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

	private sealed class ResearchWorkloadKnob : UiElement, IUiDragTarget
	{
		private readonly ParkHud owner;
		public ResearchWorkloadKnob( ParkHud owner ) => this.owner = owner;
		public override bool Focusable => Visible && Enabled;
		public override void Adjust( int direction ) => TrySetResearchWorkload( owner.ResearchEconomy, owner.level.IsReadOnlyVisit,
			(owner.ResearchEconomy?.Research.Workload ?? 0) + direction );
		public void DragTo( UiCanvas canvas, NVector2 point )
		{
			if ( !Enabled ) return;
			var rect = ScreenRect( canvas );
			if ( rect.Height <= 0 ) return;
			var y = Bounds.Y + (point.Y - rect.Y) / rect.Height * Bounds.Height;
			TrySetResearchWorkload( owner.ResearchEconomy, owner.level.IsReadOnlyVisit,
				(int)MathF.Round( (1 - (y - 220 - 33.5f) / (701 - 220 - 67)) * 100 ) );
		}
		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			var workload = owner.ResearchEconomy?.Research.Workload ?? 0;
			var top = 220 + (701 - 220 - 67) * (1 - workload / 100f);
			context.DrawModel( "b_scroller", owner.ResearchEconomy == null ? UiButton.DisabledFrame : UiButton.NormalFrame,
				context.Canvas.Map( Rect( 267, top, 334, top + 67 ), Anchor ) );
		}
	}
}
