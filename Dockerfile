FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["EcfrApi.Web/EcfrApi.Web.csproj", "EcfrApi.Web/"]
RUN dotnet restore "EcfrApi.Web/EcfrApi.Web.csproj"
COPY . .
WORKDIR "/src/EcfrApi.Web"
RUN dotnet build "EcfrApi.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "EcfrApi.Web.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "EcfrApi.Web.dll"]
