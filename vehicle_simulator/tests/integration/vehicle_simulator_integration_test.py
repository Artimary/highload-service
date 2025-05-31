import unittest
import threading
import time
from unittest.mock import patch, MagicMock
from src.vehicle_simulator.vehicle_simulator import Vehicle

class TestVehicleSimulatorIntegration(unittest.TestCase):

    def test_multithreaded_vehicle_simulation(self):
        # Создаем несколько автомобилей
        vehicles = [Vehicle(f"test_car{i}") for i in range(5)]

        # Запускаем каждый в отдельном потоке
        threads = []
        for vehicle in vehicles:
            thread = threading.Thread(target=vehicle.run)
            thread.daemon = True  # чтобы потоки не блокировали выход из теста
            thread.start()
            threads.append(thread)

        # Даем потокам поработать некоторое время
        time.sleep(5)

        # Проверяем, что потоки запущены и активны
        for thread in threads:
            self.assertTrue(thread.is_alive())

        # Завершаем тест (потоки daemon завершатся при выходе из теста)

    @patch('src.vehicle_simulator.vehicle_simulator.requests.get')
    @patch('src.vehicle_simulator.vehicle_simulator.requests.post')
    @patch('src.vehicle_simulator.vehicle_simulator.requests.delete')
    @patch('src.vehicle_simulator.vehicle_simulator.requests.put')
    def test_full_booking_and_release_flow(self, mock_put, mock_delete, mock_post, mock_get):
        # Мок ответов для /parking/status
        mock_get_status_response = MagicMock()
        mock_get_status_response.raise_for_status.return_value = None
        mock_get_status_response.json.return_value = [
            {"id": "parking1", "freeSpots": 1}
        ]
        # Мок ответов для /parking/{parking_id}/route
        mock_get_route_response = MagicMock()
        mock_get_route_response.raise_for_status.return_value = None
        mock_get_route_response.json.return_value = {"route": "route_data"}

        # Настройка side_effect для последовательных вызовов requests.get
        mock_get.side_effect = [mock_get_status_response, mock_get_route_response]

        # Мок ответов для бронирования
        mock_post_response = MagicMock()
        mock_post_response.raise_for_status.return_value = None
        mock_post_response.json.return_value = {"booking_id": "booking123"}
        mock_post.return_value = mock_post_response

        # Мок успешного удаления и обновления
        mock_delete_response = MagicMock()
        mock_delete_response.raise_for_status.return_value = None
        mock_delete.return_value = mock_delete_response

        mock_put_response = MagicMock()
        mock_put_response.raise_for_status.return_value = None
        mock_put.return_value = mock_put_response

        vehicle = Vehicle("integration_car")

        # Запуск run в отдельном потоке, ограничим время работы
        def run_vehicle():
            # Запускаем только одну итерацию цикла run
            vehicle.run_iteration = True
            vehicle.run()

        # Патчим метод run для выхода после одной итерации
        original_run = Vehicle.run
        def patched_run(self):
            if hasattr(self, 'run_iteration') and self.run_iteration:
                self.run_iteration = False
                return
            original_run(self)
        Vehicle.run = patched_run

        thread = threading.Thread(target=run_vehicle)
        thread.start()
        thread.join(timeout=10)

        # Проверяем, что все запросы были вызваны
        mock_get.assert_any_call("http://api:8000/parking/status", params=unittest.mock.ANY)
        mock_post.assert_called_once()
        mock_get.assert_any_call("http://api:8000/parking/parking1/route")
        mock_delete.assert_not_called()  # release_spot запускается в отдельном потоке с задержкой, не вызывается сразу
        mock_put.assert_not_called()

        # Восстановим оригинальный метод
        Vehicle.run = original_run

    @patch('src.vehicle_simulator.vehicle_simulator.requests.delete')
    def test_release_spot_delete_called(self, mock_delete):
        mock_response = MagicMock()
        mock_response.raise_for_status.return_value = None
        mock_delete.return_value = mock_response

        vehicle = Vehicle("integration_car2")
        vehicle.release_spot("booking123", "parking1", 1, delay=0, use_delete=True)

        mock_delete.assert_called_once_with("http://api:8000/parking/booking123")

    @patch('src.vehicle_simulator.vehicle_simulator.requests.put')
    def test_release_spot_put_called(self, mock_put):
        mock_response = MagicMock()
        mock_response.raise_for_status.return_value = None
        mock_put.return_value = mock_response

        vehicle = Vehicle("integration_car3")
        vehicle.release_spot("booking456", "parking2", 2, delay=0, use_delete=False)

        mock_put.assert_called_once_with(
            "http://api:8000/parking/booking456",
            json={"vehicleId": "integration_car3", "active": False},
            headers={"Content-Type": "application/json"}
        )

if __name__ == '__main__':
    unittest.main()
