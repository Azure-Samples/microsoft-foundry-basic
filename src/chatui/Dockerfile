FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . ./chatui
RUN dotnet publish ./chatui -c Release -o /out

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
# launchSettings.json would pin Kestrel to a localhost dev URL instead of $PORT
RUN rm -rf ./Properties/launchSettings.json
EXPOSE 80
ENTRYPOINT ["dotnet", "chatui.dll"]
