-- Reset hasła. Bez tej tabeli użytkownik, który zapomniał hasła, tracił konto
-- bezpowrotnie — a mechanizm z Claude.md §9 (wykrywanie ponownego użycia
-- refresh tokenu) wprost zakłada, że "użytkownik wraca hasłem".
--
-- Trzymamy WYŁĄCZNIE skrót tokenu, tak samo jak przy sesjach: wyciek bazy bez
-- tego byłby natychmiastowym przejęciem każdego konta z oczekującym resetem.
--
-- Bez UNIQUE na user_id: dwa równoległe żądania resetu dla tego samego konta
-- mogłyby wtedy skończyć się błędem klucza (czyli 500 zamiast 202), a jedyną
-- szkodą z drugiego wiersza jest to, że przez chwilę żyją dwa linki. Reset i
-- tak kasuje wszystkie tokeny użytkownika naraz.
CREATE TABLE password_reset_tokens (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users (id),
    token_hash VARCHAR(255) NOT NULL UNIQUE,
    created_at TIMESTAMPTZ NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX idx_password_reset_tokens_user_id ON password_reset_tokens (user_id);
