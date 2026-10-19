using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTPW.Server;

namespace OpenTPW.Tests;

/// <summary>
/// How the server sends the browser game's files (docs/SERVER.md, docs/BROWSER-PERFORMANCE.md "Transfer and memory"): the precompressed copy the browser
/// accepts, the content type of a compressed copy, what is cached for good and what is revalidated.
/// </summary>
[TestClass]
public class WebClientTransportTests
{
	private static readonly string Hash = "0123456789abcdef";

	[TestMethod]
	[DataRow( "gzip, deflate, br, zstd", true, true )]
	[DataRow( "gzip, br;q=0", false, true )]
	[DataRow( "br;q=0.5", true, false )]
	[DataRow( "BR", true, false )]
	[DataRow( "gzip", false, true )]
	[DataRow( "*", false, false )]
	[DataRow( "", false, false )]
	[DataRow( "brotli", false, false )]
	public void AcceptEncodingNamesTheCodingsTheBrowserAccepts( string header, bool brotli, bool gzip )
	{
		// a coding listed with q=0 is refused; a name that only starts like another is not that coding
		Assert.AreEqual( brotli, OpenTPW.Server.WebClient.AcceptsEncoding( header, "br" ) );
		Assert.AreEqual( gzip, OpenTPW.Server.WebClient.AcceptsEncoding( header, "gzip" ) );
	}

	private static string Publish()
	{
		var web = Path.Combine( Path.GetTempPath(), $"opentpw-web-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( Path.Combine( web, "_framework" ) );
		Directory.CreateDirectory( Path.Combine( web, "pools-sprites", "Swim" ) );
		File.WriteAllText( Path.Combine( web, "index.html" ), "<!doctype html><title>OpenTPW</title>" );
		File.WriteAllText( Path.Combine( web, "main.js" ), "// plain" );
		File.WriteAllBytes( Path.Combine( web, "main.js.br" ), [1, 2, 3] );
		File.WriteAllBytes( Path.Combine( web, "main.js.gz" ), [4, 5, 6, 7] );
		// a file with only a gzip copy (what the runtime's ICU data would be without a Brotli one)
		File.WriteAllBytes( Path.Combine( web, "_framework", "icudt_no_CJK.abcde12345.dat" ), [0, 0, 0, 0, 0, 0, 0, 0] );
		File.WriteAllBytes( Path.Combine( web, "_framework", "icudt_no_CJK.abcde12345.dat.gz" ), [9, 9, 9] );
		File.WriteAllBytes( Path.Combine( web, "pools-sprites", "Swim", "SPR_SU.TPC" ), new byte[2000] );
		File.WriteAllBytes( Path.Combine( web, "pools-sprites", "Swim", "SPR_SU.TPC.br" ), [7, 7] );
		File.WriteAllBytes( Path.Combine( web, "pools-sprites", "Swim", "SPR_SU.ESP" ), new byte[350] );
		return web;
	}

	private static async Task<HttpResponseMessage> GetAsync( HttpClient http, string path, string? acceptEncoding = null, string? ifNoneMatch = null )
	{
		using var request = new HttpRequestMessage( HttpMethod.Get, path );
		if ( acceptEncoding != null )
			request.Headers.AcceptEncoding.ParseAdd( acceptEncoding );
		if ( ifNoneMatch != null )
			request.Headers.TryAddWithoutValidation( "If-None-Match", ifNoneMatch );
		return await http.SendAsync( request );
	}

	[TestMethod]
	public async Task SendsTheBestPrecompressedCopyTheBrowserAccepts()
	{
		var web = Publish();
		await using var server = await LoopbackServer.StartAsync( options => options.WebClientDirectory = web );
		try
		{
			using var http = new HttpClient( new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None } ) { BaseAddress = server.Url };

			using var brotli = await GetAsync( http, "/main.js", "gzip, deflate, br, zstd" );
			Assert.AreEqual( "br", brotli.Content.Headers.ContentEncoding.Single() );
			CollectionAssert.AreEqual( new byte[] { 1, 2, 3 }, await brotli.Content.ReadAsByteArrayAsync() );
			Assert.IsTrue( brotli.Content.Headers.ContentType!.MediaType!.Contains( "javascript" ) );

			// a browser without Brotli gets the gzip copy, so no proxy has to compress the file again for every request
			using var gzip = await GetAsync( http, "/main.js", "gzip" );
			Assert.AreEqual( "gzip", gzip.Content.Headers.ContentEncoding.Single() );
			CollectionAssert.AreEqual( new byte[] { 4, 5, 6, 7 }, await gzip.Content.ReadAsByteArrayAsync() );
			Assert.IsTrue( gzip.Content.Headers.ContentType!.MediaType!.Contains( "javascript" ) );

			// Brotli refused with q=0 is not sent
			using var refused = await GetAsync( http, "/main.js", "br;q=0, gzip" );
			Assert.AreEqual( "gzip", refused.Content.Headers.ContentEncoding.Single() );

			using var plain = await GetAsync( http, "/main.js" );
			Assert.AreEqual( 0, plain.Content.Headers.ContentEncoding.Count );
			Assert.AreEqual( "// plain", await plain.Content.ReadAsStringAsync() );
			Assert.AreEqual( "Accept-Encoding", plain.Headers.Vary.Single() );

			using var icu = await GetAsync( http, "/_framework/icudt_no_CJK.abcde12345.dat", "gzip, br" );
			Assert.AreEqual( "gzip", icu.Content.Headers.ContentEncoding.Single() );
			Assert.AreEqual( "application/octet-stream", icu.Content.Headers.ContentType!.MediaType );
			Assert.AreEqual( "public, max-age=31536000, immutable", icu.Headers.CacheControl!.ToString() );
		}
		finally
		{
			server.DeleteData();
			Directory.Delete( web, true );
		}
	}

