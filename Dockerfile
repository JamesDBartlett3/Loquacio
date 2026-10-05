# Dockerfile for Loquacio - Linux Build & Test
# Uses Microsoft .NET SDK container to build Windows-targeting code from Linux
#
# This Dockerfile enables building and testing the entire solution on Linux,
# including Windows-specific projects (WPF app), thanks to EnableWindowsTargeting.
#
# Usage:
#   docker build -t loquacio-build .
#   docker run --rm -v $(pwd):/app loquacio-build ./build.sh Release --test

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /app

# Copy entire source tree
COPY . .

# Restore dependencies
RUN dotnet restore Loquacio.slnx

# Build the solution
ARG CONFIGURATION=Release
RUN dotnet build Loquacio.slnx -c $CONFIGURATION --no-restore

# Test stage
FROM build AS test
WORKDIR /app

# Run tests
ARG CONFIGURATION=Release
RUN dotnet test Loquacio.Tests/Loquacio.Tests.csproj -c $CONFIGURATION --no-build --verbosity normal

# Final stage for running build scripts
FROM build AS final
WORKDIR /app

# Default command (can be overridden)
CMD ["./build.sh"]
