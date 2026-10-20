using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Hud;
using OpenTPW.UI.Original;
using N = System.Numerics;

namespace OpenTPW.Tests;

[TestClass, DoNotParallelize]
public class ShopPriceIngressTests
{
	private string root=null!;
	private List<Entity> entities=null!;
	private Logger log=null!;
	private CameraMode camera=null!;
	private GameEdition edition;
	private bool text;
	private List<InputButton> down=null!, last=null!;
	private static void Set(object o,string f,object v)=>o.GetType().GetField(f,BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(o,v);
	private static T Empty<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
	private sealed class EmptyCatalog : IBuildCatalog { public IReadOnlyList<BuildItem> GetItems(BuildCategory category)=>Array.Empty<BuildItem>(); }

	[TestInitialize]
	public void Setup()
	{
		entities=Entity.All; Entity.All=new(); log=Log; Log=new(); camera=Camera.Mode; Camera.Mode=new ParkCameraMode(); edition=GameEditions.Current; GameEditions.Select(GameEdition.ThemeParkWorld);
		text=Input.TextEntryActive; Input.TextEntryActive=false; down=Input.KeysDown;last=Input.LastKeysDown; Input.KeysDown=new();Input.LastKeysDown=new();
		root=Path.Combine(Path.GetTempPath(),"opentpw-price-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
		var model=Md2ModelFileTests.CreateGeometry();
		BinaryPrimitives.WriteUInt16LittleEndian(model.AsSpan(0x42),1);BinaryPrimitives.WriteUInt16LittleEndian(model.AsSpan(0x44),0);
		BinaryPrimitives.WriteUInt32LittleEndian(model.AsSpan(0x268),0);BinaryPrimitives.WriteUInt32LittleEndian(model.AsSpan(0x78),0x264);
		foreach(var offset in new[]{0x38,0x3a,0x3c,0x3e,0x4a}) BinaryPrimitives.WriteUInt16LittleEndian(model.AsSpan(offset),0);
		File.WriteAllBytes(Path.Combine(root,"model.MD2"),model);
		File.WriteAllBytes(Path.Combine(root,"script.RSE"),new RideVMTests.Asm().I(Opcode.ENDSLICE).Build(Enum.GetNames<RideVariables>()));
	}
	[TestCleanup]
	public void Cleanup()
	{
		foreach(var entity in Entity.All.ToArray()) entity.Delete();Entity.All=entities;Log=log;Camera.Mode=camera;GameEditions.Select(edition);Input.TextEntryActive=text;Input.KeysDown=down;Input.LastKeysDown=last;Directory.Delete(root,true);
	}
	private OriginalObject Object(int info,BuildCategory category,RideScriptWorld world)
	{
		var settings=new ObjectSettings(new[]{new ObjectSettingsFile($"Info.Id {info}\nInfo.Name synthetic\nInfo.IsChoosable 1\nInfo.WhichUIType {(int)category}\nUsageInfo.InitPricePerUse 30\n", "synthetic.sam")});
		var entry=new ObjectCatalogEntry("jungle",category==BuildCategory.Shops?ObjectCategory.Shop:category==BuildCategory.Sideshows?ObjectCategory.Sideshow:ObjectCategory.Ride,"/synthetic",settings,new ObjectShape(new[]{"*"}),"/model.MD2",null,"/script.RSE",Array.Empty<string>(),Array.Empty<ObjectAnimationFile>(),Array.Empty<string>(),Array.Empty<string>(),new BaseFileSystem(root),null);
		return new OriginalObject(entry,new ObjectPlacement(1,1,0,N.Vector2.Zero,0),world);
	}
	private ParkHud Fixture(BuildCategory category,out Level level,out ParkEconomy economy,out OriginalObject first,out OriginalObject second,out GuestEconomyBridge bridge,ParkGameMode mode=ParkGameMode.FullSimulation)
	{
		VisitorWorldSelectionTests.Fixture(out var guests,out _);
		level=Empty<Level>(); var objects=new ParkObjects(null!,new SandboxParkGrid());
		Set(level,"<Objects>k__BackingField",objects);Set(level,"<Guests>k__BackingField",guests);Set(level,"<CellTool>k__BackingField",new CellBuildTool());Set(level,"<LastActionMessage>k__BackingField","");
		Set(level,"<Paths>k__BackingField",new ParkPathBuilder(guests.Grid.Cells,guests.Grid,(ParkEconomy?)null,new SandboxParkGrid()));
		economy=EconomyTestData.Park(mode:mode);CommandTestLevel.AttachPark(level,economy);var info=category==BuildCategory.Shops?1203:category==BuildCategory.Sideshows?1303:1100;
		first=Object(info,category,objects.ScriptWorld);second=Object(info,category,objects.ScriptWorld);
		((List<OriginalObject>)objects.Objects).AddRange(new[]{first,second});
		var one=economy.RegisterExisting(info);var two=economy.RegisterExisting(info);
		Set(level,"economyInstances",new Dictionary<OriginalObject,int>{{first,one.Id},{second,two.Id}});
		var current=economy;bridge=new GuestEconomyBridge(()=>current,guests);bridge.Link(first.Runtime.AttractionId,one.Id);bridge.Link(second.Runtime.AttractionId,two.Id);
		var hud=new ParkHud(level,OriginalUiTests.FakeStrings(),new EconomyParkStatus(()=>current),new EmptyCatalog(),new HudHost());hud.SelectObject(first);return hud;
	}
	private static UiContext Context()
	{
		var font=new FontAtlas(FontAtlasTests.CreateFont(8,new FontAtlasTests.SyntheticGlyph('A',2,2,0,0,3)));
		return new UiContext(OriginalUiTests.FakeStrings(),new UiFonts(_=>font),new UiModels(n=>throw new FileNotFoundException(n))) {Canvas=new UiCanvas(2048,1536),ResolveTexture=_=>null};
	}
	private static void Open(ParkHud hud,UiContext context)
	{
		hud.Update(context,UiInput.Idle(new N.Vector2(1500,1000)),false,false,false);
		var button=hud.Screen.Find("shopPrice") as UiButton;Assert.IsNotNull(button,"Selected detail has no price action.");Assert.IsTrue(button.Visible&&button.Enabled);
		hud.Screen.Focus(button);hud.Update(context,UiInput.Key(UiKeys.Accept),false,false,false);Assert.AreEqual("shopPrice",hud.Stack.Top!.Name);
	}

	[DataTestMethod,DataRow(BuildCategory.Shops,ParkGameMode.FullSimulation),DataRow(BuildCategory.Sideshows,ParkGameMode.FullSimulation),DataRow(BuildCategory.Shops,ParkGameMode.InstantAction),DataRow(BuildCategory.Sideshows,ParkGameMode.InstantAction)]
	public void SelectedDetailPriceActionChangesActualVisitorPaymentOnlyForItsInstance(BuildCategory category,ParkGameMode mode)
	{
		var hud=Fixture(category,out var level,out var economy,out var first,out var second,out var bridge,mode);var context=Context();
		Assert.IsTrue(economy.TryGetObject(level.GetEconomyInstance(first)!.Value,out var one));Assert.IsTrue(economy.TryGetObject(level.GetEconomyInstance(second)!.Value,out var two));
		var totals=economy.Ledger.CurrentTotals.ToArray();var initial=one.Price;var other=two.Price;var cost=one.CostOfGoods;var chance=one.ChanceOfLosingPercent;var cash=economy.Balance;var opened=first.IsOpen;
		Open(hud,context);hud.Update(context,UiInput.Type("45",10),false,false,false);Assert.AreEqual(initial,one.Price,"Typing is staged, not live.");
		hud.Update(context,UiInput.Key(UiKeys.Accept),false,false,false);Assert.AreEqual("hud",hud.Stack.Top!.Name);Assert.AreEqual(45,one.Price);Assert.AreEqual(other,two.Price);
		Assert.AreEqual(cost,one.CostOfGoods);Assert.AreEqual(chance,one.ChanceOfLosingPercent);Assert.AreEqual(cash,economy.Balance);Assert.AreEqual(opened,first.IsOpen);CollectionAssert.AreEqual(totals,economy.Ledger.CurrentTotals.ToArray());Assert.AreEqual(0,one.CustomersThisMonth);Assert.AreEqual(0,two.CustomersThisMonth);
		Assert.IsFalse(bridge.TryPayVisit(first.Visitors,44,out _));Assert.IsTrue(bridge.TryPayVisit(first.Visitors,100,out var paid));Assert.AreEqual(45,paid);
		Assert.IsTrue(bridge.TryPayVisit(second.Visitors,100,out var secondPaid));Assert.AreEqual(other,secondPaid);
		Assert.AreEqual((long)(45+other),economy.Ledger.CurrentTotals[category==BuildCategory.Shops?LedgerCategory.ShopTakings:LedgerCategory.SideshowTakings]);
		Assert.AreEqual(1,one.CustomersThisMonth);Assert.AreEqual(45L,one.TakingsThisMonth);Assert.AreEqual(1,two.CustomersThisMonth);Assert.AreEqual((long)other,two.TakingsThisMonth);
		var saved=ParkSaveFile.Capture(economy);Assert.AreEqual(45,saved.Objects.Single(o=>o.Id==one.Id).Price);Assert.AreEqual(other,saved.Objects.Single(o=>o.Id==two.Id).Price);
		var restored=ParkSaveFile.Restore(ParkSaveFile.Deserialize(System.Text.Encoding.UTF8.GetBytes(ParkSaveFile.Serialize(economy))),EconomyTestData.Settings(),EconomyTestData.Catalog());Assert.IsTrue(restored.TryGetObject(one.Id,out var resumed));Assert.AreEqual(45,resumed.Price);
	}

	[TestMethod]
	public void DrawingDoesNotAddActionsAndTheStatusLineIgnoresTheInformationArm()
	{
		var hud=Fixture(BuildCategory.Shops,out _,out _,out _,out _,out _);var context=Context();hud.Update(context,UiInput.Idle(new N.Vector2(1500,1000)),false,false,false);
		hud.Draw(context);Assert.IsNotNull(hud.Screen.DrawOverlay,"The status line sits above the help box and does not depend on the information arm.");
		hud.SetInfoArm(false);for(var i=0;i<3;i++)hud.Draw(context);
		Assert.AreEqual(1,hud.Screen.Elements.Count(e=>e.Id=="shopPrice"),"Drawing cannot create more price actions.");Assert.IsNotNull(hud.Screen.DrawOverlay);
	}
	[TestMethod]
	public void RetractedDetailCannotActivateItsRetainedPriceAction()
	{
		var hud=Fixture(BuildCategory.Shops,out _,out _,out _,out _,out _);var context=Context();hud.Update(context,UiInput.Idle(new N.Vector2(1500,1000)),false,false,false);
		var action=(UiButton)hud.Screen.Find("shopPrice")!;hud.SetInfoArm(false);action.Activate();Assert.AreEqual("hud",hud.Stack.Top!.Name);
	}

	[TestMethod]
	public void ActualAttractionListActivationReachesTheSelectedDetailPriceAction()
	{
		var hud=Fixture(BuildCategory.Shops,out _,out _,out var first,out _,out _);var context=Context();hud.SelectObject(null);hud.OpenAttractionsWindow();
		((UiButton)hud.Stack.Top!.Find("categoryShops")!).Activate();hud.Update(context,UiInput.Key(UiKeys.Accept),false,false,false);
		Assert.AreSame(first,hud.SelectedObject);Assert.IsTrue(hud.InfoArmOpen);Assert.AreEqual("hud",hud.Stack.Top!.Name);Open(hud,context);
	}
	[DataTestMethod,DataRow("0"),DataRow("500")]
	public void ExistingApiRangeIsAcceptedWithoutImmediateCharge(string number)
	{
		var hud=Fixture(BuildCategory.Shops,out var level,out var economy,out var item,out _,out _);var context=Context();economy.TryGetObject(level.GetEconomyInstance(item)!.Value,out var state);var cash=economy.Balance;
		Open(hud,context);hud.Update(context,UiInput.Type(number,10),false,false,false);hud.Update(context,UiInput.Key(UiKeys.Accept),false,false,false);Assert.AreEqual(int.Parse(number),state.Price);Assert.AreEqual(cash,economy.Balance);
	}

	[DataTestMethod,DataRow(""),DataRow("-1"),DataRow("501"),DataRow("2147483647"),DataRow("2147483648"),DataRow("21474836470"),DataRow("99999999999999999999999999999999999999999999999999999999999999999999999999"),DataRow("1.5"),DataRow("+1"),DataRow(" 1"),DataRow("text")]
	public void InvalidInputRemainsOpenAndLeavesTheWorldUnchanged(string value)
	{
		var hud=Fixture(BuildCategory.Shops,out var level,out var economy,out var item,out _,out _);var context=Context();var id=level.GetEconomyInstance(item)!.Value;economy.TryGetObject(id,out var state);var price=state.Price;var cash=economy.Balance;var opened=item.IsOpen;
		Open(hud,context);hud.Update(context,UiInput.Type(value,10),false,false,false);hud.Update(context,UiInput.Key(UiKeys.Accept),false,false,false);
		Assert.AreEqual("shopPrice",hud.Stack.Top!.Name);Assert.AreEqual(price,state.Price);Assert.AreEqual(cash,economy.Balance);Assert.AreEqual(opened,item.IsOpen);
		Assert.AreEqual(SupplementaryStrings.Get(OpenTpwText.InvalidPrice,"English"),((UiLabel)hud.Stack.Top.Find("error")!).Text());
	}

	[TestMethod]
	public void TruncatedLongPasteCannotBecomeADifferentValidPrice()
	{
		var hud=Fixture(BuildCategory.Shops,out var level,out var economy,out var item,out _,out _);var context=Context();economy.TryGetObject(level.GetEconomyInstance(item)!.Value,out var state);var before=state.Price;
		Open(hud,context);hud.Update(context,UiInput.Type(new string('0',54)+"21474836470",10),false,false,false);
		Assert.AreEqual(64,((UiTextField)hud.Stack.Top!.Find("price")!).Text.Length,"The existing widget keeps a bounded prefix.");
		hud.Update(context,UiInput.Key(UiKeys.Accept),false,false,false);Assert.AreEqual(before,state.Price);Assert.AreEqual("shopPrice",hud.Stack.Top!.Name,"A prefix that parses as intMax must remain invalid.");
	}

	[DataTestMethod,DataRow(false),DataRow(true)]
	public void CancelWinsOverConfirmInTheSameModalFrame(bool rightMouse)
	{
		var hud=Fixture(BuildCategory.Shops,out var level,out var economy,out var item,out _,out _);var context=Context();economy.TryGetObject(level.GetEconomyInstance(item)!.Value,out var state);var before=state.Price;
		Open(hud,context);hud.Update(context,UiInput.Type("45",10),false,false,false);
		var cancel=new UiInput(new N.Vector2(-1),false,false,false,rightMouse,UiKeys.Accept|(rightMouse?UiKeys.None:UiKeys.Back));hud.Update(context,cancel,false,false,false);
		Assert.AreEqual(before,state.Price);Assert.AreEqual("hud",hud.Stack.Top!.Name);
	}

	[DataTestMethod,DataRow("cancel"),DataRow("delete"),DataRow("selection"),DataRow("unmap"),DataRow("readonly"),DataRow("economy"),DataRow("capture"),DataRow("remap"),DataRow("record"),DataRow("tool"),DataRow("retract"),DataRow("cancelRetained"),DataRow("otherTop")]
	public void CancelOrStaleOwnershipNeverCommits(string change)
	{
		var hud=Fixture(BuildCategory.Shops,out var level,out var economy,out var item,out var other,out _);var context=Context();economy.TryGetObject(level.GetEconomyInstance(item)!.Value,out var state);var before=state.Price;
		Open(hud,context);hud.Update(context,UiInput.Type("45",10),false,false,false);
		if(change=="delete") item.Delete();if(change=="selection") hud.SelectObject(other);if(change=="unmap") ((Dictionary<OriginalObject,int>)typeof(Level).GetField("economyInstances",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(level)!).Remove(item);
		if(change=="readonly") Set(level,"<Visit>k__BackingField",Empty<ParkVisitInfo>());
		if(change=="remap") ((Dictionary<OriginalObject,int>)typeof(Level).GetField("economyInstances",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(level)!)[item]=level.GetEconomyInstance(other)!.Value;
		if(change=="record") ((SortedDictionary<int,ParkObjectState>)typeof(ParkEconomy).GetField("objects",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(economy)!)[state.Id]=new ParkObjectState{Id=state.Id,InfoId=state.InfoId,Kind=state.Kind,Price=99};
		if(change=="tool") level.IsPlacing=true;if(change=="retract")hud.SetInfoArm(false);
		if(change is "cancelRetained" or "otherTop") { var apply=(UiButton)hud.Stack.Top!.Find("apply")!;if(change=="cancelRetained")hud.Stack.Pop();else hud.Stack.Push(new UiScreen("other"));apply.Activate();Assert.AreEqual(before,state.Price);return; }
		if(change=="economy") Set(hud,"<Status>k__BackingField",new EconomyParkStatus(()=>EconomyTestData.Park()));
		hud.Update(context,UiInput.Key(change=="cancel"?UiKeys.Back:UiKeys.Accept),change=="capture",false,false);
		Assert.AreEqual(before,state.Price);if(change=="remap") { economy.TryGetObject(level.GetEconomyInstance(other)!.Value,out var untouched);Assert.AreEqual(before,untouched.Price); } if(change=="record") { economy.TryGetObject(state.Id,out var replacement);Assert.AreEqual(99,replacement.Price); } if(change=="cancel")Assert.AreEqual("hud",hud.Stack.Top!.Name);else Assert.AreEqual("shopPrice",hud.Stack.Top!.Name);
	}

	[DataTestMethod,DataRow("ride"),DataRow("readonly"),DataRow("none"),DataRow("tpi"),DataRow("mouse"),DataRow("keyboard"),DataRow("text"),DataRow("overlay"),DataRow("tool")]
	public void UnsupportedTargetsAndInputOwnersCannotOpen(string gate)
	{
		var hud=Fixture(gate=="ride"?BuildCategory.Rides:BuildCategory.Shops,out var level,out _,out _,out _,out _);var context=Context();
		if(gate=="readonly") Set(level,"<Visit>k__BackingField",Empty<ParkVisitInfo>());if(gate=="none")Set(hud,"<Status>k__BackingField",new NoEconomyStatus());if(gate=="tpi")GameEditions.Select(GameEdition.ThemeParkInc);if(gate=="text")Input.TextEntryActive=true;if(gate=="overlay")hud.Stack.Push(new UiScreen("overlay"));if(gate=="tool")level.IsPlacing=true;
		hud.Update(context,UiInput.Idle(new N.Vector2(1500,1000)),gate=="mouse",gate=="keyboard",false);
		var button=hud.Screen.Find("shopPrice") as UiButton;Assert.IsNotNull(button);Assert.IsFalse(button.Visible&&button.Enabled);
		hud.Screen.Focus(button);hud.Update(context,UiInput.Key(UiKeys.Accept),gate=="mouse",gate=="keyboard",false);Assert.IsFalse(hud.Stack.Screens.Any(s=>s.Name=="shopPrice"));
	}
	[DataTestMethod, DataRow( BuildCategory.Shops ), DataRow( BuildCategory.Sideshows )]
	public void ReviewerMouseActionEditsTheSecondIdenticalOwnerAndPostsItsRealPayment( BuildCategory category )
	{
		var hud = Fixture( category, out var level, out var economy, out var first, out var second, out var bridge );
		var context = Context();
		economy.TryGetObject( level.GetEconomyInstance( first )!.Value, out var firstState );
		economy.TryGetObject( level.GetEconomyInstance( second )!.Value, out var secondState );
		Assert.AreEqual( firstState.InfoId, secondState.InfoId );
		var firstPrice = firstState.Price;
		var cost = secondState.CostOfGoods;
		var chance = secondState.ChanceOfLosingPercent;
		hud.SelectObject( second );
		hud.Update( context, UiInput.Idle( new N.Vector2( 1500, 1000 ) ), false, false, false );
		Assert.IsFalse( hud.Screen.Find( "rideView" )!.Visible, "The shared slot cannot expose both actions." );
		var button = (UiButton)hud.Screen.Find( "shopPrice" )!;
		Assert.IsTrue( button.Visible && button.Enabled );
		hud.Update( context, UiInput.Click( button.ScreenRect( context.Canvas ).Center ), false, false, false );
		Assert.AreEqual( "shopPrice", hud.Stack.Top!.Name );
		hud.Update( context, UiInput.Type( "61", 64 ), false, false, false );
		var apply = hud.Stack.Top!.Find( "apply" )!;
		hud.Update( context, UiInput.Click( apply.ScreenRect( context.Canvas ).Center ), false, false, false );
		Assert.AreEqual( "hud", hud.Stack.Top!.Name );
		Assert.AreEqual( firstPrice, firstState.Price );
		Assert.AreEqual( 61, secondState.Price );
		Assert.AreEqual( cost, secondState.CostOfGoods );
		Assert.AreEqual( chance, secondState.ChanceOfLosingPercent );
		var balance = economy.Balance;
		var totals = economy.Ledger.CurrentTotals.ToArray();
		Assert.IsFalse( bridge.TryPayVisit( second.Visitors, 60, out _ ) );
		Assert.AreEqual( balance, economy.Balance );
		CollectionAssert.AreEqual( totals, economy.Ledger.CurrentTotals.ToArray() );
		Assert.IsTrue( bridge.TryPayVisit( second.Visitors, 100, out var paid ) );
		Assert.AreEqual( 61, paid );
		Assert.AreEqual( 0, firstState.CustomersThisMonth );
		Assert.AreEqual( 1, secondState.CustomersThisMonth );
		Assert.AreEqual( 61L, secondState.TakingsThisMonth );
		Assert.AreEqual( 61L, economy.Ledger.CurrentTotals[category == BuildCategory.Shops ? LedgerCategory.ShopTakings : LedgerCategory.SideshowTakings] );
	}

	[DataTestMethod, DataRow( "mouse" ), DataRow( "keyboard" ), DataRow( "text" )]
	public void ReviewerCapturedApplyCanRetryAfterCaptureEndsWithoutChangingTheStagedTarget( string capture )
	{
		var hud = Fixture( BuildCategory.Shops, out var level, out var economy, out var item, out var other, out _ );
		var context = Context();
		economy.TryGetObject( level.GetEconomyInstance( item )!.Value, out var state );
		economy.TryGetObject( level.GetEconomyInstance( other )!.Value, out var otherState );
		var before = state.Price;
		var otherPrice = otherState.Price;
		Open( hud, context );
		hud.Update( context, UiInput.Type( "61", 64 ), false, false, false );
		var modal = hud.Stack.Top!;
		hud.Update( context, UiInput.Key( UiKeys.Accept ), capture == "mouse", capture == "keyboard", capture == "text" );
		Assert.AreSame( modal, hud.Stack.Top );
		Assert.AreEqual( before, state.Price );
		Assert.AreEqual( "61", ((UiTextField)modal.Find( "price" )!).Text );
		hud.Update( context, UiInput.Key( UiKeys.Accept ), false, false, false );
		Assert.AreEqual( "hud", hud.Stack.Top!.Name );
		Assert.AreEqual( 61, state.Price );
		Assert.AreEqual( otherPrice, otherState.Price );
	}

	[DataTestMethod]
	[DataRow( "English" )]
	[DataRow( "Danish" )]
	[DataRow( "Dutch" )]
	[DataRow( "French" )]
	[DataRow( "German" )]
	[DataRow( "Swedish" )]
	public void ReviewerLocalizedErrorsAndInvariantDecimalInputReachTheActualEditor( string language )
	{
		var hud = Fixture( BuildCategory.Shops, out var level, out var economy, out var item, out _, out _ );
		Set( hud, "strings", OriginalUiTests.FakeStrings( language ) );
		economy.TryGetObject( level.GetEconomyInstance( item )!.Value, out var state );
		var before = state.Price;
		var culture = System.Globalization.CultureInfo.CurrentCulture;
		try
		{
			System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo( "fr-FR" );
			var context = Context();
			Open( hud, context );
			hud.Update( context, UiInput.Type( "1,5", 64 ), false, false, false );
			hud.Update( context, UiInput.Key( UiKeys.Accept ), false, false, false );
			Assert.AreEqual( before, state.Price );
			Assert.AreEqual( SupplementaryStrings.Get( OpenTpwText.InvalidPrice, language ), ((UiLabel)hud.Stack.Top!.Find( "error" )!).Text() );
			hud.Update( context, UiInput.Type( "61", 64 ), false, false, false );
			hud.Update( context, UiInput.Key( UiKeys.Accept ), false, false, false );
			Assert.AreEqual( "hud", hud.Stack.Top!.Name );
			Assert.AreEqual( 61, state.Price );
		}
		finally
		{
			System.Globalization.CultureInfo.CurrentCulture = culture;
		}
	}
}
