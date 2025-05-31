import paho.mqtt.client as mqtt
from influxdb_client import InfluxDBClient, Point, WritePrecision
from collections import defaultdict
from influxdb_client.client.write_api import SYNCHRONOUS
import json
import logging
import time

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

# Конфигурация MQTT
MQTT_HOST = 'mosquitto'
MQTT_PORT = 1883
MQTT_TOPIC = 'rule_engine_topic'

# InfluxDB connection
influx_client = InfluxDBClient(url="http://influxdb:8086", token="super-secret-token", org="iot_org")
query_api = influx_client.query_api()
write_api = influx_client.write_api(write_options=SYNCHRONOUS)
device_state = defaultdict(list)

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

def on_message(client, userdata, message):
    try:
        data = json.loads(message.payload.decode())
        logger.info(f"Received data: {data}")
        
        device_id = data.get("device_id")
        free_spots = data.get("free_spots")

        # Instant rule: free_spots > 5
        if free_spots > 5:
            logger.info(f"Alert: Device {device_id} has {free_spots} free spots.")
            point = Point("rule_instant") \
                .tag("device_id", device_id) \
                .field("free_spots", free_spots) \
                .field("alert_type", "instant") \
                .time(time.time_ns(), WritePrecision.NS)
            write_api.write(bucket="rule_engine_bucket", record=point)

        # Lasting rule: free_spots > 5 for 10 packets
        device_state[device_id].append(free_spots)
        if len(device_state[device_id]) >= 10:
            if all(x > 5 for x in device_state[device_id][-10:]):
                logger.info(f"Alert: Device {device_id} has >5 free spots for 10 packets.")
                point = Point("rule_lasting") \
                    .tag("device_id", device_id) \
                    .field("free_spots", free_spots) \
                    .field("alert_type", "lasting") \
                    .time(time.time_ns(), WritePrecision.NS)
                write_api.write(bucket="rule_engine_bucket", record=point)
            device_state[device_id].pop(0)
    except Exception as e:
        logger.error(f"Error processing message: {e}")

def setup_mqtt():
    rule_engine_client = mqtt.Client()
    rule_engine_client.on_message = on_message
    rule_engine_client.on_connect = on_connect
    rule_engine_client.on_disconnect = on_disconnect

    try:
        logger.info(f"Trying to connect to MQTT broker at {MQTT_HOST}:{MQTT_PORT}")
        rule_engine_client.connect(MQTT_HOST, MQTT_PORT)
        logger.info("Connected successfully to MQTT broker")
    except Exception as e:
        logger.error(f"Error connecting to MQTT broker: {e}")
        return None

    rule_engine_client.subscribe(MQTT_TOPIC)
    
    return rule_engine_client

def main():
    rule_engine_client = setup_mqtt()
    if not rule_engine_client:
        logger.error("Exiting: Unable to connect to MQTT broker.")
        return
    rule_engine_client.loop_forever()

if __name__ == '__main__':
    main()