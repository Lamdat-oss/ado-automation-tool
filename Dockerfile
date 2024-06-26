FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build-stage

WORKDIR /app

RUN mkdir -p ./Lamdat.ADOAutomationTool
RUN mkdir -p ./Lamdat.ADOAutomationTool.Entities
COPY Src/Lamdat.ADOAutomationTool/Lamdat.ADOAutomationTool.csproj ./Src/Lamdat.ADOAutomationTool/Lamdat.ADOAutomationTool.csproj 
COPY Src/Lamdat.ADOAutomationTool.Entities/Lamdat.ADOAutomationTool.Entities.csproj ./Src/Lamdat.ADOAutomationTool.Entities/Lamdat.ADOAutomationTool.Entities.csproj   



RUN dotnet restore  ./Src/Lamdat.ADOAutomationTool/Lamdat.ADOAutomationTool.csproj 
RUN dotnet restore  ./Src/Lamdat.ADOAutomationTool.Entities/Lamdat.ADOAutomationTool.Entities.csproj   


COPY Src/Lamdat.ADOAutomationTool/ ./Src/Lamdat.ADOAutomationTool   
COPY Src/Lamdat.ADOAutomationTool.Entities/ ./Src/Lamdat.ADOAutomationTool.Entities

RUN dotnet publish ./Src/Lamdat.ADOAutomationTool/Lamdat.ADOAutomationTool.csproj -c Release -o out

FROM mcr.microsoft.com/dotnet/aspnet:8.0.4-jammy

RUN apt update && apt install -y openssl curl net-tools 

WORKDIR /app

COPY --from=build-stage /app/out .

EXPOSE 5001

ENTRYPOINT ["dotnet", "Lamdat.ADOAutomationTool.dll"]  
