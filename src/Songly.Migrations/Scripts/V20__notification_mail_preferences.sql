-- Preferencje kanału e-mail dla powiadomień (ADR 0020).
--
-- Rządzą WYŁĄCZNIE pocztą. Skrzynka w aplikacji zostaje kompletna i nie da się
-- jej wyłączyć: decyzji moderacyjnej nie można odmówić (ADR 0012), a ścieżka
-- odwoławcza (ADR 0017) traciłaby sens, gdyby dało się nie dowiedzieć o banie.
-- Mail jest doręczeniem do systemu poza naszą kontrolą — tam wybór należy do
-- adresata; zapis o decyzji i tak zostaje tam, gdzie po niego przyjdzie.
--
-- Wiersz powstaje dopiero przy pierwszej zmianie, jak w
-- now_playing_preferences: brak wiersza = oba kanały włączone (opt-out, nie
-- opt-in — te maile są konsekwencją korzystania z usługi, nie marketingiem).
CREATE TABLE notification_mail_preferences (
    user_id UUID PRIMARY KEY REFERENCES users (id),
    -- Decyzje dotyczące MNIE: ban, usunięcie mojego posta, rozstrzygnięcie
    -- mojego zgłoszenia albo odwołania.
    decisions_about_me BOOLEAN NOT NULL,
    -- Wezwania do pracy moderacyjnej: nowe zgłoszenie, nowe odwołanie. Osobno,
    -- bo to zupełnie inna rola — moderator zasypywany pocztą ze swojej
    -- społeczności nie powinien przy okazji tracić informacji o własnym koncie.
    moderation_queue BOOLEAN NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL
);
