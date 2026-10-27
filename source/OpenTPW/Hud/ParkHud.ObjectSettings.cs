using System.Globalization;
using OpenTPW.UI.Original;
using NVector2 = System.Numerics.Vector2;

namespace OpenTPW.Hud;

/// <summary>
/// The settings controls of the object windows: the ride window's speed, capacity and duration sliders (tp.exe 0x3e30, 0x3e2d, 0x3e2f), the shop
/// window's quality slider with its three lamps, its ingredient slider and its price box (0xc06b, 0xc07f, 0xc086, 0xc085), and the sideshow window's
/// chance-of-winning slider and its price and prize boxes (0xa08e, 0xa08f, 0xa090). As in the original the controls edit the window's own values,
/// which the window applies to the object when it closes, steps to another object or shows another one; apply-all writes them to every object of the type.
/// </summary>
// [BIN:TPW-EXE:0x48d200 window base procedure] message 0x14 (the window closing) calls the window's apply, slot +0x3c, while an object is selected; the arrow keys and the arrow buttons (0x48caf0, 0x48cbe0) apply before they step
// [BIN:TPW-EXE:0x66b088 slider SetPos] a slider's position is clamped to its range (a value above the maximum takes the maximum, then one below the minimum the minimum) and a change is sent to the window as message 0x800, so the window's values are the clamped object values once it has loaded them
public sealed partial class ParkHud
{
	/// <summary>The values the open object window holds (the original window's +0x24..+0x34 words).</summary>
	internal sealed class ObjectWindowSettings
	{
		public RideSettingWords Ride;
		public int QualityStep, Ingredient, Price, Prize, WinChance;

		internal void CopyFrom( ObjectWindowSettings other ) =>
			(Ride, QualityStep, Ingredient, Price, Prize, WinChance) = (other.Ride, other.QualityStep, other.Ingredient, other.Price, other.Prize, other.WinChance);

		internal bool SameAs( ObjectWindowSettings other ) =>
			(Ride, QualityStep, Ingredient, Price, Prize, WinChance) == (other.Ride, other.QualityStep, other.Ingredient, other.Price, other.Prize, other.WinChance);
	}

	private OriginalObject? settingsOwner;
	/// <summary>
	/// The window's values. [EXT:online-coop-commands] A guest's window keeps no draft (C4b-2): they are its drawn values, taken again every frame, and each change goes at once
	/// (<see cref="SendWindowGroup"/>).
	/// </summary>
	private readonly ObjectWindowSettings pending = new();
	internal ObjectWindowSettings PendingSettings => pending;

	/// <summary>The varibox range of the shop and sideshow prices (0x4b0735, 0x4b1cd0: 0..500, step 1).</summary>
	internal const int MaximumPrice = 500;
	/// <summary>The varibox range of the sideshow's prize (0x4b1c2a: 0..10000, step 1).</summary>
	internal const int MaximumPrize = 10000;

	/// <summary>A slider position as the slider class keeps it (<c>0x66b088</c>): above the maximum takes the maximum, then below the minimum the minimum.</summary>
	internal static int SliderPosition( int value, int minimum, int maximum )
	{
		if ( value > maximum ) value = maximum;
		if ( value < minimum ) value = minimum;
		return value;
	}

	/// <summary>
	/// What a held b_minus or b_plus of a variable box with step 1 does (<c>0x66a7e1</c>): once the button has been held for 200 ms the plus button steps by 1 and either button
	/// starts a count of 20 ticks at an increment of 1; every tick after that (more than 50 ms apart, so one per frame that long after the last) changes the value by the
	/// increment and counts down, and when the count reaches 0 the increment is multiplied by 10 and the count restarts at 20. Letting go of a button that began repeating is not a click.
	/// </summary>
	// [BIN:TPW-EXE:0x66a7e1 variable box repeat] case 3 (child 1, b_plus, hold start 0x10009) and case 2 (b_minus): with step +0x148 == 1 counter +0x14c = 20 and increment +0x150 = 1, else counter 5; case 3 then value + step; cases 5 and 4 (the ticks 0x10008) with step 1: value ± the increment (0x66a71c clamps), counter - 1, at 0 the increment × 10 and the counter 20, with another step the value moves by the step each fifth tick; the plus button's hold start steps the value at once, the minus button's does not
	internal sealed class VariableBoxHold
	{
		/// <summary>The delay before a held button repeats (0x66aa61: 0x6693f0(200) on children 1 and 2).</summary>
		public const float Delay = 0.2f;
		/// <summary>The interval between ticks (0x66aa61: 0x6693d0(0x32)).</summary>
		public const float Interval = 0.05f;

