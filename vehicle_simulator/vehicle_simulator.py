import requests
import random
import time
import threading
import concurrent.futures
import logging
from datetime import datetime

# Настройка логирования
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger('vehicle_simulator')

BASE_URL = "http://nginx:80"
# Максимальное количество активных потоков освобождения
MAX_RELEASE_THREADS = 500
# Семафор для контроля количества потоков
release_semaphore = threading.Semaphore(MAX_RELEASE_THREADS)

class Vehicle:
    def __init__(self, vehicle_id):
        self.vehicle_id = vehicle_id
        self.logger = logging.getLogger(f'vehicle.{vehicle_id}')
        self.active_bookings = set()  # Отслеживаем активные бронирования

    def release_spot(self, booking_id, parking_id, spot_number, delay_minutes, use_delete=True):
        with release_semaphore:
            try:
                # Реалистичная задержка в минутах
                actual_delay = random.uniform(delay_minutes * 0.8, delay_minutes * 1.2)
                self.logger.info(f"Will release spot {spot_number} at parking {parking_id} in {actual_delay:.1f} minutes")
                time.sleep(actual_delay * 60)  # Конвертация в секунды
                
                # Проверяем, не было ли уже освобождено это бронирование
                if booking_id not in self.active_bookings:
                    self.logger.warning(f"Booking {booking_id} not found in active bookings, possibly already released")
                    return
                
                try:
                    if use_delete:
                        # Используем DELETE для полного удаления бронирования
                        response = requests.delete(f"{BASE_URL}/parking/{booking_id}")
                        response.raise_for_status()
                        self.logger.info(f"Released spot {spot_number} at parking {parking_id}, booking_id: {booking_id} (deleted)")
                    else:
                        # Используем UPDATE для изменения статуса на неактивный
                        payload = {"vehicleId": self.vehicle_id, "active": False}
                        response = requests.put(
                            f"{BASE_URL}/parking/{booking_id}",
                            json=payload,
                            headers={"Content-Type": "application/json"}
                        )
                        response.raise_for_status()
                        self.logger.info(f"Released spot {spot_number} at parking {parking_id}, booking_id: {booking_id} (updated to inactive)")
                    
                    # Успешно освободили место
                    if booking_id in self.active_bookings:
                        self.active_bookings.remove(booking_id)
                        
                except requests.exceptions.RequestException as e:
                    if hasattr(e, 'response') and e.response is not None:
                        self.logger.error(f"Error releasing spot: {e}, Response: {e.response.text}")
                    else:
                        self.logger.error(f"Error releasing spot: {e}")
            except Exception as e:
                self.logger.error(f"Unexpected error in release_spot: {e}")

    def get_random_spot_number(self, parking_id):
        """Получить реалистичный номер места на парковке"""
        try:
            # Запрашиваем детали парковки для получения capacity
            response = requests.get(f"{BASE_URL}/parking/{parking_id}")
            if response.status_code == 200:
                parking_details = response.json()
                capacity = parking_details.get("capacity", 100)
                return random.randint(1, capacity)
            else:
                # Возвращаемся к значению по умолчанию при ошибке
                self.logger.warning(f"Failed to get parking details for ID {parking_id}, using default spot range")
                return random.randint(1, 50)
        except Exception as e:
            self.logger.error(f"Error getting spot number for parking {parking_id}: {e}")
            return random.randint(1, 50)

    def run(self):
        while True:
            try:
                # ИСПРАВЛЕНИЕ: Не используем геокоординаты, так как координаты парковок не настроены
                # Запрос списка всех парковок без параметров фильтрации
                self.logger.debug(f"Requesting all parking lots")
                
                try:
                    # Первый вариант эндпоинта (список всех парковок)
                    response = requests.get(f"{BASE_URL}/parking/status")
                    
                    # Если первый вариант не работает, пробуем альтернативный эндпоинт
                    # if response.status_code != 200:
                    #     self.logger.debug(f"First endpoint returned {response.status_code}, trying alternative")
                    #     response = requests.get(f"{BASE_URL}/parking")
                    
                    response.raise_for_status()
                    parking_lots = response.json()
                    self.logger.info(f"Received {len(parking_lots)} parking lots")
                    
                    # Логирование первых трех парковок для отладки
                    if len(parking_lots) > 0:
                        sample = parking_lots[:min(3, len(parking_lots))]
                        self.logger.debug(f"Sample parking lots: {sample}")
                
                except requests.exceptions.RequestException as e:
                    if hasattr(e, 'response') and e.response is not None:
                        self.logger.error(f"Error requesting parking list: {e}, Response: {e.response.text}")
                    else:
                        self.logger.error(f"Error requesting parking list: {e}")
                    parking_lots = []
                    time.sleep(5)  # Ожидание перед следующей попыткой
                    continue

                if parking_lots:
                    # Выбираем парковку с приоритетом для тех, где больше свободных мест
                    parking_with_spots = [p for p in parking_lots if p.get("freeSpots", 0) > 0]
                    
                    if not parking_with_spots:
                        self.logger.info("No parking lots with free spots available")
                        time.sleep(random.uniform(30, 60))
                        continue
                    
                    # Взвешенный выбор парковки по количеству свободных мест
                    weighted_choice = [(p, p.get("freeSpots", 0)) for p in parking_with_spots]
                    parking = random.choices(
                        [p[0] for p in weighted_choice],
                        weights=[p[1] for p in weighted_choice]
                    )[0]
                    
                    parking_id = parking.get("id")
                    free_spots = parking.get("freeSpots", 0)
                    
                    if free_spots > 0:
                        # Получаем номер места
                        spot_number = random.randint(1, 50)  # Упрощенный подход
                        
                        # Бронирование места
                        payload = {"VehicleId": self.vehicle_id, "SpotNumber": spot_number}
                        self.logger.info(f"Trying to book spot {spot_number} at parking {parking_id}")
                        
                        try:
                            book_response = requests.post(
                                f"{BASE_URL}/parking/{parking_id}/book",
                                json=payload,
                                headers={"Content-Type": "application/json"}
                            )
                            book_response.raise_for_status()
                            booking_data = book_response.json()
                            booking_id = booking_data.get("booking_id", "unknown")
                            self.logger.info(f"Booked spot {spot_number} at parking {parking_id}, booking_id: {booking_id}")
                            
                            # Добавляем бронирование в активные
                            self.active_bookings.add(booking_id)
                            
                            # Запрос маршрута
                            time.sleep(random.uniform(1, 3))
                            route_response = requests.get(f"{BASE_URL}/parking/{parking_id}/route")
                            
                            # Запуск освобождения места через реалистичное время
                            delay_minutes = random.uniform(0.5, 2.0)  # 30-120 секунд для тестирования
                            
                            release_thread = threading.Thread(
                                target=self.release_spot,
                                args=(booking_id, parking_id, spot_number, delay_minutes, True),
                                daemon=True
                            )
                            release_thread.start()
                            
                        except requests.exceptions.RequestException as e:
                            if hasattr(e, 'response') and e.response:
                                status = e.response.status_code
                                error_text = e.response.text
                                self.logger.error(f"Error booking spot: Status {status}: {error_text}")
                                
                                # Проверка на "призрачное" бронирование
                                if "already booked" in error_text:
                                    self.logger.warning(f"Spot {spot_number} already booked at parking {parking_id}")
                                elif "column" in error_text and "not exist" in error_text:
                                    self.logger.warning("Possible 'ghost booking' detected.")
                            else:
                                self.logger.error(f"Error booking spot: {e}")
                    else:
                        self.logger.info(f"No free spots at parking {parking_id}")
                
                # Случайная пауза между попытками бронирования
                time.sleep(random.uniform(5, 10))  # Уменьшено для тестирования
                
            except Exception as e:
                self.logger.error(f"Unexpected error in run: {e}")
                time.sleep(5)

def vehicle_task(vehicle_id):
    vehicle = Vehicle(f"car{vehicle_id}")
    vehicle.run()

if __name__ == "__main__":
    # Уменьшаем количество автомобилей для тестирования
    num_vehicles = 100
    
    # Создаем и запускаем пул потоков для автомобилей
    with concurrent.futures.ThreadPoolExecutor(max_workers=num_vehicles) as executor:
        executor.map(vehicle_task, range(1, num_vehicles + 1))