# syntax=docker/dockerfile:1.7

# Image multi-stage minimaliste pour AspxLint.Server.
#
# Build :
#     docker build -t aspx-lint:dev --build-arg VERSION=0.2.0 .
#
# Run :
#     docker run --rm -p 5173:5173 \
#                -e ASPXLINT_API_KEY=secret123 \
#                -e ASPXLINT_ALLOWED_ROOT=/workspace \
#                -v /chemin/vers/projets:/workspace \
#                ghcr.io/hl-n-a/claude-aspx-lint:latest
#
# Variables d'environnement supportees :
#     ASPXLINT_API_KEY       - cle d'auth bearer (sinon aleatoire au boot)
#     ASPXLINT_ALLOWED_ROOT  - racine a laquelle les paths sont confines
#     ASPXLINT_READ_ONLY     - "true" pour bloquer /api/save et /api/restore

# ============================================================
# BUILD : SDK .NET 9, multi-arch via buildx
# ============================================================
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG VERSION=0.0.0-dev
ARG TARGETARCH

WORKDIR /src

# 1) Copy SEULEMENT les csproj + Directory.Build.props pour profiter du cache de
#    la couche `dotnet restore` quand le code change mais pas les dependances.
COPY ["Directory.Build.props", "./"]
COPY ["src/AspxLint.Core/AspxLint.Core.csproj", "src/AspxLint.Core/"]
COPY ["src/AspxLint.Server/AspxLint.Server.csproj", "src/AspxLint.Server/"]

RUN dotnet restore src/AspxLint.Server/AspxLint.Server.csproj \
        --use-current-runtime \
        -p:Version=$VERSION

# 2) Copy le reste et publie. La dashboard HTML (src/AspxLint.Web/index.html)
#    est tiree dans cette etape et embedded dans AspxLint.Server.dll.
COPY src/ src/
RUN dotnet publish src/AspxLint.Server/AspxLint.Server.csproj \
        --configuration Release \
        --no-restore \
        --output /app \
        -p:Version=$VERSION \
        -p:UseAppHost=false

# ============================================================
# RUNTIME : ASP.NET 9, image trimmee
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Utilisateur non-root pour matcher les bonnes pratiques.
RUN groupadd --system --gid 1000 aspxlint \
 && useradd --system --uid 1000 --gid aspxlint --shell /bin/false aspxlint

COPY --from=build --chown=aspxlint:aspxlint /app .

USER aspxlint

EXPOSE 5173
ENV ASPNETCORE_URLS=http://0.0.0.0:5173 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_USE_POLLING_FILE_WATCHER=false

ENTRYPOINT ["dotnet", "AspxLint.Server.dll"]
CMD ["--port", "5173"]
