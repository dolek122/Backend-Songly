-- Ścieżka odwoławcza od bana (ADR 0017). Odwołanie mieszka NA wierszu bana,
-- nie w osobnej tabeli: klucz główny (community_id, user_id) mówi „jedna para
-- — jeden ban", a odwołanie to atrybut tej jednej sprawy. Jedno odwołanie na
-- ban jest przy tym strukturalnym anty-spamem — odrzucone odwołanie jest
-- ostateczne dla tego bana, a nadpisanie wygasłego bana (reimpose, ADR 0014)
-- czyści te pola: nowy ban to nowa sprawa i nowe prawo do odwołania.
--
-- appeal_status: NULL = brak odwołania, 'PENDING' = czeka, 'REJECTED' =
-- odrzucone. Stanu ACCEPTED nie ma, bo uznanie odwołania to unban — wiersz
-- znika razem ze sprawą. Celowo bez appeal_resolved_by: kto odrzucił, niesie
-- powiadomienie (aktor), a kolejna kolumna z FK do users wymagałaby zerowania
-- przy kasowaniu konta jak banned_by.
ALTER TABLE community_bans ADD COLUMN appeal_text VARCHAR(1000);
ALTER TABLE community_bans ADD COLUMN appealed_at TIMESTAMPTZ;
ALTER TABLE community_bans ADD COLUMN appeal_status VARCHAR(20);
