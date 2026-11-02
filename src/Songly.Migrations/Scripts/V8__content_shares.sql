CREATE TABLE shares (
    post_id UUID NOT NULL REFERENCES posts (id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users (id),
    created_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (post_id, user_id)
);

CREATE INDEX idx_shares_user_id ON shares (user_id);
