-- Создание таблицы парковок
CREATE TABLE IF NOT EXISTS parking_lots (
    parking_id SERIAL PRIMARY KEY,
    name VARCHAR(100) NOT NULL,
    address VARCHAR(255) NOT NULL,
    capacity INTEGER NOT NULL,
    latitude DOUBLE PRECISION NOT NULL,
    longitude DOUBLE PRECISION NOT NULL,
    hourly_rate DECIMAL(10, 2) NOT NULL
);

-- Создание таблицы парковочных мест
CREATE TABLE IF NOT EXISTS parking_spots (
    id SERIAL PRIMARY KEY,
    parking_id INTEGER NOT NULL REFERENCES parking_lots(parking_id),
    spot_number INTEGER NOT NULL,
    status VARCHAR(20) DEFAULT 'available',
    updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    UNIQUE (parking_id, spot_number)
);

-- Создание таблицы бронирований
CREATE TABLE IF NOT EXISTS bookings (
    id SERIAL PRIMARY KEY,
    vehicle_id VARCHAR(50) NOT NULL,
    parking_id INTEGER NOT NULL,
    spot_number INTEGER NOT NULL,
    booking_time TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
    active BOOLEAN DEFAULT TRUE
);

-- Создание таблицы истории бронирований с партиционированием
CREATE TABLE IF NOT EXISTS booking_history (
    id SERIAL,
    vehicle_id VARCHAR(50) NOT NULL,
    parking_id INTEGER NOT NULL,
    spot_number INTEGER NOT NULL,
    booking_time TIMESTAMP NOT NULL,
    end_time TIMESTAMP,
    status VARCHAR(20),
    PRIMARY KEY (id, booking_time)
) PARTITION BY RANGE (booking_time);

-- Создание партиций для истории бронирований
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

-- Создание индексов для парковочных мест
CREATE INDEX IF NOT EXISTS idx_parking_spots_status ON parking_spots(status);
CREATE INDEX IF NOT EXISTS idx_parking_spots_parking_id ON parking_spots(parking_id);

-- Создание индексов для бронирований
CREATE UNIQUE INDEX IF NOT EXISTS idx_unique_active_booking 
ON bookings (parking_id, spot_number) 
WHERE active = TRUE;

CREATE INDEX IF NOT EXISTS idx_bookings_vehicle_id ON bookings(vehicle_id);
CREATE INDEX IF NOT EXISTS idx_bookings_parking_spot ON bookings(parking_id, spot_number);
CREATE INDEX IF NOT EXISTS idx_bookings_active ON bookings(active);

-- Вставка тестовых данных в таблицу бронирований
INSERT INTO bookings (vehicle_id, parking_id, spot_number, booking_time, active)
VALUES
    ('VEHICLE001', 1, 5, '2025-06-10 08:00:00', true),
    ('VEHICLE002', 1, 10, '2025-06-10 09:30:00', true),
    ('VEHICLE003', 2, 15, '2025-06-10 10:15:00', true),
    ('VEHICLE004', 3, 20, '2025-06-10 11:45:00', false);

-- Вставка тестовых данных в историю бронирований
INSERT INTO booking_history (vehicle_id, parking_id, spot_number, booking_time, end_time, status)
VALUES
    ('VEHICLE005', 1, 25, '2025-06-05 14:00:00', '2025-06-05 18:00:00', 'completed'),
    ('VEHICLE006', 2, 30, '2025-06-07 09:00:00', '2025-06-07 11:30:00', 'completed'),
    ('VEHICLE007', 3, 35, '2025-06-08 12:00:00', '2025-06-08 14:00:00', 'completed'),
    ('VEHICLE008', 1, 40, '2025-06-09 16:00:00', '2025-06-09 19:00:00', 'cancelled');

-- Создание триггера для автоматического обновления времени изменения парковочных мест
CREATE OR REPLACE FUNCTION update_spot_timestamp()
RETURNS TRIGGER AS $$
BEGIN
    NEW.updated_at = NOW();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_spot_update
BEFORE UPDATE ON parking_spots
FOR EACH ROW EXECUTE FUNCTION update_spot_timestamp();

-- Создание функции для автоматического создания партиций
CREATE OR REPLACE FUNCTION create_booking_history_partition()
RETURNS TRIGGER AS $$
BEGIN
    EXECUTE format(
        'CREATE TABLE IF NOT EXISTS booking_history_%s PARTITION OF booking_history '
        'FOR VALUES FROM (%L) TO (%L)',
        to_char(NEW.booking_time, 'y"year"m"month"'),
        date_trunc('month', NEW.booking_time),
        date_trunc('month', NEW.booking_time) + interval '1 month'
    );
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Создание триггера для автоматического создания партиций
CREATE TRIGGER trg_booking_history_partition
BEFORE INSERT ON booking_history
FOR EACH ROW EXECUTE FUNCTION create_booking_history_partition();


