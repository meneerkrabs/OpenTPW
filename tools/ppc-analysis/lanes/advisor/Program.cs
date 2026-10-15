try
{
	var count = DriverTests.Run() + ScoreQueueTests.Run();
	Console.WriteLine( $"OriginalAdvisor helpers: {count} tests passed." );
	return 0;
}
catch ( Exception exception )
{
	Console.Error.WriteLine( exception );
	return 1;
}