		public int Counter { get; private set; }
		public int Increment { get; private set; } = 1;

		/// <summary>[EXT:online-coop-commands] The value a guest's box last asked for in this hold (C4b-2): a step to it again asks nothing; emptied at the press (<see cref="Press"/>).</summary>
		public int? LastAsked { get; set; }

		/// <summary>
		/// [EXT:online-coop-commands] A guest's box's press (C4b-2, <see cref="UiButtonRepeat.Pressed"/>): the hold's arithmetic starts again as <see cref="Start"/> sets it, without its plus
		/// step, and its memory empties, so a hold whose start the pointer missed (outside the button at the delay) steps by one, not by an earlier hold's tens. A host's box keeps the
		/// original's arithmetic, which only the start resets.
		/// </summary>
		public void Press()
		{
			Counter = 20;
			Increment = 1;
			LastAsked = null;
		}

		/// <summary>The hold start (message 0x10009): arms the count; the plus button steps by one now. Returns the new value.</summary>
		public int Start( int value, bool plus, int minimum, int maximum )
		{
			Counter = 20;
			Increment = 1;
			return plus ? SliderPosition( value + 1, minimum, maximum ) : value;
		}

		/// <summary>One tick (message 0x10008). Returns the new value.</summary>
		public int Tick( int value, bool plus, int minimum, int maximum )
		{
			var next = SliderPosition( unchecked( value + (plus ? Increment : -Increment) ), minimum, maximum );
			Counter--;
			if ( Counter == 0 )
			{
				Increment = unchecked( Increment * 10 );
				Counter = 20;
			}
			return next;
		}
	}

	private static int Setting( OriginalObject item, string key ) => item.Entry.Settings.GetInt( key );

	/// <summary>Whether a sideshow's type has <c>UsageInfo.NonWinnableSideshow</c> (type <c>+0x10c</c>): no chance or prize, its price is the price of entry.</summary>
	// [BIN:TPW-EXE:0x4b1f97 sideshow window load] with type +0x10c (UsageInfo.NonWinnableSideshow) set the window hides the chance slider 0xa08e, the prize box 0xa090 and their labels 0xa0a7 and 0xa0a6 and labels the price 0xa0a5 with UITEXT 0x34 (price of entry), else UITEXT 0x33 (price of game); its apply 0x4b20b0 then sets the price only
	private static bool IsNonWinnable( OriginalObject item ) => Setting( item, "UsageInfo.NonWinnableSideshow" ) != 0;

	/// <summary>The ingredient a shop's type names (<c>UsageInfo.SpecialIngredient</c> 1..4), else 0: the window shows the ingredient slider only for these.</summary>
	// [BIN:TPW-EXE:0x4e2200 ingredient name] type +0x158 (UsageInfo.SpecialIngredient) 1..4 selects string 0..3 of the ingredient table ("Amount of fat", "... salt", "... ice", "... sugar"), anything else none; the shop window's load 0x4b0950 shows the slider 0xc086 and label 0xc084 with that name only when there is one
	private static int ShopIngredient( OriginalObject item ) => Setting( item, "UsageInfo.SpecialIngredient" ) is var value and >= 1 and <= 4 ? value : 0;

