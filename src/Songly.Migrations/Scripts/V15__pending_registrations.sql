-- Weryfikacja adresu e-mail przy rejestracji.
--
-- Rejestracja nie zakłada już konta od razu — odkłada je tutaj do czasu, aż
-- ktoś kliknie link z wiadomości. Wiersz w `users` powstaje dopiero przy
-- weryfikacji.
--
-- Osobna tabela, a NIE flaga `email_verified` na `users`: niezweryfikowane
-- konto w `users` dostawałoby profil (UserRegisteredEvent), pojawiałoby się w
-- wyszukiwarce użytkowników i w dopasowywaniu, więc każdy moduł musiałby
-- nauczyć się rozróżniać "zweryfikowany" od "nie". Przy odłożonym wierszu
-- żaden moduł poza `identity` w ogóle nie widzi tego stanu — zdarzenie
-- rejestracji po prostu leci później (Claude.md §2).
--
-- Brak FK do `users` jest tu cechą, nie brakiem: w chwili istnienia tego
-- wiersza konto jeszcze nie istnieje. Dzięki temu tabela nie potrzebuje
-- listenera kasowania konta (§9).
--
-- Hasło trzymamy już zahaszowane — bcrypt, dokładnie jak w `users`. Odłożona
-- rejestracja nie może być miejscem, w którym hasło leży słabiej chronione niż
-- w koncie docelowym.
CREATE TABLE pending_registrations (
    id UUID PRIMARY KEY,
    email VARCHAR(255) NOT NULL UNIQUE,
    password_hash VARCHAR(255) NOT NULL,
    token_hash VARCHAR(255) NOT NULL UNIQUE,
    created_at TIMESTAMPTZ NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL
);

-- Istniejące konta zostają nietknięte: powstały, zanim weryfikacja istniała,
-- więc wymaganie jej wstecz wylogowałoby wszystkich naraz. Nie ma tu żadnego
-- ALTER TABLE users — i o to chodzi.
