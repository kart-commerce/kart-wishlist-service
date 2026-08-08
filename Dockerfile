# syntax=docker/dockerfile:1

# Build context must be the kart-commerce parent directory, not this repo, because
# Kart.Wishlist.* projects cross-repo-reference kart-shared/src/Kart.Shared.* (no published
# NuGet feed exists yet — kart-shared/README.md). Build from kart-commerce/ with:
#   docker build -f kart-wishlist-service/Dockerfile -t kart-wishlist-service:latest .
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY kart-wishlist-service/Directory.Build.props kart-wishlist-service/
COPY kart-shared/Directory.Build.props kart-shared/
COPY kart-wishlist-service/KartWishlistService.sln kart-wishlist-service/
COPY kart-wishlist-service/src/Api/Kart.Wishlist.Api.csproj kart-wishlist-service/src/Api/
COPY kart-wishlist-service/src/Application/Kart.Wishlist.Application.csproj kart-wishlist-service/src/Application/
COPY kart-wishlist-service/src/Domain/Kart.Wishlist.Domain.csproj kart-wishlist-service/src/Domain/
COPY kart-wishlist-service/src/Infrastructure/Kart.Wishlist.Infrastructure.csproj kart-wishlist-service/src/Infrastructure/
COPY kart-wishlist-service/tests/UnitTests/Kart.Wishlist.UnitTests.csproj kart-wishlist-service/tests/UnitTests/
COPY kart-wishlist-service/tests/IntegrationTests/Kart.Wishlist.IntegrationTests.csproj kart-wishlist-service/tests/IntegrationTests/
COPY kart-wishlist-service/tests/ContractTests/Kart.Wishlist.ContractTests.csproj kart-wishlist-service/tests/ContractTests/
COPY kart-shared/src/Kart.Shared.Domain/Kart.Shared.Domain.csproj kart-shared/src/Kart.Shared.Domain/
COPY kart-shared/src/Kart.Shared.ErrorHandling/Kart.Shared.ErrorHandling.csproj kart-shared/src/Kart.Shared.ErrorHandling/
COPY kart-shared/src/Kart.Shared.Observability/Kart.Shared.Observability.csproj kart-shared/src/Kart.Shared.Observability/
COPY kart-shared/src/Kart.Shared.Auditing/Kart.Shared.Auditing.csproj kart-shared/src/Kart.Shared.Auditing/
COPY kart-shared/src/Kart.Shared.Configuration/Kart.Shared.Configuration.csproj kart-shared/src/Kart.Shared.Configuration/
COPY kart-shared/src/Kart.Shared.Messaging/Kart.Shared.Messaging.csproj kart-shared/src/Kart.Shared.Messaging/
# The cache mount persists extracted NuGet packages under a stable id shared by every other
# kart-*-service Dockerfile, so restore stays fast (no re-download) even on a cache-miss here
# (e.g. after a .csproj change) as long as some other service's build already warmed it.
RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget-packages \
    dotnet restore kart-wishlist-service/src/Api/Kart.Wishlist.Api.csproj

# Scoped to src/ + contracts/ from each repo instead of the previous whole-directory
# `COPY kart-wishlist-service/ kart-wishlist-service/` / `COPY kart-shared/ kart-shared/` -- those
# also pulled in tests/, README.md, kart-shared's own tests/ and docs, etc., so editing any of
# that busted this layer (and the publish below) even though none of it reaches the published
# output. contracts/ is kept because Kart.Wishlist.Api.csproj copies message-bus-manifest.json
# from it into the publish output as a <Content> item.
COPY kart-wishlist-service/src/ kart-wishlist-service/src/
COPY kart-wishlist-service/contracts/ kart-wishlist-service/contracts/
COPY kart-shared/src/ kart-shared/src/
# --no-restore only skips re-resolving the dependency graph -- publish still reads the actual
# package DLLs from the global packages folder, so it needs the same cache mount as restore
# above (the mount isn't part of the image; without it here this folder is empty again).
RUN --mount=type=cache,target=/root/.nuget/packages,id=nuget-packages \
    dotnet publish kart-wishlist-service/src/Api/Kart.Wishlist.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "Kart.Wishlist.Api.dll"]