-- Очистка таблицы перед заполнением (чтобы избежать дубликатов)
TRUNCATE TABLE parking_lots RESTART IDENTITY CASCADE;

-- Вставка данных для 150 парковок в Санкт-Петербурге (координаты из логов)
INSERT INTO parking_lots (parking_id, name, latitude, longitude, capacity) VALUES
-- City center
(1, 'Admiralteyskaya-1 Parking', 59.9367, 30.3091, 100),
(2, 'Nevsky Prospekt Parking', 59.9327, 30.3310, 150),
(3, 'Palace Square Parking', 59.9390, 30.3158, 80),
(4, 'Kazan Cathedral Parking', 59.9342, 30.3248, 60),
(5, 'Savior on Spilled Blood Parking', 59.9400, 30.3290, 40),
(6, 'St. Isaacs Cathedral Parking', 59.9336, 30.3066, 90),
(7, 'Mariinsky Theatre Parking', 59.9254, 30.2996, 120),
(8, 'Hermitage Parking', 59.9398, 30.3146, 130),
(9, 'Russian Museum Parking', 59.9387, 30.3324, 60),
(10, 'Nevsky Center Parking', 59.9308, 30.3602, 200),

-- Petrogradskaya Storona
(11, 'Peter and Paul Fortress Parking', 59.9497, 30.3166, 70),
(12, 'Krestovsky Island Parking', 59.9715, 30.2592, 200),
(13, 'Primorsky park', 59.9730, 30.2434, 150),
(14, 'Gazprom Arena stadium parking', 59.9728, 30.2210, 500),
(15, 'Parking Planetarium #1', 59.9435, 30.3168, 75),
(16, 'Parking Botanical Garden', 59.9721, 30.3293, 85),
(17, 'Parking Kamenny Island', 59.9797, 30.2701, 55),
(18, 'Parking Elagin Island', 59.9875, 30.2754, 120),
(19, 'Parking Lopukhinsky Garden', 59.9564, 30.2445, 40),
(20, 'Aptekarsky Island Parking', 59.9685, 30.3211, 60),

-- Vasilievsky Island
(21, 'Birzhevaya Ploshad Parking', 59.9440, 30.3047, 80),
(22, 'Strelka V.O. Parking', 59.9905, 30.3864, 90),
(23, 'Universitetskaya Embankment Parking', 59.9342, 30.2511, 70),
(24, 'Akademiya Khudozhestv Parking', 59.9363, 30.3567, 50),
(25, 'Morskoy Vokzal Parking', 59.9704, 30.3658, 200),
(26, 'Parking Gorny Institut', 59.9879, 30.2704, 60),
(27, 'Parking Smolenskoye Cemetery', 59.9503, 30.3487, 40),
(28, 'Parking Gavan V.O.', 59.9363, 30.2748, 100),
(29, 'Parking Sredny Prospekt', 59.9503, 30.3537, 120),
(30, 'Parking Bolshoy Prospekt', 59.9427, 30.2845, 150),

-- Northern areas
(31, 'Parking Ploshchad Muzhestva', 59.9244, 30.2009, 70),
(32, 'Parking Polytechnic Park', 59.9486, 30.3638, 90),
(33, 'Parking Finland Station', 59.9552, 30.3553, 160),
(34, 'Parking Piskarevskoye Cemetery', 59.9814, 30.4195, 50),
(35, 'Parking Udelny Park', 59.9666, 30.2891, 80),
(36, 'Parking Ozerki', 60.0370, 30.3211, 90),
(37, 'Parking Pargolovo', 59.9069, 30.3584, 60),
(38, 'Shuvalovsky Park Parking', 59.9885, 30.3472, 70),
(39, 'Komendantskaya Ploshchad Parking', 59.9426, 30.2815, 110),
(40, 'Pionerskaya Parking', 59.9064, 30.2117, 60),

-- Southern areas
(41, 'Moskovsky Vokzal Parking', 59.9312, 30.3628, 200),
(42, 'Obvodnoy Kanal Parking', 59.9463, 30.2847, 100),
(43, 'Park Pobedy Parking', 59.9974, 30.3765, 150),
(44, 'Moskovskie Vorota Parking', 59.9224, 30.2477, 90),
(45, 'Elektrosila Parking', 59.9155, 30.3534, 80),
(46, 'Park of Hero Cities Parking', 59.9132, 30.2336, 60),
(47, 'Novoizmailovsky Park Parking', 59.9169, 30.3447, 50),
(48, 'Kupchino Parking', 59.9061, 30.3682, 120),
(49, 'Pulkovskiy Park Parking', 59.9192, 30.2694, 70),
(50, 'Vitebsky vokzal Parking', 59.9765, 30.3262, 100),

