CREATE TABLE analytics_events (
    id UUID PRIMARY KEY,
    type VARCHAR(40) NOT NULL,
    occurred_at TIMESTAMPTZ NOT NULL
);

CREATE INDEX idx_analytics_events_type_occurred ON analytics_events (type, occurred_at);
