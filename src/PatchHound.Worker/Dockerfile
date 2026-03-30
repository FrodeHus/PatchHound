FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/PatchHound.Core/PatchHound.Core.csproj src/PatchHound.Core/
COPY src/PatchHound.Infrastructure/PatchHound.Infrastructure.csproj src/PatchHound.Infrastructure/
COPY src/PatchHound.Worker/PatchHound.Worker.csproj src/PatchHound.Worker/
RUN dotnet restore src/PatchHound.Worker/PatchHound.Worker.csproj
COPY src/PatchHound.Core/ src/PatchHound.Core/
COPY src/PatchHound.Infrastructure/ src/PatchHound.Infrastructure/
COPY src/PatchHound.Worker/ src/PatchHound.Worker/
RUN dotnet publish src/PatchHound.Worker/PatchHound.Worker.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system --gid 1001 appgroup \
    && useradd --system --uid 1001 --gid appgroup --no-create-home appuser
COPY --from=build /app .
USER appuser
ENTRYPOINT ["dotnet", "PatchHound.Worker.dll"]
