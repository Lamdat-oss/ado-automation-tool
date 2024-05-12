FROM mcr.microsoft.com/dotnet/sdk:6.0 AS build-stage

WORKDIR /app

COPY Src/Lamdat.ADOAutomationTool/Lamdat.ADOAutomationTool.csproj ./   

RUN dotnet restore

COPY Src/Lamdat.ADOAutomationTool/ ./   

RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:6.0.29-jammy

WORKDIR /app

COPY --from=build-stage /app/out .

EXPOSE 5000

ENTRYPOINT ["dotnet", "Lamdat.ADOAutomationTool.dll"]  
