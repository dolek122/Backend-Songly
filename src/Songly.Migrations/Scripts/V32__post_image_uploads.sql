-- Rejestr wgranych, a jeszcze nieprzypiętych zdjęć postów (ADR 0048).
--
-- Zdjęcie wgrywa się osobnym żądaniem przed publikacją posta (ADR 0010), a
-- mobilka robi to od razu po wybraniu zdjęcia. Bez rejestru plik, którego nikt
-- potem nie przypiął, leżał w magazynie na zawsze i nic nie ograniczało ich
-- liczby. Wiersz żyje od wgrania do publikacji posta (wtedy znika, a klucz
-- przechodzi do posts.image_key) albo do sprzątania porzuconych.
--
-- FK do users bez kaskady, jak wszędzie: wiersze sprząta listener kasowania
-- konta modułu content (Claude.md §9).
CREATE TABLE post_image_uploads (
    image_key VARCHAR(255) PRIMARY KEY,
    owner_id UUID NOT NULL REFERENCES users (id),
    created_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX idx_post_image_uploads_owner_id ON post_image_uploads (owner_id, created_at);
CREATE INDEX idx_post_image_uploads_created_at ON post_image_uploads (created_at);
