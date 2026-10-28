using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.UI.Original;

namespace OpenTPW.Tests;

/// <summary>
/// [EXT:online-coop-undo] Pool strokes in a shared park (docs/COOP.md, "Conflicts and undo (C5)"; C5-2): the paint and erase modes' Backspace takes back the player's own newest stroke
/// through the host's journal, whole or not at all, with the tool's own refunds and charges, for a guest and the host alike; a session's stroke is split as it is laid, so every take-back fits
/// one command; and a wave machine's settings go through the management queue under the attractions right. Each test ends with equal digests, world hashes and logs.
/// </summary>
public partial class CoopSessionTests
{
	/// <summary>The guest's stroke from <paramref name="from"/> to <paramref name="to"/>, painted or erased, drawn and back.</summary>
	private void GuestPoolStroke( Park host, Park guest, View view, (int X, int Y) from, (int X, int Y) to, bool erase = false )
	{
		guest.Enter();
		var tool = guest.Level.PoolTool!;
		Assert.IsTrue( tool.Begin( erase ) );
		tool.Press( from.X, from.Y );
		tool.Hover( to.X, to.Y );
		tool.Release();
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the stroke came back" );
	}

	/// <summary>The guest's Backspace in its pool tool, sent to the host as a take-back and back.</summary>
	private void GuestPoolBackspace( Park host, Park guest, View view )
	{
		guest.Enter();
		var tool = guest.Level.PoolTool!;
		Assert.IsTrue( tool.Undo(), "the Backspace went" );
		RoundsUntil( () => !tool.HasGuestBatches, host, guest, view, "the take-back came back" );
	}

	private static TakeBackCommand[] StrokeTakeBacksOf( Park host, int seat ) => TakeBacksOf( host, seat ).Where( command => command.Stroke ).ToArray();

	private static bool AllWater( Park park, params (int X, int Y)[] cells ) => cells.All( cell => IsWater( park, cell.X, cell.Y ) );

	private static bool AnyWater( Park park, params (int X, int Y)[] cells ) => cells.Any( cell => IsWater( park, cell.X, cell.Y ) );

	[TestMethod]
	public void AGuestsBackspaceTakesItsOwnStrokesBackNewestFirstWithTheirRefundsAndChargesOnEveryPeer()
	{
		var (host, guest, view) = UndoSession();
		var seat = guest.Level.Commands.Player;
		var cost = host.Economy.CellCost( CellPurchase.Pool );
		var paid = host.Economy.Balance;
		GuestPoolStroke( host, guest, view, (14, 26), (19, 26) );
		GuestPoolStroke( host, guest, view, (17, 26), (19, 26), erase: true );
		Assert.AreEqual( paid - 3 * cost, host.Economy.Balance, "six cells painted, three erased and refunded" );
		GuestPoolBackspace( host, guest, view );
		Assert.IsTrue( AllWater( host, Row( 14, 19, 26 ) ) && AllWater( guest, Row( 14, 19, 26 ) ), "the erase taken back: its cells painted again" );
		Assert.AreEqual( paid - 6 * cost, host.Economy.Balance, "and paid for" );
		GuestPoolBackspace( host, guest, view );
		Assert.IsFalse( AnyWater( host, Row( 14, 19, 26 ) ) || AnyWater( guest, Row( 14, 19, 26 ) ), "the paint taken back" );
		Assert.AreEqual( paid, host.Economy.Balance, "every cell refunded" );
		var taken = StrokeTakeBacksOf( host, seat );
		Assert.AreEqual( 2, taken.Length );
		Assert.IsTrue( taken.All( command => command is { Verdict: TakeBackVerdict.Taken, Inverse: UndoPoolStrokeCommand } ) );
		// no stroke of its own left: its Backspace sends nothing and says nothing
		GuestPoolBackspace( host, guest, view );
		Rounds( 5, host, guest );
		view.Update();
		Assert.AreEqual( 2, StrokeTakeBacksOf( host, seat ).Length, "nothing more went to the host" );
		Assert.AreEqual( 0, view.Says( OpenTpwText.CoopUndoNotOwn ) + view.Says( OpenTpwText.CoopUndoChanged ) + view.Says( OpenTpwText.CoopUndoInTheWay ) );
		CatchUpAndCompare( host, guest );
		Assert.AreEqual( host.Economy.Balance, guest.Economy.Balance );
	}

