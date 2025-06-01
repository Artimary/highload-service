FROM python:3.9-slim

# Copy source code to proper module structure
COPY iot_controller/src/iot_controller.py /src/iot_controller/iot_controller.py
COPY iot_controller/src/__init__.py /src/iot_controller/__init__.py

WORKDIR /app

# Copy requirements and install dependencies
COPY iot_controller/tests/requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

# Copy test file
COPY iot_controller/tests/iot-controller-test.py .

# Set environment variables
ENV PYTHONPATH=/src

CMD ["python", "-m", "unittest", "iot-controller-test.py"]
