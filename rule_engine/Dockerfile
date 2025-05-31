FROM python:3.9-slim

WORKDIR /app

COPY requirements.txt .
RUN pip install -r requirements.txt

COPY rule_engine.py .

CMD ["python", "rule_engine.py"]