	/// <summary>Loads the window's values from <paramref name="item"/> (the windows' load routines set every control's position).</summary>
	// [BIN:TPW-EXE:0x4af030 ride window load] the speed slider 0x3e30 gets the range MinSpeed..MaxSpeed (type +0x134/+0x138) and the position ride +0x58, the duration slider 0x3e2f MinDuration..MaxDuration and +0x5c (shown only with Info.DurationUnit, +0x70), the capacity slider 0x3e2d MinCapacity..MaxCapacity and +0x5d (hidden with its label 0x3e31 when the two are equal)
	// [BIN:TPW-EXE:0x4b0950 shop window load] the quality slider 0xc06b (range 0..2, 0x4b06b7) gets the quality byte / 50, the ingredient slider 0xc086 (0..100, 0x4b06d2) the amount byte, the price box 0xc085 (0..500) the price
	// [BIN:TPW-EXE:0x4b1ee0 sideshow window load] the chance slider 0xa08e (0..100, 0x4b1bc7) gets the win byte +0x190, the price box 0xa08f (0..500) the price +0x194, the prize box 0xa090 (0..10000) the cost of goods +0x188
	private void LoadObjectSettings( OriginalObject item )
	{
		if ( item.Entry.WhichUIType == (int)BuildCategory.Rides )
		{
			var words = item.Runtime.Settings;
			pending.Ride = new RideSettingWords(
				SliderPosition( words.Speed, Setting( item, "UsageInfo.MinSpeed" ), Setting( item, "UsageInfo.MaxSpeed" ) ),
				// [EXT:cheats-ride-vehicles] a ride whose vehicle limit the cheat raised has a larger capacity maximum (OriginalObjectRuntime.MaximumCapacity)
				SliderPosition( words.Capacity, Setting( item, "UsageInfo.MinCapacity" ), item.Runtime.MaximumCapacity ),
				SliderPosition( words.Duration, Setting( item, "UsageInfo.MinDuration" ), Setting( item, "UsageInfo.MaxDuration" ) ) );
			return;
		}
		if ( !TryPriceTarget( item, out _, out var state ) )
			return;
		// [BIN:TPW-EXE:0x66a71c variable box SetPos] the variable box class (vtable 0x705af0, slot +0x1c 0x66a6ae) lowers a value above its maximum (+0x13c) to it, else raises one below its minimum (+0x138), and sends 0x800 on a change and always on its first set (flag +0x154 from the constructor 0x66a558); its SetRange 0x66a5c4 clamps the value too
		// [BIN:TPW-EXE:0x66bb9b slider creation] the original's window object is static and its controls are rebuilt on each open, with sliders that start at position 0 and send 0x800 only on a change (0x66b088); but each slider's ball child forces one notification of the slider's value when it is created (message 0x15 calls 0x66b088 with -2, always sending), which puts 0 into the window's word, and SetRange 0x66b47d only stores the bounds, so the load's SetPos reaches the window as a message whenever it differs from 0 and needs none when it is 0: the window's words always end up as the loaded values, as here
		pending.Price = SliderPosition( state.Price, 0, MaximumPrice );
		pending.Prize = SliderPosition( state.CostOfGoods, 0, MaximumPrize );
		pending.QualityStep = SliderPosition( (state.QualityOfGoods & 0xFF) / 50, 0, 2 );
		pending.Ingredient = SliderPosition( state.IngredientAmount & 0xFF, 0, 100 );
		// the win byte +0x190 is 100 less the type's InitChanceOfLoosing at build (0x4db3a1); OpenTPW's economy keeps the chance of losing
		pending.WinChance = SliderPosition( (100 - state.ChanceOfLosingPercent) & 0xFF, 0, 100 );
	}

	/// <summary>The window's apply for <paramref name="item"/>: the ride window's 0x4af440, the shop window's 0x4b0aa0 or the sideshow window's 0x4b20b0.</summary>
	// [BIN:TPW-EXE:0x4b0aa0 shop window apply] SetAmountOfSpecialIngredient(window +0x28) 0x4e21e0, SetQualityOfGoods((byte)(window +0x24 × 50)) 0x4e2190, SetPrice(window +0x2c) 0x4e2770
	// [BIN:TPW-EXE:0x4b20b0 sideshow window apply] SetPrice(window +0x2c) 0x4e2770, then, unless type +0x10c, the win byte (window +0x24) 0x4e21c0 into +0x190 and the cost of goods (window +0x28) 0x4e1a20 into +0x188
	private void CommitObjectSettings( OriginalObject item )
	{
		// [EXT:online-coop-commands] a guest's window sent each change at once and holds nothing to apply (C4b-2)
		if ( item.IsDeleted || level.IsReadOnlyVisit || !level.Objects.Objects.Contains( item ) || level.Commands.SendsRequests )
			return;
		if ( item.Entry.WhichUIType == (int)BuildCategory.Rides )
		{
			if ( item.Runtime.Script != null )
				level.Commands.Issue( new SetRideSettingsCommand( item.Runtime.AttractionId, pending.Ride.Speed, pending.Ride.Capacity, pending.Ride.Duration ) );
			return;
		}
		if ( TryPriceTarget( item, out var economy, out var state ) )
			ApplyShopSettings( item, economy, state );
	}

