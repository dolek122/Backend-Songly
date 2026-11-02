-- Zbiór zawieszonych kont (UserRepository.findSuspendedIds) jest pobierany
-- przy każdym odczycie i zapisie treści, każdym podglądzie profilu, w
-- wyszukiwarce, dopasowaniach, a od ADR 0049 także w now-playing i pushach.
-- Bez indeksu każde takie zapytanie przeszukiwało całą tabelę users, choć
-- zawieszonych jest garstka (nakłada je ręcznie administrator).
--
-- Indeks częściowy: obejmuje tylko zawieszone wiersze, więc jest mały
-- niezależnie od liczby kont. (id, suspension_expires_at), żeby zapytanie
-- odczytało wynik z samego indeksu, razem z warunkiem wygaśnięcia.
CREATE INDEX idx_users_suspended ON users (id, suspension_expires_at) WHERE suspended_at IS NOT NULL;
