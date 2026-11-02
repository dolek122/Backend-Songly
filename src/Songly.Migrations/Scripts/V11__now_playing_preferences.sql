-- Przełącznik widoczności statusu "co teraz gram" (ADR 0007, Etap 4).
-- Wiersz powstaje dopiero przy pierwszej zmianie ustawienia: brak wiersza
-- znaczy "zgoda jak dotąd", czyli status widoczny dla znajomych.
CREATE TABLE now_playing_preferences (
    user_id UUID PRIMARY KEY REFERENCES users (id),
    share_with_friends BOOLEAN NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL
);
