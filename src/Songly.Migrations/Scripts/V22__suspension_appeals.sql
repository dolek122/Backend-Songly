-- Odwołania od zawieszenia konta (ADR 0023), zbudowane dokładnie na wzór
-- odwołań od banu w społeczności (ADR 0017): kolumny na wierszu, którego
-- dotyczą, bo jedno konto to jedno zawieszenie, a odwołanie to atrybut tej
-- jednej sprawy.
--
-- appeal_status: NULL = brak odwołania, 'PENDING' = czeka, 'REJECTED' =
-- odrzucone. Stanu ACCEPTED nie ma, bo uznanie odwołania to zdjęcie
-- zawieszenia — wszystkie pola zawieszenia wracają wtedy do NULL razem ze
-- sprawą.
--
-- Jedno odwołanie na zawieszenie jest strukturalnym anty-spamem: kanał prowadzi
-- od ukaranego do karzącego. Nowe zawieszenie czyści te pola i daje świeże
-- prawo do odwołania, także po wcześniejszym odrzuceniu.
--
-- Celowo bez kolumny „kto rozstrzygnął", z tego samego powodu co brak
-- suspended_by (ADR 0022): byłby to klucz obcy tabeli users do samej siebie.
ALTER TABLE users ADD COLUMN suspension_appeal_text VARCHAR(1000);
ALTER TABLE users ADD COLUMN suspension_appealed_at TIMESTAMPTZ;
ALTER TABLE users ADD COLUMN suspension_appeal_status VARCHAR(20);
