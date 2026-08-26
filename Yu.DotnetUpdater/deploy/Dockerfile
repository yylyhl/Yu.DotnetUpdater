# 请参阅 https://aka.ms/customizecontainer 以了解如何自定义调试容器，以及 Visual Studio 如何使用此 Dockerfile 生成映像以更快地进行调试。

# 此阶段用于在快速模式(默认为调试配置)下从 VS 运行时
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 9000


# 此阶段用于生成服务项目
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["projectname/projectname.csproj", "projectname/"]
COPY ["ECHO.Common.ApiResponse/ECHO.Common.ApiResponse.csproj", "ECHO.Common.ApiResponse/"]
COPY ["ECHO.Common.AwsS3/ECHO.Common.AwsS3.csproj", "ECHO.Common.AwsS3/"]
COPY ["ECHO.Common.CoreHelper/ECHO.Common.CoreHelper.csproj", "ECHO.Common.CoreHelper/"]
COPY ["ECHO.Common.FreeSqlHelper/ECHO.Common.FreeSqlHelper.csproj", "ECHO.Common.FreeSqlHelper/"]
COPY ["ECHO.Common.IMHelper/ECHO.Common.IMHelper.csproj", "ECHO.Common.IMHelper/"]
COPY ["ECHO.Common.TencentCos/ECHO.Common.TencentCos.csproj", "ECHO.Common.TencentCos/"]
COPY ["ECHO.Transaction.Blockchain/ECHO.Transaction.Blockchain.csproj", "ECHO.Transaction.Blockchain/"]
COPY ["ECHO.Transaction.Entity/ECHO.Transaction.Entity.csproj", "ECHO.Transaction.Entity/"]
COPY ["ECHO.Transaction.Enums/ECHO.Transaction.Enums.csproj", "ECHO.Transaction.Enums/"]
COPY ["ECHO.Transaction.RedisHelper/ECHO.Transaction.RedisHelper.csproj", "ECHO.Transaction.RedisHelper/"]
COPY ["ECHO.Common.FreeRedisHelper/ECHO.Common.FreeRedisHelper.csproj", "ECHO.Common.FreeRedisHelper/"]
COPY ["ECHO.Transaction.ModelData/ECHO.Transaction.ModelData.csproj", "ECHO.Transaction.ModelData/"]
COPY ["ECHO.Transaction.IMData/ECHO.Transaction.IMData.csproj", "ECHO.Transaction.IMData/"]
COPY ["ECHO.Transaction.ModelRedis/ECHO.Transaction.ModelRedis.csproj", "ECHO.Transaction.ModelRedis/"]
COPY ["ECHO.Transaction.MongoHelper/ECHO.Transaction.MongoHelper.csproj", "ECHO.Transaction.MongoHelper/"]
COPY ["ECHO.Common.MongoHelper/ECHO.Common.MongoHelper.csproj", "ECHO.Common.MongoHelper/"]
RUN dotnet restore "./projectname/projectname.csproj"
COPY . .
WORKDIR "/src/projectname"
RUN dotnet build "./projectname.csproj" -c $BUILD_CONFIGURATION -o /app/build

# 此阶段用于发布要复制到最终阶段的服务项目
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./projectname.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# 此阶段在生产中使用，或在常规模式下从 VS 运行时使用(在不使用调试配置时为默认值)
FROM base AS final
WORKDIR /app

# 创建容器内配置、日志目录，后续宿主机volume挂载，需与代码中设置的路径保持一致
RUN mkdir -p /app/Log /app/config /app/start /app/crash && chmod 777 /app/Log /app/config /app/start /app/crash

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "projectname.dll"]



# cd projectname
# docker build --progress plain -f ./projectname/Dockerfile -t echo.transaction.admin:latest -t echo.transaction.admin:1.0.0 .

# docker save -o echo.transaction.admin.tar echo.transaction.admin:1.0.0
# docker save echo.transaction.admin:1.0.0 | gzip > echo.transaction.admin.tar.gz

# docker load --input echo.transaction.admin.tar
# docker load -i echo.transaction.admin.tar

# sudo mkdir -p /home/deploy/config/echo.transaction.admin /home/deploy/logs/echo.transaction.admin
# sudo chmod -R 777 /home/deploy

# 重建容器启动
# docker stop echo.transaction.admin && docker rm echo.transaction.admin
# docker run -d \
#   --name echo.transaction.admin \
#   --restart unless-stopped \
#   -p 9201:9201 \
#   -v /home/deploy/logs/echo.transaction.admin:/app/Log \
#   -v /home/deploy/logs/echo.transaction.admin:/app/config \
#   -v /home/deploy/logs/echo.transaction.admin:/app/start \
#   -v /home/deploy/logs/echo.transaction.admin:/app/crash \
#   -v /home/deploy/config/echo.transaction.admin/appsettings.json:/app/appsettings.json:ro \
#   -v /home/deploy/config/echo.transaction.admin/appsettingsApi.json:/app/appsettingsApi.json:ro \
#   -v /home/deploy/config/echo.transaction.admin/appsettingsDB.json:/app/appsettingsDB.json:ro \
#   -v /home/deploy/config/echo.transaction.admin/appsettingsReqChainApi.json:/app/appsettingsReqChainApi.json:ro \
#   -v /home/deploy/config/echo.transaction.admin/appsettingsReqChainToken.json:/app/appsettingsReqChainToken.json:ro \
#   -v /home/deploy/config/echo.transaction.admin/appsettingsTrade.json:/app/appsettingsTrade.json:ro \
#   -v /home/deploy/config/echo.transaction.admin/appsettingsTradeLimit.json:/app/appsettingsTradeLimit.json:ro \
#   -v /home/deploy/config/echo.transaction.admin/appsettingsOss.json:/app/appsettingsOss.json:ro \
#   -v /home/deploy/config/echo.transaction.admin/appsettingsSms.json:/app/appsettingsSms.json:ro \
#   -v /home/deploy/config/echo.transaction.admin/appsettingsRateLimit.json:/app/appsettingsRateLimit.json:ro \
#   -v /home/deploy/config/echo.transaction.admin/appsettingsSmsAliyun.json:/app/appsettingsSmsAliyun.json:ro \
#   -v /home/deploy/config/echo.transaction.admin/appsettingsMetrics.json:/app/appsettingsMetrics.json:ro \
#   -e ASPNETCORE_URLS="http://+:9201" \
#   -e ASPNETCORE_ENVIRONMENT=Production \
#   -e ASPNETCORE_Kestrel__Certificates__Default__Path="" \
#   -e ASPNETCORE_Kestrel__Certificates__Default__Password="" \
#   echo.transaction.admin:1.0.0

# sudo docker logs echo.transaction.admin