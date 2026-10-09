namespace OpenTPW.UI.Original;

/// <summary>
/// Labels the original string tables do not contain. These are OpenTPW additions, not original
/// data: options added by OpenTPW (window mode, upscaling, render/UI scale, language), and a few
/// messages for OpenTPW-specific behaviour (read-only original parks, unsimulated values). Every
/// label is translated for the six verified languages and drawn with the original BF4 fonts; a test
/// checks that every character has a glyph in each language's fonts (docs/UI.md).
/// </summary>
// [EXT:strings] OpenTPW supplementary labels (display/upscaling/language rows and OpenTPW messages), not original data
public enum OpenTpwText
{
	Language,
	WindowMode,
	Windowed,
	Fullscreen,
	Borderless,
	Upscaling,
	UpscaleNative,
	UpscaleLinear,
	UpscaleNearest,
	RenderScale,
	UiScale,
	Automatic,
	EffectiveSize,
	Fallback,
	Back,
	DateFormat,
	OriginalParkReadOnly,
	ParkSaved,
	SaveFailed,
	OriginalParkEntry,
	SandboxParkEntry,
	NoSavedParks,
	CannotBuildHere,
	NotSimulated,
	Built,
	OnlyOnePrototypeRide,
	NotEnoughMoney,
	NotAvailable,
	EnhancedTextures,
	TexturePackMissing,
	GameFiles,
	GameFolder,
	CdFolder,
	ChangeFolder,
	RemoveFolder,
	NoFolder,
	RestartToApply,
	FolderNotUsable,
	BonusFolder,
	BonusNotFound,
}

