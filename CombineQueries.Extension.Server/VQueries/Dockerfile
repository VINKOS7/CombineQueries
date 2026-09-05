FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["VQueries/CombineQueries.Api.csproj", "VQueries/"]
COPY ["VQueries.Domain/CombineQueries.Domain.csproj", "VQueries.Domain/"]
COPY ["VQueries.Infra/CombineQueries.Infra.csproj", "VQueries.Infra/"]
RUN dotnet restore "VQueries/CombineQueries.Api.csproj"
COPY . .
RUN dotnet publish "VQueries/CombineQueries.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "CombineQueries.Api.dll"]
