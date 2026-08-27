# 配置示例：docker compose.yaml
```
# echo "$HARBOR_PASSWORD" | docker login harbor.demain.com -u "$HARBOR_USER"
# APP_VERSION=<versionTag> docker compose pull && docker compose up -d <container_name>
#
# sudo cd /home/deploy && sudo docker load --input image_name.tar
# APP_VERSION=<versionTag> docker compose up -d <container_name>

services:
  netapi1:
    #build: #仅限本地开发机
    #  context: .
    #  dockerfile: netapi/Dockerfile
    #  args:
    #    APP_VERSION: ${APP_VERSION:-1.0.0}  # 将环境变量传递给构建参数#docker compose build --build-arg  APP_VERSION=1.0.1
    #image: registry.example.com/myimages/netapi:v1.0.1 #docker compose pull && docker compose up -d netapi1
    image: registry.example.com/myimages/netapi:${APP_VERSION:-v1.0.0} #APP_VERSION=v1.0.3 docker compose pull && docker compose up -d netapi1
    #image: netapi:latest #本地镜像：docker load --input netapi.tar && docker compose up -f compose-prod.yaml -d netapi1
    #container_name: deploy-netapi1-1 #自动命名：deploy-servicename-1
    restart: unless-stopped #开机自启、崩溃重启，手动stop后不再拉起
    ports:
      - "8080:8080"
    volumes:
      # 配置文件/日志目录挂载
      - /home/deploy/config/netapi1/appsettings.json:/app/appsettings.json:ro
      - /home/deploy/logs/netapi1:/app/logs
    environment:
      - TZ=Asia/Shanghai
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
    # 资源限制可选
    deploy:
    #  resources:
    #    limits:
    #      cpus: '2'
    #      memory: 1G

  netapi2:
    image: registry.example.com/myimages/netapi:${APP_VERSION:-v1.0.1} #APP_VERSION=v1.0.3 docker compose pull && docker compose up -d
    restart: unless-stopped # 开机自启、崩溃重启，手动stop后不再拉起
    ports:
      - "8081:8081"
    volumes:
      # 配置文件/日志目录挂载
      - /home/deploy/config/netapi2/appsettings.json:/app/appsettings.json:ro
      - /home/deploy/logs/netapi2:/app/logs
    environment:
      - TZ=Asia/Shanghai
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8081

  adminweb:
    image: registry.example.com/myimages/adminweb:${APP_VERSION:-v1.0.1} #APP_VERSION=v1.0.3 docker compose pull && docker compose up -d
    restart: unless-stopped # 开机自启、崩溃重启，手动stop后不再拉起
    ports:
      - "8011:8011"
    volumes:
      # 配置文件/日志目录挂载
      - /home/deploy/config/adminweb/appsettings.json:/app/appsettings.json:ro
      - /home/deploy/logs/adminweb:/app/logs
    environment:
      - TZ=Asia/Shanghai
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8011
```


# 【构建镜像部署流程（在源码根目录[Dockerfile]）】
```
# 进入目录打包镜像
cd D:\Code\slnPath
cd D:\Code\slnPath\myapp
```
### 本地构建后上传服务器，服务器导入镜像部署
```
# 构建镜像：在compose.yaml目录执行,使用.env文件则不用传版本参数
APP_VERSION=1.0.1 docker compose --progress plain build <servicename>
$env:APP_VERSION="1.0.3"; docker compose --progress plain build <servicename> 
Remove-Item Env:APP_VERSION
# 将镜像导出为文件
docker save -o myapp.tar myapp:1.0.0
docker save myapp:1.0.0 | gzip > myapp.tar.gz
# 上传至服务器，然后导入镜像：
scp myapp.tar root@192.168.1.11:/home/deploy/images/
docker load --input myapp.tar
docker load -i myapp.tar
# 确认镜像是否成功加载：
docker images | grep myapp
## 重建容器启动
APP_VERSION=v1.0.1 docker compose up -d --no-deps --force-recreate <servicename>
## 回滚
APP_VERSION=v1.0.0 docker compose -f compose-prod.yaml up -d --no-deps --force-recreate <servicename>
```

### 本地构建推送仓库，服务器拉取部署
```
# 构建镜像：在compose.yaml目录执行,使用.env文件则不用传版本参数
APP_VERSION=1.0.1 docker compose --progress plain build <servicename>
$env:APP_VERSION="1.0.3"; docker compose --progress plain build <servicename>
Remove-Item Env:APP_VERSION

# 本地登录镜像仓库
echo "your_password" | docker login registry.example.com --username your_username --password-stdin
# 推送镜像
docker compose push <servicename>
```
### 重建容器启动
```
# 服务器登录镜像仓库
echo "your_password" | docker login registry.example.com --username your_username --password-stdin
## 拉新镜像,重建容器启动
APP_VERSION=v1.0.1 docker compose pull <servicename> && APP_VERSION=v1.0.1 docker compose up -d --no-deps --force-recreate <servicename>
## 回滚
APP_VERSION=v1.0.0 docker compose -f compose-prod.yaml pull <servicename> && APP_VERSION=v1.0.0 docker compose -f compose-prod.yaml up -d --no-deps --force-recreate <servicename>

# 升级
APP_VERSION=v1.0.1 sh -c 'docker compose pull <servicename> && docker compose up -d --no-deps --force-recreate <servicename>'
# 回滚
APP_VERSION=v1.0.0 sh -c 'docker compose -f compose-prod.yaml pull <servicename> && docker compose -f compose-prod.yaml up -d --no-deps --force-recreate <servicename>'
```

# 命令

### 推送compose里的镜像
docker compose push
docker compose push api socket

### 版本
docker compose version

### 容器状态
docker compose ps

### 验证并输出最终配置
docker compose config

### 只看某个服务的日志
docker compose logs -f app2
### 实时查看日志
docker compose logs -f app2

### 启动所有服务
docker compose up -d
### 启动指定服务
docker compose up -d --no-deps app2

### 停止全部容器/单个容器
```
docker compose stop
docker compose stop app2
```

### 重启全部/单个容器
```
docker compose restart
docker compose restart app2
```

### 销毁所有容器（镜像保留）
docker compose down

### 停止并销毁单个容器，然后重新启动新建容器
docker compose rm -f app2 && docker compose up -d app2

### 进入容器排查
docker compose exec -it myapp bash

### 读取`Dockerfile`，基于源码重新构建新镜，默认标签为`latest`
docker compose build && docker compose up -d

### 登录镜像仓库
docker login registry.example.com
### 从远程镜像仓库拉取已经构建好的镜像，创建并启动容器
docker compose pull && docker compose up -f compose-prod.yaml -d --no-build

### 构建并打上版本号 1.0.2
APP_VERSION=1.0.2 docker compose build \
APP_VERSION=1.0.2 docker compose up -d