namespace OpenTPW;

/// <summary>Labels without an original string; see <see cref="OnlineStrings"/>.</summary>
public enum OnlineLabel
{
	ServerAddress, Register, LogIn, LogOut, ExportPark, ImportFile, Inbox, VisitReadOnly, Chat, ReadOnlyVisit,
	RateLimited, NoBuddies, OnlineOff, Refresh, Vote, Report, To, PutInOutbox, SavedTo, FileSharing,
	DeleteAccount, DeleteAccountWarning, AccountDeleted, News, NoNews, ShownOnWebsite, NotShownOnWebsite,
}

/// <summary>
/// Online UI text. Original strings are used where they exist (indices verified against the English
/// tables by tests); OpenTPW-only labels come from <see cref="Supplementary"/> in the six verified
/// languages. [EXT:ONLINE-063] Kept in this file until the frontend slice's SupplementaryStrings lands.
/// </summary>
public static class OnlineStrings
{
	// [DATA:Language/English/UITEXT.str] (the UIStrings enum is off by one from about 206 on; these are verified).
	public const int GoOnline = 1;
	public const int GoOffline = 2;
	public const int PublishPark = 5;
	public const int SendPostcard = 207;
	public const int MessageTitle = 208;
	public const int MessageText = 209;
	public const int PostcardGreeting = 216;
	public const int ParkName = 218;
	public const int Description = 219;
	public const int OnlineLogin = 235;
	public const int LoginName = 236;
	public const int Password = 237;
	public const int PublishingPark = 256;
	public const int DownloadingPark = 257;
	public const int CreatedBy = 274;
	public const int NumberOfVisits = 278;
	public const int NumberOfVotes = 279;
	public const int VisitedBefore = 280;
	public const int VotedFor = 281;
	public const int ListParks = 292;
	public const int FindParks = 298;
	public const int SearchName = 305;
	public const int NoSearchResults = 306;
	public const int UnsentPostcards = 308;
	public const int Send = 311;
	public const int Delete = 312;
	public const int SendingOutbox = 313;
	public const int ParkPublished = 405;
	public const int MissingRides = 410;
	public const int GettingGameNews = 245;
	public const int GameNews = 267;
	public const int SystemNews = 268;
	public const int OnlineWorld = 422;
	public const int VisitedParks = 423;
	public const int OutboxWaiting = 472;
	// [DATA:Language/English/ERRORMSG.str]
	public const int ErrorNotTpwFile = 84;
	public const int ErrorFileTooLarge = 85;
	public const int ErrorInvalidLogin = 73;
	public const int ErrorConnect = 79;
	public const int ErrorChatConnect = 69;
	// [DATA:Language/English/CHAT_COMMANDS.str:160] "System message: "
	public const int SystemMessage = 160;

	private static StringFile? uiText, errors, chat, themes;
	private static bool loadFailed;

	private static string[]? Table( ref StringFile? cache, string name )
	{
		if ( cache == null && !loadFailed && GameLanguage.IsSelected )
		{
			try { cache = GameLanguage.Current.LoadStrings( name ); }
			catch ( Exception exception ) when ( exception is IOException or InvalidDataException or FileNotFoundException )
			{
				loadFailed = true;
				Log.Warning( $"Online strings: {name} unavailable ({exception.Message})." );
			}
		}
		return cache?.Entries;
	}

	private static string Entry( string[]? table, int index, string fallback ) =>
		table != null && index >= 0 && index < table.Length && table[index].Length > 0 ? table[index] : fallback;

	public static string Ui( int index, string fallback ) => Entry( Table( ref uiText, "UITEXT.str" ), index, fallback );
	public static string Error( int index, string fallback ) => Entry( Table( ref errors, "ERRORMSG.str" ), index, fallback );
	public static string[]? ChatCommands => Table( ref chat, "CHAT_COMMANDS.str" );

	/// <summary>
	/// Theme name of a level. [DATA:Language/*/THEMENAMES.str] order Lost Kingdom, Halloween World, Wonder Land,
	/// Space Zone; jungle = Lost Kingdom per lobby.wad lobby.txt ISLAND(0,…,"Lost Kingdom"); the others by name.
	/// </summary>
	public static string Theme( string level )
	{
		var index = level switch { "jungle" => 0, "hallow" => 1, "fantasy" => 2, "space" => 3, _ => -1 };
		return index < 0 ? level : Entry( Table( ref themes, "THEMENAMES.str" ), index, level );
	}

	/// <summary>Renders a chat response: the original prefix string followed by its arguments.</summary>
	public static string ChatNotice( int index, IReadOnlyList<string>? args )
	{
		var text = Entry( ChatCommands, index, $"[{index}]" );
		var joined = args == null ? "" : string.Join( " ", args.Where( arg => arg.Length > 0 ) );
		return (text + joined).Trim();
	}

