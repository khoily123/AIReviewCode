FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8090

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["CodeReviewer/CodeReviewer.csproj", "CodeReviewer/"]
COPY ["Models/Models.csproj", "Models/"]
RUN dotnet restore "CodeReviewer/CodeReviewer.csproj"
COPY . .
WORKDIR "/src/CodeReviewer"
RUN dotnet publish "CodeReviewer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8090
ENTRYPOINT ["dotnet", "CodeReviewer.dll"]