	/// <summary>Applies the window's values to a shop or sideshow.</summary>
	private void ApplyShopSettings( OriginalObject item, ParkEconomy economy, ParkObjectState state )
	{
		var attraction = item.Runtime.AttractionId;
		if ( state.Kind == ParkObjectKind.Shop )
		{
			level.Commands.Issue( new SetShopSettingsCommand( attraction, (byte)(pending.QualityStep * 50), pending.Ingredient ) );
			level.Commands.Issue( new SetPriceCommand( attraction, pending.Price ) );
		}
		else if ( IsNonWinnable( item ) )
			level.Commands.Issue( new SetPriceCommand( attraction, pending.Price ) );
		else
			level.Commands.Issue( new SetGamePrizeCommand( attraction, pending.Price, pending.Prize, 100 - (pending.WinChance & 0xFF) ) );
	}

	/// <summary>Applies the window's values to the object it showed when the window closes or shows another object, then loads the new one.</summary>
	private void SyncObjectSettings()
	{
		var shown = InfoArmOpen && selectedObject is { IsDeleted: false, Runtime: not null } item ? item : null;
		// [EXT:online-coop-commands] a guest's window draws its drawn values, taken again every frame, and sends each change at once: its close and retarget apply nothing (C4b-2)
		if ( GuestManagement != null )
		{
			settingsOwner = shown;
			if ( shown != null )
				LoadDrawnSettings( shown );
			return;
		}
		if ( ReferenceEquals( shown, settingsOwner ) )
			return;
		if ( settingsOwner != null )
			CommitObjectSettings( settingsOwner );
		settingsOwner = shown;
		if ( shown != null )
			LoadObjectSettings( shown );
	}

	/// <summary>The shop and sideshow windows' apply-all (0xc081, 0xa0a9): the window's values go to every placed object of the same type, the shown one included.</summary>
	// [BIN:TPW-EXE:0x4b0ae0 shop apply-all] the callback (run over the objects by 0x481bc0 with 0x200) compares the type id +0xe with the selected object's and runs the shop apply's three setters with the window's values; the sideshow's 0x4b20f0 runs its apply the same way
	private void ApplySettingsToAll()
	{
		if ( selectedObject is not { IsDeleted: false } owner || !TryPriceTarget( owner, out var economy, out _ ) || RejectReadOnlyAction() )
			return;
		// [EXT:online-coop-commands] a guest's apply-all goes through its queue, whole or not at all (C4b-2)
		if ( GuestManagement is { } queue )
		{
			ApplyGuestSettingsToAll( queue, owner, economy );
			return;
		}
		foreach ( var other in level.Objects.Objects )
		{
			if ( other.IsDeleted || other.Entry.InfoId != owner.Entry.InfoId || level.GetEconomyInstance( other ) is not int instance
				|| !economy.TryGetObject( instance, out var target ) || target.InfoId != owner.Entry.InfoId )
				continue;
			ApplyShopSettings( other, economy, target );
		}
	}

	// ---- controls ----

	private UiSlider? rideSpeedSlider, rideCapacitySlider, rideDurationSlider, shopQualitySlider, shopIngredientSlider, sideshowChanceSlider;
	private UiLabel? rideSpeedLabel, rideCapacityLabel, rideDurationLabel, shopQualityLabel, shopIngredientLabel, priceLabel, sideshowChanceLabel, sideshowPrizeLabel;
	private SliderRedLine? rideSpeedRedLine, rideCapacityRedLine;
	private readonly UiModelImage?[] qualityLamps = new UiModelImage?[3];
	private ValueBox? prizeBox;
	private UiButton? priceMinus, pricePlus, prizeMinus, prizePlus;

	private static string SettingNumber( int value ) => value.ToString( CultureInfo.InvariantCulture );

	/// <summary>The duration label's UITEXT by <c>Info.DurationUnit</c> (<c>0x4aec30</c>), null when the label is hidden (unit 0).</summary>
	// [BIN:TPW-EXE:0x4aec30 slider labels] the labels take UI text 0x1aa ("Speed : "; OpenTPW's 425, as tp.exe's ids above 207 are one more), for the capacity 0x1cf (track type 3), 0x1cd (track type 2) or 0x1ab, and for the duration by Info.DurationUnit 0x1ac (unit 1 and a value of 1), 0x1ad (unit 1), 0x1ae (2), 0x1af (3) or 0x1b0 (4), each with the value as its parameter (0x6acd60); unit 0 hides the label
	internal static UIStrings? DurationLabel( int unit, int value ) => unit switch
	{
		1 => value == 1 ? UIStrings.Duration : UIStrings.Durations,
		2 => UIStrings.Laps,
		3 => UIStrings.Cycles,
		4 => UIStrings.Repetitions,
		_ => null
	};

