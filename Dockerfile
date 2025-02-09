# Build .NET API
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["EcfrApi.Web/EcfrApi.Web.csproj", "EcfrApi.Web/"]
RUN dotnet restore "EcfrApi.Web/EcfrApi.Web.csproj"
COPY . .
WORKDIR "/src/EcfrApi.Web"
RUN dotnet build "EcfrApi.Web.csproj" -c Release -o /app/build
RUN dotnet publish "EcfrApi.Web.csproj" -c Release -o /app/publish

# Final image
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Copy .NET API
COPY --from=build /app/publish .

# Configure the app
ENV ASPNETCORE_URLS=http://+:80
EXPOSE 80

ENTRYPOINT ["dotnet", "EcfrApi.Web.dll"]