	public static string Get( OnlineLabel label )
	{
		var language = GameLanguage.IsSelected ? GameLanguage.Current.Name : GameLanguage.DefaultLanguage;
		return Supplementary.TryGetValue( language, out var table ) && table.TryGetValue( label, out var text ) ? text : Supplementary[GameLanguage.DefaultLanguage][label];
	}

	/// <summary>OpenTPW labels (no original string exists) in English, Danish, Dutch, French, German and Swedish.</summary>
	public static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<OnlineLabel, string>> Supplementary = new Dictionary<string, IReadOnlyDictionary<OnlineLabel, string>>
	{
		["English"] = new Dictionary<OnlineLabel, string>
		{
			[OnlineLabel.ServerAddress] = "Server address", [OnlineLabel.Register] = "Register", [OnlineLabel.LogIn] = "Log in", [OnlineLabel.LogOut] = "Log out",
			[OnlineLabel.ExportPark] = "Export park to file", [OnlineLabel.ImportFile] = "Import file", [OnlineLabel.Inbox] = "Inbox",
			[OnlineLabel.VisitReadOnly] = "Visit (read-only)", [OnlineLabel.Chat] = "Chat",
			[OnlineLabel.ReadOnlyVisit] = "Read-only visit: building and saving are disabled.",
			[OnlineLabel.RateLimited] = "Slow down: too many chat messages.", [OnlineLabel.NoBuddies] = "Your buddy list is empty.",
			[OnlineLabel.OnlineOff] = "Online play is off. Enter the address of an OpenTPW server you trust to opt in.",
			[OnlineLabel.Refresh] = "Refresh", [OnlineLabel.Vote] = "Vote", [OnlineLabel.Report] = "Report",
			[OnlineLabel.To] = "To (player names, comma separated)", [OnlineLabel.PutInOutbox] = "Put in outbox", [OnlineLabel.SavedTo] = "Saved to: ",
			[OnlineLabel.FileSharing] = "File sharing",
			[OnlineLabel.DeleteAccount] = "Delete account", [OnlineLabel.DeleteAccountWarning] = "This permanently deletes your account, your published parks and your inbox on this server. Enter your name and password to confirm.",
			[OnlineLabel.AccountDeleted] = "Your account has been deleted.",
			[OnlineLabel.News] = "News", [OnlineLabel.NoNews] = "This server has no news.",
			[OnlineLabel.ShownOnWebsite] = "Also on the OpenTPW website: yes", [OnlineLabel.NotShownOnWebsite] = "Also on the OpenTPW website: no",
		},
		["Danish"] = new Dictionary<OnlineLabel, string>
		{
			[OnlineLabel.ServerAddress] = "Serveradresse", [OnlineLabel.Register] = "Opret konto", [OnlineLabel.LogIn] = "Log ind", [OnlineLabel.LogOut] = "Log ud",
			[OnlineLabel.ExportPark] = "Eksportér park til fil", [OnlineLabel.ImportFile] = "Importér fil", [OnlineLabel.Inbox] = "Indbakke",
			[OnlineLabel.VisitReadOnly] = "Besøg (kun visning)", [OnlineLabel.Chat] = "Chat",
			[OnlineLabel.ReadOnlyVisit] = "Besøg med kun visning: bygning og gemning er slået fra.",
			[OnlineLabel.RateLimited] = "Langsommere: for mange chatbeskeder.", [OnlineLabel.NoBuddies] = "Din venneliste er tom.",
			[OnlineLabel.OnlineOff] = "Onlinespil er slået fra. Indtast adressen på en OpenTPW-server, du stoler på, for at slå det til.",
			[OnlineLabel.Refresh] = "Opdatér", [OnlineLabel.Vote] = "Stem", [OnlineLabel.Report] = "Anmeld",
			[OnlineLabel.To] = "Til (spillernavne, adskilt med komma)", [OnlineLabel.PutInOutbox] = "Læg i udbakke", [OnlineLabel.SavedTo] = "Gemt i: ",
			[OnlineLabel.FileSharing] = "Fildeling",
			[OnlineLabel.DeleteAccount] = "Slet konto", [OnlineLabel.DeleteAccountWarning] = "Dette sletter permanent din konto, dine offentliggjorte parker og din indbakke på denne server. Indtast dit navn og din adgangskode for at bekræfte.",
			[OnlineLabel.AccountDeleted] = "Din konto er slettet.",
			[OnlineLabel.News] = "Nyheder", [OnlineLabel.NoNews] = "Denne server har ingen nyheder.",
			[OnlineLabel.ShownOnWebsite] = "Også på OpenTPW-webstedet: ja", [OnlineLabel.NotShownOnWebsite] = "Også på OpenTPW-webstedet: nej",
		},
		["Dutch"] = new Dictionary<OnlineLabel, string>
		{
			[OnlineLabel.ServerAddress] = "Serveradres", [OnlineLabel.Register] = "Registreren", [OnlineLabel.LogIn] = "Inloggen", [OnlineLabel.LogOut] = "Uitloggen",
			[OnlineLabel.ExportPark] = "Park naar bestand exporteren", [OnlineLabel.ImportFile] = "Bestand importeren", [OnlineLabel.Inbox] = "Postvak IN",
			[OnlineLabel.VisitReadOnly] = "Bezoeken (alleen bekijken)", [OnlineLabel.Chat] = "Chat",
			[OnlineLabel.ReadOnlyVisit] = "Alleen bekijken: bouwen en opslaan zijn uitgeschakeld.",
			[OnlineLabel.RateLimited] = "Rustig aan: te veel chatberichten.", [OnlineLabel.NoBuddies] = "Je vriendenlijst is leeg.",
			[OnlineLabel.OnlineOff] = "Online spelen staat uit. Voer het adres in van een OpenTPW-server die je vertrouwt om mee te doen.",
			[OnlineLabel.Refresh] = "Vernieuwen", [OnlineLabel.Vote] = "Stemmen", [OnlineLabel.Report] = "Melden",
			[OnlineLabel.To] = "Aan (spelersnamen, gescheiden door komma's)", [OnlineLabel.PutInOutbox] = "In Postvak UIT plaatsen", [OnlineLabel.SavedTo] = "Opgeslagen in: ",
			[OnlineLabel.FileSharing] = "Bestanden delen",
			[OnlineLabel.DeleteAccount] = "Account verwijderen", [OnlineLabel.DeleteAccountWarning] = "Dit verwijdert je account, je gepubliceerde parken en je inbox op deze server definitief. Vul je naam en wachtwoord in om te bevestigen.",
			[OnlineLabel.AccountDeleted] = "Je account is verwijderd.",
			[OnlineLabel.News] = "Nieuws", [OnlineLabel.NoNews] = "Deze server heeft geen nieuws.",
			[OnlineLabel.ShownOnWebsite] = "Ook op de OpenTPW-website: ja", [OnlineLabel.NotShownOnWebsite] = "Ook op de OpenTPW-website: nee",
		},
		["French"] = new Dictionary<OnlineLabel, string>
		{
			[OnlineLabel.ServerAddress] = "Adresse du serveur", [OnlineLabel.Register] = "Créer un compte", [OnlineLabel.LogIn] = "Se connecter", [OnlineLabel.LogOut] = "Se déconnecter",
			[OnlineLabel.ExportPark] = "Exporter le parc vers un fichier", [OnlineLabel.ImportFile] = "Importer un fichier", [OnlineLabel.Inbox] = "Boîte de réception",
			[OnlineLabel.VisitReadOnly] = "Visiter (lecture seule)", [OnlineLabel.Chat] = "Chat",
			[OnlineLabel.ReadOnlyVisit] = "Visite en lecture seule : construction et sauvegarde désactivées.",
			[OnlineLabel.RateLimited] = "Ralentissez : trop de messages de chat.", [OnlineLabel.NoBuddies] = "Votre liste d'amis est vide.",
			[OnlineLabel.OnlineOff] = "Le jeu en ligne est désactivé. Saisissez l'adresse d'un serveur OpenTPW de confiance pour l'activer.",
			[OnlineLabel.Refresh] = "Actualiser", [OnlineLabel.Vote] = "Voter", [OnlineLabel.Report] = "Signaler",
			[OnlineLabel.To] = "À (noms des joueurs, séparés par des virgules)", [OnlineLabel.PutInOutbox] = "Mettre dans la boîte d'envoi", [OnlineLabel.SavedTo] = "Enregistré dans : ",
			[OnlineLabel.FileSharing] = "Partage de fichiers",
			[OnlineLabel.DeleteAccount] = "Supprimer le compte", [OnlineLabel.DeleteAccountWarning] = "Ceci supprime définitivement votre compte, vos parcs publiés et votre boîte de réception sur ce serveur. Saisissez votre nom et votre mot de passe pour confirmer.",
			[OnlineLabel.AccountDeleted] = "Votre compte a été supprimé.",
			[OnlineLabel.News] = "Actualités", [OnlineLabel.NoNews] = "Ce serveur n'a pas d'actualités.",
			[OnlineLabel.ShownOnWebsite] = "Aussi sur le site OpenTPW : oui", [OnlineLabel.NotShownOnWebsite] = "Aussi sur le site OpenTPW : non",
		},
		["German"] = new Dictionary<OnlineLabel, string>
		{
			[OnlineLabel.ServerAddress] = "Serveradresse", [OnlineLabel.Register] = "Registrieren", [OnlineLabel.LogIn] = "Anmelden", [OnlineLabel.LogOut] = "Abmelden",
			[OnlineLabel.ExportPark] = "Park in Datei exportieren", [OnlineLabel.ImportFile] = "Datei importieren", [OnlineLabel.Inbox] = "Posteingang",
			[OnlineLabel.VisitReadOnly] = "Besuchen (nur ansehen)", [OnlineLabel.Chat] = "Chat",
			[OnlineLabel.ReadOnlyVisit] = "Besuch zum Ansehen: Bauen und Speichern sind deaktiviert.",
			[OnlineLabel.RateLimited] = "Langsamer: zu viele Chatnachrichten.", [OnlineLabel.NoBuddies] = "Deine Kumpelliste ist leer.",
			[OnlineLabel.OnlineOff] = "Online-Spiel ist aus. Gib die Adresse eines OpenTPW-Servers ein, dem du vertraust, um es einzuschalten.",
			[OnlineLabel.Refresh] = "Aktualisieren", [OnlineLabel.Vote] = "Abstimmen", [OnlineLabel.Report] = "Melden",
			[OnlineLabel.To] = "An (Spielernamen, durch Kommas getrennt)", [OnlineLabel.PutInOutbox] = "In den Postausgang legen", [OnlineLabel.SavedTo] = "Gespeichert unter: ",
			[OnlineLabel.FileSharing] = "Dateien teilen",
			[OnlineLabel.DeleteAccount] = "Konto löschen", [OnlineLabel.DeleteAccountWarning] = "Dies löscht dein Konto, deine veröffentlichten Parks und deinen Posteingang auf diesem Server endgültig. Gib zur Bestätigung deinen Namen und dein Passwort ein.",
			[OnlineLabel.AccountDeleted] = "Dein Konto wurde gelöscht.",
			[OnlineLabel.News] = "Neuigkeiten", [OnlineLabel.NoNews] = "Dieser Server hat keine Neuigkeiten.",
			[OnlineLabel.ShownOnWebsite] = "Auch auf der OpenTPW-Website: ja", [OnlineLabel.NotShownOnWebsite] = "Auch auf der OpenTPW-Website: nein",
		},
		["Swedish"] = new Dictionary<OnlineLabel, string>
		{
			[OnlineLabel.ServerAddress] = "Serveradress", [OnlineLabel.Register] = "Registrera", [OnlineLabel.LogIn] = "Logga in", [OnlineLabel.LogOut] = "Logga ut",
			[OnlineLabel.ExportPark] = "Exportera parken till fil", [OnlineLabel.ImportFile] = "Importera fil", [OnlineLabel.Inbox] = "Inkorg",
			[OnlineLabel.VisitReadOnly] = "Besök (endast visning)", [OnlineLabel.Chat] = "Chatt",
			[OnlineLabel.ReadOnlyVisit] = "Besök med endast visning: byggande och sparande är avstängt.",
			[OnlineLabel.RateLimited] = "Lugna ner dig: för många chattmeddelanden.", [OnlineLabel.NoBuddies] = "Din kompislista är tom.",
			[OnlineLabel.OnlineOff] = "Onlinespel är avstängt. Ange adressen till en OpenTPW-server du litar på för att slå på det.",
			[OnlineLabel.Refresh] = "Uppdatera", [OnlineLabel.Vote] = "Rösta", [OnlineLabel.Report] = "Anmäl",
			[OnlineLabel.To] = "Till (spelarnamn, åtskilda med kommatecken)", [OnlineLabel.PutInOutbox] = "Lägg i utkorgen", [OnlineLabel.SavedTo] = "Sparad i: ",
			[OnlineLabel.FileSharing] = "Fildelning",
			[OnlineLabel.DeleteAccount] = "Radera konto", [OnlineLabel.DeleteAccountWarning] = "Detta raderar permanent ditt konto, dina publicerade parker och din inkorg på den här servern. Ange ditt namn och lösenord för att bekräfta.",
			[OnlineLabel.AccountDeleted] = "Ditt konto har raderats.",
			[OnlineLabel.News] = "Nyheter", [OnlineLabel.NoNews] = "Den här servern har inga nyheter.",
			[OnlineLabel.ShownOnWebsite] = "Även på OpenTPW-webbplatsen: ja", [OnlineLabel.NotShownOnWebsite] = "Även på OpenTPW-webbplatsen: nej",
		},
	};
}