	internal static UIStrings CapacityLabel( int trackType ) => trackType switch
	{
		3 => UIStrings.CoasterCapacity,
		2 => UIStrings.TrackCapacity,
		_ => UIStrings.Capacity
	};

	/// <summary>The red line of a ride slider: <c>(RedLine[level] &lt;&lt; 10) / (max − min)</c> of the 1024-unit gauge, 0 without a range.</summary>
	// [BIN:TPW-EXE:0x4af08b slider red line] the speed slider's child gauge 0x3e2e gets (Upgrades[level].RedLineSpeed (type +0x1ac + level·0x40) << 10) / (MaxSpeed − MinSpeed), the capacity slider's (RedLineCapacity (+0x19c + level·0x40) << 10) / (MaxCapacity − MinCapacity)
	internal static float RedLineFraction( int redLine, int minimum, int maximum ) => maximum == minimum ? 0f : Math.Clamp( (redLine << 10) / (maximum - minimum) / 1024f, 0f, 1f );

	private int RideLevel( OriginalObject item ) => LiveState( item )?.Level ?? item.Runtime.CatalogueTier ?? 0;

	private int RedLine( OriginalObject item, string key ) =>
		item.Entry.Upgrades.FirstOrDefault( upgrade => upgrade.Level == RideLevel( item ) )?.GetInt( key ) ?? 0;

