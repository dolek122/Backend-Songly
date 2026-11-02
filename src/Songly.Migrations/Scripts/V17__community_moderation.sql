-- Moderacja społeczności (ADR 0011). Świadomie BEZ globalnego moderatora:
-- społeczność ma właściciela, który już dziś kasuje u siebie posty (Claude.md
-- §8), więc rozbudowujemy to, co istnieje, zamiast budować równoległą władzę
-- nad całą aplikacją.

-- Ban odcina od PISANIA, nie od czytania. Społeczności są w pełni publiczne
-- (§8), więc ukrywanie treści przed zbanowanym byłoby ochroną pozorną —
-- wystarczy wylogować się albo założyć konto. Realna szkoda idzie ze ścieżek
-- zapisu i to je odcinamy.
CREATE TABLE community_bans (
    community_id UUID NOT NULL REFERENCES communities (id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users (id),
    -- Nullable, i to jest decyzja, nie niedopatrzenie: ban należy do
    -- SPOŁECZNOŚCI, nie do osoby, która go nałożyła. Gdy moderator kasuje konto,
    -- ban ma przetrwać — inaczej odejście moderatora po cichu wpuszczałoby z
    -- powrotem wszystkich, których zbanował.
    banned_by UUID REFERENCES users (id),
    reason VARCHAR(500),
    created_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (community_id, user_id)
);

CREATE INDEX idx_community_bans_user ON community_bans (user_id);

-- Zgłoszenia postów. Tabela należy do modułu content, bo to on jest
-- właścicielem postów i tylko on może mieć do nich klucz obcy (§2). Kolejkę
-- ogląda moderator społeczności — uprawnienie sprawdzamy przez publiczny
-- interfejs modułu community, nie przez zaglądanie do jego tabel.
CREATE TABLE post_reports (
    id UUID PRIMARY KEY,
    post_id UUID NOT NULL REFERENCES posts (id) ON DELETE CASCADE,
    reporter_id UUID NOT NULL REFERENCES users (id),
    reason VARCHAR(30) NOT NULL,
    details VARCHAR(500),
    status VARCHAR(20) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    reviewed_at TIMESTAMPTZ,
    reviewed_by UUID REFERENCES users (id),
    -- Jeden zgłaszający = jedno zgłoszenie na post. Bez tego jedna osoba
    -- podbijałaby licznik w nieskończoność i kolejka pokazywałaby wagę
    -- zgłoszenia zamiast liczby niezależnych osób, którym coś przeszkadza.
    UNIQUE (post_id, reporter_id)
);

CREATE INDEX idx_post_reports_post ON post_reports (post_id);
CREATE INDEX idx_post_reports_reporter ON post_reports (reporter_id);
