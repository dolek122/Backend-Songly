-- Bany czasowe (ADR 0014). NULL = ban bezterminowy — istniejące wiersze nie
-- wymagają żadnego backfillu i zachowują dotychczasowe znaczenie.
--
-- Wygasanie jest LENIWE: żaden proces nie budzi się w chwili wygaśnięcia,
-- wszystkie zapytania o ban filtrują po expires_at. Wygasły wiersz zostaje —
-- klucz główny (community_id, user_id) czyni z niego miejsce na ponowny ban
-- tej samej osoby (ta sama para nie może mieć dwóch wierszy), a jego nadpisanie
-- jest nową decyzją moderacyjną z nowym powiadomieniem.
ALTER TABLE community_bans ADD COLUMN expires_at TIMESTAMPTZ;
