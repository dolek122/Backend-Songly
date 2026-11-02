-- Posty w społecznościach. Do tej pory `communities` były wyłącznie listami
-- członków — nie istniał żaden sposób, by cokolwiek w nich opublikować.

-- NULL = zwykły post (feed znajomych), wartość = post w społeczności. Celowo
-- jedna tabela, nie osobna `community_posts`: post to ten sam byt niezależnie
-- od miejsca publikacji, a rozdzielenie zduplikowałoby lajki, komentarze i
-- udostępnienia razem z całą ich obsługą.
--
-- Świadomie BEZ ON DELETE CASCADE, mimo że upraszczałoby to usuwanie
-- społeczności: kaskada w bazie przecinałaby granicę modułów (Claude.md §2)
-- i kasowała wiersze modułu content bez jego wiedzy. Zamiast tego community
-- publikuje CommunityDeletedEvent, a content sprząta swoje posty sam — ten sam
-- mechanizm, co przy usuwaniu konta (Claude.md §9).
ALTER TABLE posts ADD COLUMN community_id UUID REFERENCES communities (id);

-- Pod listowanie postów jednej społeczności.
CREATE INDEX idx_posts_community_id_created_at ON posts (community_id, created_at DESC);

-- Feed znajomych i profile pomijają posty społecznościowe, więc filtrują po
-- community_id IS NULL. Indeks częściowy obsługuje dokładnie ten predykat i nie
-- rośnie o wiersze, których te zapytania nigdy nie dotkną.
CREATE INDEX idx_posts_created_at_not_in_community ON posts (created_at DESC) WHERE community_id IS NULL;