	[TestMethod]
	public void TheHostsBackspaceTakesBackItsOwnStrokeThroughTheJournalAndKeepsOneItCannot()
	{
		var (host, guest, view) = UndoSession();
		var hostView = ViewOf( host );
		host.Enter();
		var tool = host.Level.PoolTool!;
		Assert.IsTrue( tool.Begin( erase: false ) );
		tool.Press( 14, 26 );
		tool.Hover( 16, 26 );
		Assert.IsNotNull( tool.Release()?.Name, "a session's stroke is named by its first cell's record" );
		Assert.IsTrue( tool.Begin( erase: false ) );
		tool.Press( 20, 26 );
		tool.Hover( 21, 26 );
		tool.Release();
		RoundsUntil( () => IsWater( guest, 21, 26 ), host, guest, view, "the host's strokes reached the guest" );
		// the guest erases a cell of the host's newest stroke: the host's Backspace is refused, says why and keeps the stroke
		GuestPoolStroke( host, guest, view, (21, 26), (21, 26), erase: true );
		host.Enter();
		HudBackspace( hostView );
		Assert.AreEqual( 1, hostView.Says( OpenTpwText.CoopUndoChanged ), "said why" );
		Assert.IsTrue( IsWater( host, 20, 26 ) && !IsWater( host, 21, 26 ), "nothing changed" );
		Assert.AreEqual( TakeBackVerdict.Changed, StrokeTakeBacksOf( host, ParkCommands.LocalPlayer ).Single().Verdict );
		// the guest takes its erase back: the host's stroke goes back whole, then the one before it
		GuestPoolBackspace( host, guest, view );
		host.Enter();
		Assert.IsTrue( tool.Undo() );
		Assert.IsFalse( AnyWater( host, (20, 26), (21, 26) ) );
		Assert.IsTrue( AllWater( host, Row( 14, 16, 26 ) ) );
		Assert.IsTrue( tool.Undo() );
		Assert.IsFalse( AnyWater( host, Row( 14, 16, 26 ) ) );
		Assert.IsFalse( tool.Undo(), "no stroke left" );
		Assert.AreEqual( 2, StrokeTakeBacksOf( host, ParkCommands.LocalPlayer ).Count( command => command.Verdict == TakeBackVerdict.Taken ) );
		CatchUpAndCompare( host, guest );
		Assert.IsFalse( AnyWater( guest, Row( 14, 21, 26 ) ) );
	}

	[TestMethod]
	public void ASessionsStrokeIsSplitAtItsCellBoundAndBackspaceTakesTheLastPartFirst()
	{
		var (host, guest, view) = UndoSession();
		var seat = guest.Level.Commands.Player;
		guest.Enter();
		guest.Level.PoolTool!.StrokeCellBound = 3;
		GuestPoolStroke( host, guest, view, (14, 26), (20, 26) );
		var batches = HostCommandsOf( host, seat ).OfType<PoolCellsCommand>().ToArray();
		CollectionAssert.AreEqual( Row( 14, 20, 26 ), batches.SelectMany( batch => batch.Cells ).ToArray(), "every cell went once, in order" );
		Assert.AreEqual( 3, batches.Count( batch => batch.First ), "three strokes: three cells, three and one" );
		GuestPoolBackspace( host, guest, view );
		Assert.IsTrue( AllWater( host, Row( 14, 19, 26 ) ) && !IsWater( host, 20, 26 ), "the last part first" );
		GuestPoolBackspace( host, guest, view );
		Assert.IsTrue( AllWater( host, Row( 14, 16, 26 ) ) && !AnyWater( host, Row( 17, 19, 26 ) ) );
		GuestPoolBackspace( host, guest, view );
		Assert.IsFalse( AnyWater( host, Row( 14, 20, 26 ) ) );
		Assert.IsTrue( StrokeTakeBacksOf( host, seat ) is { Length: 3 } taken && taken.All( command => command.Verdict == TakeBackVerdict.Taken ) );
		// the host's drag the same
		host.Enter();
		var tool = host.Level.PoolTool!;
		tool.StrokeCellBound = 3;
		Assert.IsTrue( tool.Begin( erase: false ) );
		tool.Press( 14, 28 );
		tool.Hover( 20, 28 );
		tool.Release();
		Assert.AreEqual( 3, host.Level.Commands.Log.Select( entry => entry.Command ).OfType<PaintPoolCellCommand>().Count( command => command.First ) );
		Assert.IsTrue( tool.Undo() );
		Assert.IsTrue( AllWater( host, Row( 14, 19, 28 ) ) && !IsWater( host, 20, 28 ) );
		Assert.IsTrue( tool.Undo() );
		Assert.IsTrue( AllWater( host, Row( 14, 16, 28 ) ) && !AnyWater( host, Row( 17, 19, 28 ) ) );
		Assert.IsTrue( tool.Undo() );
		Assert.IsFalse( AnyWater( host, Row( 14, 20, 28 ) ) );
		CatchUpAndCompare( host, guest );
	}