-- Eastern Districts
(51, 'Aleksandrovsky Park Parking', 59.9352, 30.2748, 90),
(52, 'Smolny Cathedral Parking', 59.9297, 30.3914, 70),
(53, 'Tavrichesky Sad Parking', 59.9385, 30.3784, 60),
(54, 'Novocherkasskaya Parking', 59.9308, 30.3602, 90),
(55, 'Polyustrovsky Park Parking', 59.9793, 30.2988, 50),
(56, 'Ladozhsky Vokzal Parking', 59.9324, 30.4418, 180),
(57, 'Utkina Zavod Parking', 59.9847, 30.3483, 60),
(58, 'Zanevsky Park Parking', 59.9381, 30.2750, 70),
(59, 'Ohta Mall Parking', 59.9805, 30.2470, 300),
(60, 'Ohta Park Parking', 59.9940, 30.2577, 120),

-- Western areas
(61, 'Primorsky Prospekt Parking', 59.9574, 30.2932, 100),
(62, 'Park 300-letiya Parking', 59.9455, 30.2165, 150),
(63, 'Zenit Arena Parking', 59.9318, 30.3466, 400),
(64, 'Primorsky Park Parking', 59.9552, 30.2544, 90),
(65, 'Yuntolovsky Reserve Parking', 59.9222, 30.3414, 50),
(66, 'Udelnaya Parking', 59.9946, 30.3286, 70),
(67, 'Komendantsky Aerodrome Parking', 59.9153, 30.3407, 110),
(68, 'Yubileyniy Kvartal Parking', 59.9366, 30.2186, 80),
(69, 'Shuvalovsky Prospekt Parking', 59.9494, 30.3116, 60),
(70, 'Lakhta Center Parking', 59.9477, 30.2211, 200),

-- Shopping centers
(71, 'Galeria Shopping Mall Parking', 59.9428, 30.3603, 500),
(72, 'Stockmann Shopping Mall Parking', 59.9477, 30.2183, 300),
(73, 'Nevsky Center Shopping Mall Parking', 59.9445, 30.3548, 250),
(74, 'Parking of the Gostiny Dvor Shopping Mall', 59.9104, 30.3256, 150),
(75, 'Parking of the Atmosphere Shopping Mall', 59.9340, 30.2893, 200),
(76, 'Parking of the City Mall Shopping Mall', 59.9894, 30.2115, 400),
(77, 'Parking of the Raduga Shopping Mall', 59.9243, 30.3317, 300),
(78, 'Parking of the Evropolis Shopping Mall', 59.9553, 30.2610, 350),
(79, 'Parking of the Piterland Shopping Mall', 59.9490, 30.3193, 400),
(80, 'Leto Shopping Mall Parking', 59.9592, 30.3284, 450),
(81, 'RIO Shopping Mall Parking', 59.9892, 30.3833, 350),
(82, 'Mercury Shopping Mall Parking', 59.9151, 30.3528, 250),
(83, 'Continent Shopping Mall Parking', 59.9439, 30.3417, 300),
(84, 'Okhta Mall Shopping Mall Parking', 59.9592, 30.3284, 400),
(85, 'Grand Canyon Shopping Mall Parking', 59.9473, 30.2621, 350),

-- Railway stations and airport
(86, 'Moskovsky railway station parking', 59.9312, 30.3628, 300),
(87, 'Vitebsky railway station parking', 59.9203, 30.3269, 200),
(88, 'Baltiysky railway station parking', 59.9162, 30.3354, 200),
(89, 'Finlyandsky railway station parking', 59.9954, 30.3773, 200),
(90, 'Ladozhsky railway station parking', 59.9128, 30.2319, 250),
(91, 'Parking Pulkovo 1 Airport', 59.9327, 30.3805, 500),
(92, 'Parking Pulkovo Airport 2', 59.9548, 30.2769, 600),
(93, 'Parking VIP terminal', 59.9947, 30.3412, 100),
(94, 'Parking Cargo terminal', 59.9064, 30.2459, 150),
(95, 'Parking Bus station', 59.9663, 30.3506, 200),

