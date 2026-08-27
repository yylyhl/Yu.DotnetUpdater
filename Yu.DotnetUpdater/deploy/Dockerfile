# 请参阅 https://aka.ms/customizecontainer 以了解如何自定义调试容器，以及 Visual Studio 如何使用此 Dockerfile 生成映像以更快地进行调试。

# 此阶段用于在快速模式(默认为调试配置)下从 VS 运行时
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 9000

# 此阶段用于生成服务项目
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
# 安装 tzdata，时区改为东八区
RUN apt-get update && apt-get install -y tzdata && \
    ln -sf /usr/share/zoneinfo/Asia/Shanghai /etc/localtime && \
    echo "Asia/Shanghai" > /etc/timezone
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["projectname/projectname.csproj", "projectname/"]
COPY ["My.Common.ApiResponse/My.Common.ApiResponse.csproj", "My.Common.ApiResponse/"]
COPY ["My.Common.AwsS3/My.Common.AwsS3.csproj", "My.Common.AwsS3/"]
COPY ["My.Common.CoreHelper/My.Common.CoreHelper.csproj", "My.Common.CoreHelper/"]
COPY ["My.Common.FreeSqlHelper/My.Common.FreeSqlHelper.csproj", "My.Common.FreeSqlHelper/"]
COPY ["My.Common.IMHelper/My.Common.IMHelper.csproj", "My.Common.IMHelper/"]
COPY ["My.Common.TencentCos/My.Common.TencentCos.csproj", "My.Common.TencentCos/"]
COPY ["My.Transaction.Blockchain/My.Transaction.Blockchain.csproj", "My.Transaction.Blockchain/"]
COPY ["My.Transaction.Entity/My.Transaction.Entity.csproj", "My.Transaction.Entity/"]
COPY ["My.Transaction.Enums/My.Transaction.Enums.csproj", "My.Transaction.Enums/"]
COPY ["My.Transaction.RedisHelper/My.Transaction.RedisHelper.csproj", "My.Transaction.RedisHelper/"]
COPY ["My.Common.FreeRedisHelper/My.Common.FreeRedisHelper.csproj", "My.Common.FreeRedisHelper/"]
COPY ["My.Transaction.ModelData/My.Transaction.ModelData.csproj", "My.Transaction.ModelData/"]
COPY ["My.Transaction.IMData/My.Transaction.IMData.csproj", "My.Transaction.IMData/"]
COPY ["My.Transaction.ModelRedis/My.Transaction.ModelRedis.csproj", "My.Transaction.ModelRedis/"]
COPY ["My.Transaction.MongoHelper/My.Transaction.MongoHelper.csproj", "My.Transaction.MongoHelper/"]
COPY ["My.Common.MongoHelper/My.Common.MongoHelper.csproj", "My.Common.MongoHelper/"]
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

# 创建容器内配置、日志目录，后续宿主机volume挂载，需与代码中设置的路径保持一致 #挂载宿主机目录到/app目录，会遮蔽镜像内/app原有的内容
RUN mkdir -p /app/Log /app/config /app/start /app/crash && chmod 777 /app/Log /app/config /app/start /app/crash

COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "projectname.dll"]



# cd projectname
# docker build --progress plain -f ./projectname/Dockerfile -t My.Transaction.admin:latest -t My.Transaction.admin:1.0.0 .

# docker save -o My.Transaction.admin.tar My.Transaction.admin:1.0.0
# docker save My.Transaction.admin:1.0.0 | gzip > My.Transaction.admin.tar.gz

# docker load --input My.Transaction.admin.tar
# docker load -i My.Transaction.admin.tar

# sudo mkdir -p /home/deploy/config/My.Transaction.admin /home/deploy/logs/My.Transaction.admin
# sudo chmod -R 777 /home/deploy

# 重建容器启动
# docker stop My.Transaction.admin && docker rm My.Transaction.admin
# docker run -d \
#   --name My.Transaction.admin \
#   --restart unless-stopped \
#   -p 9201:9201 \
#   -v /home/deploy/start/My.Transaction.admin:/app/start \
#   -v /home/deploy/crash/My.Transaction.admin:/app/crash \
#   -v /home/deploy/logs/My.Transaction.admin:/app/Log \
#   -v /home/deploy/config/My.Transaction.admin:/app/config \
#   -v /home/deploy/config/My.Transaction.admin/appsettings.json:/app/appsettings.json:ro \
#   -v /home/deploy/config/My.Transaction.admin/appsettingsApi.json:/app/appsettingsApi.json:ro \
#   -v /home/deploy/config/My.Transaction.admin/appsettingsDB.json:/app/appsettingsDB.json:ro \
#   -v /home/deploy/config/My.Transaction.admin/appsettingsReqChainApi.json:/app/appsettingsReqChainApi.json:ro \
#   -v /home/deploy/config/My.Transaction.admin/appsettingsReqChainToken.json:/app/appsettingsReqChainToken.json:ro \
#   -v /home/deploy/config/My.Transaction.admin/appsettingsTrade.json:/app/appsettingsTrade.json:ro \
#   -v /home/deploy/config/My.Transaction.admin/appsettingsTradeLimit.json:/app/appsettingsTradeLimit.json:ro \
#   -v /home/deploy/config/My.Transaction.admin/appsettingsOss.json:/app/appsettingsOss.json:ro \
#   -v /home/deploy/config/My.Transaction.admin/appsettingsSms.json:/app/appsettingsSms.json:ro \
#   -v /home/deploy/config/My.Transaction.admin/appsettingsRateLimit.json:/app/appsettingsRateLimit.json:ro \
#   -v /home/deploy/config/My.Transaction.admin/appsettingsSmsAliyun.json:/app/appsettingsSmsAliyun.json:ro \
#   -v /home/deploy/config/My.Transaction.admin/appsettingsMetrics.json:/app/appsettingsMetrics.json:ro \
#   -v /etc/localtime:/etc/localtime:ro \
#   -e TZ=Asia/Shanghai \
#   -e ASPNETCORE_URLS="http://+:9201" \
#   -e ASPNETCORE_ENVIRONMENT=Production \
#   -e ASPNETCORE_Kestrel__Certificates__Default__Path="" \
#   -e ASPNETCORE_Kestrel__Certificates__Default__Password="" \
#   My.Transaction.admin:1.0.0

# sudo docker logs My.Transaction.admin