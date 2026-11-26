using System.Globalization;
using Veldrid;
using OpenTPW.UI.Original;

namespace OpenTPW;

/// <summary>A number's text, formatted again only when the number changes.</summary>
internal sealed class CachedNumber
{
	private int value;
	private string? text;

	public string Of( int number )
	{
		if ( text == null || number != value )
			(value, text) = (number, number.ToString( CultureInfo.InvariantCulture ));
		return text;
	}
}

/// <summary>The common parts of the wage windows: the font slots, the colours and the layout's controls.</summary>
internal static class ThemeParkIncWageWindowParts
{
	// [EXT:ui-font-tier-by-scale] which shipped font serves font slot 6 (0x500DE0) is OpenTPW's choice: the label role, as the tag tube and the mission window
	public static readonly Func<UiFonts, FontAtlas> Slot6 = fonts => fonts.Label;

	public static readonly RgbaByte White = new( 0xFF, 0xFF, 0xFF, 0xFF );

	public static UiModelImage Image( UiScreen screen, LayoutTable table, string id, uint control ) => screen.Add( new UiModelImage
	{
		Id = id,
		Model = table.Control( control ).Model!,
		Bounds = table.Control( control ).Rect,
		Anchor = UiAnchor.Center
	} );

	public static UiLabel Label( UiScreen screen, string id, UiRect bounds, Func<string> text, UiAlign align = UiAlign.Left, bool wrap = false ) =>
		screen.Add( new UiLabel { Id = id, Text = text, Font = Slot6, Color = White, Align = align, Wrap = wrap, Fit = !wrap, Bounds = bounds, Anchor = UiAnchor.Center } );

	public static UiLabel Title( UiScreen screen, UiRect bounds, Func<string> text ) => screen.Add( new UiLabel
	{
		Id = "title",
		Text = text,
		Font = fonts => fonts.Title,
		Shadow = true,
		ShadowAlpha = UiBatch.TpiTitleCopyAlpha,
		Color = UiColors.Title,
		Fit = true,
		Bounds = bounds,
		Anchor = UiAnchor.Center
	} );

	public static UiButton Button( UiScreen screen, UiStringTable strings, string id, LayoutNode node, Action? clicked ) => screen.Add( new UiButton
	{
		Id = id,
		Model = node.Model,
		Help = node.Help >= 0 ? strings.Help( node.Help ) : null,
		Clicked = clicked,
		Bounds = node.Rect,
		Anchor = UiAnchor.Center
	} );
}

/// <summary>
/// Theme Park Inc's wage demand window (table <c>0x9C9F64</c>, docs/reverse/TPI-WAGES.md §2): a wage demand row's go-to opens it on the member and his demand. The offer box starts at the demand and steps
/// by WageStep between MinWage and MaxWage; OK negotiates the offer, the close and cancel buttons negotiate an offer of 0, and the fire button dismisses the member.
/// </summary>
// [BIN:TPI-EXE:0x59ABB0 wage window] on the controller 0xA6C2B0 when no window is open (+0): +4 = the member, +0x14 = +0x18 = the demand; the HUD's window closes (0x5013C0), CreateWindow 0x84DEA0(0x9C9F64)
// with the procedure 0x40CCAC (0x59A8A0 -> 0x59A900), the title 0xCA199 (UITEXT 0xFB, 0x501D50), the controls (0x59ACA0), the gauges (0x59B9F0) and the delayed help 0x503AD0(0x1B0)
internal sealed class ThemeParkIncWageDemandWindow
{
	/// <summary>The screen's name on the HUD stack.</summary>
	public const string ScreenName = "tpiWageDemand";

