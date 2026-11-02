-- Usuwanie konta przez stronę WWW, bez aplikacji (ADR 0043). Google Play
-- wymaga, żeby dało się o to poprosić także po odinstalowaniu aplikacji, więc
-- tożsamość potwierdza link z maila, a nie zalogowana sesja.
--
-- Ten sam kształt co password_reset_tokens: wyłącznie skrót tokenu (wyciek
-- bazy nie może dawać gotowych linków kasujących konta), jeden żywy token na
-- konto (nowe żądanie kasuje poprzedni) i bez UNIQUE na user_id z tego samego
-- powodu co tam — dwa równoległe żądania nie mogą kończyć się błędem klucza.
CREATE TABLE account_deletion_tokens (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users (id),
    token_hash VARCHAR(255) NOT NULL UNIQUE,
    created_at TIMESTAMPTZ NOT NULL,
    expires_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX idx_account_deletion_tokens_user_id ON account_deletion_tokens (user_id);