	[TestMethod]
	public void AStrokeWhoseFirstBatchTheHostCouldNotTakeIsOpenedByItsNextBatchAndTakenBackAlone()
	{
		var (host, guest, view) = UndoSession();
		var seat = guest.Level.Commands.Player;
		// the stroke before it, of the same mode
		GuestPoolStroke( host, guest, view, (14, 28), (16, 28) );
		guest.Enter();
		var tool = guest.Level.PoolTool!;
		Assert.IsTrue( tool.Begin( erase: false ) );
		// forty requests between two of the host's frames spend its burst: the stroke's first batch after them is too fast, and the host keeps no record of it
		var requests = guest.Session!.GuestCommands!;
		var burst = Enumerable.Range( 0, 40 ).Select( _ => requests.Submit( new LayLineCommand( CellToolMode.Path, -1, 30, 30, 30, 30 ) { Player = seat } ) ).ToArray();
		tool.Press( 4, 26 );
		tool.SendGuestBatches( Time.Now );
		// the drag's later cells wait for that batch's answer, and then go as the stroke's first
		tool.Hover( 8, 26 );
		tool.Release();
		RoundsUntil( () => !tool.HasGuestBatches && burst.All( request => !request.IsOpen ), host, guest, view, "the stroke was answered" );
		Assert.IsFalse( IsWater( host, 4, 26 ), "the first cell had no record" );
		Assert.IsTrue( AllWater( host, Row( 5, 8, 26 ) ) && AllWater( guest, Row( 5, 8, 26 ) ) );
		var batches = HostCommandsOf( host, seat ).OfType<PoolCellsCommand>().ToArray();
		Assert.IsTrue( batches.Single( batch => batch.Cells.Contains( (5, 26) ) ).First, "the batch after the one with no record opened the stroke" );
		GuestPoolBackspace( host, guest, view );
		Assert.IsFalse( AnyWater( host, Row( 4, 8, 26 ) ) || AnyWater( guest, Row( 4, 8, 26 ) ) );
		Assert.IsTrue( AllWater( host, Row( 14, 16, 28 ) ), "the stroke before it untouched" );
		Assert.AreEqual( TakeBackVerdict.Taken, StrokeTakeBacksOf( host, seat ).Single().Verdict );
		CatchUpAndCompare( host, guest );
	}