	[TestMethod]
	public async Task PoolSpriteFilesAreCompressedAndCachedByTheirContentHash()
	{
		var web = Publish();
		await using var server = await LoopbackServer.StartAsync( options => options.WebClientDirectory = web );
		try
		{
			using var http = new HttpClient( new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None } ) { BaseAddress = server.Url };

			using var sprite = await GetAsync( http, $"/pools-sprites/Swim/SPR_SU.TPC?v={Hash}", "gzip, br" );
			Assert.AreEqual( "br", sprite.Content.Headers.ContentEncoding.Single() );
			Assert.AreEqual( "application/octet-stream", sprite.Content.Headers.ContentType!.MediaType );
			// the address names the content, so a changed file is a new address and the copy may be kept for good
			Assert.AreEqual( "public, max-age=31536000, immutable", sprite.Headers.CacheControl!.ToString() );

			// without the hash (or with something else) the file is revalidated like the page's own scripts
			foreach ( var path in new[] { "/pools-sprites/Swim/SPR_SU.TPC", "/pools-sprites/Swim/SPR_SU.TPC?v=short", "/pools-sprites/Swim/SPR_SU.TPC?x=0123456789abcdef" } )
			{
				using var revalidated = await GetAsync( http, path, "br" );
				StringAssert.Contains( revalidated.Headers.CacheControl!.ToString(), "no-cache", path );
			}

			// the hash does not make other files permanent
			using var script = await GetAsync( http, $"/main.js?v={Hash}", "br" );
			StringAssert.Contains( script.Headers.CacheControl!.ToString(), "no-cache" );

			// the tiny .ESP files have no compressed copy and stay as they are
			using var esp = await GetAsync( http, $"/pools-sprites/Swim/SPR_SU.ESP?v={Hash}", "gzip, br" );
			Assert.AreEqual( 0, esp.Content.Headers.ContentEncoding.Count );
			Assert.AreEqual( 350, (await esp.Content.ReadAsByteArrayAsync()).Length );
		}
		finally
		{
			server.DeleteData();
			Directory.Delete( web, true );
		}
	}

	[TestMethod]
	public async Task ARevalidatedFileAnswersNotModified()
	{
		var web = Publish();
		await using var server = await LoopbackServer.StartAsync( options => options.WebClientDirectory = web );
		try
		{
			using var http = new HttpClient( new HttpClientHandler { AutomaticDecompression = DecompressionMethods.None } ) { BaseAddress = server.Url };
			using var first = await GetAsync( http, "/main.js", "br" );
			var etag = first.Headers.ETag?.ToString();
			Assert.IsNotNull( etag, "the compressed copy has an ETag" );
			using var second = await GetAsync( http, "/main.js", "br", etag );
			Assert.AreEqual( HttpStatusCode.NotModified, second.StatusCode );
			// the copies of different codings are different entities
			using var gzip = await GetAsync( http, "/main.js", "gzip" );
			Assert.AreNotEqual( etag, gzip.Headers.ETag?.ToString() );
		}
		finally
		{
			server.DeleteData();
			Directory.Delete( web, true );
		}
	}
}
