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
RUN dotnet restore kart-wishlist-service/src/Api/Kart.Wishlist.Api.csproj

COPY kart-wishlist-service/ kart-wishlist-service/
COPY kart-shared/ kart-shared/
RUN dotnet publish kart-wishlist-service/src/Api/Kart.Wishlist.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

USER $APP_UID
ENTRYPOINT ["dotnet", "Kart.Wishlist.Api.dll"]
