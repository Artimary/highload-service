import paho.mqtt.client as mqtt
import time
import random
import logging
import json
import psycopg2

MQTT_HOST = 'mosquitto'
MQTT_PORT = 1883
MQTT_TOPIC = 'iot_topic'

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

pg_conn = psycopg2.connect(dbname="parking", user="postgres", password="secret", host="postgresql")

logger.info("Data simulator started.")

def on_connect(client, userdata, flags, rc):
    if rc == 0:
        logger.info("Connected to MQTT Broker!")
        client.subscribe(MQTT_TOPIC)
    else:
        logger.error(f"Failed to connect, return code {rc}")

def on_disconnect(client, userdata, rc):
    logger.warning(f"Disconnected from MQTT Broker. Reason: {rc}")
    while True:
        try:
            logger.info("Attempting to reconnect...")
            client.reconnect()
            break
        except Exception as e:
            logger.error(f"Reconnection failed: {e}")
            time.sleep(5)

def init_parking_lots():
    try:
        with pg_conn.cursor() as cur:
            # Создание таблицы, если она не существует
            cur.execute("""
                CREATE TABLE IF NOT EXISTS parking_lots (
                    id SERIAL PRIMARY KEY,
                    latitude FLOAT NOT NULL,
                    longitude FLOAT NOT NULL,
                    total_spots INTEGER
                )
            """)
            # Проверка наличия столбца total_spots
            cur.execute("""
                SELECT column_name
                FROM information_schema.columns
                WHERE table_name = 'parking_lots' AND column_name = 'total_spots'
            """)
            if cur.fetchone() is None:
                cur.execute("ALTER TABLE parking_lots ADD COLUMN total_spots INTEGER")
            # Инициализация парковок
            for device_id in range(1, 151):
                cur.execute("SELECT total_spots FROM parking_lots WHERE id = %s", (device_id,))
                row = cur.fetchone()
                if row is None:
                    lat = random.uniform(59.9, 60.0)
                    lon = random.uniform(30.2, 30.4)
                    total_spots = random.randint(10, 50)
                    cur.execute(
                        "INSERT INTO parking_lots (id, latitude, longitude, total_spots) VALUES (%s, %s, %s, %s)",
                        (device_id, lat, lon, total_spots)
                    )
                elif row[0] is None:
                    total_spots = random.randint(10, 50)
                    cur.execute(
                        "UPDATE parking_lots SET total_spots = %s WHERE id = %s",
                        (total_spots, device_id)
                    )
            pg_conn.commit()
            logger.info("Initialized parking_lots with 150 entries")
    except Exception as e:
        logger.error(f"Error initializing parking_lots: {str(e)}")
        pg_conn.rollback()

def init_bookings_table():
    try:
        with pg_conn.cursor() as cur:
            # Создание таблицы, если она не существует
            cur.execute("""
                CREATE TABLE IF NOT EXISTS bookings (
                    id SERIAL PRIMARY KEY,
                    vehicle_id TEXT NOT NULL,
                    parking_id INTEGER NOT NULL,
                    spot_number INTEGER NOT NULL,
                    active BOOLEAN NOT NULL DEFAULT TRUE,
                    booked_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                )
            """)
            cur.execute("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM pg_constraint
                        WHERE conname = 'unique_booking'
                    ) THEN
                        ALTER TABLE bookings DROP CONSTRAINT unique_booking;
                    END IF;
                    IF NOT EXISTS (
                        SELECT 1
                        FROM pg_indexes
                        WHERE indexname = 'unique_active_booking'
                    ) THEN
                        CREATE UNIQUE INDEX unique_active_booking ON bookings (parking_id, spot_number) WHERE active = true;
                    END IF;
                END $$;
            """)
            # Проверка наличия столбца booked_at
            cur.execute("""
                SELECT column_name
                FROM information_schema.columns
                WHERE table_name = 'bookings' AND column_name = 'booked_at'
            """)
            if cur.fetchone() is None:
                cur.execute("ALTER TABLE bookings ADD COLUMN booked_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP")
            pg_conn.commit()
            logger.info("Initialized bookings table")
    except Exception as e:
        logger.error(f"Error initializing bookings table: {str(e)}")
        pg_conn.rollback()

# def release_old_bookings():
#     while True:
#         try:
#             conn = psycopg2.connect(dbname="parking", user="postgres", password="secret", host="postgresql")
#             with conn.cursor() as cur:
#                 cur.execute("""
#                     UPDATE bookings
#                     SET active = false
#                     WHERE active = true AND booked_at < NOW() - INTERVAL '20 minutes'
#                 """)
#                 conn.commit()
#                 logger.info("Released old bookings")
#             conn.close()
#         except Exception as e:
#             logger.error(f"Error releasing old bookings: {e}")
#         time.sleep(1200)

data_simulator_client = mqtt.Client()
data_simulator_client.on_connect = on_connect
data_simulator_client.on_disconnect = on_disconnect
try:
    data_simulator_client.connect(MQTT_HOST, MQTT_PORT)
except Exception as e:
    logger.error(f"Failed to connect to MQTT broker: {e}")
    exit(1)

init_parking_lots()
init_bookings_table()

# Запуск потока для освобождения старых бронирований
# release_thread = threading.Thread(target=release_old_bookings)
# release_thread.start()

while True:
    for device_id in range(1, 151):
        # time.sleep(random.uniform(1, 10))
        try:
            with pg_conn.cursor() as cur:
                cur.execute("SELECT total_spots FROM parking_lots WHERE id = %s", (device_id,))
                total_spots = cur.fetchone()[0]
                cur.execute("SELECT COUNT(*) FROM bookings WHERE parking_id = %s AND active = true", (device_id,))
                booked_spots = cur.fetchone()[0]
                free_spots = total_spots - booked_spots
                if free_spots < 0:
                    free_spots = 0
                data_controller = {
                    "device_id": device_id,
                    "free_spots": free_spots,
                    "timestamp": int(time.time() * 1e9)
                }
                payload_controller = json.dumps(data_controller)
                data_simulator_client.publish(MQTT_TOPIC, payload_controller)
        except Exception as e:
            logger.error(f"Error calculating free spots for device {device_id}: {e}")
    time.sleep(15)