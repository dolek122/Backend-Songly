CREATE TABLE posts (
    id UUID PRIMARY KEY,
    author_id UUID NOT NULL REFERENCES users (id),
    content VARCHAR(1000) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX idx_posts_author_id ON posts (author_id);
CREATE INDEX idx_posts_created_at ON posts (created_at DESC);

CREATE TABLE likes (
    post_id UUID NOT NULL REFERENCES posts (id) ON DELETE CASCADE,
    user_id UUID NOT NULL REFERENCES users (id),
    created_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (post_id, user_id)
);

CREATE TABLE comments (
    id UUID PRIMARY KEY,
    post_id UUID NOT NULL REFERENCES posts (id) ON DELETE CASCADE,
    author_id UUID NOT NULL REFERENCES users (id),
    content VARCHAR(500) NOT NULL,
    created_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX idx_comments_post_id ON comments (post_id);
