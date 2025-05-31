FROM python:3.9-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install -r requirements.txt

COPY iot_controller.py .

CMD ["python", "iot_controller.py"]