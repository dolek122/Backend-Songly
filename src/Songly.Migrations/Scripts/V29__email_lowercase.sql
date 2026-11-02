-- Adresy e-mail małymi literami (ADR 0042). Od tej wersji DTO normalizują
-- adres przy każdym wejściu (rejestracja, logowanie, reset hasła), więc wiersz
-- zapisany wcześniej z wielką literą byłby nie do znalezienia: logowanie pytałoby
-- o "jan@gmail.com", a w bazie leży "Jan@gmail.com".
--
-- NOT EXISTS chroni przed kolizją: jeśli dwa konta różnią się wyłącznie
-- wielkością liter, żadne nie jest ruszane — UNIQUE na email odrzuciłby
-- migrację, a wybór, które konto „wygrywa", nie należy do migracji. Takie pary
-- trzeba rozstrzygnąć ręcznie (zapytanie w ADR 0042).
UPDATE users u
SET email = lower(u.email)
WHERE u.email <> lower(u.email)
  AND NOT EXISTS (SELECT 1 FROM users o WHERE o.id <> u.id AND lower(o.email) = lower(u.email));

-- Odłożone rejestracje tak samo: potwierdzenie linku założyłoby inaczej konto
-- z wielką literą, którego potem nie da się zalogować.
UPDATE pending_registrations p
SET email = lower(p.email)
WHERE p.email <> lower(p.email)
  AND NOT EXISTS (SELECT 1 FROM pending_registrations o WHERE o.id <> p.id AND lower(o.email) = lower(p.email));
