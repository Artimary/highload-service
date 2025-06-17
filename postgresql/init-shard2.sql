-- Таблица для текущих бронирований
CREATE TABLE IF NOT EXISTS bookings (
    id SERIAL PRIMARY KEY,
    vehicle_id VARCHAR(50) NOT NULL,
    parking_id INTEGER NOT NULL,
    spot_number INTEGER NOT NULL,
    booking_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    active BOOLEAN DEFAULT TRUE
);

-- Создаем частичный уникальный индекс вместо ограничения с WHERE
CREATE UNIQUE INDEX idx_unique_active_booking 
ON bookings (parking_id, spot_number) 
WHERE active = TRUE;

-- Партиционированная таблица для истории бронирований
CREATE TABLE booking_history (
    id SERIAL,
    vehicle_id VARCHAR(50) NOT NULL,
    parking_id INTEGER NOT NULL,
    spot_number INTEGER NOT NULL,
    booking_time TIMESTAMP NOT NULL,
    end_time TIMESTAMP,
    status VARCHAR(20),
    PRIMARY KEY (id, booking_time)
) PARTITION BY RANGE (booking_time);

-- Создание партиций по месяцам
CREATE TABLE booking_history_y2025m01 PARTITION OF booking_history
    FOR VALUES FROM ('2025-01-01') TO ('2025-02-01');

CREATE TABLE booking_history_y2025m02 PARTITION OF booking_history
    FOR VALUES FROM ('2025-02-01') TO ('2025-03-01');

CREATE TABLE booking_history_y2025m03 PARTITION OF booking_history
    FOR VALUES FROM ('2025-03-01') TO ('2025-04-01');

CREATE TABLE booking_history_y2025m04 PARTITION OF booking_history
    FOR VALUES FROM ('2025-04-01') TO ('2025-05-01');

CREATE TABLE booking_history_y2025m05 PARTITION OF booking_history
    FOR VALUES FROM ('2025-05-01') TO ('2025-06-01');

CREATE TABLE booking_history_y2025m06 PARTITION OF booking_history
    FOR VALUES FROM ('2025-06-01') TO ('2025-07-01');

-- Индексы для ускорения запросов
CREATE INDEX idx_bookings_vehicle_id ON bookings(vehicle_id);
CREATE INDEX idx_bookings_parking_spot ON bookings(parking_id, spot_number);
CREATE INDEX idx_bookings_active ON bookings(active);

-- Добавление демо-данных
INSERT INTO bookings (vehicle_id, parking_id, spot_number, booking_time, active)
VALUES
    ('VEHICLE001', 1, 5, '2025-06-10 08:00:00', true),
    ('VEHICLE002', 1, 10, '2025-06-10 09:30:00', true),
    ('VEHICLE003', 2, 15, '2025-06-10 10:15:00', true),
    ('VEHICLE004', 3, 20, '2025-06-10 11:45:00', false);

-- Добавление демо-данных в историю бронирований
INSERT INTO booking_history (vehicle_id, parking_id, spot_number, booking_time, end_time, status)
VALUES
    ('VEHICLE005', 1, 25, '2025-06-05 14:00:00', '2025-06-05 18:00:00', 'completed'),
    ('VEHICLE006', 2, 30, '2025-06-07 09:00:00', '2025-06-07 11:30:00', 'completed'),
    ('VEHICLE007', 3, 35, '2025-06-08 12:00:00', '2025-06-08 14:00:00', 'completed'),
    ('VEHICLE008', 1, 40, '2025-06-09 16:00:00', '2025-06-09 19:00:00', 'cancelled');
