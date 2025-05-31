FROM python:3.9-slim

# Копируем всю папку src в контейнер в /src
COPY src/iot_controller /src/iot_controller

WORKDIR /app

COPY tests/unit/iot-controller-test/requirements.txt .
RUN pip install --no-cache-dir -r requirements.txt

COPY tests/unit/iot-controller-test/iot-controller-test.py .

# Устанавливаем PYTHONPATH, чтобы можно было импортировать модули из /src
ENV PYTHONPATH=/src

CMD ["python", "iot-controller-test.py"]
