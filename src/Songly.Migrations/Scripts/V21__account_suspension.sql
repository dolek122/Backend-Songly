-- Globalne zawieszanie kont (ADR 0022). Do tej pory jedyną sankcją na poziomie
-- platformy było usunięcie konta przez samego użytkownika: troll zbanowany w
-- kilku społecznościach dalej miał konto, profil i wszystkie ścieżki zapisu
-- poza tymi społecznościami.
--
-- Kolumny na users, nie osobna tabela: jedno konto to jedno zawieszenie, czyli
-- atrybut tego konta (ten sam argument co przy odwołaniach od bana, ADR 0017).
--
-- Celowo BEZ kolumny suspended_by: kto zawiesił, niesie wpis audytowy i licznik
-- (ADR 0021), a kolejny klucz obcy do users wymagałby zerowania przy kasowaniu
-- konta administratora — i to klucz obcy tabeli do samej siebie.
--
-- NULL w suspended_at = konto aktywne. Wygasanie jest LENIWE, jak przy banach
-- czasowych (ADR 0014): nic nie dzieje się w chwili wygaśnięcia, każde pytanie
-- o stan konta filtruje po suspension_expires_at.
ALTER TABLE users ADD COLUMN suspended_at TIMESTAMPTZ;
ALTER TABLE users ADD COLUMN suspension_reason VARCHAR(500);
ALTER TABLE users ADD COLUMN suspension_expires_at TIMESTAMPTZ;
