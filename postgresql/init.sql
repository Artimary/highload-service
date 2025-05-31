-- Создание таблицы bookings
CREATE TABLE IF NOT EXISTS bookings (
    id SERIAL PRIMARY KEY,
    vehicle_id TEXT NOT NULL,
    parking_id INTEGER NOT NULL,
    spot_number INTEGER NOT NULL,
    active BOOLEAN NOT NULL DEFAULT TRUE
);

-- Добавление уникального ограничения
DO $$ 
BEGIN
    IF NOT EXISTS (
        SELECT 1 
        FROM pg_constraint 
        WHERE conname = 'unique_booking'
    ) THEN
        ALTER TABLE bookings 
        ADD CONSTRAINT unique_booking 
        UNIQUE (parking_id, spot_number, active);
    END IF;
END $$;