-- Urządzenia, na które wysyłamy pushe (ADR 0040).
--
-- Kluczem jest token, nie para (użytkownik, token): token identyfikuje
-- instalację aplikacji na telefonie, a na jednym telefonie zalogowany jest
-- naraz jeden użytkownik. Logowanie innym kontem przepina wiersz — inaczej
-- poprzedni właściciel dalej dostawałby swoje powiadomienia na cudzy telefon.
--
-- Wiersz znika: przy wylogowaniu (klient wyrejestrowuje urządzenie), przy
-- odwołaniu wszystkich sesji (zmiana/reset hasła, kradzież tokenu), gdy FCM
-- powie, że urządzenia już nie ma, i przy usunięciu konta.
CREATE TABLE push_devices (
    token VARCHAR(512) PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users (id),
    platform VARCHAR(16) NOT NULL,
    -- Ostatnia rejestracja. Klient zgłasza token przy każdym starcie, więc to
    -- przybliżenie "ostatnio używane" — po nim przycinamy nadmiar urządzeń.
    updated_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX idx_push_devices_user ON push_devices (user_id, updated_at);
