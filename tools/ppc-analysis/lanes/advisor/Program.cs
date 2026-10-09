try
{
	var count = DriverTests.Run();
	Console.WriteLine( $"OriginalAdvisorLipDriver: {count} tests passed." );
	return 0;
}
catch ( Exception exception )
{
	Console.Error.WriteLine( exception );
	return 1;
}
