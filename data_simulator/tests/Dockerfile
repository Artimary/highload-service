# Use official Python image as base
FROM python:3.9-slim

# Set working directory
WORKDIR /app

# Copy source code - исправляем путь для правильного копирования
COPY data_simulator/src/data_simulator.py /src/data_simulator/data_simulator.py
COPY data_simulator/src/data_simulator/__init__.py /src/data_simulator/__init__.py

# Copy requirements and install dependencies
COPY data_simulator/tests/requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy test file
COPY data_simulator/tests/data-simulator-test.py .

# Set environment variables
ENV PYTHONPATH=/src
ENV MQTT_HOST=mosquitto

# Run tests
CMD ["python", "-m", "unittest", "data-simulator-test.py"]