	// [BIN:TPI-EXE:0x59ACA0 wage window texts] UITEXT 0xFB (the title), 0xFC ("Your Offer", 0xCA197), 0xBE/0xC4/0xC1/0xC3/0xBF/0xC5/0xC6 (the labels 0xCA18E, 0xCA192, 0xCA190, 0xCA191, 0xCA18C, 0xCA193,
	// 0xCA196: Employed for, Motivation, Experience level, Happiness, Monthly wage, Supplying agency, Special ability) and 0xFD (the demand, 0xCA186, with the offer word +0x14 as parameter 13)
	public const int TitleText = 0xFB, OfferText = 0xFC, DemandText = 0xFD, EmployedForText = 0xBE, MotivationText = 0xC4, ExperienceText = 0xC1, HappinessText = 0xC3, WageText = 0xBF,
		AgencyLabelText = 0xC5, AbilityLabelText = 0xC6;

	/// <summary>The agencies' names: UITEXT <c>0x6E</c> + the agency (the table <c>0x9D2CD8</c>, 8-byte rows, read by <c>0x62F840</c>).</summary>
	// [BIN:TPI-EXE:0x62F840 agency name] the word at 0x9D2CD8 + 8 × agency: 0x6E, 0x6F, 0x70, 0x71 for the four agencies
	public static int AgencyText( int agency ) => 0x6E + agency;

	private readonly UiStringTable strings;
	private readonly ThemeParkIncStaffSettings settings;
	private readonly Action close;
	private readonly Action<ThemeParkIncStaffMember, int, int> negotiate;
	private readonly Action<ThemeParkIncStaffMember> dismiss;

	public ThemeParkIncWageDemandWindow( UiStringTable strings, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, int demand, Action close,
		Action<ThemeParkIncStaffMember, int, int> negotiate, Action<ThemeParkIncStaffMember> dismiss )
	{
		this.strings = strings;
		this.settings = settings;
		Member = member;
		Demand = demand;
		// [BIN:TPI-EXE:0x85CDCB varibox value] the box's value setter (vtable +0x20, 0x85CD5D) clamps the value to its range (+0x144..+0x148) and, when it changed, posts message 0x800 with it
		// (0x85CE75), which the procedure stores at +0x14: the offer is the demand clamped to MinWage..MaxWage
		Offer = Math.Clamp( demand, settings.MinWage, settings.MaxWage );
		this.close = close;
		this.negotiate = negotiate;
		this.dismiss = dismiss;
		ComposeTexts();
	}

	/// <summary><c>+4</c>: the member who demands.</summary>
	public ThemeParkIncStaffMember Member { get; }

	/// <summary><c>+0x18</c>: the demand.</summary>
	public int Demand { get; }

	/// <summary><c>+0x14</c>: the offer, the demand until the offer box changes it (message 0x800).</summary>
	public int Offer { get; private set; }

	/// <summary>The window has closed (the procedure's message 0x14).</summary>
	public bool Closed { get; private set; }

	/// <summary>The member's name, the demand text and the agency's and ability's names as the window shows them: set once, as the builder 0x59ACA0 sets the controls' texts.</summary>
	public (string Name, string DemandText, string Agency, string Ability) Texts { get; private set; }

	private readonly CachedNumber offerText = new(), salaryText = new();

	private void ComposeTexts() => Texts = (strings.StaffName( (int)Member.Role, Member.NameIndex ),
		strings.FormatByType( DemandText, new Dictionary<int, string> { [0xD] = Demand.ToString( CultureInfo.InvariantCulture ) } ), strings.Entry( AgencyText( Member.Agency ) ),
		strings.Ability( Member.Ability ));

	/// <summary>The advisor the window's help reaches; null: no advisor (the window still works).</summary>
	internal IThemeParkIncWindowAdvice? Advice { get; set; }

	/// <summary>The window's help (<c>0x1B0</c>): said after the panel delay when it opens, by its help button, and cancelled when it goes.</summary>
	public const int HelpMessage = 0x1B0;

