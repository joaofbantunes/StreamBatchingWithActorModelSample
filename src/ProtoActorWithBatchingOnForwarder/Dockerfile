FROM mcr.microsoft.com/dotnet/aspnet:8.0-jammy-chiseled AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0-jammy AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/ProtoActorWithBatchingOnForwarder/ProtoActorWithBatchingOnForwarder.csproj", "src/ProtoActorWithBatchingOnForwarder/"]
COPY ["src/Shared/Shared.csproj", "src/Shared/"]
RUN dotnet restore "src/ProtoActorWithBatchingOnForwarder/ProtoActorWithBatchingOnForwarder.csproj"
COPY . .
WORKDIR "/src/src/ProtoActorWithBatchingOnForwarder"
RUN dotnet build "ProtoActorWithBatchingOnForwarder.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "ProtoActorWithBatchingOnForwarder.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ProtoActorWithBatchingOnForwarder.dll"]
