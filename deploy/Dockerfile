# Obraz runtime-only: aplikacja jest budowana poza kontenerem (dotnet publish
# -r linux-x64 --self-contained false), zeby nie zzerac RAM-u i dysku maszyny
# budujacej na warstwie SDK. Restore w kontenerze bylby mozliwy (DevExpress
# 26.1.4 jest publicznie na nuget.org), ale jest niepotrzebnie kosztowny.
FROM mcr.microsoft.com/dotnet/aspnet:8.0

# DevExpress renderuje obrazy przez SkiaSharp, a obraz aspnet (Debian slim) nie
# ma jej natywnych zaleznosci. Bez tej warstwy aplikacja wstaje, ale KAZDE
# zadanie konczy sie HTTP 500:
#   ImageLoadException -> DllNotFoundException: Unable to load shared library 'libSkiaSharp'
# Po zmianie tej warstwy budowac z --no-cache, inaczej Docker odda obraz z cache.
RUN apt-get update \
 && apt-get install -y --no-install-recommends libfontconfig1 libfreetype6 \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY publish/ ./

# Deploy Schema konczy proces kodem 42 — restart zapewnia polityka w compose,
# nie skrypt. Kontener jest PID 1, wiec kod wyjscia jest czysty.
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Development \
    DOTNET_RUNNING_IN_CONTAINER=true
EXPOSE 8080

ENTRYPOINT ["dotnet", "XafXPODynAssem.Blazor.Server.dll"]