	public UiScreen Build()
	{
		var table = ThemeParkIncLayoutTables.WageDemand;
		// [BIN:TPI-EXE:0x59A9F6 wage window Enter] the Enter key is the command -1, which the procedure takes as OK (whatever control the pointer rests on)
		var screen = new UiScreen( ScreenName ) { Back = Close, Accept = PressOkay };
		ThemeParkIncWageWindowParts.Image( screen, table, "window", 0xCA181 );
		ThemeParkIncWageWindowParts.Title( screen, table.Control( 0xCA199 ).Rect, () => strings.Entry( TitleText ) );
		// [BIN:TPI-EXE:0x59AD4B wage window name] the frame 0xCA187 in font slot 6 with the member's name (+0x1C0, 0x62B600)
		ThemeParkIncWageWindowParts.Image( screen, table, "nameFrame", 0xCA187 );
		var nameFrame = table.Control( 0xCA187 );
		ThemeParkIncWageWindowParts.Label( screen, "name", nameFrame.Inner ?? nameFrame.Rect, () => Texts.Name, UiAlign.Center );
		// [BIN:TPI-EXE:0x59ADCC offer label] 0xCA197 centred (0x84A4D2(1, 1, 3), 0x84A577(1, 1, 3)), font slot 6, white (0x84A6BB): UITEXT 0xFC
		ThemeParkIncWageWindowParts.Label( screen, "offerLabel", table.Control( 0xCA197 ).Rect, () => strings.Entry( OfferText ), UiAlign.Center );
		// [BIN:TPI-EXE:0x59ACE1 offer box] the varibox 0xCA185 with the range MinWage..MaxWage (0x85CC73), the step WageStep (0x85CD3C) and the value +0x14 (its vtable +0x20); a change sends message
		// 0x800 with the new value, which the procedure stores at +0x14 (0x59AA9F)
		var box = table.Control( 0xCA185 );
		ThemeParkIncWageWindowParts.Image( screen, table, "offerBox", 0xCA185 );
		var field = box.Children.First( child => child.Type == 5 );
		ThemeParkIncWageWindowParts.Label( screen, "offer", field.Rect, () => offerText.Of( Offer ), UiAlign.Center );
		foreach ( var part in box.Children.Where( child => child.Model is "b_minus" or "b_plus" ) )
		{
			var up = part.Model == "b_plus";
			screen.Add( new UiButton { Id = up ? "offerUp" : "offerDown", Model = part.Model, Clicked = () => Step( up ? 1 : -1 ), Bounds = part.Rect, Anchor = UiAnchor.Center } );
		}
		// [BIN:TPI-EXE:0x59AEAC statistics] the frame 0xCA188 with its labels (font slot 6) and values: the wage 0xCA18D (the salary, 0x62B620), the agency 0xCA194 (0x62F840), the ability 0xCA195
		// (ABILITIES.str 0xA0D648 at +0x218, 0x62B8E0); "Employed for" 0xCA18F takes UITEXT 0x3B8 with the days since the hire date +0x208 (0x6297E0, refreshed by 0x59B9F0), which OpenTPW's member does
		// not keep, so the value stays empty
		ThemeParkIncWageWindowParts.Image( screen, table, "statsFrame", 0xCA188 );
		void Pair( string id, uint label, int text, uint value, Func<string> shown )
		{
			ThemeParkIncWageWindowParts.Label( screen, id + "Label", table.Control( label ).Rect, () => strings.Entry( text ) );
			ThemeParkIncWageWindowParts.Label( screen, id, table.Control( value ).Rect, shown );
		}
		Pair( "employedFor", 0xCA18E, EmployedForText, 0xCA18F, () => "" );
		Pair( "wage", 0xCA18C, WageText, 0xCA18D, () => salaryText.Of( Member.Salary ) );
		Pair( "agency", 0xCA193, AgencyLabelText, 0xCA194, () => Texts.Agency );
		Pair( "ability", 0xCA196, AbilityLabelText, 0xCA195, () => Texts.Ability );
		ThemeParkIncWageWindowParts.Label( screen, "experienceLabel", table.Control( 0xCA190 ).Rect, () => strings.Entry( ExperienceText ) );
		ThemeParkIncWageWindowParts.Label( screen, "motivationLabel", table.Control( 0xCA192 ).Rect, () => strings.Entry( MotivationText ) );
		ThemeParkIncWageWindowParts.Label( screen, "happinessLabel", table.Control( 0xCA191 ).Rect, () => strings.Entry( HappinessText ) );
		// [BIN:TPI-EXE:0x59B9F0 wage window gauges] (every 2000 ms, the timer 0x80080): 0xCA18A happiness ((u8)ftol(+0x230) << 10) / 100 on happygrad.tga, 0xCA18B motivation (+0x234) the same on
		// ridestatbar.tga, 0xCA189 experience (+0x210 << 10) / MaxExperience (unsigned); the gauges here read the member every frame
		screen.Add( new UiGaugeBar { Id = "happinessBar", Art = UiGauge.HappinessArtPath, Value = () => UiGauge.OfPercent( (int)Member.Happiness & 0xFF ), Bounds = table.Control( 0xCA18A ).Rect,
			Anchor = UiAnchor.Center } );
		screen.Add( new UiGaugeBar { Id = "motivationBar", Value = () => UiGauge.OfPercent( (int)Member.Motivation & 0xFF ), Bounds = table.Control( 0xCA18B ).Rect, Anchor = UiAnchor.Center } );
		screen.Add( new UiGaugeBar { Id = "experienceBar", Value = () => ExperienceGauge( Member.Experience, settings.MaxExperience ), Bounds = table.Control( 0xCA189 ).Rect,
			Anchor = UiAnchor.Center } );
		// [BIN:TPI-EXE:0x59AFF8 demand text] the frame 0xCA186 centred, white, font slot 6, UITEXT 0xFD with the offer word (the demand at the build) as parameter 13
		ThemeParkIncWageWindowParts.Image( screen, table, "demandFrame", 0xCA186 );
		var demandFrame = table.Control( 0xCA186 );
		ThemeParkIncWageWindowParts.Label( screen, "demand", demandFrame.Inner ?? demandFrame.Rect, () => Texts.DemandText, UiAlign.Center, wrap: true );
		// the member's picture (0xCA198: 0x502B60 renders the member into the frame) is not ported: Theme Park Inc's person renders are not built
		ThemeParkIncWageWindowParts.Image( screen, table, "pictureFrame", 0xCA198 );
		ThemeParkIncWageWindowParts.Button( screen, strings, "ok", table.Control( 0xCA184 ), PressOkay );
		ThemeParkIncWageWindowParts.Button( screen, strings, "close", table.Control( 0xCA182 ), PressClose );
		ThemeParkIncWageWindowParts.Button( screen, strings, "cancel", table.Control( 0xFFFFFFFE ), PressClose );
		ThemeParkIncWageWindowParts.Button( screen, strings, "fire", table.Control( 0xCA183 ), PressFire );
		// [BIN:TPI-EXE:0x59AA8D wage window help] 0xCA19A calls 0x503A40(0x1B0): the advisor's help button
		ThemeParkIncWageWindowParts.Button( screen, strings, "help", table.Control( 0xCA19A ), () => Advice?.HelpButton( HelpMessage ) );
		// [BIN:TPI-EXE:0x59AC16 wage window delayed help] the builder ends with 0x503AD0(0x1B0): the advisor says the window's help once the panel delay has passed
		Advice?.DelayedHelp( HelpMessage );
		return screen;
	}

