-- Okno czasowe dla historii zgłaszającego (ADR 0030).
--
-- ADR 0025 zapowiedział, że okno czasowe na karę to „jedna linia w zapytaniu".
-- Było to prawdą przy liczeniu po post_reports, które mają created_at — ale ADR
-- 0027 przeszedł na liczniki (bo uwzględnione zgłoszenia giną kaskadą razem z
-- postem), a licznik nie wie, KIEDY przyszło to, co zliczył. Dlatego zamiast
-- kolumny z datą ostatniej zmiany trzymamy jeden wiersz na rozstrzygnięcie:
-- wtedy okno jest przesuwane i dokładne, a nie skokowe.
CREATE TABLE reporter_decisions (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users (id),
    community_id UUID NOT NULL REFERENCES communities (id),
    -- UPHELD = post usunięty przez moderację, DISMISSED = zgłoszenie odrzucone.
    outcome VARCHAR(20) NOT NULL,
    decided_at TIMESTAMPTZ NOT NULL
);

-- Przydział liczony globalnie (ADR 0025), więc po user_id bez społeczności.
CREATE INDEX idx_reporter_decisions_user ON reporter_decisions (user_id, decided_at);

-- Kolejka moderacji pyta o jedną społeczność i całą stronę zgłaszających naraz.
CREATE INDEX idx_reporter_decisions_community ON reporter_decisions (community_id, user_id, decided_at);

-- Przeniesienie liczników na wiersze: jeden wiersz na każdą zliczoną decyzję.
-- Dokładnej daty nie znamy — licznik jej nie zapisywał — więc bierzemy
-- updated_at, czyli moment ostatniej zmiany licznika. To jest wybór świadomie
-- ostrożny: datuje starsze decyzje na później, niż zapadły, więc kara wygasa
-- najpóźniej, jak może. Odwrotna pomyłka (data wcześniejsza) wybaczałaby po
-- migracji odrzucenia, które powinny jeszcze liczyć się tygodniami.
INSERT INTO reporter_decisions (id, user_id, community_id, outcome, decided_at)
SELECT gen_random_uuid(), r.user_id, r.community_id, 'DISMISSED', r.updated_at
FROM reporter_reputation r, generate_series(1, r.dismissed);

INSERT INTO reporter_decisions (id, user_id, community_id, outcome, decided_at)
SELECT gen_random_uuid(), r.user_id, r.community_id, 'UPHELD', r.updated_at
FROM reporter_reputation r, generate_series(1, r.upheld);

DROP TABLE reporter_reputation;
