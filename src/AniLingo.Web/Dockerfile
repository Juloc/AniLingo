FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/AniLingo.Web/AniLingo.Web.csproj src/AniLingo.Web/
RUN dotnet restore src/AniLingo.Web/AniLingo.Web.csproj

COPY src/AniLingo.Web/ src/AniLingo.Web/
RUN dotnet publish src/AniLingo.Web/AniLingo.Web.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "AniLingo.Web.dll"]