	/// <summary>The experience gauge's share (<c>0x59BA96</c>): (experience &lt;&lt; 10) / MaxExperience as unsigned words, clamped to the gauge.</summary>
	internal static float ExperienceGauge( int experience, int maxExperience ) =>
		Math.Min( unchecked( (uint)(experience << 10) ) / (uint)maxExperience, (uint)UiGauge.FullValue ) / (float)UiGauge.FullValue;

	/// <summary>A press of the offer box's minus or plus: the offer moves by WageStep, kept between MinWage and MaxWage.</summary>
	// [BIN:TPI-EXE:0x85CC73 varibox range] the box's value stays in its range; a step changes it by the step and the box sends message 0x800 (0x59AA9F stores it at +0x14)
	internal void Step( int direction ) => Offer = Math.Clamp( unchecked( Offer + direction * settings.WageStep ), settings.MinWage, settings.MaxWage );

	/// <summary>OK (<c>0xCA184</c>, as the Enter command -1): the window closes and the negotiation opens on the offer.</summary>
	// [BIN:TPI-EXE:0x59AA0E wage negotiate] message 0x100 with 0xCA184 or -1: message 4 to the window (0x85049F, whose destroy's message 0x14 runs 0x503B40(0x1B0) at once), 0x503B40(0x1B0) again,
	// and with a member (+4) the dialog's builder 0x559C40(member, +0x18, +0x14)
	internal void PressOkay() => Finish( Offer );

