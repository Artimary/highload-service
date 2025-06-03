FROM erlang:24-alpine

# Install dependencies
RUN apk add --no-cache \
    bash \
    gnuplot \
    perl \
    python3 \
    make \
    git \
    wget \
    curl \
    build-base \
    bc \
    tar \
    gzip

# Install Tsung
WORKDIR /tmp
RUN wget https://github.com/processone/tsung/archive/v1.8.0.tar.gz && \
    tar -xzf v1.8.0.tar.gz && \
    cd tsung-1.8.0 && \
    ./configure && \
    make && \
    make install

# Create working directory
WORKDIR /tsung

# Copy configuration files
COPY scenarios/ /tsung/scenarios/
COPY scripts/ /tsung/scripts/
COPY config/ /tsung/config/

# Make scripts executable
RUN chmod +x /tsung/scripts/*.sh

# Create results directory
RUN mkdir -p /tsung/results

# Default command
CMD ["bash"]
