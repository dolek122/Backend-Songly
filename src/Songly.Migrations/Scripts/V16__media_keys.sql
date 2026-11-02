-- Upload mediów (ADR 0010). Trzymamy KLUCZ w magazynie obiektów, nie gotowy
-- URL: adres publiczny zależy od domeny CDN-u, a ta bywa zmieniana (migracja
-- między S3 a R2, wpięcie własnej domeny). Z kluczem to jedna zmiana w
-- konfiguracji; z zapisanymi URL-ami — UPDATE po wszystkich wierszach każdej
-- tabeli, która kiedykolwiek taki adres zapisała.

-- profiles.avatar_url ZNIKA, a nie zostaje obok. Była to dowolna wartość
-- wpisywana przez użytkownika w PUT /api/v1/profiles/me, czyli adres pod
-- kontrolą obcej osoby renderowany w kliencie każdego, kto zobaczy ten profil —
-- gotowy piksel śledzący na cudze IP. Dopóki nie było jak wgrać pliku, dało się
-- to znieść; teraz jest, więc nie ma po co tego trzymać.
--
-- Kolumna jest kasowana razem z danymi. To świadome: wartości pochodziły od
-- użytkowników, wskazywały na cudze serwery i nie chcemy ich dalej serwować,
-- a produkt nie jest jeszcze wydany (Claude.md §15, faza 3).
ALTER TABLE profiles DROP COLUMN avatar_url;
ALTER TABLE profiles ADD COLUMN avatar_key VARCHAR(512);

-- Jedno zdjęcie na post. Nie galeria: galeria to osobna tabela, własna
-- kolejność i cała obsługa sortowania — do zrobienia wtedy, gdy ktoś naprawdę
-- będzie tego potrzebował, a nie "na zapas" (Claude.md §10).
ALTER TABLE posts ADD COLUMN image_key VARCHAR(512);
