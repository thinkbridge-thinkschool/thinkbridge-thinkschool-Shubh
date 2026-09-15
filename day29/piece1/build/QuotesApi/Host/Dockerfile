# Build context is the QuotesApi/ root (one level up from this file), because the Host
# project now has ProjectReferences to sibling Modules/*/ and Shared/ projects:
#   docker build -f Host/Dockerfile -t quotesapi .      (run from QuotesApi/)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Host/QuotesApi.Host.csproj Host/
COPY Modules/Quotes/QuotesApi.Modules.Quotes.csproj Modules/Quotes/
COPY Modules/Identity/QuotesApi.Modules.Identity.csproj Modules/Identity/
COPY Modules/Notifications/QuotesApi.Modules.Notifications.csproj Modules/Notifications/
COPY Shared/QuotesApi.Shared.csproj Shared/
RUN dotnet restore Host/QuotesApi.Host.csproj
COPY . .
RUN dotnet publish Host/QuotesApi.Host.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "QuotesApi.dll"]
