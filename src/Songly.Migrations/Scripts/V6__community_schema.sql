CREATE TABLE communities (
    id UUID PRIMARY KEY,
    name VARCHAR(100) NOT NULL UNIQUE,
    description VARCHAR(500),
    owner_id UUID NOT NULL REFERENCES users (id),
    created_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE community_members (
    community_id UUID NOT NULL REFERENCES communities (id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users (id),
    role VARCHAR(20) NOT NULL,
    joined_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (community_id, user_id)
);

CREATE INDEX idx_community_members_user ON community_members (user_id);
