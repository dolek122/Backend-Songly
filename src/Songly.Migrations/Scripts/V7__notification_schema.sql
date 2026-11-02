CREATE TABLE notifications (
    id UUID PRIMARY KEY,
    user_id UUID NOT NULL REFERENCES users (id),
    type VARCHAR(40) NOT NULL,
    actor_id UUID NOT NULL REFERENCES users (id),
    resource_id UUID NOT NULL,
    created_at TIMESTAMPTZ NOT NULL,
    read_at TIMESTAMPTZ
);

CREATE INDEX idx_notifications_user_created ON notifications (user_id, created_at DESC);
CREATE INDEX idx_notifications_user_unread ON notifications (user_id) WHERE read_at IS NULL;
