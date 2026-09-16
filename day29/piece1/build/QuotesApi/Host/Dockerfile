# Build context is the QuotesApi/ root (one level up from this file), because the Host
# project now has ProjectReferences to sibling Modules/*/ and Shared/ projects:
#   docker build -f Host/Dockerfile -t quotesapi .      (run from QuotesApi/)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Copying full source before restore (instead of csproj-only layer caching) — the
# split restore-then-copy pattern hit NETSDK1064 ("Microsoft.AspNetCore.OpenApi ...
# was not found. It might have been deleted since NuGet restore.") on first deploy,
# where a --no-restore publish couldn't see packages the earlier RUN dotnet restore
# had just fetched. Restoring and publishing in the same step avoids that mismatch.
COPY . .
RUN dotnet publish Host/QuotesApi.Host.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "QuotesApi.dll"]
