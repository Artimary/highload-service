#!/bin/bash

# Tsung Load Testing Automation Script
# Usage: ./run-load-test.sh <scenario> [options]

set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(dirname "$SCRIPT_DIR")"
SCENARIOS_DIR="$PROJECT_ROOT/scenarios"
RESULTS_DIR="$PROJECT_ROOT/results"

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Default values
SCENARIO=""
TIMEOUT=600
CLEANUP=true
VERBOSE=false

# Function to print colored output
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

# Function to show usage
usage() {
    cat << EOF
Usage: $0 <scenario> [options]

Available scenarios:
  - simple_test
  - cd_validation
  - few_users_high_frequency
  - many_users_low_frequency
  - peak_load
  - database_stress
  - all (run all scenarios)

Options:
  -t, --timeout SECONDS    Test timeout in seconds (default: 600)
  -n, --no-cleanup         Don't cleanup containers after test
  -v, --verbose            Verbose output
  -h, --help               Show this help message

Examples:
  $0 simple_test
  $0 peak_load --timeout 900
  $0 all --verbose
EOF
}

# Function to check if scenario file exists
check_scenario() {
    local scenario=$1
    if [ "$scenario" != "all" ] && [ ! -f "$SCENARIOS_DIR/${scenario}.xml" ]; then
        print_error "Scenario file not found: $SCENARIOS_DIR/${scenario}.xml"
        print_status "Available scenarios:"
        ls -1 "$SCENARIOS_DIR"/*.xml 2>/dev/null | xargs -n1 basename | sed 's/.xml$//' | sed 's/^/  - /'
        exit 1
    fi
}

# Function to start services
start_services() {
    print_status "Starting testing environment..."
    cd "$PROJECT_ROOT/.."
    
    if ! docker compose --profile testing up -d --build; then
        print_error "Failed to start testing environment"
        exit 1
    fi
    
    print_status "Waiting for services to be ready..."
    timeout 300 bash -c 'until curl -sf http://localhost:8000/health >/dev/null 2>&1; do sleep 5; done' || {
        print_error "API failed to start within timeout"
        exit 1
    }
    
    print_success "Testing environment is ready"
}

# Function to run a single test scenario
run_test() {
    local scenario=$1
    print_status "Running test scenario: $scenario"
    
    # Clear previous results
    docker compose --profile testing exec -T tsung rm -rf /tsung/results/* 2>/dev/null || true
    
    # Run the test
    if [ "$VERBOSE" = true ]; then
        docker compose --profile testing exec -T tsung tsung -f /tsung/scenarios/${scenario}.xml start
    else
        docker compose --profile testing exec -T tsung tsung -f /tsung/scenarios/${scenario}.xml start >/dev/null 2>&1
    fi
    
    # Wait for test completion
    print_status "Waiting for test completion (timeout: ${TIMEOUT}s)..."
    if timeout "$TIMEOUT" bash -c 'while docker compose --profile testing exec -T tsung pgrep -f tsung >/dev/null 2>&1; do sleep 10; done'; then
        print_success "Test $scenario completed"
    else
        print_warning "Test $scenario reached timeout"
    fi
    
    # Copy results
    local timestamp=$(date +%Y%m%d-%H%M)
    local result_dir="$RESULTS_DIR/${scenario}_${timestamp}"
    mkdir -p "$result_dir"
    
    if docker cp $(docker compose --profile testing ps -q tsung):/tsung/results/. "$result_dir/" 2>/dev/null; then
        print_success "Results saved to: $result_dir"
        
        # Quick analysis
        if [ -f "$result_dir/tsung.log" ]; then
            local total_requests=$(grep -c "request" "$result_dir/tsung.log" 2>/dev/null || echo "0")
            local errors=$(grep -c "error" "$result_dir/tsung.log" 2>/dev/null || echo "0")
            local success_rate=0
            
            if [ "$total_requests" -gt 0 ]; then
                success_rate=$(( (total_requests - errors) * 100 / total_requests ))
            fi
            
            echo "========================================="
            echo "Test Results Summary for $scenario:"
            echo "  Total Requests: $total_requests"
            echo "  Errors: $errors"
            echo "  Success Rate: $success_rate%"
            echo "========================================="
        fi
    else
        print_error "Failed to copy test results"
    fi
}

# Function to cleanup
cleanup() {
    if [ "$CLEANUP" = true ]; then
        print_status "Cleaning up..."
        cd "$PROJECT_ROOT/.."
        docker compose --profile testing down -v >/dev/null 2>&1 || true
        print_success "Cleanup completed"
    fi
}

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        -t|--timeout)
            TIMEOUT="$2"
            shift 2
            ;;
        -n|--no-cleanup)
            CLEANUP=false
            shift
            ;;
        -v|--verbose)
            VERBOSE=true
            shift
            ;;
        -h|--help)
            usage
            exit 0
            ;;
        -*)
            print_error "Unknown option: $1"
            usage
            exit 1
            ;;
        *)
            if [ -z "$SCENARIO" ]; then
                SCENARIO="$1"
            else
                print_error "Multiple scenarios specified"
                usage
                exit 1
            fi
            shift
            ;;
    esac
done

# Validate arguments
if [ -z "$SCENARIO" ]; then
    print_error "No scenario specified"
    usage
    exit 1
fi

# Set trap for cleanup on exit
trap cleanup EXIT

# Main execution
print_status "Starting Tsung load testing..."
print_status "Scenario: $SCENARIO"
print_status "Timeout: ${TIMEOUT}s"

if [ "$SCENARIO" = "all" ]; then
    check_scenario "simple_test"  # Just check if any scenario exists
    start_services
    
    scenarios=("simple_test" "cd_validation" "few_users_high_frequency" "many_users_low_frequency" "peak_load" "database_stress")
    for scenario in "${scenarios[@]}"; do
        if [ -f "$SCENARIOS_DIR/${scenario}.xml" ]; then
            run_test "$scenario"
            if [ "$scenario" != "${scenarios[-1]}" ]; then
                print_status "Waiting 30 seconds before next test..."
                sleep 30
            fi
        else
            print_warning "Skipping missing scenario: $scenario"
        fi
    done
else
    check_scenario "$SCENARIO"
    start_services
    run_test "$SCENARIO"
fi

print_success "Load testing completed!"
