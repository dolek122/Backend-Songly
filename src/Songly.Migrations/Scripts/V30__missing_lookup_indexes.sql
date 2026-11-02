-- Indeksy pod zapytania, które do tej pory przeszukiwały całą tabelę. Klucze
-- główne złożone obsługują wyszukiwanie tylko po PIERWSZEJ kolumnie, więc pytanie
-- o drugą szło bez indeksu.

-- „Kto blokuje mnie" — SocialGraph.blockedEitherWayIds woła to przy KAŻDYM
-- odczycie treści, powiadomień i dopasowań. PK (blocker_id, blocked_id) pokrywa
-- tylko drugą połowę pytania.
CREATE INDEX idx_blocks_blocked ON blocks (blocked_id);

-- Kandydaci do dopasowań: użytkownicy z którymkolwiek z moich gatunków
-- (ProfileDirectory.userIdsWithAnyGenre). PK zaczyna się od user_id.
CREATE INDEX idx_user_genres_genre ON user_genres (genre_id);

-- Kasowanie konta (Claude.md §9) sprząta ślady użytkownika pod cudzymi postami i
-- w cudzych skrzynkach. Bez tych indeksów każde usunięcie konta przeszukiwało
-- całe tabele lajków, komentarzy i powiadomień.
CREATE INDEX idx_likes_user ON likes (user_id);
CREATE INDEX idx_comments_author ON comments (author_id);
CREATE INDEX idx_notifications_actor ON notifications (actor_id);
