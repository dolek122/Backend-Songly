CREATE TABLE profiles (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL UNIQUE REFERENCES users (id),
    display_name VARCHAR(80) NOT NULL,
    bio VARCHAR(500),
    avatar_url VARCHAR(2048),
    created_at TIMESTAMPTZ NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL
);

CREATE TABLE genres (
    id UUID PRIMARY KEY,
    name VARCHAR(100) NOT NULL UNIQUE
);

CREATE TABLE user_genres (
    user_id UUID NOT NULL REFERENCES users (id),
    genre_id UUID NOT NULL REFERENCES genres (id),
    created_at TIMESTAMPTZ NOT NULL,
    PRIMARY KEY (user_id, genre_id)
);

INSERT INTO genres (id, name) VALUES
    ('a5a9f290-1111-4a2e-8b1a-000000000001', 'Pop'),
    ('a5a9f290-1111-4a2e-8b1a-000000000002', 'Rock'),
    ('a5a9f290-1111-4a2e-8b1a-000000000003', 'Hip-Hop'),
    ('a5a9f290-1111-4a2e-8b1a-000000000004', 'R&B'),
    ('a5a9f290-1111-4a2e-8b1a-000000000005', 'Jazz'),
    ('a5a9f290-1111-4a2e-8b1a-000000000006', 'Elektronika'),
    ('a5a9f290-1111-4a2e-8b1a-000000000007', 'Klasyczna'),
    ('a5a9f290-1111-4a2e-8b1a-000000000008', 'Metal'),
    ('a5a9f290-1111-4a2e-8b1a-000000000009', 'Indie'),
    ('a5a9f290-1111-4a2e-8b1a-00000000000a', 'Country'),
    ('a5a9f290-1111-4a2e-8b1a-00000000000b', 'Reggae'),
    ('a5a9f290-1111-4a2e-8b1a-00000000000c', 'Latynoska'),
    ('a5a9f290-1111-4a2e-8b1a-00000000000d', 'K-pop'),
    ('a5a9f290-1111-4a2e-8b1a-00000000000e', 'Folk'),
    ('a5a9f290-1111-4a2e-8b1a-00000000000f', 'Punk');