	/// <summary>The close (<c>0xCA182</c>) and cancel (-2) buttons: the offer becomes 0 and the negotiation opens on it.</summary>
	// [BIN:TPI-EXE:0x59AA05 wage cancel] 0xCA182 and -2 store +0x14 = 0 first, then the same as 0xCA184
	internal void PressClose()
	{
		Offer = 0;
		Finish( 0 );
	}

	/// <summary>The fire button (<c>0xCA183</c>): the member is dismissed (<c>0x629040</c>) and the window closes without a negotiation.</summary>
	// [BIN:TPI-EXE:0x59A9C7 wage fire] 0x629040 on the member (+4), +4 = 0, message 4 to the window (its destroy cancels the help), 0x503B40(0x1B0); no negotiation
	internal void PressFire()
	{
		if ( Closed )
			return;
		Closed = true;
		dismiss( Member );
		Destroy();
	}

	/// <summary>The window closes without a negotiation: the HUD's message 5 and the Escape key (the default procedure <c>0x507FA0</c> posts message 4 on key 0x1B).</summary>
	// [BIN:TPI-EXE:0x59A936 wage window close] message 5: message 4 to the window and 0x503B40(0x1B0); message 0x14 then clears +0 (0x502E20); the member word +4 stays, and no negotiation opens
	public void Close()
	{
		if ( Closed )
			return;
		Closed = true;
		Destroy();
	}

	private void Finish( int offer )
	{
		if ( Closed )
			return;
		Closed = true;
		Destroy();
		negotiate( Member, Demand, offer );
	}

	// [BIN:TPI-EXE:0x59A92A wage window destroy] message 0x14: +0 = 0, 0x403B11, 0x503B40(0x1B0) (the help's delay dropped and the help cancelled); every close then calls 0x503B40(0x1B0) again,
	// which finds nothing more to cancel
	private void Destroy()
	{
		close();
		Advice?.CancelHelp( HelpMessage );
	}
}

/// <summary>
/// Theme Park Inc's wage negotiation results dialog (table <c>0x9BE518</c>, docs/reverse/TPI-WAGES.md §1): the decision on the offer (<see cref="ThemeParkIncWageNegotiation.Decide"/>) as the member's
/// answer; a refused offer shows the two choices, accept the demand (selected) or let them leave. Closing the dialog in any way applies the decision.
/// </summary>
// [BIN:TPI-EXE:0x559C40 negotiation dialog] the dialog 0xA60408 with no window (+0): +4 = the member, +8 = the offer, +0xC = the demand; the HUD's window closes (0x5013C0), CreateWindow 0x84DEA0(0x9BE518)
// with the procedure 0x559DA0 (-> 0x559860), the title 0xB28A (UITEXT 0xFE), the member's picture in 0xB289 (0x502B60), the decision 0x559E00 and the delayed help 0x503AD0(0x1B1)
internal sealed class ThemeParkIncWageNegotiationWindow
{
	/// <summary>The screen's name on the HUD stack.</summary>
	public const string ScreenName = "tpiWageNegotiation";

