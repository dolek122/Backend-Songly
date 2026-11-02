using System.Net;

namespace Songly.Identity.AccountDeletion;

/// <summary>
/// The account deletion pages (original ADR 0043) — the only HTML this backend serves.
/// </summary>
/// <remarks>
/// <para>
/// Plain strings instead of a template engine: there are four pages, and the only value not from
/// our own code is the token from the address, which is HTML-encoded. One encoding call does not
/// justify a view engine for the whole application.
/// </para>
/// <para>
/// The text answers what Google Play asks of a deletion page: whose app it is, how to request
/// deletion, what is deleted, and what stays and for how long. The "what we delete" list describes
/// the whole product, and it must keep matching the <c>UserAccountDeletionRequested</c> handlers of
/// every module — a new module with per-user data adds itself in both places.
/// </para>
/// </remarks>
internal static class AccountDeletionPages
{
    public const string StartPath = "/account-deletion";
    public const string ConfirmPath = "/account-deletion/confirm";

    public static string Start(int ttlMinutes, string? error)
    {
        var errorBlock = error is null ? string.Empty : $"""<p class="error" role="alert">{WebUtility.HtmlEncode(error)}</p>""";
        return Layout("Usuwanie konta Songly", $"""
            <h1>Usuwanie konta Songly</h1>
            <p>Na tej stronie usuniesz swoje konto w aplikacji Songly razem ze wszystkimi
            danymi — także wtedy, gdy nie masz już aplikacji na telefonie.</p>

            <h2>W aplikacji</h2>
            <p>Zaloguj się i w zakładce <b>Profil</b> wybierz usunięcie konta. Aplikacja
            poprosi o hasło.</p>

            <h2>Bez aplikacji</h2>
            <ol>
              <li>Wpisz adres e-mail, na który założono konto.</li>
              <li>Otwórz link z wiadomości, którą od nas dostaniesz. Jest ważny przez {ttlMinutes} min.</li>
              <li>Na stronie z linku potwierdź usunięcie przyciskiem.</li>
            </ol>
            {errorBlock}
            <form method="post" action="{StartPath}">
              <label for="email">Adres e-mail konta</label>
              <input id="email" name="email" type="email" autocomplete="email" required>
              <button type="submit">Wyślij link</button>
            </form>
            <p class="note">Ze względów bezpieczeństwa nie mówimy tutaj, czy pod danym adresem
            jest konto. Wiadomość przyjdzie tylko wtedy, gdy jest.</p>

            <h2>Co usuwamy</h2>
            <p>Od razu i na stałe — bez okresu karencji i bez możliwości przywrócenia:</p>
            <ul>
              <li>konto i dane logowania,</li>
              <li>profil, zdjęcie profilowe i ulubione gatunki,</li>
              <li>posty ze zdjęciami (razem z komentarzami innych osób pod nimi), zdjęcia wgrane,
              ale nieopublikowane, komentarze, polubienia i udostępnienia,</li>
              <li>znajomych, zaproszenia i blokady,</li>
              <li>członkostwa w społecznościach; społeczności, których jesteś właścicielem,
              usuwamy w całości, razem z postami innych osób,</li>
              <li>powiadomienia, ustawienia powiadomień i telefony zarejestrowane do powiadomień
              push,</li>
              <li>połączenie ze Spotify i status „teraz gram”,</li>
              <li>złożone przez Ciebie zgłoszenia i historię decyzji moderacyjnych wobec Ciebie.</li>
            </ul>

            <h2>Co zostaje</h2>
            <ul>
              <li>Anonimowe liczniki statystyczne (np. ile postów dodano danego dnia). Nie
              zawierają niczego, co wskazuje na Ciebie.</li>
              <li>Decyzje, które podjąłeś jako moderator społeczności (np. bany), zostają w
              społeczności, ale bez wskazania Ciebie jako ich autora.</li>
              <li>Logi techniczne serwera mogą przez krótki czas zawierać identyfikator konta
              (nigdy adres e-mail). Usuwają się same po upływie okresu przechowywania logów.</li>
            </ul>
            """);
    }

