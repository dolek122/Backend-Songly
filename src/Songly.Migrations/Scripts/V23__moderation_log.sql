-- Historia moderacji (ADR 0024). Zamyka dług zapisany w konsekwencjach czterech
-- ADR-ów: 0014 („wygasły ban znika z listy — moderator nie odróżni «nigdy nie
-- był zbanowany» od «ban wygasł»"), 0017 i 0023 („brak historii odwołań"), 0019
-- („nie da się policzyć, ile zgłoszeń było trafnych").
--
-- Dziennik jest append-only: wpisów się nie zmienia ani nie kasuje, poza
-- sprzątaniem przy usunięciu konta i społeczności. Bez zadania sprzątającego po
-- czasie — decyzje moderacyjne podejmują ludzie, więc tabela rośnie w tempie
-- ludzkim (inaczej niż sessions, ADR o SessionCleanupJob).
CREATE TABLE moderation_log_entries (
    id UUID PRIMARY KEY,
    action VARCHAR(40) NOT NULL,
    -- NULL dla decyzji platformowych (zawieszenie konta): nie należą do żadnej
    -- społeczności. Klucz obcy BEZ ON DELETE CASCADE, bo kasowanie wierszy
    -- innego modułu kaskadą bazodanową przecinałoby granice modułów (§8) —
    -- sprząta CommunityDeletedListener w tym module.
    community_id UUID REFERENCES communities (id),
    -- Kogo dotyczyła decyzja. Wpisy znikają razem z jego kontem: historia
    -- sankcji wobec nieistniejącego konta nie ma czego opisywać, a nowe konto
    -- to i tak nowa tożsamość.
    subject_id UUID NOT NULL REFERENCES users (id),
    -- Kto podjął decyzję; NULL po skasowaniu jego konta. Dokładnie jak
    -- banned_by (ADR 0011): decyzja należy do społeczności, nie do osoby, więc
    -- przeżywa odejście swojego autora.
    actor_id UUID REFERENCES users (id),
    -- Termin sankcji przy banach i zawieszeniach czasowych; NULL = bezterminowa
    -- albo nie dotyczy.
    expires_at TIMESTAMPTZ,
    -- Liczba zgłoszeń, które post miał w chwili usunięcia (ADR 0019): bez tego
    -- nie da się odróżnić usunięcia zamykającego zgłoszenie od usunięcia
    -- post factum.
    report_count INT,
    occurred_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX idx_moderation_log_community ON moderation_log_entries (community_id, occurred_at DESC);
CREATE INDEX idx_moderation_log_subject ON moderation_log_entries (subject_id, occurred_at DESC);
CREATE INDEX idx_moderation_log_actor ON moderation_log_entries (actor_id);