	private void BuildObjectSettingControls()
	{
		T Info<T>( T element ) where T : UiElement
		{
			element.Anchor = UiAnchor.Center;
			element.Visible = false;
			infoElements.Add( hud.Add( element ) );
			return element;
		}
		// [EXT:online-coop-commands] a guest's change goes at once with its group (C4b-2), and its gesture asks for each step once
		UiSlider Slider( string id, Func<OriginalObject, (int Min, int Max)> range, Func<int> value, Action<int> set, Func<OriginalObject, ManagementKnob> group ) => Info( new UiSlider
		{
			Id = id, Knob = "b_scrollera", Truncates = true, AsksOncePerStep = () => GuestManagement != null,
			Steps = () => selectedObject is { } item ? Math.Max( 1, range( item ).Max - range( item ).Min + 1 ) : 2,
			Value = () => selectedObject is { } item ? value() - range( item ).Min : 0,
			Changed = index =>
			{
				if ( selectedObject is not { } item )
					return;
				set( range( item ).Min + index );
				SendWindowGroup( group( item ) );
			}
		} );
		UiLabel Label( string id, Func<string> text ) => Info( new UiLabel { Id = id, Text = text, Font = fonts => fonts.Small, Color = UiColors.Text, Align = UiAlign.Center, Fit = true } );

		(int, int) Speeds( OriginalObject item ) => (Setting( item, "UsageInfo.MinSpeed" ), Setting( item, "UsageInfo.MaxSpeed" ));
		(int, int) Capacities( OriginalObject item ) => (Setting( item, "UsageInfo.MinCapacity" ), item.Runtime.MaximumCapacity);
		(int, int) Durations( OriginalObject item ) => (Setting( item, "UsageInfo.MinDuration" ), Setting( item, "UsageInfo.MaxDuration" ));

		rideSpeedSlider = Slider( "rideSpeed", Speeds, () => pending.Ride.Speed, value => pending.Ride = pending.Ride with { Speed = value }, _ => ManagementKnob.RideSettings );
		rideCapacitySlider = Slider( "rideCapacity", Capacities, () => pending.Ride.Capacity, value => pending.Ride = pending.Ride with { Capacity = value }, _ => ManagementKnob.RideSettings );
		rideDurationSlider = Slider( "rideDuration", Durations, () => pending.Ride.Duration, value => pending.Ride = pending.Ride with { Duration = value }, _ => ManagementKnob.RideSettings );
		rideSpeedRedLine = Info( new SliderRedLine( () => selectedObject is { } item ? RedLineFraction( RedLine( item, "RedLineSpeed" ), Speeds( item ).Item1, Speeds( item ).Item2 ) : 0f ) { Id = "rideSpeedRedLine" } );
		rideCapacityRedLine = Info( new SliderRedLine( () => selectedObject is { } item ? RedLineFraction( RedLine( item, "RedLineCapacity" ), Capacities( item ).Item1, Capacities( item ).Item2 ) : 0f ) { Id = "rideCapacityRedLine" } );
		rideSpeedLabel = Label( "rideSpeedLabel", () => strings.Format( UIStrings.Speed, SettingNumber( pending.Ride.Speed ) ) );
		rideCapacityLabel = Label( "rideCapacityLabel", () => selectedObject is { } item ? strings.Format( CapacityLabel( Setting( item, "Bumper.WhichTrackType" ) ), SettingNumber( pending.Ride.Capacity ) ) : "" );
		rideDurationLabel = Label( "rideDurationLabel", () => selectedObject is { } item && DurationLabel( Setting( item, "Info.DurationUnit" ), pending.Ride.Duration ) is { } label ? strings.Format( label, SettingNumber( pending.Ride.Duration ) ) : "" );

		shopQualitySlider = Slider( "shopQuality", _ => (0, 2), () => pending.QualityStep, value => pending.QualityStep = value, _ => ManagementKnob.ShopSettings );
		// [BIN:TPW-EXE:0x4b0b30 quality lamps] the shop window procedure turns a quality step s (message 0x800 from 0xc06b) into the value 1 << s of the group 0xc07f, which lights lamp s of the group 0xc07f (value 1 << s: l_low, l_med, l_high)
		// [BIN:TPW-EXE:0x66c85a light switch] the type-13 control's SetValue(mask) sets the type-6 child 0x11100000 | i to lit for each set bit i and unlit for each clear one (0x65d3a3), and a lamp's SetState (0x66da4d) sends its two models message 1 carrying the state (1 lit, 0 unlit), which OpenTPW draws as the model's frame 1 and frame 0
		for ( var lamp = 0; lamp < qualityLamps.Length; lamp++ )
		{
			var index = lamp;
			qualityLamps[lamp] = Info( new UiModelImage { Id = $"qualityLamp{lamp}", Model = new[] { "l_low", "l_med", "l_high" }[lamp], Frame = () => pending.QualityStep == index ? 1 : 0 } );
		}
		shopQualityLabel = Label( "shopQualityLabel", () => strings[UIStrings.QualityOfGoods] );
		shopIngredientSlider = Slider( "shopIngredient", _ => (0, 100), () => pending.Ingredient, value => pending.Ingredient = value, _ => ManagementKnob.ShopSettings );
		shopIngredientLabel = Label( "shopIngredientLabel", () => selectedObject is { } item && ShopIngredient( item ) is > 0 and var ingredient ? strings.Ingredient( ingredient - 1 ) : "" );
		priceLabel = Label( "priceLabel", () => selectedObject is { } item ? strings[item.Entry.WhichUIType == (int)BuildCategory.Shops ? UIStrings.SalePrice : IsNonWinnable( item ) ? UIStrings.PriceOfEntry : UIStrings.PriceOfGame] : "" );
		sideshowChanceSlider = Slider( "sideshowChance", _ => (0, 100), () => pending.WinChance, value => pending.WinChance = value, _ => ManagementKnob.Game );
		sideshowChanceLabel = Label( "sideshowChanceLabel", () => strings[UIStrings.ChanceOfWinning] );
		sideshowPrizeLabel = Label( "sideshowPrizeLabel", () => strings[UIStrings.CostOfPrize] );
		prizeBox = Info( new ValueBox( () => SettingNumber( pending.Prize ) ) { Id = "prizeBox" } );
		// [BIN:TPW-EXE:0x4b0735 price box] the variable boxes step by 1 (0x66a68d) through b_minus and b_plus, which the table creates with attributes 0x21: 0x20 is the repeat style (the callback 0x66aa61 sets their delay to 200 ms and their interval to 50 ms)
		(UiButton Minus, UiButton Plus) Steps( string minusId, string plusId, Func<int> value, Action<int> set, int maximum, Func<OriginalObject, ManagementKnob> group )
		{
			var hold = new VariableBoxHold();
			// [EXT:online-coop-commands] a guest's step goes at once with its group (C4b-2): a step that changes nothing, or that its hold asked for already, asks nothing
			void Ask( int next, bool held )
			{
				if ( GuestManagement == null )
				{
					set( next );
					return;
				}
				if ( next == value() || held && next == hold.LastAsked || selectedObject is not { } item )
					return;
				if ( held )
					hold.LastAsked = next;
				set( next );
				SendWindowGroup( group( item ) );
			}
			void Change( bool plus, Func<int, bool, int, int, int> how ) { if ( !RejectReadOnlyAction() ) Ask( how( value(), plus, 0, maximum ), held: true ); }
			UiButton Step( string id, string model, bool plus ) => Info( new UiButton
			{
				Id = id, Model = model, Clicked = () => { if ( !RejectReadOnlyAction() ) Ask( SliderPosition( value() + (plus ? 1 : -1), 0, maximum ), held: false ); },
				Repeat = new UiButtonRepeat( VariableBoxHold.Delay, VariableBoxHold.Interval, () => Change( plus, hold.Start ), () => Change( plus, hold.Tick ) )
				{
					// a guest's press starts its hold afresh, whether the hold's start runs or the pointer misses it (C4b-2)
					Pressed = () => { if ( GuestManagement != null ) hold.Press(); }
				}
			} );
			return (Step( minusId, "b_minus", false ), Step( plusId, "b_plus", true ));
		}
		(priceMinus, pricePlus) = Steps( "priceMinus", "pricePlus", () => pending.Price, value => pending.Price = value, MaximumPrice, PriceKnob );
		(prizeMinus, prizePlus) = Steps( "prizeMinus", "prizePlus", () => pending.Prize, value => pending.Prize = value, MaximumPrize, _ => ManagementKnob.Game );
	}

