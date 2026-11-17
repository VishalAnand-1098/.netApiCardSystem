FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/InviteCraft.Domain/InviteCraft.Domain.csproj src/InviteCraft.Domain/
COPY src/InviteCraft.Application/InviteCraft.Application.csproj src/InviteCraft.Application/
COPY src/InviteCraft.Infrastructure/InviteCraft.Infrastructure.csproj src/InviteCraft.Infrastructure/
COPY src/InviteCraft.Api/InviteCraft.Api.csproj src/InviteCraft.Api/
RUN dotnet restore src/InviteCraft.Api/InviteCraft.Api.csproj

COPY src/ src/
RUN dotnet publish src/InviteCraft.Api/InviteCraft.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
COPY deploy/media/public/ App_Data/storage/public/

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true \
    Rendering__NoSandbox=true

EXPOSE 8080
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-8080} exec dotnet InviteCraft.Api.dll"]
