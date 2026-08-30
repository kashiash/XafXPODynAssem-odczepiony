# Obraz runtime-only: aplikacja jest budowana na Macu (dotnet publish),
# bo feed DevExpress ma stabilne max 25.2.9, a projekt wymaga 26.1.4
# (pakiety sa tylko w lokalnym cache ~/.nuget). Restore w kontenerze by padl.
FROM mcr.microsoft.com/dotnet/aspnet:8.0

WORKDIR /app
COPY publish/ ./

# Deploy Schema konczy proces kodem 42 — restart zapewnia polityka w compose,
# nie skrypt. Kontener jest PID 1, wiec kod wyjscia jest czysty.
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Development \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

ENTRYPOINT ["dotnet", "XafXPODynAssem.Blazor.Server.dll"]
