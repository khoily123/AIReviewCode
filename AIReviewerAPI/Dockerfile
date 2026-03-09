FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["AIReviewerAPI/AIReviewerAPI.csproj", "AIReviewerAPI/"]
COPY ["Repositories/Repositories.csproj", "Repositories/"]
COPY ["Services/Services.csproj", "Services/"]
COPY ["Models/Models.csproj", "Models/"]
RUN dotnet restore "AIReviewerAPI/AIReviewerAPI.csproj"
COPY . .
WORKDIR "/src/AIReviewerAPI"
RUN dotnet publish "AIReviewerAPI.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "AIReviewerAPI.dll"]

