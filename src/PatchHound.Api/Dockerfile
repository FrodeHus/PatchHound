FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/PatchHound.Core/PatchHound.Core.csproj src/PatchHound.Core/
COPY src/PatchHound.Infrastructure/PatchHound.Infrastructure.csproj src/PatchHound.Infrastructure/
COPY src/PatchHound.Api/PatchHound.Api.csproj src/PatchHound.Api/
RUN dotnet restore src/PatchHound.Api/PatchHound.Api.csproj
COPY src/PatchHound.Core/ src/PatchHound.Core/
COPY src/PatchHound.Infrastructure/ src/PatchHound.Infrastructure/
COPY src/PatchHound.Api/ src/PatchHound.Api/
RUN dotnet publish src/PatchHound.Api/PatchHound.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system --gid 1001 appgroup \
    && useradd --system --uid 1001 --gid appgroup --no-create-home appuser
COPY --from=build /app .
USER appuser
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "PatchHound.Api.dll"]
