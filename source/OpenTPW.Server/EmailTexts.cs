namespace OpenTPW.Server;

/// <summary>The kinds of account e-mail.</summary>
public enum EmailKind { VerifyAddress, Recovery, PasswordChanged, PasswordAndTwoStepChanged }

/// <summary>
/// [EXT:online-email] Account e-mails in the nine languages of the game (the player's game language, else English).
/// Subjects never hold the code. {0} is the code or the player name, {1} the player name, {2} the reply address.
/// </summary>
public static class EmailTexts
{
	public static readonly IReadOnlyList<string> Languages = new[] { "English", "Danish", "Dutch", "French", "German", "Korean", "Polish", "Spanish", "Swedish" };

	private static readonly Dictionary<EmailKind, (string Subject, string Text)[]> texts = new()
	{
		[EmailKind.VerifyAddress] = new[]
		{
			("Your OpenTPW code", "Your code to confirm this e-mail address for the OpenTPW player {1} is {0}.\nIt works for 15 minutes. If you did not ask for it, ignore this e-mail."),
			("Din OpenTPW-kode", "Din kode til at bekræfte denne e-mailadresse for OpenTPW-spilleren {1} er {0}.\nDen virker i 15 minutter. Har du ikke bedt om den, så se bort fra denne e-mail."),
			("Je OpenTPW-code", "Je code om dit e-mailadres te bevestigen voor de OpenTPW-speler {1} is {0}.\nDe code werkt 15 minuten. Heb je hier niet om gevraagd, negeer dan deze e-mail."),
			("Votre code OpenTPW", "Votre code pour confirmer cette adresse e-mail pour le joueur OpenTPW {1} est {0}.\nIl est valable 15 minutes. Si vous ne l'avez pas demandé, ignorez cet e-mail."),
			("Dein OpenTPW-Code", "Dein Code zur Bestätigung dieser E-Mail-Adresse für den OpenTPW-Spieler {1} lautet {0}.\nEr gilt 15 Minuten. Wenn du ihn nicht angefordert hast, ignoriere diese E-Mail."),
			("OpenTPW 코드", "OpenTPW 플레이어 {1}의 이메일 주소를 확인하는 코드는 {0}입니다.\n코드는 15분 동안 유효합니다. 요청하지 않았다면 이 이메일을 무시하십시오."),
			("Twój kod OpenTPW", "Twój kod do potwierdzenia tego adresu e-mail dla gracza OpenTPW {1} to {0}.\nDziała przez 15 minut. Jeśli o niego nie prosiłeś, zignoruj ten e-mail."),
			("Tu código de OpenTPW", "Tu código para confirmar esta dirección de e-mail para el jugador de OpenTPW {1} es {0}.\nFunciona durante 15 minutos. Si no lo has pedido, ignora este e-mail."),
			("Din OpenTPW-kod", "Din kod för att bekräfta den här e-postadressen för OpenTPW-spelaren {1} är {0}.\nDen gäller i 15 minuter. Om du inte har bett om den kan du bortse från det här mejlet."),
		},
		[EmailKind.Recovery] = new[]
		{
			("Reset your OpenTPW password", "Someone asked to reset the password of the OpenTPW player {1}. The code is {0}.\nIt works for 15 minutes. If it was not you, ignore this e-mail: nothing changes without the code."),
			("Nulstil din OpenTPW-adgangskode", "Nogen har bedt om at nulstille adgangskoden for OpenTPW-spilleren {1}. Koden er {0}.\nDen virker i 15 minutter. Var det ikke dig, så se bort fra denne e-mail: intet ændres uden koden."),
			("Stel je OpenTPW-wachtwoord opnieuw in", "Iemand vroeg om het wachtwoord van de OpenTPW-speler {1} opnieuw in te stellen. De code is {0}.\nDe code werkt 15 minuten. Was jij het niet, negeer dan deze e-mail: zonder de code verandert er niets."),
			("Réinitialiser votre mot de passe OpenTPW", "Quelqu'un a demandé à réinitialiser le mot de passe du joueur OpenTPW {1}. Le code est {0}.\nIl est valable 15 minutes. Si ce n'était pas vous, ignorez cet e-mail : rien ne change sans le code."),
			("Setze dein OpenTPW-Passwort zurück", "Jemand hat angefordert, das Passwort des OpenTPW-Spielers {1} zurückzusetzen. Der Code lautet {0}.\nEr gilt 15 Minuten. Warst du es nicht, ignoriere diese E-Mail: ohne den Code ändert sich nichts."),
			("OpenTPW 패스워드 재설정", "누군가 OpenTPW 플레이어 {1}의 패스워드 재설정을 요청했습니다. 코드는 {0}입니다.\n코드는 15분 동안 유효합니다. 본인이 아니라면 이 이메일을 무시하십시오. 코드 없이는 아무것도 바뀌지 않습니다."),
			("Zresetuj hasło OpenTPW", "Ktoś poprosił o zresetowanie hasła gracza OpenTPW {1}. Kod to {0}.\nDziała przez 15 minut. Jeśli to nie Ty, zignoruj ten e-mail: bez kodu nic się nie zmieni."),
			("Restablece tu contraseña de OpenTPW", "Alguien ha pedido restablecer la contraseña del jugador de OpenTPW {1}. El código es {0}.\nFunciona durante 15 minutos. Si no has sido tú, ignora este e-mail: sin el código no cambia nada."),
			("Återställ ditt OpenTPW-lösenord", "Någon har bett om att återställa lösenordet för OpenTPW-spelaren {1}. Koden är {0}.\nDen gäller i 15 minuter. Om det inte var du kan du bortse från det här mejlet: inget ändras utan koden."),
		},
		[EmailKind.PasswordChanged] = new[]
		{
			("Your OpenTPW password was changed", "The password of the OpenTPW player {0} was just changed with a code from this e-mail address, and every session was logged out.\nIf this was not you, write to {2}."),
			("Din OpenTPW-adgangskode blev ændret", "Adgangskoden for OpenTPW-spilleren {0} blev netop ændret med en kode fra denne e-mailadresse, og alle sessioner blev logget ud.\nVar det ikke dig, så skriv til {2}."),
			("Je OpenTPW-wachtwoord is gewijzigd", "Het wachtwoord van de OpenTPW-speler {0} is zojuist gewijzigd met een code van dit e-mailadres, en alle sessies zijn uitgelogd.\nWas jij dit niet, schrijf dan naar {2}."),
			("Votre mot de passe OpenTPW a été modifié", "Le mot de passe du joueur OpenTPW {0} vient d'être modifié avec un code envoyé à cette adresse, et toutes les sessions ont été déconnectées.\nSi ce n'était pas vous, écrivez à {2}."),
			("Dein OpenTPW-Passwort wurde geändert", "Das Passwort des OpenTPW-Spielers {0} wurde gerade mit einem Code von dieser E-Mail-Adresse geändert, und alle Sitzungen wurden abgemeldet.\nWarst du es nicht, schreibe an {2}."),
			("OpenTPW 패스워드 변경됨", "OpenTPW 플레이어 {0}의 패스워드가 이 이메일 주소로 받은 코드로 방금 변경되었고, 모든 세션이 로그아웃되었습니다.\n본인이 아니라면 {2}(으)로 문의하십시오."),
			("Twoje hasło OpenTPW zostało zmienione", "Hasło gracza OpenTPW {0} zostało właśnie zmienione kodem z tego adresu e-mail, a wszystkie sesje wylogowano.\nJeśli to nie Ty, napisz na {2}."),
			("Tu contraseña de OpenTPW ha cambiado", "La contraseña del jugador de OpenTPW {0} se acaba de cambiar con un código de esta dirección de e-mail, y se cerraron todas las sesiones.\nSi no has sido tú, escribe a {2}."),
			("Ditt OpenTPW-lösenord har ändrats", "Lösenordet för OpenTPW-spelaren {0} ändrades nyss med en kod från den här e-postadressen, och alla sessioner loggades ut.\nOm det inte var du, skriv till {2}."),
		},
		[EmailKind.PasswordAndTwoStepChanged] = new[]
		{
			("Your OpenTPW password and two-step login were reset", "The password of the OpenTPW player {0} was just changed with a code from this e-mail address, two-step login was reset, and every session was logged out.\nIf this was not you, write to {2}."),
			("Din OpenTPW-adgangskode og totrinslogin blev nulstillet", "Adgangskoden for OpenTPW-spilleren {0} blev netop ændret med en kode fra denne e-mailadresse, totrinslogin blev nulstillet, og alle sessioner blev logget ud.\nVar det ikke dig, så skriv til {2}."),
			("Je OpenTPW-wachtwoord en inloggen in twee stappen zijn opnieuw ingesteld", "Het wachtwoord van de OpenTPW-speler {0} is zojuist gewijzigd met een code van dit e-mailadres, inloggen in twee stappen is opnieuw ingesteld, en alle sessies zijn uitgelogd.\nWas jij dit niet, schrijf dan naar {2}."),
			("Votre mot de passe OpenTPW et la connexion en deux étapes ont été réinitialisés", "Le mot de passe du joueur OpenTPW {0} vient d'être modifié avec un code envoyé à cette adresse, la connexion en deux étapes a été réinitialisée, et toutes les sessions ont été déconnectées.\nSi ce n'était pas vous, écrivez à {2}."),
			("Dein OpenTPW-Passwort und die zweistufige Anmeldung wurden zurückgesetzt", "Das Passwort des OpenTPW-Spielers {0} wurde gerade mit einem Code von dieser E-Mail-Adresse geändert, die zweistufige Anmeldung wurde zurückgesetzt, und alle Sitzungen wurden abgemeldet.\nWarst du es nicht, schreibe an {2}."),
			("OpenTPW 패스워드와 2단계 로그인 재설정됨", "OpenTPW 플레이어 {0}의 패스워드가 이 이메일 주소로 받은 코드로 방금 변경되었고, 2단계 로그인이 재설정되었으며, 모든 세션이 로그아웃되었습니다.\n본인이 아니라면 {2}(으)로 문의하십시오."),
			("Hasło OpenTPW i logowanie dwuetapowe zresetowano", "Hasło gracza OpenTPW {0} zostało właśnie zmienione kodem z tego adresu e-mail, logowanie dwuetapowe zresetowano, a wszystkie sesje wylogowano.\nJeśli to nie Ty, napisz na {2}."),
			("Se restablecieron tu contraseña de OpenTPW y el acceso en dos pasos", "La contraseña del jugador de OpenTPW {0} se acaba de cambiar con un código de esta dirección de e-mail, el acceso en dos pasos se restableció y se cerraron todas las sesiones.\nSi no has sido tú, escribe a {2}."),
			("Ditt OpenTPW-lösenord och tvåstegsinloggning har återställts", "Lösenordet för OpenTPW-spelaren {0} ändrades nyss med en kod från den här e-postadressen, tvåstegsinloggningen återställdes och alla sessioner loggades ut.\nOm det inte var du, skriv till {2}."),
		},
	};

	/// <summary>The e-mail of <paramref name="kind"/> in <paramref name="language"/> (English when unknown).</summary>
	public static EmailMessage Compose( EmailKind kind, string? language, string to, string first, string second, string replyTo )
	{
		var index = Math.Max( 0, Languages.ToList().FindIndex( name => string.Equals( name, language, StringComparison.OrdinalIgnoreCase ) ) );
		var (subject, text) = texts[kind][index];
		return new EmailMessage( to, subject, string.Format( System.Globalization.CultureInfo.InvariantCulture, text, first, second, replyTo ) );
	}

	public static IReadOnlyDictionary<EmailKind, (string Subject, string Text)[]> All => texts;
}
