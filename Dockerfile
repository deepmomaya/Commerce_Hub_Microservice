FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/CommerceHub.API/CommerceHub.API.csproj", "src/CommerceHub.API/"]
RUN dotnet restore "src/CommerceHub.API/CommerceHub.API.csproj"

COPY . .
WORKDIR "/src/src/CommerceHub.API"
RUN dotnet build "CommerceHub.API.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "CommerceHub.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CommerceHub.API.dll"]
