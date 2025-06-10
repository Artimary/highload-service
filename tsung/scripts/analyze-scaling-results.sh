set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BASE_DIR="$(dirname "$(dirname "$SCRIPT_DIR")")"
RESULTS_DIR="$BASE_DIR/scaling-results"
REPORT_FILE="$RESULTS_DIR/scaling_analysis.md"

echo "# Анализ результатов масштабирования" > $REPORT_FILE
echo "" >> $REPORT_FILE
echo "Дата: $(date)" >> $REPORT_FILE
echo "" >> $REPORT_FILE

# Анализ вертикального масштабирования
echo "## Результаты вертикального масштабирования" >> $REPORT_FILE
echo "" >> $REPORT_FILE
echo "| Конфигурация | Запросы | Ошибки | Успешность | Ср. время отклика |" >> $REPORT_FILE
echo "|--------------|---------|--------|------------|-------------------|" >> $REPORT_FILE

for dir in $RESULTS_DIR/vertical/*/; do
  config=$(basename "$dir")
  log_file=$(find "$dir" -name "tsung.log" | head -1)
  
  if [ -f "$log_file" ]; then
    total_requests=$(grep -c "request" "$log_file" || echo "0")
    errors=$(grep -c "error\|connection_error" "$log_file" || echo "0")
    success_rate=$(( total_requests > 0 ? (total_requests - errors) * 100 / total_requests : 0 ))
    
    # Примерное среднее время отклика
    response_times=$(grep "stats: request" "$log_file" | grep -oP "mean=\K[0-9]+(\.[0-9]+)?" | sort -n)
    avg_response_time=$(echo "$response_times" | awk '{ sum += $1; n++ } END { if (n > 0) print sum / n; else print "N/A"; }')
    
    echo "| $config | $total_requests | $errors | $success_rate% | ${avg_response_time}ms |" >> $REPORT_FILE
  fi
done

# Анализ горизонтального масштабирования
echo "" >> $REPORT_FILE
echo "## Результаты горизонтального масштабирования" >> $REPORT_FILE
echo "" >> $REPORT_FILE
echo "| Конфигурация | Сценарий | Запросы | Ошибки | Успешность | Ср. время отклика |" >> $REPORT_FILE
echo "|--------------|----------|---------|--------|------------|-------------------|" >> $REPORT_FILE

for dir in $RESULTS_DIR/horizontal/*/; do
  config=$(basename "$dir")
  
  for scenario_dir in $(find "$dir" -type d -name "*_*" | sort); do
    scenario=$(basename "$scenario_dir" | sed 's/_.*//')
    log_file=$(find "$scenario_dir" -name "tsung.log")
    
    if [ -f "$log_file" ]; then
      total_requests=$(grep -c "request" "$log_file" || echo "0")
      errors=$(grep -c "error\|connection_error" "$log_file" || echo "0")
      success_rate=$(( total_requests > 0 ? (total_requests - errors) * 100 / total_requests : 0 ))
      
      # Примерное среднее время отклика
      response_times=$(grep "stats: request" "$log_file" | grep -oP "mean=\K[0-9]+(\.[0-9]+)?" | sort -n)
      avg_response_time=$(echo "$response_times" | awk '{ sum += $1; n++ } END { if (n > 0) print sum / n; else print "N/A"; }')
      
      echo "| $config | $scenario | $total_requests | $errors | $success_rate% | ${avg_response_time}ms |" >> $REPORT_FILE
    fi
  done
done

echo "" >> $REPORT_FILE
echo "## Выводы" >> $REPORT_FILE
echo "" >> $REPORT_FILE
echo "На основе проведенных тестов можно сделать следующие выводы:" >> $REPORT_FILE
echo "" >> $REPORT_FILE
echo "1. **Вертикальное масштабирование**: _(заполните на основе результатов)_" >> $REPORT_FILE
echo "2. **Горизонтальное масштабирование**: _(заполните на основе результатов)_" >> $REPORT_FILE
echo "3. **Оптимальная конфигурация**: _(заполните на основе результатов)_" >> $REPORT_FILE
echo "" >> $REPORT_FILE
echo "_Отчет сгенерирован автоматически на основе результатов тестирования._" >> $REPORT_FILE

echo "Анализ результатов завершен. Отчет сохранен в $REPORT_FILE"