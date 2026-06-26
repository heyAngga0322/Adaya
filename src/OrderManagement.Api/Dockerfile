FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["src/OrderManagement.Api/OrderManagement.Api.csproj", "src/OrderManagement.Api/"]
COPY ["src/OrderManagement.Core/OrderManagement.Core.csproj", "src/OrderManagement.Core/"]

RUN dotnet restore "src/OrderManagement.Api/OrderManagement.Api.csproj"

COPY . .
WORKDIR "/src/src/OrderManagement.Api"
RUN dotnet publish "OrderManagement.Api.csproj" -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:80
ENTRYPOINT ["dotnet", "OrderManagement.Api.dll"]
