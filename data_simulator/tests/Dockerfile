# Use official Python image as base
FROM python:3.9-slim

COPY src/data_simulator /src/data_simulator

# Set working directory
WORKDIR /app

COPY tests/data-simulator-test/requirements.txt .

# Install dependencies needed for tests
RUN pip install --no-cache-dir -r requirements.txt

COPY tests/integration/data-simulator-test/data-simulator-test.py .

ENV PYTHONPATH=/src
ENV MQTT_HOST=mosquitto

# Run tests with pattern matching test files starting with 'test_'
CMD ["python", "data-simulator-test.py"]
