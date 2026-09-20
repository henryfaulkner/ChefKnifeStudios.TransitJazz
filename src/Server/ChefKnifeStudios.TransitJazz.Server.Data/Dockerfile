FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["./src", "/src"]

WORKDIR /src/Server/ChefKnifeStudios.TransitJazz.Server.Data

RUN dotnet build -c Release

FROM build AS publish

WORKDIR /src/Server/ChefKnifeStudios.TransitJazz.Server.Data

RUN dotnet tool install dotnet-ef -g --version 9.0.5 
ENV PATH="$PATH:/root/.dotnet/tools"
RUN dotnet ef migrations bundle --self-contained -r linux-x64 --output /app/publish/chefknifestudios.transitjazz.efbundle

FROM mcr.microsoft.com/dotnet/runtime:9.0 AS final
WORKDIR /app

COPY --from=publish /app/publish .

RUN chmod +x ./chefknifestudios.transitjazz.efbundle
ENTRYPOINT ./chefknifestudios.transitjazz.efbundle --connection "$CONNECTIONSTRINGS__TRANSITJAZZDB"