	/// <summary>A synthetic two-cell wave machine (water side north) at (14, 31) beside a pool painted over (14..17, 30), alike on every park before its session.</summary>
	private static void AddWaveMachine( Park park )
	{
		park.Enter();
		var level = park.Level;
		level.Objects.Things ??= new ThingPool();
		level.PoolBuilder!.PaintCells( PoolBuilder.Rectangle( (14, 30), (17, 30) ) );
		var settings = new ObjectSettings( new[] { new ObjectSettingsFile( "Info.Id 9401\nInfo.Name Synthetic9401\nInfo.IsChoosable 0\nInfo.OverwritePriority 5\nPools.Attraction wavemachine\nPools.WaterSide N\n",
			"synthetic9401.sam" ) } );
		var entry = new ObjectCatalogEntry( "jungle", ObjectCategory.Feature, "/synthetic", settings, new ObjectShape( new[] { "**" } ), "/model.MD2", null, "/script.RSE",
			Array.Empty<string>(), Array.Empty<ObjectAnimationFile>(), Array.Empty<string>(), Array.Empty<string>(), park.One.Entry.FileSystem, null );
		Assert.IsNotNull( level.Objects.TryPlace( entry, 14, 31, 0, out _ ), "the wave machine stands at the pool's south edge" );
	}

	private static OriginalObject WaveMachineOf( Park park ) =>
		park.Level.Objects.Objects.Single( item => !item.IsDeleted && PoolAttractions.KindOf( item.Entry ) == PoolAttractionKind.WaveMachine );

	/// <summary>The wave machine's window on <paramref name="view"/>, <paramref name="choice"/> chosen and applied.</summary>
	private static void ApplyWaves( Park park, View view, OriginalObject machine, string choice )
	{
		park.Enter();
		view.Hud.OpenWaveSettings( machine );
		var screen = view.Hud.Stack.Top!;
		Assert.AreEqual( "waveSettings", screen.Name );
		screen.Find( choice )!.Activate();
		screen.Find( "apply" )!.Activate();
		Assert.AreNotSame( screen, view.Hud.Stack.Top, "the tick closes the window" );
	}

	private static bool WaveChoice( UiScreen screen, string id ) => ((UiButton)screen.Find( id )!).Selected!();

	[TestMethod]
	public void AGuestsWaveSettingsGoThroughTheQueueUnderTheAttractionsRightAndAMachineGoneMeanwhileSaysSo()
	{
		InParkView( () =>
		{
			var (host, guest, view) = AttractionsSession( ( park, _ ) => AddWaveMachine( park ), CoopRights.None );
			var seat = guest.Level.Commands.Player;
			var machine = WaveMachineOf( guest );
			Assert.AreEqual( PoolWaveSettings.Default, guest.Level.PoolWavesOf( machine ) );
			// without the right: refused before anything goes, and said once the window has closed
			ApplyWaves( guest, view, machine, "height3" );
			view.Update();
			Assert.AreEqual( 1, view.Says( OpenTpwText.CoopNotPermitted ) );
			Rounds( 5, host, guest );
			Assert.AreEqual( 0, HostCommandsOf( host, seat ).OfType<SetPoolWavesCommand>().Count(), "nothing went" );
			// with the attractions right: the settings go, and the window opens on them until they are back
			GiveRights( host, guest, view, CoopRights.Attractions );
			ApplyWaves( guest, view, machine, "height3" );
			view.Hud.OpenWaveSettings( machine );
			Assert.IsTrue( WaveChoice( view.Hud.Stack.Top!, "height3" ), "drawn as asked" );
			view.Hud.Stack.Top!.Find( "cancel" )!.Activate();
			UntilManaged( host, guest, view );
			foreach ( var park in new[] { host, guest } )
				Assert.AreEqual( new PoolWaveSettings( true, 3, 1 ), park.Level.PoolWavesOf( WaveMachineOf( park ) ) );
			// a machine the host removes while the guest's settings wait: refused, and the guest is told it is no longer there
			ApplyWaves( guest, view, machine, "height1" );
			host.Enter();
			Assert.IsTrue( host.Issue( new RemoveAttractionCommand( WaveMachineOf( host ).Runtime.AttractionId ) ).Accepted );
			UntilManaged( host, guest, view );
			view.Update();
			Assert.AreEqual( 1, view.Says( OpenTpwText.NotAvailable ) );
			Assert.AreEqual( 2, HostCommandsOf( host, seat ).OfType<SetPoolWavesCommand>().Count(), "both went" );
			CutAndCompare( host, guest );
		} );
	}
}
