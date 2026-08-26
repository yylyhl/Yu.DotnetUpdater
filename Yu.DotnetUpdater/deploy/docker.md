# 【1.搭建 Docker 的仓库】
## Add Docker's official GPG key:
```
sudo apt update
sudo apt install ca-certificates curl
sudo install -m 0755 -d /etc/apt/keyrings
sudo curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
sudo chmod a+r /etc/apt/keyrings/docker.asc
```
## Add the repository to Apt sources:
```
sudo tee /etc/apt/sources.list.d/docker.sources <<EOF
Types: deb
URIs: https://download.docker.com/linux/ubuntu
Suites: $(. /etc/os-release && echo "${UBUNTU_CODENAME:-$VERSION_CODENAME}")
Components: stable
Architectures: $(dpkg --print-architecture)
Signed-By: /etc/apt/keyrings/docker.asc
EOF
sudo apt update
```

# 【2.装 Docker 包】
```
sudo apt install docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
sudo systemctl status docker
sudo systemctl start docker
sudo docker run hello-world
```

# 【构建镜像部署流程（在源码根目录[Dockerfile]）】
## 1.本地构建上传服务器
```
# 进入目录打包镜像
cd D:\Code\slnPath
cd D:\Code\slnPath\img-app1
docker build --progress plain -f ./img-app1/Dockerfile -t img-app1:latest -t img-app1:1.0.0 .
# 将镜像导出为文件
docker save -o img-app1.tar img-app1:1.0.0
docker save img-app1:1.0.0 | gzip > img-app1.tar.gz
# 上传至服务器，然后导入镜像：
scp img-app1.tar root@192.168.1.11:/home/deploy/images/
docker load --input img-app1.tar
docker load -i img-app1.tar
# 确认镜像是否成功加载：
docker images | grep img-app1
```

## 2.本地构建推送-服务器拉取
### 2.1本地构建推送
```
# 本地登录镜像仓库
docker login harbor.domain.com

# 构建镜像：在解决方案同级目录执行 build，指定 Dockerfile 路径。
docker build --progress plain -f ./img-app1/Dockerfile -t harbor.domain.com/myimages/img-app1:1.0.0 -t harbor.domain.com/myimages/img-app1:latest
# 或
VERSION="v1.0.9"
docker build --progress plain -f ./img-app1/Dockerfile -t harbor.domain.com/myimages/img-app1:${VERSION} .
docker tag harbor.domain.com/myimages/img-app1:${VERSION} harbor.domain.com/myimages/img-app1:latest

# 推送镜像
docker push harbor.domain.com/myimages/img-app1:${VERSION}
docker push harbor.domain.com/myimages/img-app1:latest
```
### 2.2服务器拉取部署
```
# 服务器登录镜像仓库
docker login harbor.domain.com

## 服务器首次部署命令
cd /home/deploy

## 拉取镜像
docker pull

## 创建并启动容器
docker up -d

## 拉新镜像
docker pull harbor.domain.com/myimages/img-app1:latest

## 重建容器启动。不能直接更新，只能停止并删掉重建。:
## Dockerfile里：RUN mkdir -p /app/config /app/logs
## sudo chmod -R 777 /home/deploy
docker stop container-myapp1-1 && \
docker rm container-myapp1-1 && \
#挂载整个/app目录，会遮蔽镜像内原有的/app内容
docker run -d \
  --name container-myapp1-1 \
  --restart unless-stopped \
  -p 8082:8080 \
  -v /home/deploy/config/myapp1:/app/config:ro \
  -v /home/deploy/config/common:/app/config-com:ro \
  -v /home/deploy/logs/myapp1:/app/logs \
  -v /etc/localtime:/etc/localtime:ro \
  -e TZ=Asia/Shanghai \
  -e ASPNETCORE_URLS="http://+:8080" \
  img-app1:latest
```

### 3.重建容器启动。不能直接更新，只能停止并删掉重建。:
```
docker stop container-myapp1-1 && \
docker rm container-myapp1-1 && \
#挂载整个/app目录，会遮蔽镜像内原有的/app内容
docker run -d \
  --name container-myapp1-1 \
  --restart unless-stopped \
  -p 8082:8080 \
  -v /home/deploy/config/myapp1:/app/config:ro \
  -v /home/deploy/config/common:/app/config-com:ro \
  -v /home/deploy/logs/myapp1:/app/logs \
  -v /etc/localtime:/etc/localtime:ro \
  -e TZ=Asia/Shanghai \
  -e ASPNETCORE_URLS="http://+:8080" \
  img-app1:latest
```

# 【 Docker 命令】

### 列出全部镜像:
docker images/docker image ls
### 显示镜像完整信息(含分层、环境变量、构建历史):
docker inspect aspnet:latest
### 镜像构建历史:
docker history redis:latest
### 删除指定镜像:
docker rmi img-app1:latest
### 删除<none>悬空镜像（构建残留、无标签、无引用）:
docker image prune
### 清理所有未被使用的镜像(谨慎):
docker image prune -a
### 给镜像新增别名:docker tag SOURCE_IMAGE[:TAG] TARGET_IMAGE[:TAG]
docker tag img-app1:latest img-app1:v1.0.0
## 导出镜像
docker save -o img-app1.tar img-app1:1.0.0
docker save img-app1:1.0.0 | gzip > img-app1.tar.gz
### 导入镜像：
docker load --input img-app1.tar
docker load -i img-app1.tar
### 使用Dockerfile构建，打标签:
```
docker build --progress plain -f ./img-app1/Dockerfile -t img-app1:latest -t img-app1:v1.0.0 .

```
### 查看容器使用的镜像ID:
docker ps --format "table {{.Names}}\t{{.Image}}\t{{.Status}}"
### 查看容器详细信息（挂载卷、环境变量、启动参数）:
docker inspect container-myapp1-1
### 列出所有容器:
docker ps -a
### 只看运行中的:
docker ps
### 容器是否还存在:
docker ps -a | grep container-myapp1-1
### 容器的日志输出:
docker logs container-myapp1-1
### 进入容器内部看：
```
docker exec -it 容器id/容器名称 ls /app
docker exec -it 容器id/容器名称 cat /app/appsettings.json
```
### 停止容器:
docker stop container-myapp1-1
### 重启容器:
docker restart container-myapp1-1
### 销毁容器（镜像挂载保留）:
docker rm -f net10-app1 container-myapp1-1
### 启动旧容器:
docker start container-myapp1-1
### 启动新容器:
```
#挂载整个/app目录，会遮蔽镜像内原有的/app内容
docker run -d \
  --name container-myapp1-1 \
  --restart unless-stopped \
  -p 8082:8080 \
  -v /home/deploy/config/myapp1:/app/config:ro \
  -v /home/deploy/config/common:/app/config-com:ro \
  -v /home/deploy/logs/myapp1:/app/logs \
  -v /etc/localtime:/etc/localtime:ro \
  -e TZ=Asia/Shanghai \
  -e ASPNETCORE_URLS="http://+:8080" \
  img-app1:latest
```