-- Hotels
(96, 'Parking Hotel Astoria', 59.9683, 30.3636, 80),
(97, 'Parking Grand Hotel Europe', 59.9338, 30.2560, 100),
(98, 'Parking Hotel Corinthia', 59.9560, 30.3741, 120),
(99, 'Parking Hotel Angleterre', 59.9341, 30.3087, 90),
(100, 'Parking Hotel Nevsky Palace', 59.9906, 30.3384, 100),

-- Outskirts
(101, 'Parking Mega Dybenko', 60.0537, 30.4386, 2000),
(102, 'Parking Mega Parnas', 60.0671, 30.3345, 2000),
(103, 'Parking Gallery', 59.9285, 30.3602, 500),
(104, 'Parking June', 60.0537, 30.3339, 700),
(105, 'Parking Rio', 59.8837, 30.3685, 600),
(106, 'Parking IKEA Parnas', 60.0671, 30.3345, 1500),
(107, 'Parking IKEA Dybenko', 60.0537, 30.4386, 1500),
(108, 'Parking Grand Canyon', 60.0183, 30.2579, 800),
(109, 'Parking Pearl Plaza', 59.8497, 30.1495, 900),
(110, 'Parking Piter Raduga', 59.8330, 30.3790, 1000),

-- Additional parking
(111, 'Parking Peterhof', 59.8646, 29.9169, 300),
(112, 'Parking Pavlovsk', 59.6857, 30.4547, 200),
(113, 'Parking Tsarskoe Selo', 59.7163, 30.3952, 250),
(114, 'Parking Kronstadt', 60.0043, 29.7633, 150),
(115, 'Parking Gatchina', 59.5651, 30.1282, 200),
(116, 'Vyborg Parking', 60.7130, 28.7546, 150),
(117, 'Sestroretsk Parking', 60.0987, 29.9702, 100),
(118, 'Zelenogorsk Parking', 60.1950, 29.7019, 80),
(119, 'Pushkin Parking', 59.7246, 30.4161, 200),
(120, 'Repino Parking', 60.1691, 29.8576, 70),

-- Business centers
(121, 'Parking BC Nevsky Plaza', 59.9285, 30.3602, 200),
(122, 'Parking BC Atrium', 59.9346, 30.3330, 150),
(123, 'Parking BC Petrovsky Fort', 59.9666, 30.2856, 180),
(124, 'Parking BC Renaissance Pravda', 59.9331, 30.3590, 200),
(125, 'Parking BC Saint Petersburg Plaza', 59.9217, 30.3400, 250),
(126, 'Parking BC Pulkovo Sky', 59.8365, 30.2621, 300),
(127, 'Parking BC Osen', 59.9499, 30.3158, 150),
(128, 'Bazel BC Parking', 59.9652, 30.3106, 120),
(129, 'Senator BC Parking', 59.9334, 30.3455, 200),
(130, 'Leader BC Parking', 59.9952, 30.2190, 250),

-- Residential complexes
(131, 'Clean Sky Residential Complex Parking', 59.9719, 30.2327, 300),
(132, 'Baltic Pearl Residential Complex Parking', 59.8497, 30.1495, 400),
(133, 'Parking of the residential complex Severnaya Dolina', 60.0671, 30.3345, 350),
(134, 'Parking of the residential complex Novoorlovsky', 60.0044, 30.2166, 200),
(135, 'Parking of the residential complex Yuntolovo', 60.0138, 30.1577, 250),
(136, 'Parking of the residential complex Seven Capitals', 59.9885, 30.2569, 300),
(137, 'Parking of the residential complex Nevskaya Zvezda', 59.9418, 30.4801, 150),
(138, 'Parking of the residential complex Panorama 360', 59.9173, 30.3479, 200),
(139, 'Parking Residential Complex Marine Facade', 59.9445, 30.2169, 300),
(140, 'Parking Residential Complex Vasilievsky Quarter', 59.9437, 30.2537, 250),

-- Parks and Attractions
(141, 'Parking Summer Garden', 59.9447, 30.3363, 50),
(142, 'Parking Park Pobedy', 59.8700, 30.3222, 150),
(143, 'Parking Moskovsky Vokzal', 59.9312, 30.3628, 150),
(144, 'Parking Gostiny Dvor', 59.9340, 30.3356, 100),
(145, 'Sportivnaya Parking', 59.9500, 30.2900, 120),
(146, 'Peterhof Fountains Parking', 59.8794, 29.9071, 200),
(147, 'Konstantinovsky Palace Parking', 59.8551, 30.0705, 150),
(148, 'Krestovsky Island Parking', 59.9730, 30.2591, 200),
(149, 'Hermitage Museum Parking', 59.9410, 30.3127, 100),
(150, 'Mariinsky Theatre Parking', 59.9254, 30.2996, 80);