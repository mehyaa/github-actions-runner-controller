FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY Github.EphemeralRunner.Controller.csproj .

RUN dotnet restore Github.EphemeralRunner.Controller.csproj

COPY . .

RUN dotnet publish Github.EphemeralRunner.Controller.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Github.EphemeralRunner.Controller.dll"]