CREATE TABLE friend_requests (
    id UUID PRIMARY KEY,
    requester_id UUID NOT NULL REFERENCES users (id),
    addressee_id UUID NOT NULL REFERENCES users (id),
    status VARCHAR(20) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    responded_at TIMESTAMPTZ
);

-- Tylko jedno oczekujące zaproszenie w danym kierunku; historia (ACCEPTED/DECLINED) bez limitu.
CREATE UNIQUE INDEX uq_friend_requests_pending ON friend_requests (requester_id, addressee_id)
    WHERE status = 'PENDING';

CREATE INDEX idx_friend_requests_addressee_pending ON friend_requests (addressee_id) WHERE status = 'PENDING';

CREATE TABLE friendships (
    user_a_id UUID NOT NULL REFERENCES users (id),
    user_b_id UUID NOT NULL REFERENCES users (id),
    created_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (user_a_id, user_b_id),
    -- Para kanoniczna (a < b wg porządku bajtowego Postgresa) — jedna znajomość to jeden wiersz.
    CONSTRAINT chk_friendships_canonical_order CHECK (user_a_id < user_b_id)
);

CREATE INDEX idx_friendships_user_b ON friendships (user_b_id);

CREATE TABLE blocks (
    blocker_id UUID NOT NULL REFERENCES users (id),
    blocked_id UUID NOT NULL REFERENCES users (id),
    created_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (blocker_id, blocked_id)
);
