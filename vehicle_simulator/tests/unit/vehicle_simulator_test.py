import unittest
from unittest.mock import patch, MagicMock
import threading
import time

from src.vehicle_simulator.vehicle_simulator import Vehicle

class TestVehicleUnit(unittest.TestCase):

    @patch('src.vehicle_simulator.vehicle_simulator.requests.delete')
    def test_release_spot_delete_success(self, mock_delete):
        mock_response = MagicMock()
        mock_response.raise_for_status.return_value = None
        mock_delete.return_value = mock_response

        vehicle = Vehicle("car1")
        vehicle.release_spot("booking123", "parking1", 5, delay=0, use_delete=True)

        mock_delete.assert_called_once_with("http://api:8000/parking/booking123")
        mock_response.raise_for_status.assert_called_once()

    @patch('src.vehicle_simulator.vehicle_simulator.requests.put')
    def test_release_spot_put_success(self, mock_put):
        mock_response = MagicMock()
        mock_response.raise_for_status.return_value = None
        mock_put.return_value = mock_response

        vehicle = Vehicle("car2")
        vehicle.release_spot("booking456", "parking2", 3, delay=0, use_delete=False)

        mock_put.assert_called_once_with(
            "http://api:8000/parking/booking456",
            json={"vehicleId": "car2", "active": False},
            headers={"Content-Type": "application/json"}
        )
        mock_response.raise_for_status.assert_called_once()

    @patch('src.vehicle_simulator.vehicle_simulator.requests.delete')
    def test_release_spot_delete_exception(self, mock_delete):
        mock_delete.side_effect = Exception("Network error")

        vehicle = Vehicle("car3")
        vehicle.release_spot("booking789", "parking3", 1, delay=0, use_delete=True)

        mock_delete.assert_called_once()

    @patch('src.vehicle_simulator.vehicle_simulator.requests.get')
    @patch('src.vehicle_simulator.vehicle_simulator.requests.post')
    @patch('src.vehicle_simulator.vehicle_simulator.threading.Thread')
    def test_run_success(self, mock_thread, mock_post, mock_get):
        # Мок ответа для /parking/status
        mock_get_response = MagicMock()
        mock_get_response.raise_for_status.return_value = None
        mock_get_response.json.return_value = [
            {"id": "parking1", "freeSpots": 2}
        ]
        mock_get.side_effect = [mock_get_response,  # для /parking/status
                               MagicMock(raise_for_status=MagicMock(), json=MagicMock(return_value={"booking_id": "book123"})),  # для post /book
                               MagicMock(raise_for_status=MagicMock(), json=MagicMock(return_value={"route": "route_data"}))]  # для get /route

        # Мок threading.Thread для запуска release_spot
        mock_thread_instance = MagicMock()
        mock_thread.return_value = mock_thread_instance

        vehicle = Vehicle("car4")

        # Запустим run в отдельном потоке и остановим после первой итерации
        def run_once():
            vehicle.run_iteration = True
            vehicle.run()
        # Добавим флаг для выхода из бесконечного цикла
        def patched_run(self):
            if hasattr(self, 'run_iteration') and self.run_iteration:
                self.run_iteration = False
                return
            # Вызов оригинального кода
            original_run(self)
        original_run = Vehicle.run
        Vehicle.run = patched_run

        # Запуск теста
        vehicle.run_iteration = True
        vehicle.run()

        # Проверки вызовов
        self.assertTrue(mock_get.called)
        self.assertTrue(mock_post.called)
        self.assertTrue(mock_thread.called)

        # Восстановим оригинальный метод
        Vehicle.run = original_run

if __name__ == '__main__':
    unittest.main()
