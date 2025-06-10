set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BASE_DIR="$(dirname "$(dirname "$SCRIPT_DIR")")"
RESULTS_DIR="$BASE_DIR/scaling-results"

mkdir -p "$RESULTS_DIR/vertical"

# Конфигурации для тестирования
CONFIGS=(
  "cpu=1,mem=1G"
  "cpu=2,mem=2G"
  "cpu=4,mem=4G"
)

# Тестируемый сценарий
SCENARIO="few_users_high_frequency"

for config in "${CONFIGS[@]}"; do
  # Извлечь параметры
  cpu=$(echo $config | cut -d',' -f1 | cut -d'=' -f2)
  mem=$(echo $config | cut -d',' -f2 | cut -d'=' -f2)
  
  echo "=== Тестирование с CPU=$cpu, Memory=$mem ==="
  
  # Обновить docker-compose.yml с новыми ресурсами для API
  sed -i "s/cpus: '[0-9.]*'/cpus: '$cpu'/g" "$BASE_DIR/docker-compose.yml"
  sed -i "s/memory: [0-9]*G/memory: $mem/g" "$BASE_DIR/docker-compose.yml"
  
  # Перезапустить контейнеры с новыми ресурсами
  docker-compose down
  docker-compose --profile testing up -d
  
  # Подождать инициализацию сервисов
  sleep 30
  
  # Запустить тест
  "$SCRIPT_DIR/run-load-test.sh" "$SCENARIO" -t 600
  
  # Сохранить результаты с пометкой конфигурации
  mkdir -p "$RESULTS_DIR/vertical/cpu${cpu}_mem${mem}"
  cp -r "$BASE_DIR/tsung/results/${SCENARIO}_"* "$RESULTS_DIR/vertical/cpu${cpu}_mem${mem}/"
  
  echo "=== Завершено тестирование конфигурации: CPU=$cpu, Memory=$mem ==="
done

echo "Все тесты вертикального масштабирования завершены. Результаты в $RESULTS_DIR/vertical/"