-- Trafność zgłoszeń per zgłaszający (ADR 0027). Zamyka ostatni brak z ADR 0019
-- („zgłoszenie zamknięte usunięciem posta nie zostawia śladu jako
-- rozstrzygnięcie") i jednostronność sygnału z ADR 0025 („liczymy odrzucenia,
-- nie trafienia").
--
-- Liczniki, nie wyliczanie z post_reports, i to jest tu sedno: usunięcie posta
-- zabiera kaskadą jego zgłoszenia, więc historia trafnych zgłoszeń ginęła
-- dokładnie wtedy, gdy zgłoszenie okazało się TRAFNE. Licznik przeżywa post, bo
-- nie jest z nim powiązany.
--
-- Klucz per (osoba, społeczność), bo te same liczby odpowiadają na dwa pytania w
-- dwóch różnych zasięgach (ADR 0025): limit sumuje wszystkie społeczności (bo
-- zasypywanie rozkłada się na wiele), a kolejka pokazuje moderatorowi wyłącznie
-- jego społeczność (bo nie ma prawa wiedzieć, co ten człowiek robił w obcych).
CREATE TABLE reporter_reputation (
    user_id UUID NOT NULL REFERENCES users (id),
    community_id UUID NOT NULL REFERENCES communities (id),
    -- Zgłoszenia uwzględnione: post usunięty przez moderację.
    upheld INT NOT NULL,
    -- Zgłoszenia odrzucone.
    dismissed INT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (user_id, community_id)
);

-- Przeniesienie tego, co jeszcze da się odzyskać: odrzucenia, których post nadal
-- istnieje. Bez tego przełączenie limitu na te liczniki wyzerowałoby historię
-- wszystkim, których zgłoszenia już odrzucono — czyli po migracji troll dostałby
-- z powrotem pełny przydział.
INSERT INTO reporter_reputation (user_id, community_id, upheld, dismissed, updated_at)
SELECT r.reporter_id, p.community_id, 0, COUNT(*), now()
FROM post_reports r
JOIN posts p ON p.id = r.post_id
WHERE r.status = 'DISMISSED' AND p.community_id IS NOT NULL
GROUP BY r.reporter_id, p.community_id;
