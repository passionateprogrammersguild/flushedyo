# Define global arguments accessible across ALL build stages
ARG USERNAME=vscode
ARG USER_UID=1000
ARG USER_GID=$USER_UID


# ==============================================================================
# STAGE 1: Development Environment (Using Official Node 24 Image)
# ==============================================================================
FROM node:24-slim AS buildnode

# Bring global ARGs into this stage's scope
ARG USERNAME
ARG USER_UID
ARG USER_GID

ENV DEBIAN_FRONTEND=noninteractive

# Install sudo for development convenience (matching your original requirements)
RUN apt-get update && apt-get install -y --no-install-recommends sudo \
    && groupadd --gid $USER_GID $USERNAME || true \
    && useradd --uid $USER_UID --gid $USER_GID -m $USERNAME || true \
    && echo $USERNAME ALL=\(root\) NOPASSWD:ALL > /etc/sudoers.d/$USERNAME \
    && chmod 0440 /etc/sudoers.d/$USERNAME \
    && apt-get clean && rm -rf /var/lib/apt/lists/*

WORKDIR /workspace

# Dev containers/DevPod will drop right into this target
USER $USERNAME


# ==============================================================================
# STAGE 2: Production Environment (Pure Debian Base with Copied Node Binaries)
# ==============================================================================
FROM debian:bookworm-slim AS final

# Bring global ARGs into this stage's scope
ARG USERNAME
ARG USER_UID
ARG USER_GID

ENV DEBIAN_FRONTEND=noninteractive

# Create a secure, non-root user matching your app runner specs
RUN groupadd --gid $USER_GID $USERNAME \
    && useradd --uid $USER_UID --gid $USER_GID -m -s /bin/bash $USERNAME

# Copy the pre-compiled Node runtime directly from the development stage
COPY --from=buildnode /usr/local/bin/node /usr/local/bin/node
COPY --from=buildnode /usr/local/include/node /usr/local/include/node
COPY --from=buildnode /usr/local/lib/node_modules /usr/local/lib/node_modules

# Re-link executable paths for npm and npx
RUN ln -s /usr/local/lib/node_modules/npm/bin/npm-cli.js /usr/local/bin/npm \
    && ln -s /usr/local/lib/node_modules/npm/bin/npx-cli.js /usr/local/bin/npx


# (Optional) Set default user if running outside devcontainers
USER $USERNAME
