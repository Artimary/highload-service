#!/bin/bash
set -e

# Ждем пока мастер будет готов
until pg_isready -h "$POSTGRES_MASTER_SERVICE_HOST" -p "$POSTGRES_MASTER_SERVICE_PORT"; do
  echo "Waiting for master to become ready..."
  sleep 2
done

# Проверяем, настроена ли уже репликация
if [ -f "/var/lib/postgresql/data/standby.signal" ]; then
  echo "Replication is already configured. Starting PostgreSQL in replica mode."
  exec postgres
fi

# Удаляем существующие файлы данных
rm -rf /var/lib/postgresql/data/*

# Создаем пользователя для репликации на мастере, если его еще нет
PGPASSWORD=secret psql -h "$POSTGRES_MASTER_SERVICE_HOST" -p "$POSTGRES_MASTER_SERVICE_PORT" -U postgres -d postgres -c "DO \$\$ BEGIN CREATE USER replicator REPLICATION PASSWORD 'replpass'; EXCEPTION WHEN duplicate_object THEN RAISE NOTICE 'User replicator already exists'; END \$\$;"

# Создаем слот репликации, если его еще нет
PGPASSWORD=secret psql -h "$POSTGRES_MASTER_SERVICE_HOST" -p "$POSTGRES_MASTER_SERVICE_PORT" -U postgres -d postgres -c "SELECT pg_create_physical_replication_slot('replica_slot', true) WHERE NOT EXISTS (SELECT 1 FROM pg_replication_slots WHERE slot_name = 'replica_slot');"

# Создаем базовую резервную копию с мастера
PGPASSWORD=replpass pg_basebackup -h "$POSTGRES_MASTER_SERVICE_HOST" -p "$POSTGRES_MASTER_SERVICE_PORT" -U replicator -D /var/lib/postgresql/data -X stream -P -v

# Создаем файл standby.signal для PostgreSQL 15
touch /var/lib/postgresql/data/standby.signal

# Копируем конфигурационный файл
cp /etc/postgresql/postgresql.conf /var/lib/postgresql/data/postgresql.conf

# Настраиваем подключение к мастеру
echo "primary_conninfo = 'host=$POSTGRES_MASTER_SERVICE_HOST port=$POSTGRES_MASTER_SERVICE_PORT user=replicator password=replpass'" >> /var/lib/postgresql/data/postgresql.conf
echo "primary_slot_name = 'replica_slot'" >> /var/lib/postgresql/data/postgresql.conf

# Запускаем PostgreSQL
exec postgres