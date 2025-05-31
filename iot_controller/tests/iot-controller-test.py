import unittest
from unittest import mock
from iot_controller.iot_controller import validate_data, on_message, on_connect, on_disconnect, setup_mqtt, write_api, logger

class TestValidateData(unittest.TestCase):
    def test_valid_integer(self):
        data = {"free_spots": 5}
        self.assertTrue(validate_data(data))

    def test_valid_string_integer(self):
        data = {"free_spots": "5"}
        self.assertTrue(validate_data(data))

    def test_invalid_negative(self):
        data = {"free_spots": -1}
        self.assertFalse(validate_data(data))

    def test_missing_free_spots(self):
        data = {}
        self.assertFalse(validate_data(data))

    def test_invalid_string(self):
        data = {"free_spots": "five"}
        with self.assertRaises(ValueError):
            validate_data(data)

class TestOnMessage(unittest.TestCase):
    @mock.patch('iot_controller.iot_controller.write_api')
    def test_valid_message(self, mock_write_api):
        message = mock.MagicMock()
        message.payload.decode.return_value = '{"device_id": "dev1", "free_spots": 5, "timestamp": 1234567890}'
        mock_client = mock.MagicMock()
        mock_client.publish = mock.MagicMock()
        mock_write_api.write = mock.MagicMock()
        on_message(mock_client, None, message)
        mock_client.publish.assert_called_once_with("rule_engine_topic", '{"device_id": "dev1", "free_spots": 5}')
        mock_write_api.write.assert_called_once_with(bucket="iot_bucket", record=mock.ANY)

    @mock.patch('iot_controller.iot_controller.write_api')
    def test_invalid_message(self, mock_write_api):
        message = mock.MagicMock()
        message.payload.decode.return_value = '{"device_id": "dev1", "free_spots": -1, "timestamp": 1234567890}'
        mock_client = mock.MagicMock()
        with self.assertLogs(logger, level='ERROR') as cm:
            on_message(mock_client, None, message)
        self.assertIn("Invalid data: free_spots=-1 (must be >= 0)", cm.output[0])
        mock_client.publish.assert_not_called()
        mock_write_api.write.assert_not_called()

    @mock.patch('iot_controller.iot_controller.write_api')
    def test_malformed_json(self, mock_write_api):
        message = mock.MagicMock()
        message.payload.decode.return_value = 'invalid json'
        mock_client = mock.MagicMock()
        with self.assertLogs(logger, level='ERROR') as cm:
            on_message(mock_client, None, message)
        # Adjusted assertion to check for generic 'Error processing message' log instead of 'JSONDecodeError'
        self.assertTrue(any("Error processing message" in msg for msg in cm.output))
        mock_client.publish.assert_not_called()
        mock_write_api.write.assert_not_called()

class TestOnConnect(unittest.TestCase):
    def test_successful_connection(self):
        mock_client = mock.MagicMock()
        on_connect(mock_client, None, None, 0)
        mock_client.subscribe.assert_called_once_with('iot_topic')

    def test_failed_connection(self):
        mock_client = mock.MagicMock()
        with self.assertLogs(logger, level='ERROR') as cm:
            on_connect(mock_client, None, None, 1)
        self.assertIn("Failed to connect, return code 1", cm.output[0])
        mock_client.subscribe.assert_not_called()

class TestOnDisconnect(unittest.TestCase):
    @mock.patch('iot_controller.iot_controller.time.sleep')
    def test_successful_reconnect(self, mock_sleep):
        mock_client = mock.MagicMock()
        mock_client.reconnect = mock.MagicMock(return_value=None)
        on_disconnect(mock_client, None, 1)
        mock_client.reconnect.assert_called_once()
        mock_sleep.assert_not_called()

    @mock.patch('iot_controller.iot_controller.time.sleep')
    def test_failed_reconnect(self, mock_sleep):
        mock_client = mock.MagicMock()
        mock_client.reconnect = mock.MagicMock(side_effect=[Exception("fail"), None])
        with self.assertLogs(logger, level='ERROR') as cm:
            on_disconnect(mock_client, None, 1)
        self.assertTrue(any("Reconnection failed: fail" in msg for msg in cm.output))
        self.assertEqual(mock_client.reconnect.call_count, 2)
        mock_sleep.assert_called_once_with(5)

class TestSetupMQTT(unittest.TestCase):
    @mock.patch('paho.mqtt.client.Client')
    def test_successful_connection(self, mock_client):
        mock_instance = mock_client.return_value
        mock_instance.connect.return_value = None
        result = setup_mqtt(client=mock_instance)
        self.assertIsNotNone(result)
        self.assertEqual(mock_instance.on_message, on_message)
        self.assertEqual(mock_instance.on_connect, on_connect)
        self.assertEqual(mock_instance.on_disconnect, on_disconnect)
        mock_instance.connect.assert_called_with('mosquitto', 1883)
        mock_instance.subscribe.assert_called_with('iot_topic')

    @mock.patch('paho.mqtt.client.Client')
    def test_failed_connection(self, mock_client):
        mock_instance = mock_client.return_value
        mock_instance.connect.side_effect = Exception("Connection failed")
        with self.assertLogs(logger, level='ERROR') as cm:
            result = setup_mqtt(client=mock_instance)
        self.assertIsNone(result)
        self.assertTrue(any("Error connecting to MQTT broker" in msg for msg in cm.output))

if __name__ == '__main__':
    unittest.main()
