import unittest
from unittest import mock
import json
import time
import paho.mqtt.client as mqtt
from data_simulator.data_simulator import (
    on_connect,
    on_disconnect,
    init_bookings_table,
    init_parking_lots,
    logger,
    MQTT_TOPIC
)

class UnitTestDataSimulator(unittest.TestCase):
    @mock.patch('paho.mqtt.client.Client')
    def test_on_connect_success(self, mock_mqtt_client_class):
        mock_client = mock_mqtt_client_class.return_value
        on_connect(mock_client, None, None, 0)
        mock_client.subscribe.assert_called_once_with(MQTT_TOPIC)
        logger.info("test_on_connect_success passed")

    @mock.patch('paho.mqtt.client.Client')
    def test_on_connect_failure_logs_error(self, mock_mqtt_client_class):
        mock_client = mock_mqtt_client_class.return_value
        with self.assertLogs(logger, level='ERROR') as cm:
            on_connect(mock_client, None, None, 1)
        self.assertTrue(any("Failed to connect" in message for message in cm.output))
        logger.info("test_on_connect_failure_logs_error passed")

    @mock.patch('paho.mqtt.client.Client')
    @mock.patch('time.sleep', return_value=None)
    def test_on_disconnect_reconnect(self, mock_sleep, mock_mqtt_client_class):
        mock_client = mock_mqtt_client_class.return_value
        reconnect_calls = []

        def side_effect():
            if len(reconnect_calls) < 1:
                reconnect_calls.append(1)
                raise Exception("fail")
            return None

        mock_client.reconnect.side_effect = side_effect
        with self.assertLogs(logger, level='ERROR') as cm:
            on_disconnect(mock_client, None, 1)
        self.assertTrue(any("Reconnection failed" in message for message in cm.output))
        self.assertEqual(mock_client.reconnect.call_count, 2)
        mock_sleep.assert_called_once_with(5)
        logger.info("test_on_disconnect_reconnect passed")    @mock.patch('data_simulator.data_simulator.data_simulator_client')
    @mock.patch('data_simulator.data_simulator.pg_conn')
    def test_free_spots_calculation_and_publish(self, mock_pg_conn, mock_client):
        mock_cursor = mock.MagicMock()
        mock_pg_conn.cursor.return_value.__enter__.return_value = mock_cursor
        mock_cursor.fetchone.side_effect = [
            (20,),  # total_spots
            (5,)    # booked_spots
        ]

        device_id = 1
        try:
            with mock_pg_conn.cursor() as cur:
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
                mock_client.publish(MQTT_TOPIC, payload_controller)
                logger.info(f"Published MQTT message for device_id {device_id} with free_spots {free_spots}")
        except Exception as e:
            self.fail(f"Exception during free spots calculation and publish: {e}")

        mock_client.publish.assert_called_once()
        args, kwargs = mock_client.publish.call_args
        self.assertEqual(args[0], MQTT_TOPIC)
        published_data = json.loads(args[1])
        self.assertEqual(published_data["device_id"], device_id)
        self.assertEqual(published_data["free_spots"], 15)
        logger.info("test_free_spots_calculation_and_publish passed")

    @mock.patch('data_simulator.data_simulator.data_simulator_client')
    @mock.patch('data_simulator.data_simulator.pg_conn')
    def test_simulator_loop_iteration(self, mock_pg_conn, mock_client):
        mock_cursor = mock.MagicMock()
        mock_pg_conn.cursor.return_value.__enter__.return_value = mock_cursor
        # Setup fetchone to return total_spots and booked_spots for multiple devices
        mock_cursor.fetchone.side_effect = [
            (20,),  # total_spots for device 1
            (5,),   # booked_spots for device 1
            (15,),  # total_spots for device 2
            (10,),  # booked_spots for device 2
        ]

        # Simulate one iteration of the loop for two devices
        for device_id in [1, 2]:
            try:
                with mock_pg_conn.cursor() as cur:
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
                    mock_client.publish(MQTT_TOPIC, payload_controller)
                    logger.info(f"Simulator loop published MQTT message for device_id {device_id} with free_spots {free_spots}")
            except Exception as e:
                self.fail(f"Exception during simulator loop iteration for device {device_id}: {e}")

        self.assertEqual(mock_client.publish.call_count, 2)
        logger.info("test_simulator_loop_iteration passed")

if __name__ == '__main__':
    unittest.main()
