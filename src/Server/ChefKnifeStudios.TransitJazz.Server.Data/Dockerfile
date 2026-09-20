FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["./src", "/src"]

WORKDIR /src/Server/ChefKnifeStudios.TransitJazz.Server.Data

RUN dotnet restore --runtime linux-x64
RUN dotnet build -c Release --no-restore

FROM build AS publish

WORKDIR /src/Server/ChefKnifeStudios.TransitJazz.Server.Data

RUN dotnet tool install dotnet-ef -g --version 10.0.4
ENV PATH="$PATH:/root/.dotnet/tools"
RUN dotnet ef migrations bundle --self-contained --no-build --configuration Release -r linux-x64 --output /app/publish/chefknifestudios.transitjazz.efbundle

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0 AS final
WORKDIR /app

COPY --from=publish /app/publish .

RUN chmod +x ./chefknifestudios.transitjazz.efbundle
ENTRYPOINT ["/bin/sh", "-c", "exec ./chefknifestudios.transitjazz.efbundle --connection \"$CONNECTIONSTRINGS__TRANSITJAZZDB\""]