	private static LayoutNode? Control( LayoutTable table, uint id ) => table.All.FirstOrDefault( node => node.Id == id );

	private void PlaceSlider( UiSlider? slider, LayoutNode? node )
	{
		if ( slider == null )
			return;
		if ( node == null )
		{
			slider.Visible = false;
			return;
		}
		slider.Bounds = node.Rect;
		slider.Anchor = UiAnchor.Center;
		slider.TrackModel = node.Model;
		slider.Help = HelpOf( node.Help );
		var thumb = node.Children.FirstOrDefault( child => child.Model == "b_scrollera" );
		// [BIN:TPW-EXE:0x66ac00 slider default track] a slider created with the 0x40 attribute (0x61 in these tables) copies its own rectangle into its track rectangle (+0x2c, 0x66ace3..0x66acf0) and only the layout command 3 replaces it, so the shop's and the sideshow's sliders, which have no such command (none of the three object tables holds a command 3 on them or a command 10), run the ball along their whole rectangle: left = track left + (position - minimum) × (track width - ball width) / range (0x66b088)
		slider.Track = node.Inner ?? node.Rect;
		if ( thumb != null )
		{
			slider.KnobSize = new NVector2( thumb.Rect.Width, thumb.Rect.Height );
			slider.KnobTop = thumb.Rect.Y;
		}
	}

	private static void PlaceAt( UiElement? element, LayoutNode? node, bool visible )
	{
		if ( element == null )
			return;
		if ( node == null || !visible )
		{
			element.Visible = false;
			return;
		}
		element.Bounds = node.Rect;
		element.Anchor = UiAnchor.Center;
	}

