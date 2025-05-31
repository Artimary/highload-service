import paho.mqtt.client as mqtt
from influxdb_client import InfluxDBClient, Point, WritePrecision
from influxdb_client.client.write_api import SYNCHRONOUS
import json
import logging
import time

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

MQTT_HOST = 'mosquitto' 
MQTT_PORT = 1883
MQTT_TOPIC = 'iot_topic'

# InfluxDB connection
influx_client = InfluxDBClient(url="http://influxdb:8086", token="super-secret-token", org="iot_org")
query_api = influx_client.query_api()
write_api = influx_client.write_api(write_options=SYNCHRONOUS)

def on_message(client, userdata, message):
    try:
        data = json.loads(message.payload.decode())
        device_id = data.get("device_id")
        free_spots = data.get("free_spots")
        timestamp = data.get("timestamp")
        
        if validate_data(data):
            data_rule = {
                "device_id": device_id,
                "free_spots": free_spots,
            }
            payload_rule = json.dumps(data_rule)
            client.publish("rule_engine_topic", payload_rule)
            # Save to InfluxDB
            point = Point("parking_data") \
                .tag("device_id", device_id) \
                .field("free_spots", free_spots) \
                .time(timestamp, WritePrecision.NS)
            try:
                write_api.write(bucket="iot_bucket", record=point)
                logger.info(f"Data written to InfluxDB: {data}")
            except Exception as e:
                logger.error(f"Error writing data to InfluxDB: {e}")
    except Exception as e:
        logger.error(f"Error processing message: {e}")
    
def validate_data(data):
    free_spots = int(data.get("free_spots", -1))
    if free_spots < 0:
        logger.error(f"Invalid data: free_spots={free_spots} (must be >= 0)")
    return free_spots >= 0

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

def setup_mqtt(client=None):
    if client is None:
        client = mqtt.Client()
    client.on_message = on_message
    client.on_connect = on_connect
    client.on_disconnect = on_disconnect
    
    try:
        logger.info(f"Trying to connect to MQTT broker at {MQTT_HOST}:{MQTT_PORT}")
        client.connect(MQTT_HOST, MQTT_PORT)
        logger.info("Connected successfully to MQTT broker")
    except Exception as e:
        logger.error(f"Error connecting to MQTT broker: {e}")
        return None

    client.subscribe(MQTT_TOPIC)
    
    return client

def main():
    client = setup_mqtt()
    if not client:
        logger.error("Exiting: Unable to connect to MQTT broker.")
        return

    client.loop_forever()

if __name__ == '__main__':
    main()