    public static string LinkSent(int ttlMinutes) => Layout("Sprawdź skrzynkę — Songly", $"""
        <h1>Sprawdź skrzynkę</h1>
        <p>Jeśli pod podanym adresem jest konto Songly, wysłaliśmy na niego link do
        potwierdzenia usunięcia. Link jest ważny przez {ttlMinutes} min.</p>
        <p>Nic nie przyszło? Zajrzyj do folderu ze spamem albo spróbuj ponownie za minutę.</p>
        <p><a href="{StartPath}">Wróć</a></p>
        """);

    public static string Confirm(string token) => Layout("Potwierdź usunięcie konta — Songly", $"""
        <h1>Potwierdź usunięcie konta</h1>
        <p>Usunięcie jest <b>nieodwracalne</b>. Znikną profil, zdjęcia, posty, komentarze,
        znajomości i społeczności, których jesteś właścicielem.</p>
        <form method="post" action="{ConfirmPath}">
          <input type="hidden" name="token" value="{WebUtility.HtmlEncode(token)}">
          <button type="submit" class="danger">Usuń konto na stałe</button>
        </form>
        <p class="note">Jeśli nie chcesz usuwać konta, po prostu zamknij tę stronę.</p>
        """);

    public static string Deleted() => Layout("Konto usunięte — Songly", """
        <h1>Konto usunięte</h1>
        <p>Twoje konto Songly i wszystkie dane opisane na stronie usuwania konta zostały
        usunięte.</p>
        <p>Jeśli aplikacja jest jeszcze na Twoim telefonie, wyloguje Cię przy najbliższym
        połączeniu z serwerem.</p>
        """);

    public static string InvalidLink(int ttlMinutes) => Layout("Link nieważny — Songly", $"""
        <h1>Ten link jest nieważny</h1>
        <p>Mógł wygasnąć (jest ważny przez {ttlMinutes} min), zostać już użyty albo zastąpiony
        nowszym linkiem.</p>
        <p><a href="{StartPath}">Zamów nowy link</a></p>
        """);

    /// <summary>
    /// No external resources: the page loads nothing from outside the document, so the token in the
    /// address cannot leak through a <c>Referer</c> header, and the Content-Security-Policy can
    /// forbid everything but inline styles.
    /// </summary>
    private static string Layout(string title, string body) => $$"""
        <!doctype html>
        <html lang="pl">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{WebUtility.HtmlEncode(title)}}</title>
        <style>
          :root { color-scheme: dark; }
          body { margin: 0; background: #0b0f0c; color: #e8efe9;
                 font: 16px/1.55 system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; }
          main { max-width: 640px; margin: 0 auto; padding: 32px 16px 48px; }
          h1 { font-size: 1.6rem; margin: 0 0 16px; }
          h2 { font-size: 1.15rem; margin: 32px 0 8px; color: #7ee2a0; }
          a { color: #7ee2a0; }
          label { display: block; margin: 16px 0 6px; font-weight: 600; }
          input[type=email] { width: 100%; box-sizing: border-box; padding: 12px; font: inherit;
                 border-radius: 8px; border: 1px solid #3a4a3f; background: #141b16; color: inherit; }
          button { margin-top: 12px; min-height: 48px; padding: 0 20px; font: inherit; font-weight: 600;
                 border: 0; border-radius: 24px; background: #1db954; color: #04130a; cursor: pointer; }
          button.danger { background: #e5484d; color: #fff; }
          .note { color: #9fb0a4; font-size: 0.92rem; }
          .error { color: #ff8f8f; font-weight: 600; }
        </style>
        </head>
        <body>
        <main>
        {{body}}
        </main>
        </body>
        </html>
        """;
}