	/// <summary>Places the setting controls of the shown table each frame and hides the ones the object does not have.</summary>
	private void ApplyObjectSettingLayout( ObjectWindowLayout layout )
	{
		var item = selectedObject!;
		var table = layout.Table;
		var ride = table == OriginalLayoutTables.RideWindow && item.Runtime.Script != null && !level.IsReadOnlyVisit;
		// TryPriceTarget refuses a read-only visit, so the shop and sideshow controls hide there like the ride's
		ParkObjectState? state = null;
		var priced = !level.IsReadOnlyVisit && TryPriceTarget( item, out _, out state );
		var shop = table == OriginalLayoutTables.ShopWindow && priced && state!.Kind == ParkObjectKind.Shop;
		var sideshow = table == OriginalLayoutTables.SideshowWindow && priced && state!.Kind == ParkObjectKind.Sideshow;
		var winnable = sideshow && !IsNonWinnable( item );

		void Slider( UiSlider? slider, uint id, bool visible )
		{
			PlaceSlider( slider, visible ? Control( table, id ) : null );
			if ( slider != null && !visible ) slider.Visible = false;
		}
		Slider( rideSpeedSlider, 0x3E30, ride );
		var capacityShown = ride && Setting( item, "UsageInfo.MinCapacity" ) != Setting( item, "UsageInfo.MaxCapacity" );
		Slider( rideCapacitySlider, 0x3E2D, capacityShown );
		var unit = Setting( item, "Info.DurationUnit" );
		Slider( rideDurationSlider, 0x3E2F, ride && unit != 0 );
		PlaceAt( rideSpeedRedLine, ride ? Control( table, 0x3E30 )?.Children.FirstOrDefault( child => child.Type == 9 ) : null, ride );
		PlaceAt( rideCapacityRedLine, capacityShown ? Control( table, 0x3E2D )?.Children.FirstOrDefault( child => child.Type == 9 ) : null, capacityShown );
		PlaceAt( rideSpeedLabel, Control( table, 0x3E33 ), ride );
		PlaceAt( rideCapacityLabel, Control( table, 0x3E31 ), capacityShown );
		PlaceAt( rideDurationLabel, Control( table, 0x3E32 ), ride && unit is >= 1 and <= 4 );

		Slider( shopQualitySlider, 0xC06B, shop );
		var lamps = Control( table, 0xC07F );
		for ( var lamp = 0; lamp < qualityLamps.Length; lamp++ )
			PlaceAt( qualityLamps[lamp], lamps?.Children.ElementAtOrDefault( lamp ), shop );
		PlaceAt( shopQualityLabel, Control( table, 0xC07E ), shop );
		var ingredient = shop && ShopIngredient( item ) != 0;
		Slider( shopIngredientSlider, 0xC086, ingredient );
		PlaceAt( shopIngredientLabel, Control( table, 0xC084 ), ingredient );

		Slider( sideshowChanceSlider, 0xA08E, winnable );
		PlaceAt( sideshowChanceLabel, Control( table, 0xA0A7 ), winnable );
		var prize = Control( table, 0xA090 );
		PlaceAt( prizeBox, prize, winnable );
		if ( prizeBox != null && prize?.Inner is { } prizeInner ) prizeBox.Inner = prizeInner;
		PlaceAt( sideshowPrizeLabel, Control( table, 0xA0A6 ), winnable );
		PlaceAt( prizeMinus, prize?.Children.FirstOrDefault( child => child.Model == "b_minus" ), winnable );
		PlaceAt( prizePlus, prize?.Children.FirstOrDefault( child => child.Model == "b_plus" ), winnable );

		var price = Control( table, shop ? 0xC085u : 0xA08Fu );
		var priceShown = shop || sideshow;
		PlaceAt( priceLabel, Control( table, shop ? 0xC083u : 0xA0A5u ), priceShown );
		PlaceAt( priceMinus, price?.Children.FirstOrDefault( child => child.Model == "b_minus" ), priceShown );
		PlaceAt( pricePlus, price?.Children.FirstOrDefault( child => child.Model == "b_plus" ), priceShown );
		if ( shopPriceButton != null && priceShown && price != null )
		{
			shopPriceButton.Bounds = price.Rect;
			shopPriceButton.Anchor = UiAnchor.Center;
			shopPriceButton.Help = HelpOf( price.Help );
		}
		// [EXT:pools-waves] a wave machine (a Features object, shown in the shop table) has none of these controls; its "Wave settings" button takes their row, from the quality slider's left (0xc06b) to the price box's right (0xc085)
		if ( waveSettingsButton != null && Control( table, 0xC06B ) is { } quality && Control( table, 0xC085 ) is { } box )
		{
			var row = quality.Rect.Y + quality.Rect.Height / 2;
			waveSettingsButton.Bounds = UiRect.FromCenter( new NVector2( (quality.Rect.X + box.Rect.Right) / 2, row ), 760, 100 );
			waveSettingsButton.Anchor = UiAnchor.Center;
		}
	}

	/// <summary>The type-9 child gauge of a ride slider that marks its red line.</summary>
	private sealed class SliderRedLine : UiElement
	{
		private readonly Func<float> fraction;
		public SliderRedLine( Func<float> fraction ) => this.fraction = fraction;

		public override void Draw( UiContext context, bool focused, bool pressed ) => UiGauge.Draw( context.Batch, ScreenRect( context.Canvas ), fraction() );
	}

	/// <summary>A variable box that only shows its value: the <c>varibox</c> model over the control and the number in its inner rectangle.</summary>
	private sealed class ValueBox : UiElement
	{
		private readonly Func<string> text;
		public ValueBox( Func<string> text ) => this.text = text;
		public UiRect Inner { get; set; }

		public override void Draw( UiContext context, bool focused, bool pressed )
		{
			context.DrawModel( "varibox", 0, ScreenRect( context.Canvas ) );
			context.DrawFittedText( context.Fonts.Label, text(), context.Canvas.Map( Inner, Anchor ), UiColors.Text, UiAlign.Center );
		}
	}
}
