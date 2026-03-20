FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["src/AutoPartsERP.Api/AutoPartsERP.Api.csproj", "AutoPartsERP.Api/"]
COPY ["src/AutoPartsERP.Application/AutoPartsERP.Application.csproj", "AutoPartsERP.Application/"]
COPY ["src/AutoPartsERP.Domain/AutoPartsERP.Domain.csproj", "AutoPartsERP.Domain/"]
COPY ["src/AutoPartsERP.Infrastructure/AutoPartsERP.Infrastructure.csproj", "AutoPartsERP.Infrastructure/"]
COPY ["src/AutoPartsERP.Contracts/AutoPartsERP.Contracts.csproj", "AutoPartsERP.Contracts/"]

RUN dotnet restore "AutoPartsERP.Api/AutoPartsERP.Api.csproj"

COPY src/ .
WORKDIR "/src/AutoPartsERP.Api"
RUN dotnet build "AutoPartsERP.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "AutoPartsERP.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

RUN addgroup --system --gid 1001 erp && \
    adduser --system --uid 1001 --ingroup erp erp

COPY --from=publish /app/publish .
RUN chown -R erp:erp /app

USER erp
EXPOSE 8080

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "AutoPartsERP.Api.dll"]
