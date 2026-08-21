# 配置示例：docker-compose.yml
```
version: '3.8'

services:
  appserv1:
    build: #仅限本地开发机
      context: .
      dockerfile: myapp1/Dockerfile
    image: harbor.domain.com/myimages/img-app1:${APP_VERSION:-v1.0.1} #APP_VERSION=v1.0.3 docker-compose pull && docker-compose up -d
    #image: harbor.domain.com/myimages/img-app1:v1.0.1
    #image: harbor.domain.com/myimages/img-app1:latest
    container_name: container-myapp1-1
    restart: unless-stopped # 开机自启、崩溃重启，手动stop后不再拉起
    ports:
      - "8080:8080"
    volumes:
      # 配置文件/日志目录挂载
      - /home/deploy/config/app1/appsettings.json:/app/appsettings.json:ro
      - /home/deploy/config/app/appsettings.json:/app/appsettings.json:ro
      - /home/deploy/logs/app1:/app/logs
    environment:
      - TZ=Asia/Shanghai
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8080
    # 资源限制可选
    deploy:
      resources:
        limits:
          cpus: '2'
          memory: 1G

  appserv2:
    build: #本地开发机
      context: .
      dockerfile: myapp1/Dockerfile
    image: harbor.domain.com/myimages/img-app1:${APP_VERSION:-v1.0.1} #APP_VERSION=v1.0.3 docker-compose pull && docker-compose up -d
    #image: harbor.domain.com/myimages/img-app1:v1.0.1
    #image: harbor.domain.com/myimages/img-app1:latest
    container_name: container-myapp1-2
    restart: unless-stopped # 开机自启、崩溃重启，手动stop后不再拉起
    ports:
      - "8081:8081"
    volumes:
      # 配置文件/日志目录挂载
      - /home/deploy/config/app2/appsettings.json:/app/appsettings.json:ro
      - /home/deploy/config/app/appsettings.json:/app/appsettings.json:ro
      - /home/deploy/logs/app2:/app/logs
    environment:
      - TZ=Asia/Shanghai
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8081

  appserv3:
    build: #本地开发机
      context: .
      dockerfile: myapp3/Dockerfile
    container_name: container-myapp3-1
    restart: unless-stopped # 开机自启、崩溃重启，手动stop后不再拉起
    ports:
      - "8011:8011"
    volumes:
      # 配置文件/日志目录挂载
      - /home/deploy/config/app3/appsettings.json:/app/appsettings.json:ro
      - /home/deploy/config/app/appsettings.json:/app/appsettings.json:ro
      - /home/deploy/logs/app3:/app/logs
    environment:
      - TZ=Asia/Shanghai
      - ASPNETCORE_ENVIRONMENT=Production
      - ASPNETCORE_URLS=http://+:8011
```

# 命令

### 发布更新：从仓库拉取新镜像，创建并启动容器
#### 方式 1：docker-compose.yml同目录新建 `.env` 文件
```
#内容
APP_VERSION=v1.0.2
#直接执行
docker-compose pull && docker-compose up -d
```
#### 方式 2：命令行临时传入版本
cd /home/deploy && APP_VERSION=v1.0.3 docker-compose pull && docker-compose up -d

#### 回滚
cd /home/deploy && APP_VERSION=v1.0.1 docker-compose pull && docker-compose up -d


### 容器状态
docker compose ps

### 渲染后的image完整配置
APP_VERSION=v2.0.0 docker-compose config

### 启动全部容器/单个容器
```
docker compose up -d
docker compose up -d app2
```

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

### 查看所有容器状态
docker compose ps

### 销毁所有容器（镜像保留）
docker compose down

### 停止并销毁单个容器，然后重新启动新建容器
docker compose rm -f app2 && docker compose up -d app2

### 进入容器排查
docker compose exec -it myapp bash

### 读取`Dockerfile`，基于源码重新构建新镜，默认标签为`latest`
docker compose build && docker compose up -d

### 登录镜像仓库
docker login harbor.domain.com
### 从远程镜像仓库拉取已经构建好的镜像，创建并启动容器
docker compose pull && docker compose up -d


### 构建并打上版本号 1.0.2
APP_VERSION=1.0.2 docker compose build \
APP_VERSION=1.0.2 docker compose up -d