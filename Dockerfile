# ------------------------------------
# Stage 0 - Build the Application
# ------------------------------------

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["FileManager.csproj", "./"]
RUN dotnet restore "FileManager.csproj"

COPY . .
RUN dotnet publish "FileManager.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ------------------------------------
# Stage 1 - Configure the Runtime
# ------------------------------------

# Use the microsfot aspnet as the runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# Copy the build into the runtime
WORKDIR /app
COPY --from=build /app/publish .

# Default ENV
ENV ASPNETCORE_URLS=http://+:80
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV RootDirectory=/data/root

# Export Port
EXPOSE 80

# Entry Point
ENTRYPOINT ["dotnet", "FileManager.dll"]