	// [BIN:TPI-EXE:0x559E9D negotiation texts] UITEXT 0xFE (the title), 0xA2 ("Name", 0xB287), the body 0xFF (2), 0x100 (1) or 0x101 with the demand as parameter 13 (0), the choices 0x102 and 0x103
	public const int TitleText = 0xFE, NameText = 0xA2, AcceptedText = 0xFF, GrudgingText = 0x100, RefusedText = 0x101, AcceptDemandText = 0x102, LetThemLeaveText = 0x103;

	private readonly UiStringTable strings;
	private readonly Action close;
	private readonly Action<int, ThemeParkIncWageNegotiation.Choice> finish;
	private readonly string memberName;

	public ThemeParkIncWageNegotiationWindow( UiStringTable strings, ThemeParkIncStaffSettings settings, ThemeParkIncStaffMember member, int demand, int offer, Action close,
		Action<int, ThemeParkIncWageNegotiation.Choice> finish )
	{
		this.strings = strings;
		Member = member;
		Demand = demand;
		Offer = offer;
		this.close = close;
		this.finish = finish;
		Result = ThemeParkIncWageNegotiation.Decide( member.Salary, offer, demand, settings.PercentageForBadWageDecision );
		Body = Result switch
		{
			ThemeParkIncWageNegotiation.Accepted => strings.Entry( AcceptedText ),
			ThemeParkIncWageNegotiation.Grudging => strings.Entry( GrudgingText ),
			_ => strings.FormatByType( RefusedText, new Dictionary<int, string> { [0xD] = Demand.ToString( CultureInfo.InvariantCulture ) } )
		};
		memberName = strings.StaffName( (int)member.Role, member.NameIndex );
	}

	public ThemeParkIncStaffMember Member { get; }
	public int Demand { get; }
	public int Offer { get; }

	/// <summary><c>+0x10</c>: the decision.</summary>
	public int Result { get; }

	/// <summary>The panel's selected toggle (<c>0x85ACF1</c>), "Accept the demand" at the build (<c>0x85AD05(0xB28D)</c>).</summary>
	public ThemeParkIncWageNegotiation.Choice Choice { get; set; } = ThemeParkIncWageNegotiation.Choice.AcceptDemand;

	/// <summary>The dialog has closed and applied its decision.</summary>
	public bool Closed { get; private set; }

	/// <summary>The body: the member's answer, composed once into the body buffer 0xA4E56C as the decision does.</summary>
	public string Body { get; }

	/// <summary>The advisor the dialog's help reaches; null: no advisor (the dialog still works).</summary>
	internal IThemeParkIncWindowAdvice? Advice { get; set; }

	/// <summary>The dialog's help (<c>0x1B1</c>): said after the panel delay when it opens, by its help button, and cancelled when it goes.</summary>
	public const int HelpMessage = 0x1B1;

