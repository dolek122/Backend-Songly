-- Sprzątanie martwych sesji. Do tej pory nic nigdy nie kasowało wierszy z
-- `sessions`: każde logowanie wstawia wiersz, a każde odświeżenie tokenu
-- wstawia kolejny i revoke'uje poprzedni. Aktywny użytkownik zostawiał więc
-- ~4 martwe wiersze na godzinę, bez żadnego górnego ograniczenia.

-- Kiedy sesja została odwołana. Bez tej kolumny nie da się odróżnić "umarła
-- przed chwilą" od "umarła miesiąc temu", a odwołany wiersz zachowuje swoje
-- pierwotne expires_at (nawet +30 dni) — czyli czekałby na skasowanie
-- znacznie dłużej, niż jest do czegokolwiek potrzebny.
ALTER TABLE sessions ADD COLUMN revoked_at TIMESTAMPTZ;

-- Wsteczne uzupełnienie: istniejące odwołane sesje dostają moment powstania
-- jako przybliżenie. Zawyża wiek co najwyżej o czas ich życia, więc pierwszy
-- przebieg sprzątania i tak potraktuje je jako dawno martwe — a to jedyne, co
-- ma tu znaczenie.
UPDATE sessions SET revoked_at = created_at WHERE revoked = TRUE;

-- Indeksy pod predykat sprzątania. Bez nich każdy przebieg skanowałby całą
-- tabelę, czyli dokładnie tę, której rozmiar jest problemem.
CREATE INDEX idx_sessions_expires_at ON sessions (expires_at);
CREATE INDEX idx_sessions_revoked_at ON sessions (revoked_at) WHERE revoked_at IS NOT NULL;
