-- Druga instancja odwoławcza od banu w społeczności (ADR 0028). ADR 0017
-- zostawił odrzucenie odwołania jako ostateczne, bo „nad moderacją społeczności
-- nie ma dziś nikogo" — i sam wskazał, że gdy pojawi się rola globalna, będzie
-- to naturalne miejsce na drugą instancję. Rola jest zarządzalna od ADR 0021.
--
-- Nowe wartości appeal_status ('ESCALATED', 'ESCALATION_REJECTED') mieszczą się
-- w istniejącej kolumnie, więc dochodzi tylko znacznik czasu eskalacji: po nim
-- administracja porządkuje swoją kolejkę i po nim widać, jak długo sprawa czeka.
ALTER TABLE community_bans ADD COLUMN appeal_escalated_at TIMESTAMPTZ;
