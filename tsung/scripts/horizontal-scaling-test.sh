set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BASE_DIR="$(dirname "$(dirname "$SCRIPT_DIR")")"
RESULTS_DIR="$BASE_DIR/scaling-results"

mkdir -p "$RESULTS_DIR/horizontal"

# Конфигурации для горизонтального масштабирования
CONFIGS=(
  "api=1,pg=100"
  "api=3,pg=200"
  "api=5,pg=300"
)

# Тестируемые сценарии
SCENARIOS=("peak_load" "many_users_low_frequency")

for config in "${CONFIGS[@]}"; do
  # Извлечь параметры
  api_replicas=$(echo $config | cut -d',' -f1 | cut -d'=' -f2)
  pg_connections=$(echo $config | cut -d',' -f2 | cut -d'=' -f2)
  
  echo "=== Тестирование с API_REPLICAS=$api_replicas, PG_MAX_CONNECTIONS=$pg_connections ==="
  
  # Остановить существующие контейнеры
  docker-compose down
  
  # Запустить с горизонтальным масштабированием
  API_REPLICAS=$api_replicas PG_MAX_CONNECTIONS=$pg_connections docker-compose -f docker-compose.yml -f docker-compose.scale.yml --profile testing up -d
  
  # Подождать инициализацию
  sleep 45
  
  # Проверить, что все контейнеры запущены
  docker-compose ps
  
  for scenario in "${SCENARIOS[@]}"; do
    echo "Запуск сценария: $scenario"
    
    # Запуск теста
    "$SCRIPT_DIR/run-load-test.sh" "$scenario" -t 900
    
    # Сохранить результаты
    mkdir -p "$RESULTS_DIR/horizontal/api${api_replicas}_pg${pg_connections}"
    cp -r "$BASE_DIR/tsung/results/${scenario}_"* "$RESULTS_DIR/horizontal/api${api_replicas}_pg${pg_connections}/"
    
    echo "=== Завершено тестирование сценария $scenario для конфигурации: API=$api_replicas, PG=$pg_connections ==="
    
    # Пауза между тестами
    sleep 30
  done
done

echo "Все тесты горизонтального масштабирования завершены. Результаты в $RESULTS_DIR/horizontal/"