FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-stage

WORKDIR /app

COPY Src/Lamdat.ADOAutomationTool/Lamdat.ADOAutomationTool.csproj ./   
COPY Src/Lamdat.ADOAutomationTool.Entities/Lamdat.ADOAutomationTool.Entities.csproj ./   


RUN dotnet restore

COPY Src/Lamdat.ADOAutomationTool/ ./   

RUN dotnet publish Src/Lamdat.ADOAutomationTool/Lamdat.ADOAutomationTool.csproj -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:8.0.4-jammy

RUN apt update && apt install -y openssl curl net-tools 

WORKDIR /app

COPY --from=build-stage /app/out .

EXPOSE 5001

ENTRYPOINT ["dotnet", "Lamdat.ADOAutomationTool.dll"]  
