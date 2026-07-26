FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["./src", "/src"]
WORKDIR /src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI
RUN dotnet build -c Release

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS publish
WORKDIR /src
COPY --from=build /src .
WORKDIR /src/Server/ChefKnifeStudios.TransitJazz.Server.WebAPI
RUN dotnet publish -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ChefKnifeStudios.TransitJazz.Server.WebAPI.dll"]
