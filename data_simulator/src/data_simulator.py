import paho.mqtt.client as mqtt
import time
import random
import logging
import json
import os
from datetime import datetime, timedelta
import math

# Configure logging with more detail
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)

# Configuration
MQTT_HOST = os.getenv('MQTT_HOST', 'mosquitto')
MQTT_PORT = 1883
MQTT_TOPIC = 'iot_topic'

# Simulation parameters
NUM_PARKINGS = 150
MIN_CAPACITY = 20
MAX_CAPACITY = 500
UPDATE_INTERVAL = 15  # seconds between updates

def on_connect(client, userdata, flags, rc):
    if rc == 0:
        logger.info("Connected to MQTT Broker!")
    else:
        logger.error(f"Failed to connect to MQTT, return code {rc}")

def on_disconnect(client, userdata, rc):
    logger.warning(f"Disconnected from MQTT Broker. Reason: {rc}")

class ParkingSimulator:
    def __init__(self, num_parkings=NUM_PARKINGS):
        self.parkings = []
        
        # Initialize parking lots
        for i in range(1, num_parkings + 1):
            capacity = random.randint(MIN_CAPACITY, MAX_CAPACITY)
            # Start with random occupancy between 20% and 80%
            occupied_spots = int(capacity * random.uniform(0.2, 0.8))
            free_spots = capacity - occupied_spots
            
            self.parkings.append({
                'id': i,
                'name': f"Parking {i}",
                'capacity': capacity,
                'free_spots': free_spots,
                'occupied_spots': occupied_spots,
                # Add randomness to simulation patterns
                'volatility': random.uniform(0.8, 1.5),  # How quickly occupancy changes
                'peak_hour_factor': random.uniform(0.8, 1.2),  # How much peak hours affect this parking
                'weekend_factor': random.uniform(0.4, 0.8),  # How weekend occupancy differs from weekday
            })
            
        logger.info(f"Initialized {len(self.parkings)} parking lots for simulation")

    def get_occupancy_trend(self):
        """Calculate the current trend factor based on time of day"""
        now = datetime.now()
        hour = now.hour
        minute = now.minute
        day_of_week = now.weekday()  # 0-6, Monday to Sunday
        
        # Weekend factor (lower occupancy on weekends)
        is_weekend = day_of_week >= 5  # Saturday or Sunday
        
        # Base trend calculation based on time of day
        if is_weekend:
            # Weekend pattern: gradually increases from morning, peaks in afternoon, then decreases
            if hour < 8:  # Early morning
                trend = 0.1 + (hour * 0.05)
            elif hour < 12:  # Morning to noon
                trend = 0.3 + ((hour - 8) * 0.1)
            elif hour < 17:  # Afternoon peak
                trend = 0.5 + ((hour - 12) * 0.05)
            else:  # Evening decrease
                trend = 0.5 - ((hour - 17) * 0.05)
        else:
            # Weekday pattern: morning rush, midday lull, evening rush
            if hour < 7:  # Very early morning
                trend = 0.1 + (hour * 0.05)
            elif hour < 10:  # Morning rush hour
                trend = 0.4 + ((hour - 7) * 0.2)
            elif hour < 16:  # Midday
                trend = 0.8 - ((hour - 10) * 0.05)
            elif hour < 19:  # Evening rush hour
                trend = 0.5 + ((hour - 16) * 0.15)
            else:  # Night
                trend = 0.8 - ((hour - 19) * 0.1)
        
        # Minute-level fluctuations for smoother transitions
        minute_factor = math.sin(minute * 6 * math.pi / 180) * 0.05
        trend += minute_factor
        
        # Ensure trend is within sensible bounds
        trend = max(0.05, min(trend, 0.95))
        
        return trend, is_weekend
    
    def update(self):
        """Update parking occupancy based on time trends and randomness"""
        trend, is_weekend = self.get_occupancy_trend()
        
        for parking in self.parkings:
            capacity = parking['capacity']
            current_free = parking['free_spots']
            current_occupied = parking['occupied_spots']
            
            # Apply time-based trend, adjusted by parking-specific factors
            base_occupancy = capacity * trend
            if is_weekend:
                base_occupancy *= parking['weekend_factor']
            else:
                base_occupancy *= parking['peak_hour_factor']
            
            # Calculate delta with randomness
            target_occupied = int(base_occupancy)
            delta = target_occupied - current_occupied
            
            # Add randomness to the delta
            randomness = int(capacity * random.uniform(-0.03, 0.03) * parking['volatility'])
            delta += randomness
            
            # Dampen large swings
            if abs(delta) > capacity * 0.1:
                delta = int(delta * 0.5)
            
            # Apply change but ensure we stay within bounds
            new_occupied = max(0, min(current_occupied + delta, capacity))
            new_free = capacity - new_occupied
            
            parking['occupied_spots'] = new_occupied
            parking['free_spots'] = new_free
            
        return self.parkings

def run_simulator():
    """Main function to run the parking simulator"""
    # Initialize MQTT client
    client = mqtt.Client()
    client.on_connect = on_connect
    client.on_disconnect = on_disconnect
    
    try:
        client.connect(MQTT_HOST, MQTT_PORT, 60)
        client.loop_start()
        logger.info("MQTT client initialized")
    except Exception as e:
        logger.error(f"MQTT connection error: {e}")
        exit(1)
    
    # Create parking simulator
    simulator = ParkingSimulator(NUM_PARKINGS)
    
    # Main loop
    cycle = 0
    while True:
        try:
            cycle += 1
            start_time = time.time()
            
            # Update parking lots
            parking_lots = simulator.update()
            
            # Log current time trend
            trend, is_weekend = simulator.get_occupancy_trend()
            if cycle % 4 == 0:  # Log every 4 cycles
                day_type = "weekend" if is_weekend else "weekday"
                logger.info(f"Current occupancy trend: {trend:.2f} ({day_type}, {datetime.now().hour:02d}:{datetime.now().minute:02d})")
            
            # Publish data for each parking
            for parking in parking_lots:
                # Prepare data
                data = {
                    "device_id": parking['id'],
                    "free_spots": parking['free_spots'],
                    "total_capacity": parking['capacity'],
                    "occupied_spots": parking['occupied_spots'],
                    "timestamp": int(time.time() * 1e9)
                }
                
                # Convert to JSON and publish
                payload = json.dumps(data)
                client.publish(MQTT_TOPIC, payload)
                
                # Log some data for verification
                if parking['id'] % 30 == 0:  # Log every 30th parking
                    occupancy_percent = (parking['occupied_spots'] / parking['capacity']) * 100
                    logger.info(f"Parking {parking['id']}: {parking['free_spots']} free, {parking['occupied_spots']} occupied, {occupancy_percent:.1f}% full")
            
            # Ensure consistent timing between updates
            elapsed = time.time() - start_time
            sleep_time = max(1, UPDATE_INTERVAL - elapsed)  # At least 1 second
            time.sleep(sleep_time)
            
        except Exception as e:
            logger.error(f"Error in simulator loop: {e}")
            time.sleep(5)

if __name__ == "__main__":
    logger.info("Parking occupancy simulator starting")
    run_simulator()