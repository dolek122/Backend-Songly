CREATE TABLE music_provider_links (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users (id),
    provider VARCHAR(30) NOT NULL,
    access_token_encrypted TEXT NOT NULL,
    refresh_token_encrypted TEXT NOT NULL,
    access_token_expires_at TIMESTAMPTZ NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL,
    UNIQUE (user_id, provider)
);

CREATE INDEX idx_music_provider_links_user_id ON music_provider_links (user_id);
