using System;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace OpenTPW.Tests;

/// <summary>The shop window's settings in the economy: quality of goods (0x4e2190) and the amount of the special ingredient (0x4e21e0), saved with the park.</summary>
[TestClass]
public class ShopSettingsTests
{
	[TestMethod]
	public void ShopSettingsStoreTheQualityByteAndTheRawIngredientAmount()
	{
		var economy = EconomyTestData.Park();
		var shop = economy.RegisterExisting( 1203 );
		Assert.AreEqual( (50, 50), (shop.QualityOfGoods, shop.IngredientAmount), "the build's 50 and 50 (0x004DB389, 0x4db3b3)" );
		economy.SetShopSettings( shop.Id, qualityOfGoods: 100, ingredientAmount: 75 );
		Assert.AreEqual( (100, 75), (shop.QualityOfGoods, shop.IngredientAmount) );
		economy.SetShopSettings( shop.Id, qualityOfGoods: 300 );
		Assert.AreEqual( 44, shop.QualityOfGoods, "the setter keeps the low byte" );
		Assert.AreEqual( 75, shop.IngredientAmount, "null leaves the amount" );
		var ride = economy.RegisterExisting( 1100 );
		Assert.ThrowsException<InvalidOperationException>( () => economy.SetShopSettings( ride.Id, qualityOfGoods: 0 ) );
	}

	[TestMethod]
	public void ShopSettingsSurviveTheSaveAndOldSavesLoadTheBuildDefaults()
	{
		var economy = EconomyTestData.Park();
		var untouched = economy.RegisterExisting( 1203 );
		var changed = economy.RegisterExisting( 1203 );
		economy.SetShopSettings( changed.Id, qualityOfGoods: 0, ingredientAmount: 100 );
		var json = ParkSaveFile.Serialize( economy );
		var restored = ParkSaveFile.Restore( ParkSaveFile.Deserialize( Encoding.UTF8.GetBytes( json ) ), EconomyTestData.Settings(), EconomyTestData.Catalog() );
		Assert.IsTrue( restored.TryGetObject( changed.Id, out var back ) );
		Assert.AreEqual( (0, 100), (back.QualityOfGoods, back.IngredientAmount) );
		Assert.IsTrue( restored.TryGetObject( untouched.Id, out var plain ) );
		Assert.AreEqual( (50, 50), (plain.QualityOfGoods, plain.IngredientAmount) );
		var data = ParkSaveFile.Capture( economy );
		Assert.IsNull( data.Objects.Single( item => item.Id == untouched.Id ).QualityOfGoods, "an untouched shop writes no setting fields" );
		Assert.IsNull( data.Objects.Single( item => item.Id == untouched.Id ).IngredientAmount );
		StringAssert.Contains( json, "\"QualityOfGoods\": 0" );
		var bad = Encoding.UTF8.GetBytes( json.Replace( "\"QualityOfGoods\": 0", "\"QualityOfGoods\": 256" ) );
		Assert.ThrowsException<System.IO.InvalidDataException>( () => ParkSaveFile.Deserialize( bad ), "a quality outside a byte is refused" );
	}
}
