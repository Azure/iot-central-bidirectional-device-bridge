# Use SDK to build release package
FROM mcr.microsoft.com/dotnet/core/sdk:3.1 AS build-img
WORKDIR /app
COPY DeviceBridge ./
RUN dotnet --info
RUN dotnet publish -c Release -o out

# Use runtime for final image
FROM mcr.microsoft.com/dotnet/core/aspnet:3.1
WORKDIR /app
COPY --from=build-img /app/out ./
ENTRYPOINT ["dotnet", "DeviceBridge.dll"]

EXPOSE 5001