	public UiScreen Build()
	{
		var table = ThemeParkIncLayoutTables.WageNegotiation;
		// [BIN:TPI-EXE:0x55999F negotiation commands] command 0x100 is taken for 0xB285, 0xB28B and 0xB291 only, and the default procedure 0x507FA0 ignores it: the Enter key's -1 does nothing
		var screen = new UiScreen( ScreenName ) { Back = Close, Accept = () => { } };
		ThemeParkIncWageWindowParts.Image( screen, table, "window", 0xB284 );
		ThemeParkIncWageWindowParts.Title( screen, table.Control( 0xB28A ).Rect, () => strings.Entry( TitleText ) );
		// [BIN:TPI-EXE:0x559E1D negotiation fields] 0xB287 (UITEXT 0xA2) and 0xB288 (the member's name, 0x62B600) inside the body frame 0xB286, font slot 6; the body (0xA4E56C) centred (0x84A4D2(1, 1, 3),
		// 0x84A577(1, 1, 3)), font slot 6, white (0x84A6BB(0xFF, 0xFF, 0xFF, 0xFF))
		var frame = table.Control( 0xB286 );
		ThemeParkIncWageWindowParts.Image( screen, table, "bodyFrame", 0xB286 );
		ThemeParkIncWageWindowParts.Label( screen, "nameLabel", table.Control( 0xB287 ).Rect, () => strings.Entry( NameText ) );
		ThemeParkIncWageWindowParts.Label( screen, "name", table.Control( 0xB288 ).Rect, () => memberName );
		ThemeParkIncWageWindowParts.Label( screen, "body", frame.Inner ?? frame.Rect, () => Body, UiAlign.Center, wrap: true );
		// the member's picture (0xB289: 0x502B60 renders the member into the frame) is not ported: Theme Park Inc's person renders are not built
		ThemeParkIncWageWindowParts.Image( screen, table, "pictureFrame", 0xB289 );
		// [BIN:TPI-EXE:0x55A10A choice panel] results 1 and 2 hide the panel 0xB28C (0x84BAF3(0)); result 0 shows it with the labels 0xB28F (UITEXT 0x102) and 0xB290 (0x103), font slot 6, and selects 0xB28D
		if ( Result == ThemeParkIncWageNegotiation.Refused )
		{
			ThemeParkIncWageWindowParts.Image( screen, table, "choicePanel", 0xB28C );
			foreach ( var (control, label, labelControl) in new[] { (0xB28Du, AcceptDemandText, 0xB28Fu), (0xB28Eu, LetThemLeaveText, 0xB290u) } )
			{
				var choice = (ThemeParkIncWageNegotiation.Choice)control;
				var node = table.Control( control );
				screen.Add( new UiButton { Id = choice == ThemeParkIncWageNegotiation.Choice.AcceptDemand ? "acceptDemand" : "letThemLeave", Model = node.Model, Selected = () => Choice == choice,
					Clicked = () => Choice = choice, Bounds = node.Rect, Anchor = UiAnchor.Center } );
				ThemeParkIncWageWindowParts.Label( screen, choice == ThemeParkIncWageNegotiation.Choice.AcceptDemand ? "acceptDemandLabel" : "letThemLeaveLabel", table.Control( labelControl ).Rect,
					() => strings.Entry( label ) );
			}
		}
		// [BIN:TPI-EXE:0x5599A1 negotiation buttons] 0xB285 (b_close) and 0xB28B (b_okay) post message 4 and call 0x503B40(0x1B1); the window's destroy sends message 0x14, which applies the decision
		ThemeParkIncWageWindowParts.Button( screen, strings, "ok", table.Control( 0xB28B ), Close );
		ThemeParkIncWageWindowParts.Button( screen, strings, "close", table.Control( 0xB285 ), Close );
		// [BIN:TPI-EXE:0x5599B2 negotiation help] 0xB291 calls 0x503A40(0x1B1): the advisor's help button
		ThemeParkIncWageWindowParts.Button( screen, strings, "help", table.Control( 0xB291 ), () => Advice?.HelpButton( HelpMessage ) );
		// [BIN:TPI-EXE:0x559D15 negotiation delayed help] the builder ends with 0x503AD0(0x1B1): the advisor says the dialog's help once the panel delay has passed
		Advice?.DelayedHelp( HelpMessage );
		return screen;
	}

	/// <summary>
	/// The dialog's close in any way (OK, the close button, the Escape key through the default procedure <c>0x507FA0</c>, the HUD's message 5): the window goes and its message 0x14 applies the decision.
	/// </summary>
	// [BIN:TPI-EXE:0x5598B6 negotiation close] message 0x14: 0x503B40(0x1B1) (the help's delay dropped and the help cancelled), then the decision's branch (ParkEconomy.EndThemeParkIncNegotiation), +4 = 0,
	// +0 = 0 and 0x502E20; the buttons and message 5 (0x55988F) call 0x503B40(0x1B1) again after the destroy, which finds nothing more to cancel
	public void Close()
	{
		if ( Closed )
			return;
		Closed = true;
		close();
		Advice?.CancelHelp( HelpMessage );
		finish( Result, Choice );
	}
}
