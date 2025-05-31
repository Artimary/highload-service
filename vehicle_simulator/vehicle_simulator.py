import requests
import random
import time
import threading
import concurrent.futures

BASE_URL = "http://api:8000"

class Vehicle:
    def __init__(self, vehicle_id):
        self.vehicle_id = vehicle_id

    def release_spot(self, booking_id, parking_id, spot_number, delay, use_delete=True):
        time.sleep(delay)  # Задержка 5–15 минут
        try:
            if use_delete:
                # Используем DELETE для полного удаления бронирования
                response = requests.delete(f"{BASE_URL}/parking/{booking_id}")
                response.raise_for_status()
                print(f"{self.vehicle_id}: Released spot {spot_number} at parking {parking_id}, booking_id: {booking_id} (deleted)")
            else:
                # Используем UPDATE для изменения статуса на неактивный
                payload = {"vehicleId": self.vehicle_id, "active": False}
                response = requests.put(
                    f"{BASE_URL}/parking/{booking_id}",
                    json=payload,
                    headers={"Content-Type": "application/json"}
                )
                response.raise_for_status()
                print(f"{self.vehicle_id}: Released spot {spot_number} at parking {parking_id}, booking_id: {booking_id} (updated to inactive)")
        except requests.exceptions.RequestException as e:
            if hasattr(e, 'response') and e.response is not None:
                print(f"{self.vehicle_id}: Error releasing spot: {e}, Response: {e.response.text}")
            else:
                print(f"{self.vehicle_id}: Error releasing spot: {e}")

    def run(self):
        while True:
            # Генерация случайных координат
            lat = random.uniform(59.9, 60.0)
            lon = random.uniform(30.2, 30.4)
            params = {"lat": lat, "lon": lon, "radius": 1000}
            try:
                # Запрос списка парковок
                time.sleep(random.uniform(10, 20))
                response = requests.get(f"{BASE_URL}/parking/status", params=params)
                time.sleep(1)
                response.raise_for_status()
                parking_lots = response.json()
                print(f"{self.vehicle_id}: Received {len(parking_lots)} parking lots")

                if parking_lots:
                    parking = random.choice(parking_lots)
                    parking_id = parking["id"]
                    if parking["freeSpots"] > 0:
                        spot_number = random.randint(1, parking["freeSpots"])
                        payload = {"VehicleId": self.vehicle_id, "SpotNumber": spot_number}
                        # Бронирование места
                        time.sleep(random.uniform(10, 20))
                        book_response = requests.post(
                            f"{BASE_URL}/parking/{parking_id}/book",
                            json=payload,
                            headers={"Content-Type": "application/json"}
                        )
                        book_response.raise_for_status()
                        booking_id = book_response.json().get("booking_id", "unknown")
                        print(f"{self.vehicle_id}: Booked spot {spot_number} at parking {parking_id}, booking_id: {booking_id}")
                        # Запрос маршрута
                        time.sleep(random.uniform(10, 20))
                        route_response = requests.get(f"{BASE_URL}/parking/{parking_id}/route")
                        route_response.raise_for_status()
                        route = route_response.json()
                        print(f"{self.vehicle_id}: Got route to parking {parking_id}: {route}")
                        # Запуск освобождения места через 5–15 минут
                        delay = 1 # 5–15 минут
                        threading.Thread(
                            target=self.release_spot,
                            args=(booking_id, parking_id, spot_number, delay, True)  # True для DELETE, False для UPDATE
                        ).start()
                    else:
                        print(f"{self.vehicle_id}: No free spots at parking {parking_id}")
            except requests.exceptions.RequestException as e:
                if hasattr(e, 'response') and e.response is not None:
                    print(f"{self.vehicle_id}: Error: {e}, Response: {e.response.text}")
                else:
                    print(f"{self.vehicle_id}: Error: {e}")
            time.sleep(random.uniform(10, 20))

def vehicle_task(vehicle):
    vehicle.run()

vehicles = [Vehicle(f"car{i}") for i in range(1, 1001)]

with concurrent.futures.ThreadPoolExecutor(max_workers=100) as executor:
    executor.map(vehicle_task, vehicles)