public static class SupplementaryStrings
{
	/// <summary>Language names shown in the options screen, in their own language.</summary>
	public static readonly IReadOnlyDictionary<string, string> LanguageNames = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase )
	{
		["English"] = "English",
		["Danish"] = "Dansk",
		["Dutch"] = "Nederlands",
		["French"] = "Français",
		["German"] = "Deutsch",
		["Swedish"] = "Svenska",
	};

	private static readonly string[] Languages = { "English", "Danish", "Dutch", "French", "German", "Swedish" };

	// Columns follow Languages: English, Danish, Dutch, French, German, Swedish.
	private static readonly Dictionary<OpenTpwText, string[]> table = new()
	{
		[OpenTpwText.Language] = new[] { "Language:", "Sprog:", "Taal:", "Langue :", "Sprache:", "Språk:" },
		[OpenTpwText.WindowMode] = new[] { "Window mode:", "Vinduestilstand:", "Venstermodus:", "Mode d'affichage :", "Fenstermodus:", "Fönsterläge:" },
		[OpenTpwText.Windowed] = new[] { " Windowed", " I vindue", " Venster", " Fenêtré", " Fenster", " Fönster" },
		[OpenTpwText.Fullscreen] = new[] { " Full screen", " Fuld skærm", " Volledig scherm", " Plein écran", " Vollbild", " Helskärm" },
		[OpenTpwText.Borderless] = new[] { " Borderless", " Uden kant", " Randloos", " Sans bordure", " Randlos", " Kantlöst" },
		[OpenTpwText.Upscaling] = new[] { "Upscaling:", "Opskalering:", "Opschaling:", "Mise à l'échelle :", "Hochskalierung:", "Uppskalning:" },
		[OpenTpwText.UpscaleNative] = new[] { " Native", " Oprindelig", " Oorspronkelijk", " Native", " Nativ", " Ursprunglig" },
		[OpenTpwText.UpscaleLinear] = new[] { " Linear", " Lineær", " Lineair", " Linéaire", " Linear", " Linjär" },
		[OpenTpwText.UpscaleNearest] = new[] { " Nearest neighbour", " Nærmeste nabo", " Dichtstbijzijnde", " Plus proche voisin", " Nächster Nachbar", " Närmaste granne" },
		[OpenTpwText.RenderScale] = new[] { "Render scale:", "Renderskala:", "Renderschaal:", "Échelle de rendu :", "Renderskalierung:", "Renderingsskala:" },
		[OpenTpwText.UiScale] = new[] { "Interface scale:", "Skala for brugerflade:", "Interfaceschaal:", "Échelle de l'interface :", "Oberflächenskalierung:", "Gränssnittsskala:" },
		[OpenTpwText.Automatic] = new[] { " Automatic", " Automatisk", " Automatisch", " Automatique", " Automatisch", " Automatisk" },
		[OpenTpwText.EffectiveSize] = new[] { "Internal {0} x {1}, output {2} x {3}", "Intern {0} x {1}, output {2} x {3}", "Intern {0} x {1}, uitvoer {2} x {3}", "Interne {0} x {1}, sortie {2} x {3}", "Intern {0} x {1}, Ausgabe {2} x {3}", "Intern {0} x {1}, utdata {2} x {3}" },
		[OpenTpwText.Fallback] = new[] { "Fallback: {0}", "Reserve: {0}", "Terugval: {0}", "Repli : {0}", "Ausweichlösung: {0}", "Reserv: {0}" },
		[OpenTpwText.Back] = new[] { "Back", "Tilbage", "Terug", "Retour", "Zurück", "Tillbaka" },
		[OpenTpwText.DateFormat] = new[] { "Month {1}, Year {0}", "Måned {1}, år {0}", "Maand {1}, jaar {0}", "Mois {1}, année {0}", "Monat {1}, Jahr {0}", "Månad {1}, år {0}" },
		[OpenTpwText.OriginalParkReadOnly] = new[] {
			"Original parks are read-only and cannot be saved.",
			"Originale parker er skrivebeskyttede og kan ikke gemmes.",
			"Originele parken zijn alleen-lezen en kunnen niet worden opgeslagen.",
			"Les parcs d'origine sont en lecture seule et ne peuvent pas être sauvegardés.",
			"Originalparks sind schreibgeschützt und können nicht gespeichert werden.",
			"Originalparker är skrivskyddade och kan inte sparas." },
		[OpenTpwText.ParkSaved] = new[] { "Park saved (OpenTPW save).", "Park gemt (OpenTPW).", "Park opgeslagen (OpenTPW).", "Parc sauvegardé (OpenTPW).", "Park gespeichert (OpenTPW).", "Parken sparad (OpenTPW)." },
		[OpenTpwText.SaveFailed] = new[] { "Save failed: {0}", "Kunne ikke gemme: {0}", "Opslaan mislukt: {0}", "Échec de la sauvegarde : {0}", "Speichern fehlgeschlagen: {0}", "Det gick inte att spara: {0}" },
		[OpenTpwText.OriginalParkEntry] = new[] { "{0} - original park (read-only)", "{0} - original park (skrivebeskyttet)", "{0} - origineel park (alleen-lezen)", "{0} - parc d'origine (lecture seule)", "{0} - Originalpark (schreibgeschützt)", "{0} - originalpark (skrivskyddad)" },
		[OpenTpwText.SandboxParkEntry] = new[] { "OpenTPW sandbox park", "OpenTPW-sandkassepark", "OpenTPW-zandbakpark", "Parc bac à sable OpenTPW", "OpenTPW-Sandkastenpark", "OpenTPW-sandlådepark" },
		[OpenTpwText.NoSavedParks] = new[] { "No saved parks", "Ingen gemte parker", "Geen opgeslagen parken", "Aucun parc sauvegardé", "Keine gespeicherten Parks", "Inga sparade parker" },
		[OpenTpwText.CannotBuildHere] = new[] { "You can't build here", "Du kan ikke bygge her", "U kunt hier niet bouwen", "Impossible de construire ici", "Hier kann nicht gebaut werden", "Du kan inte bygga här" },
		[OpenTpwText.NotSimulated] = new[] { "Not simulated yet", "Endnu ikke simuleret", "Nog niet gesimuleerd", "Pas encore simulé", "Noch nicht simuliert", "Inte simulerad ännu" },
		[OpenTpwText.Built] = new[] { "{0} built.", "{0} er bygget.", "{0} gebouwd.", "{0} construit.", "{0} gebaut.", "{0} byggd." },
		[OpenTpwText.NotEnoughMoney] = new[] { "Not enough money", "Ikke penge nok", "Niet genoeg geld", "Pas assez d'argent", "Nicht genug Geld", "Inte tillräckligt med pengar" },
		[OpenTpwText.EnhancedTextures] = new[] { "Enhanced textures:", "Forbedrede teksturer:", "Verbeterde textures:", "Textures améliorées :", "Verbesserte Texturen:", "Förbättrade texturer:" },
		[OpenTpwText.TexturePackMissing] = new[] { "No pack built", "Ingen pakke bygget", "Geen pakket gebouwd", "Aucun pack créé", "Kein Paket erstellt", "Inget paket byggt" },
		[OpenTpwText.GameFiles] = new[] { "Game files", "Spilfiler", "Spelbestanden", "Fichiers du jeu", "Spieldateien", "Spelfiler" },
		[OpenTpwText.GameFolder] = new[] { "Game folder:", "Spilmappe:", "Spelmap:", "Dossier du jeu :", "Spielordner:", "Spelmapp:" },
		[OpenTpwText.CdFolder] = new[] { "CD (music and movies):", "Cd (musik og film):", "Cd (muziek en films):", "CD (musique et films) :", "CD (Musik und Filme):", "Cd (musik och filmer):" },
		[OpenTpwText.ChangeFolder] = new[] { "Change...", "Skift...", "Wijzigen...", "Modifier...", "Wählen...", "Välj..." },
		[OpenTpwText.RemoveFolder] = new[] { "Remove", "Fjern", "Verwijderen", "Retirer", "Entfernen", "Ta bort" },
		[OpenTpwText.NoFolder] = new[] { "None", "Ingen", "Geen", "Aucun", "Keine", "Ingen" },
		[OpenTpwText.RestartToApply] = new[] {
			"Restart OpenTPW to use the new folders.",
			"Genstart OpenTPW for at bruge de nye mapper.",
			"Start OpenTPW opnieuw om de nieuwe mappen te gebruiken.",
			"Redémarrez OpenTPW pour utiliser les nouveaux dossiers.",
			"Starten Sie OpenTPW neu, um die neuen Ordner zu verwenden.",
			"Starta om OpenTPW för att använda de nya mapparna." },
		[OpenTpwText.FolderNotUsable] = new[] {
			"This folder has no Theme Park World data.",
			"Denne mappe indeholder ingen Theme Park World-data.",
			"Deze map bevat geen Theme Park World-gegevens.",
			"Ce dossier ne contient pas de données de Theme Park World.",
			"Dieser Ordner enthält keine Theme Park World-Daten.",
			"Den här mappen innehåller inga Theme Park World-data." },
		[OpenTpwText.BonusFolder] = new[] { "Bonus content:", "Bonusindhold:", "Bonusinhoud:", "Contenu bonus :", "Bonusinhalte:", "Bonusinnehåll:" },
		[OpenTpwText.BonusNotFound] = new[] {
			"No bonus content was found in this folder or zip file.",
			"Der blev ikke fundet noget bonusindhold i denne mappe eller zip-fil.",
			"Er is geen bonusinhoud gevonden in deze map of zip-bestand.",
			"Aucun contenu bonus n'a été trouvé dans ce dossier ou fichier zip.",
			"In diesem Ordner oder dieser ZIP-Datei wurden keine Bonusinhalte gefunden.",
			"Inget bonusinnehåll hittades i den här mappen eller zip-filen." },
		[OpenTpwText.NotAvailable] = new[] { "Not available yet", "Endnu ikke tilgængelig", "Nog niet beschikbaar", "Pas encore disponible", "Noch nicht verfügbar", "Inte tillgänglig ännu" },
		[OpenTpwText.OnlyOnePrototypeRide] = new[] {
			"Only one ride can be placed in this build",
			"Der kan kun placeres én forlystelse i denne version",
			"In deze versie kan maar één attractie worden geplaatst",
			"Une seule attraction peut être placée dans cette version",
			"In dieser Version kann nur ein Fahrgeschäft platziert werden",
			"Endast en attraktion kan placeras i den här versionen" },
	};

	public static IReadOnlyList<string> SupportedLanguages => Languages;

	/// <summary>The label in <paramref name="language"/>; English for languages without a translation.</summary>
	public static string Get( OpenTpwText key, string language )
	{
		var column = Array.FindIndex( Languages, name => string.Equals( name, language, StringComparison.OrdinalIgnoreCase ) );
		return table[key][Math.Max( 0, column )];
	}

	public static IEnumerable<string> AllTexts( string language ) =>
		Enum.GetValues<OpenTpwText>().Select( key => Get( key, language ) ).Concat( LanguageNames.Values );
}
