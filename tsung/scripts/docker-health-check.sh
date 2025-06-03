#!/bin/bash

# Docker Compose Health Check and Diagnostics Script
# Usage: ./docker-health-check.sh [profile]

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

print_status() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

# Function to check system resources
check_system_resources() {
    print_status "Checking system resources..."
    
    echo "=== Disk Space ==="
    df -h | head -10
    
    echo -e "\n=== Memory Usage ==="
    free -h
    
    echo -e "\n=== Docker System Info ==="
    docker system df
    
    echo -e "\n=== Running Containers ==="
    docker ps --format "table {{.Names}}\t{{.Status}}\t{{.Ports}}"
}

# Function to check container health
check_container_health() {
    local profile=${1:-""}
    
    print_status "Checking container health..."
    
    if [ -n "$profile" ]; then
        print_status "Using profile: $profile"
        containers=$(docker compose --profile "$profile" ps -q)
    else
        containers=$(docker compose ps -q)
    fi
    
    if [ -z "$containers" ]; then
        print_warning "No containers found"
        return 1
    fi
    
    for container in $containers; do
        container_name=$(docker inspect --format='{{.Name}}' "$container" | sed 's/\///')
        container_status=$(docker inspect --format='{{.State.Status}}' "$container")
        
        if [ "$container_status" = "running" ]; then
            print_success "Container $container_name is running"
            
            # Check if it's a service container and test connectivity
            case $container_name in
                *api*)
                    print_status "Testing API connectivity..."
                    if curl -sf http://localhost:8000/health >/dev/null 2>&1; then
                        print_success "API health check passed"
                    else
                        print_error "API health check failed"
                    fi
                    ;;
                *postgresql*|*postgres*)
                    print_status "Testing PostgreSQL connectivity..."
                    if docker exec "$container" pg_isready -U postgres >/dev/null 2>&1; then
                        print_success "PostgreSQL is ready"
                    else
                        print_error "PostgreSQL is not ready"
                    fi
                    ;;
                *influxdb*)
                    print_status "Testing InfluxDB connectivity..."
                    if curl -sf http://localhost:8086/health >/dev/null 2>&1; then
                        print_success "InfluxDB health check passed"
                    else
                        print_error "InfluxDB health check failed"
                    fi
                    ;;
                *mosquitto*)
                    print_status "Testing MQTT broker..."
                    if docker exec "$container" mosquitto_pub -h localhost -t test -m "health check" >/dev/null 2>&1; then
                        print_success "MQTT broker is responding"
                    else
                        print_warning "MQTT broker test inconclusive"
                    fi
                    ;;
            esac
        else
            print_error "Container $container_name is $container_status"
            
            # Show recent logs for failed containers
            print_status "Recent logs for $container_name:"
            docker logs --tail 20 "$container" 2>&1 | head -20
        fi
    done
}

# Function to cleanup problematic containers
cleanup_containers() {
    local profile=${1:-""}
    
    print_status "Cleaning up containers..."
    
    # Stop containers
    if [ -n "$profile" ]; then
        docker compose --profile "$profile" down -v --timeout 30 2>/dev/null || true
    else
        docker compose down -v --timeout 30 2>/dev/null || true
    fi
    
    # Remove dangling containers and volumes
    docker container prune -f >/dev/null 2>&1 || true
    docker volume prune -f >/dev/null 2>&1 || true
    docker network prune -f >/dev/null 2>&1 || true
    
    print_success "Cleanup completed"
}

# Function to restart services with health monitoring
restart_services() {
    local profile=${1:-""}
    
    print_status "Restarting services..."
    
    # Cleanup first
    cleanup_containers "$profile"
    
    # Start services
    if [ -n "$profile" ]; then
        print_status "Starting services with profile: $profile"
        docker compose --profile "$profile" up -d --build
    else
        print_status "Starting all services"
        docker compose up -d --build
    fi
    
    # Wait and check health
    sleep 10
    check_container_health "$profile"
}

# Main execution
case "${1:-check}" in
    "check")
        check_system_resources
        echo ""
        check_container_health "${2:-}"
        ;;
    "cleanup")
        cleanup_containers "${2:-}"
        ;;
    "restart")
        restart_services "${2:-}"
        ;;
    "health")
        check_container_health "${2:-}"
        ;;
    "resources")
        check_system_resources
        ;;
    *)
        echo "Usage: $0 {check|cleanup|restart|health|resources} [profile]"
        echo ""
        echo "Commands:"
        echo "  check      - Check system resources and container health"
        echo "  cleanup    - Stop and clean up containers"
        echo "  restart    - Restart services with health monitoring"
        echo "  health     - Check only container health"
        echo "  resources  - Check only system resources"
        echo ""
        echo "Examples:"
        echo "  $0 check testing"
        echo "  $0 restart testing"
        echo "  $0 cleanup"
        exit 1
        ;;
esac
